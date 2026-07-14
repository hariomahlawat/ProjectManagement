using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Scheduling;

namespace ProjectManagement.Services.Usage;

public sealed record ErpAdoptionTrendPoint(
    DateOnly Date,
    int SignedInUsers,
    int UsedErpUsers,
    int OperationalContributors,
    bool IsWorkingDay);

public sealed record ErpAdoptionAttentionRow(
    string UserId,
    string DisplayName,
    string Rank,
    string UserName,
    string Observation,
    DateTime? LastRecordedUseUtc,
    bool SignedInDuringPeriod);

public sealed record ErpCommandAdoptionSnapshot(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateTimeOffset TrackingInceptionUtc,
    int TrackingWorkingDays,
    int RequiredWorkingDays,
    bool ReviewAvailable,
    int TotalUsers,
    int ActiveToday,
    int SignedInUsers,
    int UsedErpUsers,
    int OperationalContributors,
    int AdoptionGap,
    int ReviewCaseCount,
    IReadOnlyList<ErpAdoptionTrendPoint> Trend,
    IReadOnlyList<ErpAdoptionAttentionRow> Attention);

public interface IErpCommandAdoptionQueryService
{
    Task<ErpCommandAdoptionSnapshot> GetAsync(
        int monitoredWorkingDays = 7,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds the command-facing adoption picture from three distinct signals:
/// successful authentication, monitored ERP use and recognised operational actions.
/// All counts use one effective population: active human accounts that are not pending deletion.
/// </summary>
public sealed class ErpCommandAdoptionQueryService : IErpCommandAdoptionQueryService
{
    private const int MinimumWorkingDays = 7;
    private const int MaximumWorkingDays = 30;
    private const int MaximumAttentionRows = 8;

    private readonly ApplicationDbContext _db;
    private readonly IOfficeCalendarService _officeCalendar;
    private readonly IAdminTimeService _time;
    private readonly IOptions<ErpUsageOptions> _options;

    public ErpCommandAdoptionQueryService(
        ApplicationDbContext db,
        IOfficeCalendarService officeCalendar,
        IAdminTimeService time,
        IOptions<ErpUsageOptions> options)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _officeCalendar = officeCalendar ?? throw new ArgumentNullException(nameof(officeCalendar));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<ErpCommandAdoptionSnapshot> GetAsync(
        int monitoredWorkingDays = MinimumWorkingDays,
        CancellationToken cancellationToken = default)
    {
        var options = _options.Value;
        var requiredWorkingDays = Math.Clamp(
            monitoredWorkingDays,
            MinimumWorkingDays,
            MaximumWorkingDays);
        var nowUtc = _time.UtcNow;
        var today = _time.TodayIst;
        var trackingInceptionUtc = options.TrackingInceptionUtc.ToUniversalTime();
        var trackingInceptionDate = DateOnly.FromDateTime(
            _time.ToIst(trackingInceptionUtc).DateTime);

        var users = await _db.Users
            .AsNoTracking()
            .Where(user =>
                !user.IsDisabled
                && !user.PendingDeletion
                && user.AccountKind == UserAccountKind.Human)
            .OrderBy(user => user.FullName)
            .ThenBy(user => user.UserName)
            .Select(user => new UserProjection(
                user.Id,
                user.FullName,
                user.Rank,
                user.UserName ?? string.Empty,
                user.CreatedUtc))
            .ToListAsync(cancellationToken);

        var userIds = users.Select(user => user.Id).ToArray();
        if (userIds.Length == 0 || nowUtc < trackingInceptionUtc)
        {
            return EmptySnapshot(
                today,
                trackingInceptionUtc,
                requiredWorkingDays,
                users.Count);
        }

        var nonWorkingDates = await _officeCalendar.GetNonWorkingDatesAsync(
            trackingInceptionDate,
            today.AddDays(1),
            cancellationToken);
        var configuredWorkingDays = options.WorkingDays.ToHashSet();
        var monitoredWorkingDates = EnumerateDates(trackingInceptionDate, today)
            .Where(date =>
                configuredWorkingDays.Contains(date.DayOfWeek)
                && !nonWorkingDates.Contains(date))
            .ToArray();
        var selectedWorkingDates = monitoredWorkingDates
            .TakeLast(requiredWorkingDays)
            .ToArray();
        var periodStart = selectedWorkingDates.FirstOrDefault();
        if (periodStart == default)
        {
            periodStart = trackingInceptionDate > today
                ? today
                : trackingInceptionDate;
        }

        var periodStartUtc = LaterOf(
            _time.StartOfIstDayUtc(periodStart),
            trackingInceptionUtc);
        var periodEndUtcExclusive = _time.EndExclusiveOfIstDayUtc(today);
        var periodStartUtcDateTime = periodStartUtc.UtcDateTime;
        var periodEndUtcDateTime = periodEndUtcExclusive.UtcDateTime;

        var loginRows = await _db.AuthEvents
            .AsNoTracking()
            .Where(authEvent =>
                userIds.Contains(authEvent.UserId)
                && authEvent.Event == AuthenticationEventNames.LoginSucceeded
                && authEvent.WhenUtc >= periodStartUtc
                && authEvent.WhenUtc < periodEndUtcExclusive)
            .Select(authEvent => new LoginProjection(
                authEvent.UserId,
                authEvent.WhenUtc))
            .ToListAsync(cancellationToken);

        var bucketRows = await _db.UserActivityBuckets
            .AsNoTracking()
            .Where(bucket =>
                userIds.Contains(bucket.UserId)
                && bucket.BucketStartUtc >= periodStartUtcDateTime
                && bucket.BucketStartUtc < periodEndUtcDateTime
                && bucket.BucketStartUtc >= trackingInceptionUtc.UtcDateTime)
            .Select(bucket => new BucketProjection(
                bucket.UserId,
                bucket.ActivityDateIst,
                bucket.LastSeenUtc))
            .ToListAsync(cancellationToken);

        var rawActionRows = await CandidateAuditQuery()
            .Where(audit =>
                audit.UserId != null
                && userIds.Contains(audit.UserId)
                && audit.TimeUtc >= periodStartUtcDateTime
                && audit.TimeUtc < periodEndUtcDateTime
                && audit.TimeUtc >= trackingInceptionUtc.UtcDateTime)
            .Select(audit => new RawActionProjection(
                audit.UserId!,
                audit.TimeUtc,
                audit.Action))
            .ToListAsync(cancellationToken);

        var actionRows = rawActionRows
            .Select(action => new ActionProjection(
                action.UserId,
                action.TimeUtc,
                ErpUsageActionClassifier.Classify(action.Action)))
            .Where(action => action.Kind != ErpUsageActionKind.Ignored)
            .ToList();

        var signedInUserIds = loginRows
            .Select(row => row.UserId)
            .ToHashSet(StringComparer.Ordinal);
        var usedErpUserIds = bucketRows
            .Select(row => row.UserId)
            .Concat(actionRows.Select(row => row.UserId))
            .ToHashSet(StringComparer.Ordinal);
        var operationalContributorIds = actionRows
            .Where(row => row.Kind == ErpUsageActionKind.Operational)
            .Select(row => row.UserId)
            .ToHashSet(StringComparer.Ordinal);
        var activeTodayUserIds = bucketRows
            .Where(row => row.ActivityDateIst == today)
            .Select(row => row.UserId)
            .Concat(actionRows
                .Where(row => ToIstDate(row.TimeUtc) == today)
                .Select(row => row.UserId))
            .ToHashSet(StringComparer.Ordinal);
        var adoptionGapUserIds = signedInUserIds
            .Except(usedErpUserIds, StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var trend = EnumerateDates(periodStart, today)
            .Select(date => new ErpAdoptionTrendPoint(
                date,
                loginRows
                    .Where(row => ToIstDate(row.WhenUtc) == date)
                    .Select(row => row.UserId)
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
                bucketRows
                    .Where(row => row.ActivityDateIst == date)
                    .Select(row => row.UserId)
                    .Concat(actionRows
                        .Where(row => ToIstDate(row.TimeUtc) == date)
                        .Select(row => row.UserId))
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
                actionRows
                    .Where(row =>
                        row.Kind == ErpUsageActionKind.Operational
                        && ToIstDate(row.TimeUtc) == date)
                    .Select(row => row.UserId)
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
                configuredWorkingDays.Contains(date.DayOfWeek)
                    && !nonWorkingDates.Contains(date)))
            .ToArray();

        var reviewAvailable = monitoredWorkingDates.Length >= requiredWorkingDays;
        var reviewWindowStart = selectedWorkingDates.FirstOrDefault();
        var reviewEligibleUsers = reviewAvailable && reviewWindowStart != default
            ? users
                .Where(user => ToIstDate(user.CreatedUtc) <= reviewWindowStart)
                .ToArray()
            : Array.Empty<UserProjection>();
        var attention = reviewAvailable
            ? await BuildAttentionAsync(
                reviewEligibleUsers,
                signedInUserIds,
                usedErpUserIds,
                trackingInceptionUtc,
                cancellationToken)
            : Array.Empty<ErpAdoptionAttentionRow>();

        return new ErpCommandAdoptionSnapshot(
            periodStart,
            today,
            trackingInceptionUtc,
            monitoredWorkingDates.Length,
            requiredWorkingDays,
            reviewAvailable,
            users.Count,
            activeTodayUserIds.Count,
            signedInUserIds.Count,
            usedErpUserIds.Count,
            operationalContributorIds.Count,
            adoptionGapUserIds.Count,
            reviewAvailable
                ? reviewEligibleUsers.Count(user => !usedErpUserIds.Contains(user.Id))
                : 0,
            trend,
            attention);
    }

    private async Task<IReadOnlyList<ErpAdoptionAttentionRow>> BuildAttentionAsync(
        IReadOnlyList<UserProjection> users,
        IReadOnlySet<string> signedInUserIds,
        IReadOnlySet<string> usedErpUserIds,
        DateTimeOffset trackingInceptionUtc,
        CancellationToken cancellationToken)
    {
        var attentionUsers = users
            .Where(user => !usedErpUserIds.Contains(user.Id))
            .ToArray();
        if (attentionUsers.Length == 0)
        {
            return Array.Empty<ErpAdoptionAttentionRow>();
        }

        var attentionUserIds = attentionUsers.Select(user => user.Id).ToArray();
        var lastBucketRows = await _db.UserActivityBuckets
            .AsNoTracking()
            .Where(bucket =>
                attentionUserIds.Contains(bucket.UserId)
                && bucket.BucketStartUtc >= trackingInceptionUtc.UtcDateTime)
            .GroupBy(bucket => bucket.UserId)
            .Select(group => new LastActiveProjection(
                group.Key,
                group.Max(bucket => bucket.LastSeenUtc)))
            .ToListAsync(cancellationToken);

        var lastActivityByUser = lastBucketRows.ToDictionary(
            row => row.UserId,
            row => (DateTime?)row.LastActiveUtc,
            StringComparer.Ordinal);

        var historicalActions = await CandidateAuditQuery()
            .Where(audit =>
                audit.UserId != null
                && attentionUserIds.Contains(audit.UserId)
                && audit.TimeUtc >= trackingInceptionUtc.UtcDateTime)
            .Select(audit => new RawActionProjection(
                audit.UserId!,
                audit.TimeUtc,
                audit.Action))
            .ToListAsync(cancellationToken);

        foreach (var group in historicalActions
                     .Where(action =>
                         ErpUsageActionClassifier.Classify(action.Action)
                         != ErpUsageActionKind.Ignored)
                     .GroupBy(action => action.UserId, StringComparer.Ordinal))
        {
            var latestAction = group.Max(action => action.TimeUtc);
            if (!lastActivityByUser.TryGetValue(group.Key, out var current)
                || !current.HasValue
                || latestAction > current.Value)
            {
                lastActivityByUser[group.Key] = latestAction;
            }
        }

        return attentionUsers
            .Select(user =>
            {
                var signedIn = signedInUserIds.Contains(user.Id);
                return new ErpAdoptionAttentionRow(
                    user.Id,
                    string.IsNullOrWhiteSpace(user.FullName)
                        ? user.UserName
                        : user.FullName,
                    user.Rank,
                    user.UserName,
                    signedIn
                        ? "Signed in but no monitored ERP use"
                        : "No ERP use in 7 monitored working days",
                    lastActivityByUser.GetValueOrDefault(user.Id),
                    signedIn);
            })
            .OrderByDescending(row => row.SignedInDuringPeriod)
            .ThenBy(row => row.LastRecordedUseUtc ?? DateTime.MinValue)
            .ThenBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumAttentionRows)
            .ToArray();
    }

    private IQueryable<AuditLog> CandidateAuditQuery() =>
        _db.AuditLogs
            .AsNoTracking()
            .Where(audit =>
                !audit.Action.StartsWith("Login")
                && !audit.Action.StartsWith("Logout")
                && !audit.Action.StartsWith("Auth")
                && !audit.Action.StartsWith("Password")
                && !audit.Action.StartsWith("UserActivity")
                && !audit.Action.StartsWith("ErpUsage")
                && !audit.Action.StartsWith("SystemHealth")
                && !audit.Action.StartsWith("Session")
                && !audit.Action.StartsWith("Antiforgery"));

    private DateOnly ToIstDate(DateTime utc) =>
        DateOnly.FromDateTime(_time.ToIst(utc).Date);

    private DateOnly ToIstDate(DateTimeOffset utc) =>
        DateOnly.FromDateTime(_time.ToIst(utc).DateTime);

    private static DateTimeOffset LaterOf(
        DateTimeOffset first,
        DateTimeOffset second) =>
        first >= second ? first : second;

    private static IEnumerable<DateOnly> EnumerateDates(
        DateOnly start,
        DateOnly endInclusive)
    {
        for (var date = start; date <= endInclusive; date = date.AddDays(1))
        {
            yield return date;
        }
    }

    private static ErpCommandAdoptionSnapshot EmptySnapshot(
        DateOnly today,
        DateTimeOffset trackingInceptionUtc,
        int requiredWorkingDays,
        int totalUsers) =>
        new(
            today,
            today,
            trackingInceptionUtc,
            0,
            requiredWorkingDays,
            false,
            totalUsers,
            0,
            0,
            0,
            0,
            0,
            0,
            Array.Empty<ErpAdoptionTrendPoint>(),
            Array.Empty<ErpAdoptionAttentionRow>());

    private sealed record UserProjection(
        string Id,
        string FullName,
        string Rank,
        string UserName,
        DateTime CreatedUtc);

    private sealed record LoginProjection(
        string UserId,
        DateTimeOffset WhenUtc);

    private sealed record BucketProjection(
        string UserId,
        DateOnly ActivityDateIst,
        DateTime LastSeenUtc);

    private sealed record RawActionProjection(
        string UserId,
        DateTime TimeUtc,
        string Action);

    private sealed record ActionProjection(
        string UserId,
        DateTime TimeUtc,
        ErpUsageActionKind Kind);

    private sealed record LastActiveProjection(
        string UserId,
        DateTime LastActiveUtc);
}
