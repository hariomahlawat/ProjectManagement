using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Scheduling;

namespace ProjectManagement.Services.Usage;

public sealed class ErpUsageQuery
{
    public int Days { get; init; } = 30;
    public string? Search { get; init; }
    public string? Role { get; init; }
    public string? Module { get; init; }
    public string? Posture { get; init; }
    public string? AccountState { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}

public sealed record ErpUsageSummary(
    int UserCount,
    int ActiveToday,
    int RegularUsers,
    int OccasionalUsers,
    int NoUsageSevenWorkingDays,
    int NoUsageThirtyDays,
    int BusinessContributors);

public sealed record ErpUsageModuleSummary(
    string Key,
    string Label,
    string Icon,
    int UserCount,
    int ActiveBucketCount);

public enum ErpUsageHeatmapState
{
    NoActivity = 0,
    Navigation = 1,
    Interactive = 2,
    BusinessAction = 3,
    NonWorkingDay = 4
}

public sealed record ErpUsageHeatmapCell(
    DateOnly Date,
    ErpUsageHeatmapState State,
    string Label);

public sealed record ErpUsageUserRow(
    string UserId,
    string UserName,
    string FullName,
    string Rank,
    IReadOnlyList<string> Roles,
    string AccountState,
    bool UsedToday,
    DateTime? LastActiveUtc,
    int ActiveWorkingDays,
    int AvailableWorkingDays,
    int ActivePercentage,
    int ApproximateActiveMinutes,
    IReadOnlyList<string> Modules,
    int RecordedActionCount,
    string Posture,
    string PostureTone,
    IReadOnlyList<ErpUsageHeatmapCell> Heatmap);

public sealed record ErpUsageResult(
    DateOnly StartDate,
    DateOnly EndDate,
    int Days,
    int RegularThresholdPercent,
    ErpUsageSummary Summary,
    IReadOnlyList<ErpUsageModuleSummary> Modules,
    IReadOnlyList<string> RoleOptions,
    IReadOnlyList<ErpUsageUserRow> Users,
    int TotalUsers,
    int Page,
    int PageSize,
    int TotalPages,
    IReadOnlyList<DateOnly> HeatmapDates);

public sealed record ErpUsageCommandSummary(
    int TotalUsers,
    int ActiveToday,
    int RegularUsers,
    int NoUsageSevenWorkingDays);

public interface IErpUsageQueryService
{
    Task<ErpUsageResult> GetAsync(
        ErpUsageQuery query,
        CancellationToken cancellationToken = default);

    Task<ErpUsageCommandSummary> GetCommandSummaryAsync(
        CancellationToken cancellationToken = default);
}

public sealed class ErpUsageQueryService : IErpUsageQueryService
{
    // A 45-day history is sufficient to determine the independent 30-calendar-day and
    // seven-office-working-day inactivity indicators even when the selected report is 7 days.
    private const int MinimumInactivityHistoryDays = 45;

    private readonly ApplicationDbContext _db;
    private readonly IOfficeCalendarService _officeCalendar;
    private readonly IErpUsageModuleCatalog _modules;
    private readonly IAdminTimeService _time;
    private readonly IOptions<ErpUsageOptions> _options;

    public ErpUsageQueryService(
        ApplicationDbContext db,
        IOfficeCalendarService officeCalendar,
        IErpUsageModuleCatalog modules,
        IAdminTimeService time,
        IOptions<ErpUsageOptions> options)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _officeCalendar = officeCalendar ?? throw new ArgumentNullException(nameof(officeCalendar));
        _modules = modules ?? throw new ArgumentNullException(nameof(modules));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<ErpUsageResult> GetAsync(
        ErpUsageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var options = _options.Value;
        var days = NormaliseDays(query.Days, options.MaximumLookbackDays);
        var today = _time.TodayIst;
        var startDate = today.AddDays(-(days - 1));
        var endDateExclusive = today.AddDays(1);
        var historyStartDate = EarlierOf(startDate, today.AddDays(-(MinimumInactivityHistoryDays - 1)));
        var startUtc = _time.StartOfIstDayUtc(startDate).UtcDateTime;
        var historyStartUtc = _time.StartOfIstDayUtc(historyStartDate).UtcDateTime;
        var endUtc = _time.EndExclusiveOfIstDayUtc(today).UtcDateTime;
        var pageSize = Math.Clamp(query.PageSize, 10, 100);
        var page = Math.Max(1, query.Page);

        IQueryable<ApplicationUser> userEntityQuery = _db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var searchPattern = $"%{query.Search.Trim()}%";
            userEntityQuery = userEntityQuery.Where(user =>
                (user.UserName != null && EF.Functions.ILike(user.UserName, searchPattern))
                || EF.Functions.ILike(user.FullName, searchPattern)
                || EF.Functions.ILike(user.Rank, searchPattern));
        }

        var users = await userEntityQuery
            .OrderBy(user => user.FullName)
            .ThenBy(user => user.UserName)
            .Select(user => new UserProjection(
                user.Id,
                user.UserName ?? string.Empty,
                user.FullName,
                user.Rank,
                user.CreatedUtc,
                user.IsDisabled,
                user.PendingDeletion))
            .ToListAsync(cancellationToken);

        var roleRows = await (
            from userRole in _db.UserRoles.AsNoTracking()
            join role in _db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            select new { userRole.UserId, RoleName = role.Name ?? string.Empty })
            .ToListAsync(cancellationToken);

        var rolesByUser = roleRows
            .GroupBy(row => row.UserId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(row => row.RoleName)
                    .Where(role => !string.IsNullOrWhiteSpace(role))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                StringComparer.Ordinal);

        var roleOptions = roleRows
            .Select(row => row.RoleName)
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            var role = query.Role.Trim();
            users = users
                .Where(user => rolesByUser.TryGetValue(user.Id, out var assigned)
                    && assigned.Contains(role, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(query.AccountState))
        {
            var state = query.AccountState.Trim();
            users = users
                .Where(user => AccountState(user).Equals(state, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var userIds = users.Select(user => user.Id).ToArray();
        var allHistoryBuckets = userIds.Length == 0
            ? new List<BucketProjection>()
            : await _db.UserActivityBuckets
                .AsNoTracking()
                .Where(bucket =>
                    userIds.Contains(bucket.UserId)
                    && bucket.ActivityDateIst >= historyStartDate
                    && bucket.ActivityDateIst < endDateExclusive)
                .Select(bucket => new BucketProjection(
                    bucket.UserId,
                    bucket.ActivityDateIst,
                    bucket.BucketStartUtc,
                    bucket.ModuleKey,
                    bucket.HadNavigation,
                    bucket.HadInteractiveHeartbeat,
                    bucket.LastSeenUtc))
                .ToListAsync(cancellationToken);

        var periodBuckets = allHistoryBuckets
            .Where(bucket => bucket.ActivityDateIst >= startDate)
            .ToList();

        if (!string.IsNullOrWhiteSpace(query.Module))
        {
            var module = query.Module.Trim();
            var usersInModule = periodBuckets
                .Where(bucket => string.Equals(bucket.ModuleKey, module, StringComparison.OrdinalIgnoreCase))
                .Select(bucket => bucket.UserId)
                .ToHashSet(StringComparer.Ordinal);

            users = users.Where(user => usersInModule.Contains(user.Id)).ToList();
            userIds = users.Select(user => user.Id).ToArray();
            var selectedUserIds = userIds.ToHashSet(StringComparer.Ordinal);
            allHistoryBuckets = allHistoryBuckets
                .Where(bucket => selectedUserIds.Contains(bucket.UserId))
                .ToList();
            periodBuckets = periodBuckets
                .Where(bucket => selectedUserIds.Contains(bucket.UserId))
                .ToList();
        }

        var lastActiveRows = userIds.Length == 0
            ? new List<LastActiveProjection>()
            : await _db.UserActivityBuckets
                .AsNoTracking()
                .Where(bucket => userIds.Contains(bucket.UserId))
                .GroupBy(bucket => bucket.UserId)
                .Select(group => new LastActiveProjection(
                    group.Key,
                    group.Max(bucket => bucket.LastSeenUtc)))
                .ToListAsync(cancellationToken);
        var lastActiveByUser = lastActiveRows.ToDictionary(
            row => row.UserId,
            row => (DateTime?)row.LastActiveUtc,
            StringComparer.Ordinal);

        var allHistoryActions = userIds.Length == 0
            ? new List<ActionProjection>()
            : await OperationalAuditQuery()
                .Where(audit =>
                    audit.UserId != null
                    && userIds.Contains(audit.UserId)
                    && audit.TimeUtc >= historyStartUtc
                    && audit.TimeUtc < endUtc)
                .Select(audit => new ActionProjection(
                    audit.UserId!,
                    audit.TimeUtc,
                    audit.Action))
                .ToListAsync(cancellationToken);
        var periodActions = allHistoryActions
            .Where(action => action.TimeUtc >= startUtc)
            .ToList();

        var lastActionRows = userIds.Length == 0
            ? new List<LastActiveProjection>()
            : await OperationalAuditQuery()
                .Where(audit => audit.UserId != null && userIds.Contains(audit.UserId))
                .GroupBy(audit => audit.UserId!)
                .Select(group => new LastActiveProjection(
                    group.Key,
                    group.Max(audit => audit.TimeUtc)))
                .ToListAsync(cancellationToken);
        foreach (var action in lastActionRows)
        {
            if (!lastActiveByUser.TryGetValue(action.UserId, out var current)
                || !current.HasValue
                || action.LastActiveUtc > current.Value)
            {
                lastActiveByUser[action.UserId] = action.LastActiveUtc;
            }
        }

        var nonWorkingDates = await _officeCalendar.GetNonWorkingDatesAsync(
            historyStartDate,
            endDateExclusive,
            cancellationToken);
        var configuredWorkingDays = options.WorkingDays.ToHashSet();
        var historyWorkingDays = EnumerateDates(historyStartDate, today)
            .Where(date => configuredWorkingDays.Contains(date.DayOfWeek) && !nonWorkingDates.Contains(date))
            .ToArray();
        var periodWorkingDays = historyWorkingDays.Where(date => date >= startDate).ToArray();
        var lastSevenWorkingDays = historyWorkingDays.TakeLast(7).ToArray();
        var thirtyCalendarCutoff = today.AddDays(-29);
        var heatmapDates = EnumerateDates(
                days > 30 ? today.AddDays(-29) : startDate,
                today)
            .ToArray();

        var historyBucketsByUser = allHistoryBuckets
            .GroupBy(bucket => bucket.UserId)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var periodBucketsByUser = periodBuckets
            .GroupBy(bucket => bucket.UserId)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var historyActionsByUser = allHistoryActions
            .GroupBy(action => action.UserId)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var periodActionsByUser = periodActions
            .GroupBy(action => action.UserId)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var inactivityByUser = new Dictionary<string, UserInactivityState>(StringComparer.Ordinal);
        var rows = users.Select(user =>
        {
            var userHistoryBuckets = historyBucketsByUser.GetValueOrDefault(user.Id) ?? new List<BucketProjection>();
            var userPeriodBuckets = periodBucketsByUser.GetValueOrDefault(user.Id) ?? new List<BucketProjection>();
            var userHistoryActions = historyActionsByUser.GetValueOrDefault(user.Id) ?? new List<ActionProjection>();
            var userPeriodActions = periodActionsByUser.GetValueOrDefault(user.Id) ?? new List<ActionProjection>();
            var periodActionDates = userPeriodActions
                .Select(action => DateOnly.FromDateTime(IstClock.ToIst(action.TimeUtc).Date))
                .ToHashSet();
            var historyActionDates = userHistoryActions
                .Select(action => DateOnly.FromDateTime(IstClock.ToIst(action.TimeUtc).Date))
                .ToHashSet();
            var periodActivityDates = userPeriodBuckets
                .Select(bucket => bucket.ActivityDateIst)
                .Concat(periodActionDates)
                .ToHashSet();
            var historyActivityDates = userHistoryBuckets
                .Select(bucket => bucket.ActivityDateIst)
                .Concat(historyActionDates)
                .ToHashSet();
            var interactiveDates = userPeriodBuckets
                .Where(bucket => bucket.HadInteractiveHeartbeat)
                .Select(bucket => bucket.ActivityDateIst)
                .ToHashSet();
            var navigationDates = userPeriodBuckets
                .Where(bucket => bucket.HadNavigation)
                .Select(bucket => bucket.ActivityDateIst)
                .ToHashSet();
            var actionDates = periodActionDates;

            var createdDate = DateOnly.FromDateTime(IstClock.ToIst(user.CreatedUtc).Date);
            var availableWorkingDays = periodWorkingDays
                .Where(date => date >= createdDate)
                .ToArray();
            var activeWorkingDays = availableWorkingDays.Count(periodActivityDates.Contains);
            var percentage = availableWorkingDays.Length == 0
                ? 0
                : (int)Math.Round(
                    activeWorkingDays * 100d / availableWorkingDays.Length,
                    MidpointRounding.AwayFromZero);
            var lastActive = lastActiveByUser.GetValueOrDefault(user.Id);
            var distinctBuckets = userPeriodBuckets
                .Select(bucket => bucket.BucketStartUtc)
                .Distinct()
                .Count();
            var moduleLabels = userPeriodBuckets
                .Select(bucket => _modules.Find(bucket.ModuleKey)?.Label ?? bucket.ModuleKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var eligibleSevenWorkingDays = lastSevenWorkingDays
                .Where(date => date >= createdDate)
                .ToArray();
            var hasFullSevenWorkingDayExposure = eligibleSevenWorkingDays.Length == 7;
            var noUsageSevenWorkingDays = hasFullSevenWorkingDayExposure
                && !eligibleSevenWorkingDays.Any(historyActivityDates.Contains);
            var noUsageThirtyDays = createdDate <= thirtyCalendarCutoff
                && (!lastActive.HasValue
                    || DateOnly.FromDateTime(IstClock.ToIst(lastActive.Value).Date) < thirtyCalendarCutoff);
            inactivityByUser[user.Id] = new UserInactivityState(
                noUsageSevenWorkingDays,
                noUsageThirtyDays);

            var posture = ResolvePosture(
                lastActive,
                noUsageSevenWorkingDays,
                percentage,
                options.RegularUserThresholdPercent);

            var heatmap = heatmapDates.Select(date =>
            {
                if (!configuredWorkingDays.Contains(date.DayOfWeek) || nonWorkingDates.Contains(date))
                {
                    return new ErpUsageHeatmapCell(date, ErpUsageHeatmapState.NonWorkingDay, "Non-working day");
                }

                if (actionDates.Contains(date))
                {
                    return new ErpUsageHeatmapCell(date, ErpUsageHeatmapState.BusinessAction, "Recorded operational action");
                }

                if (interactiveDates.Contains(date))
                {
                    return new ErpUsageHeatmapCell(date, ErpUsageHeatmapState.Interactive, "Interactive ERP use");
                }

                if (navigationDates.Contains(date))
                {
                    return new ErpUsageHeatmapCell(date, ErpUsageHeatmapState.Navigation, "ERP navigation");
                }

                return new ErpUsageHeatmapCell(date, ErpUsageHeatmapState.NoActivity, "No recorded use");
            }).ToArray();

            return new ErpUsageUserRow(
                user.Id,
                user.UserName,
                user.FullName,
                user.Rank,
                rolesByUser.GetValueOrDefault(user.Id) ?? Array.Empty<string>(),
                AccountState(user),
                periodActivityDates.Contains(today),
                lastActive,
                activeWorkingDays,
                availableWorkingDays.Length,
                percentage,
                distinctBuckets * options.BucketMinutes,
                moduleLabels,
                userPeriodActions.Count,
                posture.Label,
                posture.Tone,
                heatmap);
        }).ToList();

        if (!string.IsNullOrWhiteSpace(query.Posture))
        {
            var posture = query.Posture.Trim();
            rows = rows
                .Where(row => NormalisePostureKey(row.Posture) == NormalisePostureKey(posture))
                .ToList();
        }

        rows = rows
            .OrderByDescending(row => row.UsedToday)
            .ThenByDescending(row => row.ActivePercentage)
            .ThenByDescending(row => row.LastActiveUtc)
            .ThenBy(row => row.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var displayedUserIds = rows.Select(row => row.UserId).ToHashSet(StringComparer.Ordinal);
        var activeAccountRows = rows
            .Where(row => string.Equals(row.AccountState, "Active", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var summary = new ErpUsageSummary(
            activeAccountRows.Length,
            activeAccountRows.Count(row => row.UsedToday),
            activeAccountRows.Count(row => row.Posture == "Regular user"),
            activeAccountRows.Count(row => row.Posture == "Occasional user"),
            activeAccountRows.Count(row => inactivityByUser.GetValueOrDefault(row.UserId)?.NoUsageSevenWorkingDays == true),
            activeAccountRows.Count(row => inactivityByUser.GetValueOrDefault(row.UserId)?.NoUsageThirtyDays == true),
            activeAccountRows.Count(row => row.RecordedActionCount > 0));

        var moduleSummary = periodBuckets
            .Where(bucket => displayedUserIds.Contains(bucket.UserId))
            .GroupBy(bucket => bucket.ModuleKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var descriptor = _modules.Find(group.Key);
                return new ErpUsageModuleSummary(
                    group.Key,
                    descriptor?.Label ?? group.Key,
                    descriptor?.Icon ?? "bi-grid",
                    group.Select(bucket => bucket.UserId).Distinct(StringComparer.Ordinal).Count(),
                    group.Select(bucket => new { bucket.UserId, bucket.BucketStartUtc }).Distinct().Count());
            })
            .OrderByDescending(module => module.ActiveBucketCount)
            .ThenBy(module => module.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var totalUsers = rows.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalUsers / (double)pageSize));
        page = Math.Min(page, totalPages);
        var pagedRows = rows.Skip((page - 1) * pageSize).Take(pageSize).ToArray();

        return new ErpUsageResult(
            startDate,
            today,
            days,
            options.RegularUserThresholdPercent,
            summary,
            moduleSummary,
            roleOptions,
            pagedRows,
            totalUsers,
            page,
            pageSize,
            totalPages,
            heatmapDates);
    }

    public async Task<ErpUsageCommandSummary> GetCommandSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        var options = _options.Value;
        var today = _time.TodayIst;
        var startDate = today.AddDays(-29);
        var historyStartDate = EarlierOf(startDate, today.AddDays(-(MinimumInactivityHistoryDays - 1)));
        var historyStartUtc = _time.StartOfIstDayUtc(historyStartDate).UtcDateTime;
        var endUtc = _time.EndExclusiveOfIstDayUtc(today).UtcDateTime;

        var users = await _db.Users
            .AsNoTracking()
            .Where(user => !user.IsDisabled && !user.PendingDeletion)
            .Select(user => new CommandUserProjection(user.Id, user.CreatedUtc))
            .ToListAsync(cancellationToken);
        if (users.Count == 0)
        {
            return new ErpUsageCommandSummary(0, 0, 0, 0);
        }

        var userIds = users.Select(user => user.Id).ToArray();
        var bucketDays = await _db.UserActivityBuckets
            .AsNoTracking()
            .Where(bucket =>
                userIds.Contains(bucket.UserId)
                && bucket.ActivityDateIst >= historyStartDate
                && bucket.ActivityDateIst <= today)
            .Select(bucket => new ActivityDayProjection(bucket.UserId, bucket.ActivityDateIst))
            .Distinct()
            .ToListAsync(cancellationToken);
        var actionRows = await OperationalAuditQuery()
            .Where(audit =>
                audit.UserId != null
                && userIds.Contains(audit.UserId)
                && audit.TimeUtc >= historyStartUtc
                && audit.TimeUtc < endUtc)
            .Select(audit => new ActionProjection(audit.UserId!, audit.TimeUtc, audit.Action))
            .ToListAsync(cancellationToken);

        var activityDatesByUser = bucketDays
            .GroupBy(row => row.UserId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => row.Date).ToHashSet(),
                StringComparer.Ordinal);
        foreach (var action in actionRows)
        {
            if (!activityDatesByUser.TryGetValue(action.UserId, out var dates))
            {
                dates = new HashSet<DateOnly>();
                activityDatesByUser[action.UserId] = dates;
            }
            dates.Add(DateOnly.FromDateTime(IstClock.ToIst(action.TimeUtc).Date));
        }

        var nonWorkingDates = await _officeCalendar.GetNonWorkingDatesAsync(
            historyStartDate,
            today.AddDays(1),
            cancellationToken);
        var configuredWorkingDays = options.WorkingDays.ToHashSet();
        var historyWorkingDays = EnumerateDates(historyStartDate, today)
            .Where(date => configuredWorkingDays.Contains(date.DayOfWeek) && !nonWorkingDates.Contains(date))
            .ToArray();
        var periodWorkingDays = historyWorkingDays.Where(date => date >= startDate).ToArray();
        var lastSevenWorkingDays = historyWorkingDays.TakeLast(7).ToArray();

        var activeToday = 0;
        var regularUsers = 0;
        var noUsageSevenWorkingDays = 0;
        foreach (var user in users)
        {
            var createdDate = DateOnly.FromDateTime(IstClock.ToIst(user.CreatedUtc).Date);
            var dates = activityDatesByUser.GetValueOrDefault(user.Id) ?? new HashSet<DateOnly>();
            if (dates.Contains(today))
            {
                activeToday++;
            }

            var availableDays = periodWorkingDays.Where(date => date >= createdDate).ToArray();
            var activeDays = availableDays.Count(dates.Contains);
            var activePercentage = availableDays.Length == 0
                ? 0
                : (int)Math.Round(activeDays * 100d / availableDays.Length, MidpointRounding.AwayFromZero);
            if (activePercentage >= options.RegularUserThresholdPercent)
            {
                regularUsers++;
            }

            var exposedSevenDays = lastSevenWorkingDays.Where(date => date >= createdDate).ToArray();
            if (exposedSevenDays.Length == 7 && !exposedSevenDays.Any(dates.Contains))
            {
                noUsageSevenWorkingDays++;
            }
        }

        return new ErpUsageCommandSummary(
            users.Count,
            activeToday,
            regularUsers,
            noUsageSevenWorkingDays);
    }

    private IQueryable<AuditLog> OperationalAuditQuery() =>
        _db.AuditLogs
            .AsNoTracking()
            .Where(audit =>
                !audit.Action.StartsWith("Login")
                && !audit.Action.StartsWith("Auth")
                && !audit.Action.StartsWith("Password")
                && !audit.Action.StartsWith("UserActivity")
                && !audit.Action.StartsWith("ErpUsage")
                && !audit.Action.StartsWith("SystemHealth"));

    private static (string Label, string Tone) ResolvePosture(
        DateTime? lastActiveUtc,
        bool noUsageSevenWorkingDays,
        int percentage,
        int regularThreshold)
    {
        if (!lastActiveUtc.HasValue)
        {
            return ("No usage", "danger");
        }

        if (noUsageSevenWorkingDays)
        {
            return ("Inactive", "warning");
        }

        return percentage >= regularThreshold
            ? ("Regular user", "success")
            : ("Occasional user", "info");
    }

    private static string AccountState(UserProjection user) =>
        user.PendingDeletion
            ? "Pending deletion"
            : user.IsDisabled
                ? "Disabled"
                : "Active";

    private static int NormaliseDays(int requested, int maximum)
    {
        var allowedMaximum = Math.Clamp(maximum, 7, 365);
        return requested switch
        {
            <= 7 => 7,
            <= 30 => Math.Min(30, allowedMaximum),
            <= 60 => Math.Min(60, allowedMaximum),
            _ => Math.Min(90, allowedMaximum)
        };
    }

    private static DateOnly EarlierOf(DateOnly first, DateOnly second) =>
        first <= second ? first : second;

    private static IEnumerable<DateOnly> EnumerateDates(DateOnly start, DateOnly endInclusive)
    {
        for (var date = start; date <= endInclusive; date = date.AddDays(1))
        {
            yield return date;
        }
    }

    private static string NormalisePostureKey(string value) =>
        value.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

    private sealed record UserProjection(
        string Id,
        string UserName,
        string FullName,
        string Rank,
        DateTime CreatedUtc,
        bool IsDisabled,
        bool PendingDeletion);

    private sealed record BucketProjection(
        string UserId,
        DateOnly ActivityDateIst,
        DateTime BucketStartUtc,
        string ModuleKey,
        bool HadNavigation,
        bool HadInteractiveHeartbeat,
        DateTime LastSeenUtc);

    private sealed record ActionProjection(
        string UserId,
        DateTime TimeUtc,
        string Action);

    private sealed record LastActiveProjection(
        string UserId,
        DateTime LastActiveUtc);

    private sealed record CommandUserProjection(
        string Id,
        DateTime CreatedUtc);

    private sealed record ActivityDayProjection(
        string UserId,
        DateOnly Date);

    private sealed record UserInactivityState(
        bool NoUsageSevenWorkingDays,
        bool NoUsageThirtyDays);
}
