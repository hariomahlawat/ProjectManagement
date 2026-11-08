using System.Linq.Expressions;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
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
    int ReviewPageSize,
    // Successful sign-ins only: the caption under Successful sign-ins counts these, while
    // UniqueUsers and UniqueSourceIps also count failed attempts
    // (docs/ux/admin-review-4-fixes.md, PR 9).
    int SuccessfulPeople = 0,
    int SuccessfulComputers = 0)
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
                // Typed user names are matched case-insensitively, as Identity signs in.
                var selectedLogin = (selectedUser.UserName ?? string.Empty).ToUpperInvariant();
                auditQuery = auditQuery.Where(audit =>
                    audit.UserId == selectedId
                    || (audit.UserName != null && audit.UserName.ToUpper() == selectedLogin));
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
        // A failed attempt carries no user id, only the name as typed. Resolve typed names to
        // the account they sign in to (case-insensitively, through Identity's normalised name)
        // so "ADMIN" and "admin" are one affected account, and a person with both a success and
        // a failure counts once among active users.
        var auditKeyRows = await auditQuery
            .Select(audit => new
            {
                audit.UserId,
                Login = audit.UserName == null ? null : audit.UserName.ToUpper()
            })
            .Distinct()
            .ToListAsync(cancellationToken);
        var typedLogins = auditKeyRows
            .Where(row => row.UserId == null && row.Login != null)
            .Select(row => row.Login!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var accountByLogin = typedLogins.Length == 0
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : (await _db.Users.AsNoTracking()
                    .Where(user => user.NormalizedUserName != null && typedLogins.Contains(user.NormalizedUserName))
                    .Select(user => new { user.Id, user.NormalizedUserName })
                    .ToListAsync(cancellationToken))
                .ToDictionary(user => user.NormalizedUserName!, user => user.Id, StringComparer.Ordinal);
        var auditUserKeys = auditKeyRows
            .Select(row => row.UserId
                ?? (row.Login is not null && accountByLogin.TryGetValue(row.Login, out var accountId)
                    ? accountId
                    : "login:" + (row.Login ?? "UNKNOWN")))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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
        var successfulPeople = successfulUserKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var successfulComputers = successfulIps
            .Select(NormalizeSourceAddress)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

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

        // Everything in the period, newest first: the "usual sign-in times" figures.
        var analysed = await LoadNewestAsync(successfulQuery, auditQuery, maximumPoints, cancellationToken);

        // The Outcome and "Only sign-ins to look at" filters run in the query, before the cap,
        // so an older failed sign-in is still listed when newer successful ones would fill the
        // cap (third review, C3). The review queue always uses the review rule; the chart and
        // the export use it only when the filter asks for it.
        var (filteredSuccessful, filteredAudit) = ApplyOutcomeFilter(successfulQuery, auditQuery, request.Outcome);
        var hasOutcomeFilter = !ReferenceEquals(filteredSuccessful, successfulQuery)
            || !ReferenceEquals(filteredAudit, auditQuery);

        var review = await LoadNewestAsync(
            filteredSuccessful?.Where(LateNightSuccessfulSignIn),
            filteredAudit,
            maximumPoints,
            cancellationToken);
        var filtered = request.ReviewOnly
            ? review
            : hasOutcomeFilter
                ? await LoadNewestAsync(filteredSuccessful, filteredAudit, maximumPoints, cancellationToken)
                : analysed;

        // A failed sign-in may keep only the typed user name. When it matches an account, the
        // person's name is shown with it, through the audit log's one name lookup (one query).
        var signIns = analysed.Rows
            .Concat(filtered.Rows)
            .Concat(review.Rows)
            .ToArray();
        var identities = await ResolveIdentitiesAsync(
            signIns.Select(row => row.UserId),
            signIns.Where(row => string.IsNullOrWhiteSpace(row.UserId)).Select(row => row.TypedLogin),
            cancellationToken);

        var analysedEvents = BuildEvents(analysed.Rows, identities, markWeekends);
        var filteredEvents = ReferenceEquals(filtered, analysed)
            ? analysedEvents
            : BuildEvents(filtered.Rows, identities, markWeekends);
        var reviewSource = ReferenceEquals(review, filtered)
            ? filteredEvents
            : BuildEvents(review.Rows, identities, markWeekends);

        // Cut off means a list on the page (chart, export or review queue) lost rows to the cap.
        var isTruncated = filtered.Truncated || review.Truncated;

        var patternPoints = filteredEvents
            .OrderBy(row => row.LocalTime)
            .ToArray();
        var reviewEvents = ConsolidateReviewEvents(
                reviewSource.Where(row => row.RequiresReview),
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
            pageSize,
            successfulPeople,
            successfulComputers);
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

    private async Task<IdentityDirectory> ResolveIdentitiesAsync(
        IEnumerable<string?> userIds,
        IEnumerable<string?> typedLogins,
        CancellationToken cancellationToken)
    {
        var (byId, byUserName) = await AuditActorNames.LoadDirectoryAsync(
            _db,
            userIds,
            typedLogins,
            cancellationToken);
        return new IdentityDirectory(byId, byUserName);
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
            reasons.Add("Failed sign-in");
            reviewLevel = AdminLoginReviewLevel.Review;
        }
        else if (outcome == AdminLoginOutcome.LockedOut)
        {
            reasons.Add("Account locked");
            reviewLevel = AdminLoginReviewLevel.Critical;
        }
        else
        {
            var outsideHours = local.TimeOfDay < _options.WorkdayStart || local.TimeOfDay > _options.WorkdayEnd;
            var weekend = markWeekends && local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            if (outsideHours) reasons.Add("Outside working hours");
            if (weekend) reasons.Add("Weekend sign-in");

            // Routine outside-hours activity remains informational. Only materially
            // abnormal successful sign-ins enter the administrator review queue.
            if (local.TimeOfDay < TimeSpan.FromHours(5) || local.TimeOfDay >= TimeSpan.FromHours(22))
            {
                reasons.Add("Late at night (between 22:00 and 05:00)");
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

    /// <summary>
    /// The Outcome filter as queries. A null stream is left out entirely; an unknown value
    /// leaves both streams as they are.
    /// </summary>
    private static (IQueryable<AuthEvent>? Successful, IQueryable<AuditLog>? Audit) ApplyOutcomeFilter(
        IQueryable<AuthEvent> successful,
        IQueryable<AuditLog> audit,
        string? outcome) => outcome?.Trim().ToLowerInvariant() switch
    {
        "successful" or "success" => (successful, null),
        "failed" => (null, audit.Where(row => row.Action == AuthenticationEventNames.AuditLoginFailed)),
        "locked" or "locked-out" => (null, audit.Where(row => row.Action == AuthenticationEventNames.AuditLoginLockedOut)),
        _ => (successful, audit)
    };

    /// <summary>
    /// The review rule for a successful sign-in, as SQL: late at night in IST (22:00 to 05:00).
    /// Failed and locked-out attempts always need a look. Keep in step with <see cref="BuildEvent"/>.
    /// </summary>
    internal static readonly Expression<Func<AuthEvent, bool>> LateNightSuccessfulSignIn =
        authEvent => authEvent.WhenUtc.AddMinutes(IstOffsetMinutes).Hour < 5
            || authEvent.WhenUtc.AddMinutes(IstOffsetMinutes).Hour >= 22;

    /// <summary>
    /// The newest sign-ins from both streams, at most <paramref name="maximum"/>. Each stream is
    /// read newest first with one extra row, so the caller knows when the cap cut something off.
    /// </summary>
    private async Task<SignInSlice> LoadNewestAsync(
        IQueryable<AuthEvent>? successfulQuery,
        IQueryable<AuditLog>? auditQuery,
        int maximum,
        CancellationToken cancellationToken)
    {
        var rows = new List<RawSignIn>();
        var truncated = false;

        if (successfulQuery is not null)
        {
            var successful = await successfulQuery
                .OrderByDescending(authEvent => authEvent.WhenUtc)
                .Take(maximum + 1)
                .Select(authEvent => new
                {
                    authEvent.WhenUtc,
                    authEvent.UserId,
                    authEvent.Ip,
                    authEvent.UserAgent
                })
                .ToListAsync(cancellationToken);
            truncated |= successful.Count > maximum;
            rows.AddRange(successful.Take(maximum).Select(row => new RawSignIn(
                row.WhenUtc,
                row.UserId,
                null,
                AdminLoginOutcome.Successful,
                row.Ip,
                row.UserAgent)));
        }

        if (auditQuery is not null)
        {
            var audit = await auditQuery
                .OrderByDescending(row => row.TimeUtc)
                .ThenByDescending(row => row.Id)
                .Take(maximum + 1)
                .Select(row => new
                {
                    row.TimeUtc,
                    row.UserId,
                    row.UserName,
                    row.Action,
                    row.Ip,
                    row.UserAgent
                })
                .ToListAsync(cancellationToken);
            truncated |= audit.Count > maximum;
            rows.AddRange(audit.Take(maximum).Select(row => new RawSignIn(
                new DateTimeOffset(EnsureUtc(row.TimeUtc)),
                row.UserId,
                row.UserName,
                row.Action == AuthenticationEventNames.AuditLoginLockedOut
                    ? AdminLoginOutcome.LockedOut
                    : AdminLoginOutcome.Failed,
                row.Ip,
                row.UserAgent)));
        }

        truncated |= rows.Count > maximum;
        return new SignInSlice(
            rows.OrderByDescending(row => row.WhenUtc).Take(maximum).ToArray(),
            truncated);
    }

    private AdminLoginEventRow[] BuildEvents(
        IEnumerable<RawSignIn> rows,
        IdentityDirectory identities,
        bool markWeekends) => rows
        .Select(row =>
        {
            var identity = ResolveIdentity(row.UserId, row.TypedLogin, identities);
            return BuildEvent(
                row.WhenUtc,
                row.UserId,
                identity.LoginName,
                identity.DisplayName,
                row.Outcome,
                row.Ip,
                row.UserAgent,
                markWeekends);
        })
        .ToArray();

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
        IdentityDirectory identities)
    {
        if (!string.IsNullOrWhiteSpace(userId)
            && identities.ById.TryGetValue(userId, out var person))
        {
            return ToIdentity(person);
        }

        // Only the typed user name was kept (a failed sign-in): name the account it matches.
        if (!string.IsNullOrWhiteSpace(fallbackLogin)
            && identities.ByUserName.TryGetValue(fallbackLogin.Trim(), out var named))
        {
            return ToIdentity(named);
        }

        var login = string.IsNullOrWhiteSpace(fallbackLogin) ? "Unknown user" : fallbackLogin;
        return new UserIdentity(userId ?? string.Empty, login, login);
    }

    private static UserIdentity ToIdentity(AuditActorDirectoryEntry person) => new(
        person.Id,
        person.UserName ?? string.Empty,
        person.DisplayName ?? "Unknown user");

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

    private sealed record IdentityDirectory(
        IReadOnlyDictionary<string, AuditActorDirectoryEntry> ById,
        IReadOnlyDictionary<string, AuditActorDirectoryEntry> ByUserName);

    private sealed record RawSignIn(
        DateTimeOffset WhenUtc,
        string? UserId,
        string? TypedLogin,
        AdminLoginOutcome Outcome,
        string? Ip,
        string? UserAgent);

    private sealed record SignInSlice(IReadOnlyList<RawSignIn> Rows, bool Truncated);
}
