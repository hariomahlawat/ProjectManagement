using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Services;

namespace ProjectManagement.Services.Admin;

public enum AdminLoginOutcome
{
    Successful = 0,
    Failed = 1,
    LockedOut = 2
}

public enum AdminLoginReviewLevel
{
    Informational = 0,
    Review = 1,
    Critical = 2
}

public sealed record AdminLoginMonitoringRequest(
    int Days = 30,
    string? UserId = null,
    string? Outcome = null,
    bool ReviewOnly = false,
    bool? MarkWeekendsForReview = null,
    int Page = 1,
    int PageSize = 25);

public sealed record AdminLoginUserOption(
    string Id,
    string UserName,
    string DisplayName,
    string Rank);

public sealed record AdminLoginEventRow(
    DateTimeOffset WhenUtc,
    DateTimeOffset LocalTime,
    string? UserId,
    string LoginName,
    string DisplayName,
    AdminLoginOutcome Outcome,
    string? Ip,
    string? UserAgent,
    int MinutesOfDay,
    bool RequiresReview,
    string ReviewReason,
    AdminLoginReviewLevel ReviewLevel,
    int OccurrenceCount);

public sealed record AdminLoginTrendPoint(
    DateOnly Date,
    int Successful,
    int Failed,
    int LockedOut);

public sealed record AdminLoginMonitoringSnapshot(
    int Days,
    DateOnly FromDate,
    DateOnly ToDate,
    TimeSpan WorkdayStart,
    TimeSpan WorkdayEnd,
    bool MarkWeekendsForReview,
    int Successful,
    int Failed,
    int LockedOut,
    int UniqueUsers,
    int UniqueSourceIps,
    int AffectedAccounts,
    int ReviewSignals,
    int MedianMinutesOfDay,
    int P90MinutesOfDay,
    bool IsTruncated,
    IReadOnlyList<AdminLoginUserOption> Users,
    IReadOnlyList<AdminLoginTrendPoint> Trend,
    IReadOnlyList<AdminLoginEventRow> AnalysedEvents,
    IReadOnlyList<AdminLoginEventRow> PatternPoints,
    IReadOnlyList<AdminLoginEventRow> ReviewRows,
    int ReviewTotal,
    int ReviewPage,
    int ReviewPageSize)
{
    public int ReviewTotalPages => ReviewTotal == 0
        ? 1
        : (int)Math.Ceiling(ReviewTotal / (double)ReviewPageSize);
}

public interface IAdminLoginMonitoringService
{
    Task<AdminLoginMonitoringSnapshot> GetAsync(
        AdminLoginMonitoringRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds bounded operational login analytics from the canonical successful
/// authentication stream and the audit stream used for failed and locked-out attempts.
/// </summary>
public sealed class AdminLoginMonitoringService : IAdminLoginMonitoringService
{
    private const int IstOffsetMinutes = 330;

    private readonly ApplicationDbContext _db;
    private readonly IAdminTimeService _time;
    private readonly AdminLoginMonitoringOptions _options;

    public AdminLoginMonitoringService(
        ApplicationDbContext db,
        IAdminTimeService time,
        IOptions<AdminLoginMonitoringOptions> options)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<AdminLoginMonitoringSnapshot> GetAsync(
        AdminLoginMonitoringRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestedDays = request.Days > 0 ? request.Days : _options.DefaultLookbackDays;
        var days = Math.Clamp(requestedDays, 1, _options.MaximumLookbackDays);
        var toDate = _time.TodayIst;
        var fromDate = toDate.AddDays(-(days - 1));
        var fromUtc = _time.StartOfIstDayUtc(fromDate);
        var toUtcExclusive = _time.EndExclusiveOfIstDayUtc(toDate);
        var markWeekends = request.MarkWeekendsForReview ?? _options.MarkWeekendsForReview;
        var pageSize = request.PageSize is > 0 and <= 500
            ? Math.Min(request.PageSize, _options.MaximumReviewPageSize)
            : _options.DefaultReviewPageSize;
        var page = Math.Max(1, request.Page);

        var selectedUser = await ResolveSelectedUserAsync(request.UserId, cancellationToken);
        var users = await GetUserOptionsAsync(cancellationToken);

        var successfulQuery = _db.AuthEvents.AsNoTracking()
            .Where(authEvent =>
                authEvent.Event == AuthenticationEventNames.LoginSucceeded
                && authEvent.WhenUtc >= fromUtc
                && authEvent.WhenUtc < toUtcExclusive);

        var auditQuery = _db.AuditLogs.AsNoTracking()
            .Where(audit =>
                audit.TimeUtc >= fromUtc.UtcDateTime
                && audit.TimeUtc < toUtcExclusive.UtcDateTime
                && (audit.Action == AuthenticationEventNames.AuditLoginFailed
                    || audit.Action == AuthenticationEventNames.AuditLoginLockedOut));

        if (!string.IsNullOrWhiteSpace(request.UserId))
        {
            successfulQuery = successfulQuery.Where(authEvent => authEvent.UserId == request.UserId);

            if (selectedUser is null)
            {
                auditQuery = auditQuery.Where(_ => false);
            }
            else
            {
                var selectedId = selectedUser.Id;
                var selectedLogin = selectedUser.UserName;
                auditQuery = auditQuery.Where(audit =>
                    audit.UserId == selectedId || audit.UserName == selectedLogin);
            }
        }

        var successfulCount = await successfulQuery.CountAsync(cancellationToken);
        var failedCount = await auditQuery
            .CountAsync(audit => audit.Action == AuthenticationEventNames.AuditLoginFailed, cancellationToken);
        var lockedOutCount = await auditQuery
            .CountAsync(audit => audit.Action == AuthenticationEventNames.AuditLoginLockedOut, cancellationToken);

        var successfulUserKeys = await successfulQuery
            .Select(authEvent => authEvent.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var auditUserKeys = await auditQuery
            .Select(audit => audit.UserId ?? ("login:" + (audit.UserName ?? "unknown")))
            .Distinct()
            .ToListAsync(cancellationToken);
        var uniqueUsers = successfulUserKeys
            .Concat(auditUserKeys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var successfulIps = await successfulQuery
            .Where(authEvent => authEvent.Ip != null && authEvent.Ip != string.Empty)
            .Select(authEvent => authEvent.Ip!)
            .Distinct()
            .ToListAsync(cancellationToken);
        var auditIps = await auditQuery
            .Where(audit => audit.Ip != null && audit.Ip != string.Empty)
            .Select(audit => audit.Ip!)
            .Distinct()
            .ToListAsync(cancellationToken);
        var uniqueSourceIps = successfulIps
            .Concat(auditIps)
            .Select(NormalizeSourceAddress)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var affectedAccounts = auditUserKeys.Count;

        var successfulTrendRows = await successfulQuery
            .GroupBy(authEvent => authEvent.WhenUtc.AddMinutes(IstOffsetMinutes).Date)
            .Select(group => new { Day = group.Key, Count = group.Count() })
            .OrderBy(row => row.Day)
            .ToListAsync(cancellationToken);
        var failedTrendRows = await auditQuery
            .Where(audit => audit.Action == AuthenticationEventNames.AuditLoginFailed)
            .GroupBy(audit => audit.TimeUtc.AddMinutes(IstOffsetMinutes).Date)
            .Select(group => new { Day = group.Key, Count = group.Count() })
            .OrderBy(row => row.Day)
            .ToListAsync(cancellationToken);
        var lockedTrendRows = await auditQuery
            .Where(audit => audit.Action == AuthenticationEventNames.AuditLoginLockedOut)
            .GroupBy(audit => audit.TimeUtc.AddMinutes(IstOffsetMinutes).Date)
            .Select(group => new { Day = group.Key, Count = group.Count() })
            .OrderBy(row => row.Day)
            .ToListAsync(cancellationToken);

        var trend = BuildTrend(
            fromDate,
            toDate,
            successfulTrendRows.Select(row => (DateOnly.FromDateTime(row.Day), row.Count)),
            failedTrendRows.Select(row => (DateOnly.FromDateTime(row.Day), row.Count)),
            lockedTrendRows.Select(row => (DateOnly.FromDateTime(row.Day), row.Count)));

        var maximumPoints = _options.MaximumChartPoints;
        var successfulRows = await successfulQuery
            .OrderByDescending(authEvent => authEvent.WhenUtc)
            .Take(maximumPoints + 1)
            .Select(authEvent => new
            {
                authEvent.WhenUtc,
                authEvent.UserId,
                authEvent.Ip,
                authEvent.UserAgent
            })
            .ToListAsync(cancellationToken);
        var failedRows = await auditQuery
            .OrderByDescending(audit => audit.TimeUtc)
            .ThenByDescending(audit => audit.Id)
            .Take(maximumPoints + 1)
            .Select(audit => new
            {
                audit.TimeUtc,
                audit.UserId,
                audit.UserName,
                audit.Action,
                audit.Ip,
                audit.UserAgent
            })
            .ToListAsync(cancellationToken);

        var identityIds = successfulRows.Select(row => row.UserId)
            .Concat(failedRows.Where(row => !string.IsNullOrWhiteSpace(row.UserId)).Select(row => row.UserId!))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var identities = await ResolveIdentitiesAsync(identityIds, cancellationToken);

        var events = new List<AdminLoginEventRow>(successfulRows.Count + failedRows.Count);
        foreach (var row in successfulRows.Take(maximumPoints))
        {
            var identity = ResolveIdentity(row.UserId, null, identities);
            events.Add(BuildEvent(
                row.WhenUtc,
                row.UserId,
                identity.LoginName,
                identity.DisplayName,
                AdminLoginOutcome.Successful,
                row.Ip,
                row.UserAgent,
                markWeekends));
        }

        foreach (var row in failedRows.Take(maximumPoints))
        {
            var identity = ResolveIdentity(row.UserId, row.UserName, identities);
            var outcome = row.Action == AuthenticationEventNames.AuditLoginLockedOut
                ? AdminLoginOutcome.LockedOut
                : AdminLoginOutcome.Failed;
            events.Add(BuildEvent(
                new DateTimeOffset(EnsureUtc(row.TimeUtc)),
                row.UserId,
                identity.LoginName,
                identity.DisplayName,
                outcome,
                row.Ip,
                row.UserAgent,
                markWeekends));
        }

        var isTruncated = successfulRows.Count > maximumPoints
            || failedRows.Count > maximumPoints
            || events.Count > maximumPoints;
        var analysedEvents = events
            .OrderByDescending(row => row.WhenUtc)
            .Take(maximumPoints)
            .ToArray();

        var filteredEvents = ApplyOutcomeFilter(analysedEvents, request.Outcome);
        if (request.ReviewOnly)
        {
            filteredEvents = filteredEvents.Where(row => row.RequiresReview);
        }

        var patternPoints = filteredEvents
            .OrderBy(row => row.LocalTime)
            .ToArray();
        var reviewEvents = ConsolidateReviewEvents(
                filteredEvents.Where(row => row.RequiresReview),
                _options.DuplicateWindowMinutes)
            .OrderByDescending(row => row.WhenUtc)
            .ToArray();
        var reviewTotal = reviewEvents.Length;
        page = reviewTotal == 0
            ? 1
            : Math.Min(page, (int)Math.Ceiling(reviewTotal / (double)pageSize));
        var reviewRows = reviewEvents
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArray();

        var successfulMinutes = analysedEvents
            .Where(row => row.Outcome == AdminLoginOutcome.Successful)
            .Select(row => row.MinutesOfDay)
            .OrderBy(value => value)
            .ToArray();

        return new AdminLoginMonitoringSnapshot(
            days,
            fromDate,
            toDate,
            _options.WorkdayStart,
            _options.WorkdayEnd,
            markWeekends,
            successfulCount,
            failedCount,
            lockedOutCount,
            uniqueUsers,
            uniqueSourceIps,
            affectedAccounts,
            reviewTotal,
            Percentile(successfulMinutes, 50),
            Percentile(successfulMinutes, 90),
            isTruncated,
            users,
            trend,
            analysedEvents,
            patternPoints,
            reviewRows,
            reviewTotal,
            page,
            pageSize);
    }

    private async Task<AdminLoginUserOption?> ResolveSelectedUserAsync(
        string? userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId)) return null;

        return await _db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new AdminLoginUserOption(
                user.Id,
                user.UserName ?? string.Empty,
                string.IsNullOrWhiteSpace(user.FullName)
                    ? (user.UserName ?? "Unknown user")
                    : user.FullName,
                user.Rank))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<AdminLoginUserOption>> GetUserOptionsAsync(
        CancellationToken cancellationToken) =>
        await _db.Users.AsNoTracking()
            .Where(user => !user.PendingDeletion)
            .OrderBy(user => user.FullName)
            .ThenBy(user => user.UserName)
            .Select(user => new AdminLoginUserOption(
                user.Id,
                user.UserName ?? string.Empty,
                string.IsNullOrWhiteSpace(user.FullName)
                    ? (user.UserName ?? "Unknown user")
                    : user.FullName,
                user.Rank))
            .ToListAsync(cancellationToken);

    private async Task<IReadOnlyDictionary<string, UserIdentity>> ResolveIdentitiesAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<string, UserIdentity>(StringComparer.Ordinal);
        }

        var rows = await _db.Users.AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .Select(user => new UserIdentity(
                user.Id,
                user.UserName ?? string.Empty,
                string.IsNullOrWhiteSpace(user.FullName)
                    ? (user.UserName ?? "Unknown user")
                    : user.FullName))
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.Id, StringComparer.Ordinal);
    }

    private AdminLoginEventRow BuildEvent(
        DateTimeOffset whenUtc,
        string? userId,
        string loginName,
        string displayName,
        AdminLoginOutcome outcome,
        string? ip,
        string? userAgent,
        bool markWeekends)
    {
        var local = _time.ToIst(whenUtc);
        var minutes = local.Hour * 60 + local.Minute;
        var reasons = new List<string>();
        var reviewLevel = AdminLoginReviewLevel.Informational;

        if (outcome == AdminLoginOutcome.Failed)
        {
            reasons.Add("Failed authentication event");
            reviewLevel = AdminLoginReviewLevel.Review;
        }
        else if (outcome == AdminLoginOutcome.LockedOut)
        {
            reasons.Add("Account lockout event");
            reviewLevel = AdminLoginReviewLevel.Critical;
        }
        else
        {
            var outsideHours = local.TimeOfDay < _options.WorkdayStart || local.TimeOfDay > _options.WorkdayEnd;
            var weekend = markWeekends && local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            if (outsideHours) reasons.Add("Outside configured working hours");
            if (weekend) reasons.Add("Weekend sign-in");

            // Routine outside-hours activity remains informational. Only materially
            // abnormal successful sign-ins enter the administrator review queue.
            if (local.TimeOfDay < TimeSpan.FromHours(5) || local.TimeOfDay >= TimeSpan.FromHours(22))
            {
                reasons.Add("Materially abnormal sign-in time");
                reviewLevel = AdminLoginReviewLevel.Review;
            }
        }

        return new AdminLoginEventRow(
            whenUtc,
            local,
            userId,
            string.IsNullOrWhiteSpace(loginName) ? "Unknown user" : loginName,
            string.IsNullOrWhiteSpace(displayName) ? "Unknown user" : displayName,
            outcome,
            NormalizeSourceAddress(ip),
            userAgent,
            minutes,
            reviewLevel != AdminLoginReviewLevel.Informational,
            reasons.Count == 0 ? "Expected pattern" : string.Join("; ", reasons),
            reviewLevel,
            1);
    }


    private static IEnumerable<AdminLoginEventRow> ConsolidateReviewEvents(
        IEnumerable<AdminLoginEventRow> source,
        int windowMinutes)
    {
        var seconds = Math.Max(1, windowMinutes) * 60L;
        return source
            .GroupBy(row => new
            {
                Identity = string.IsNullOrWhiteSpace(row.UserId) ? row.LoginName : row.UserId,
                row.Outcome,
                Source = row.Ip ?? string.Empty,
                row.ReviewReason,
                Window = row.WhenUtc.ToUnixTimeSeconds() / seconds
            })
            .Select(group => (group
                .OrderByDescending(row => row.WhenUtc)
                .First()) with { OccurrenceCount = group.Count() });
    }

    private static string? NormalizeSourceAddress(string? value)
    {
        var source = value?.Trim();
        if (string.IsNullOrWhiteSpace(source)) return null;
        if (!IPAddress.TryParse(source, out var address)) return source;
        if (IPAddress.IsLoopback(address)) return "Local server";
        if (address.IsIPv4MappedToIPv6) return address.MapToIPv4().ToString();
        return address.ToString();
    }

    private static IEnumerable<AdminLoginEventRow> ApplyOutcomeFilter(
        IEnumerable<AdminLoginEventRow> source,
        string? outcome) => outcome?.Trim().ToLowerInvariant() switch
    {
        "successful" or "success" => source.Where(row => row.Outcome == AdminLoginOutcome.Successful),
        "failed" => source.Where(row => row.Outcome == AdminLoginOutcome.Failed),
        "locked" or "locked-out" => source.Where(row => row.Outcome == AdminLoginOutcome.LockedOut),
        _ => source
    };

    private static IReadOnlyList<AdminLoginTrendPoint> BuildTrend(
        DateOnly fromDate,
        DateOnly toDate,
        IEnumerable<(DateOnly Date, int Count)> successful,
        IEnumerable<(DateOnly Date, int Count)> failed,
        IEnumerable<(DateOnly Date, int Count)> locked)
    {
        var successMap = successful.ToDictionary(row => row.Date, row => row.Count);
        var failedMap = failed.ToDictionary(row => row.Date, row => row.Count);
        var lockedMap = locked.ToDictionary(row => row.Date, row => row.Count);
        var result = new List<AdminLoginTrendPoint>();

        for (var day = fromDate; day <= toDate; day = day.AddDays(1))
        {
            successMap.TryGetValue(day, out var successCount);
            failedMap.TryGetValue(day, out var failedCount);
            lockedMap.TryGetValue(day, out var lockedCount);
            result.Add(new(day, successCount, failedCount, lockedCount));
        }

        return result;
    }

    private static UserIdentity ResolveIdentity(
        string? userId,
        string? fallbackLogin,
        IReadOnlyDictionary<string, UserIdentity> identities)
    {
        if (!string.IsNullOrWhiteSpace(userId)
            && identities.TryGetValue(userId, out var identity))
        {
            return identity;
        }

        var login = string.IsNullOrWhiteSpace(fallbackLogin) ? "Unknown user" : fallbackLogin;
        return new UserIdentity(userId ?? string.Empty, login, login);
    }

    private static int Percentile(IReadOnlyList<int> sortedValues, int percentile)
    {
        if (sortedValues.Count == 0) return 0;
        if (sortedValues.Count == 1) return sortedValues[0];

        var rank = (percentile / 100d) * (sortedValues.Count - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        if (lower == upper) return sortedValues[lower];

        return (int)Math.Round(
            sortedValues[lower] + (sortedValues[upper] - sortedValues[lower]) * (rank - lower));
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private sealed record UserIdentity(string Id, string LoginName, string DisplayName);
}
