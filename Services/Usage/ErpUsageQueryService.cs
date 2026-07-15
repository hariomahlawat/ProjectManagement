using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Scheduling;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Services.Usage;

public sealed class ErpUsageQuery
{
    public int Days { get; init; } = 30;
    public string? Search { get; init; }
    public string? Role { get; init; }
    public string? Module { get; init; }
    public string? Posture { get; init; }
    public string? QuickFilter { get; init; }
    public bool IncludeDisabledAccounts { get; init; }
    public bool IncludeNonHumanAccounts { get; init; }

    // Backward-compatible query-string support. New UI uses the explicit scope toggles above.
    public string? AccountState { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}

public sealed record ErpUsageSummary(
    int UserCount,
    int ActiveToday,
    int UsersWithMonitoredUse,
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
    AdministrativeAction = 4,
    NonWorkingDay = 5,
    NotObserved = 6
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
    UserAccountKind AccountKind,
    DateOnly EffectiveTrackingStart,
    bool UsedToday,
    DateTime? LastActiveUtc,
    int ActiveWorkingDays,
    int AvailableWorkingDays,
    int ActivePercentage,
    int ApproximateActiveMinutes,
    IReadOnlyList<string> Modules,
    int OperationalActionCount,
    int AdministrativeActionCount,
    string Posture,
    string PostureTone,
    IReadOnlyList<ErpUsageHeatmapCell> Heatmap);

public sealed record ErpUsageResult(
    DateOnly StartDate,
    DateOnly EndDate,
    int Days,
    int RegularThresholdPercent,
    DateTimeOffset TrackingInceptionUtc,
    int TrackingWorkingDays,
    bool RegularClassificationAvailable,
    bool SevenDayReviewAvailable,
    bool ThirtyDayReviewAvailable,
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
    int NoUsageSevenWorkingDays,
    bool RegularClassificationAvailable,
    bool SevenDayReviewAvailable);

public interface IErpUsageQueryService
{
    Task<ErpUsageResult> GetAsync(
        ErpUsageQuery query,
        CancellationToken cancellationToken = default);

    Task<ErpUsageCommandSummary> GetCommandSummaryAsync(
        CancellationToken cancellationToken = default);

    Task<ErpActivityStripVm> GetActivityStripAsync(
        string userId,
        int days = 14,
        CancellationToken cancellationToken = default);
}

public sealed class ErpUsageQueryService : IErpUsageQueryService
{
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
        var nowUtc = _time.UtcNow;
        var trackingInceptionUtc = options.TrackingInceptionUtc.ToUniversalTime();
        var trackingInceptionDate = DateOnly.FromDateTime(_time.ToIst(trackingInceptionUtc).DateTime);
        var today = _time.TodayIst;
        var days = NormaliseDays(query.Days, options.MaximumLookbackDays);
        var selectedStartDate = today.AddDays(-(days - 1));
        var historyStartDate = EarlierOf(selectedStartDate, trackingInceptionDate);
        var selectedStartUtc = _time.StartOfIstDayUtc(selectedStartDate).UtcDateTime;
        var historyStartUtc = _time.StartOfIstDayUtc(historyStartDate).UtcDateTime;
        var endDateExclusive = today.AddDays(1);
        var endUtc = _time.EndExclusiveOfIstDayUtc(today).UtcDateTime;
        var trackingInceptionDateTimeUtc = trackingInceptionUtc.UtcDateTime;
        var pageSize = Math.Clamp(query.PageSize, 10, 100);
        var page = Math.Max(1, query.Page);

        var requestedAccountState = query.AccountState?.Trim();
        var explicitlyRequestingDisabled = string.Equals(
            requestedAccountState,
            "Disabled",
            StringComparison.OrdinalIgnoreCase);
        var includeDisabled = query.IncludeDisabledAccounts || explicitlyRequestingDisabled;

        IQueryable<ApplicationUser> userEntityQuery = _db.Users
            .AsNoTracking()
            .Where(user => !user.PendingDeletion);

        if (!includeDisabled)
        {
            userEntityQuery = userEntityQuery.Where(user => !user.IsDisabled);
        }

        if (!query.IncludeNonHumanAccounts)
        {
            userEntityQuery = userEntityQuery.Where(user => user.AccountKind == UserAccountKind.Human);
        }

        if (string.Equals(requestedAccountState, "Active", StringComparison.OrdinalIgnoreCase))
        {
            userEntityQuery = userEntityQuery.Where(user => !user.IsDisabled);
        }
        else if (explicitlyRequestingDisabled)
        {
            userEntityQuery = userEntityQuery.Where(user => user.IsDisabled);
        }

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
                user.PendingDeletion,
                user.AccountKind))
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

        var userIds = users.Select(user => user.Id).ToArray();
        var allTrackedBuckets = userIds.Length == 0 || nowUtc < trackingInceptionUtc
            ? new List<BucketProjection>()
            : await _db.UserActivityBuckets
                .AsNoTracking()
                .Where(bucket =>
                    userIds.Contains(bucket.UserId)
                    && bucket.ActivityDateIst >= historyStartDate
                    && bucket.ActivityDateIst < endDateExclusive
                    && bucket.BucketStartUtc >= trackingInceptionDateTimeUtc)
                .Select(bucket => new BucketProjection(
                    bucket.UserId,
                    bucket.ActivityDateIst,
                    bucket.BucketStartUtc,
                    bucket.ModuleKey,
                    bucket.HadNavigation,
                    bucket.HadInteractiveHeartbeat,
                    bucket.LastSeenUtc))
                .ToListAsync(cancellationToken);

        var selectedBuckets = allTrackedBuckets
            .Where(bucket => bucket.ActivityDateIst >= selectedStartDate)
            .ToList();

        if (!string.IsNullOrWhiteSpace(query.Module))
        {
            var module = query.Module.Trim();
            var usersInModule = selectedBuckets
                .Where(bucket => string.Equals(bucket.ModuleKey, module, StringComparison.OrdinalIgnoreCase))
                .Select(bucket => bucket.UserId)
                .ToHashSet(StringComparer.Ordinal);

            users = users.Where(user => usersInModule.Contains(user.Id)).ToList();
            userIds = users.Select(user => user.Id).ToArray();
            var selectedIds = userIds.ToHashSet(StringComparer.Ordinal);
            allTrackedBuckets = allTrackedBuckets.Where(bucket => selectedIds.Contains(bucket.UserId)).ToList();
            selectedBuckets = selectedBuckets.Where(bucket => selectedIds.Contains(bucket.UserId)).ToList();
        }

        var lastActiveByUser = new Dictionary<string, DateTime?>(StringComparer.Ordinal);
        if (userIds.Length > 0)
        {
            if (nowUtc >= trackingInceptionUtc)
            {
                var lastBucketRows = await _db.UserActivityBuckets
                    .AsNoTracking()
                    .Where(bucket =>
                        userIds.Contains(bucket.UserId)
                        && bucket.BucketStartUtc >= trackingInceptionDateTimeUtc)
                    .GroupBy(bucket => bucket.UserId)
                    .Select(group => new LastActiveProjection(
                        group.Key,
                        group.Max(bucket => bucket.LastSeenUtc)))
                    .ToListAsync(cancellationToken);

                foreach (var row in lastBucketRows)
                {
                    lastActiveByUser[row.UserId] = row.LastActiveUtc;
                }
            }

            var lastActionRows = await CandidateAuditQuery()
                .Where(audit => audit.UserId != null && userIds.Contains(audit.UserId))
                .GroupBy(audit => audit.UserId!)
                .Select(group => new LastActiveProjection(
                    group.Key,
                    group.Max(audit => audit.TimeUtc)))
                .ToListAsync(cancellationToken);

            foreach (var row in lastActionRows)
            {
                var existing = lastActiveByUser.GetValueOrDefault(row.UserId);
                lastActiveByUser[row.UserId] = LaterOf(existing, row.LastActiveUtc);
            }
        }

        var rawActions = userIds.Length == 0
            ? new List<RawActionProjection>()
            : await CandidateAuditQuery()
                .Where(audit =>
                    audit.UserId != null
                    && userIds.Contains(audit.UserId)
                    && audit.TimeUtc >= historyStartUtc
                    && audit.TimeUtc < endUtc)
                .Select(audit => new RawActionProjection(audit.UserId!, audit.TimeUtc, audit.Action))
                .ToListAsync(cancellationToken);

        var allActions = rawActions
            .Select(action => new ActionProjection(
                action.UserId,
                action.TimeUtc,
                action.Action,
                ErpUsageActionClassifier.Classify(action.Action)))
            .Where(action => action.Kind != ErpUsageActionKind.Ignored)
            .ToList();
        var selectedActions = allActions.Where(action => action.TimeUtc >= selectedStartUtc).ToList();
        var trackedActions = allActions
            .Where(action => action.TimeUtc >= trackingInceptionDateTimeUtc)
            .ToList();

        var nonWorkingDates = await _officeCalendar.GetNonWorkingDatesAsync(
            historyStartDate,
            endDateExclusive,
            cancellationToken);
        var configuredWorkingDays = options.WorkingDays.ToHashSet();
        var allWorkingDays = EnumerateDates(historyStartDate, today)
            .Where(date => configuredWorkingDays.Contains(date.DayOfWeek) && !nonWorkingDates.Contains(date))
            .ToArray();
        var trackingWorkingDays = nowUtc < trackingInceptionUtc
            ? Array.Empty<DateOnly>()
            : allWorkingDays.Where(date => date >= trackingInceptionDate).ToArray();
        var selectedWorkingDays = allWorkingDays.Where(date => date >= selectedStartDate).ToArray();
        var lastSevenTrackingWorkingDays = trackingWorkingDays.TakeLast(7).ToArray();
        var trackingWorkingDayCount = trackingWorkingDays.Length;
        var regularClassificationAvailable = trackingWorkingDayCount >= 7;
        var sevenDayReviewAvailable = regularClassificationAvailable;
        var thirtyDayReviewAvailable = nowUtc >= trackingInceptionUtc
            && today >= trackingInceptionDate.AddDays(29);
        var thirtyCalendarCutoff = today.AddDays(-29);
        var heatmapDates = EnumerateDates(days > 30 ? today.AddDays(-29) : selectedStartDate, today).ToArray();

        var trackedBucketsByUser = allTrackedBuckets
            .GroupBy(bucket => bucket.UserId)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var selectedBucketsByUser = selectedBuckets
            .GroupBy(bucket => bucket.UserId)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var selectedActionsByUser = selectedActions
            .GroupBy(action => action.UserId)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var trackedActionsByUser = trackedActions
            .GroupBy(action => action.UserId)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var inactivityByUser = new Dictionary<string, UserInactivityState>(StringComparer.Ordinal);
        var trackedActivityByUser = new Dictionary<string, bool>(StringComparer.Ordinal);

        var rows = users.Select(user =>
        {
            var userTrackedBuckets = trackedBucketsByUser.GetValueOrDefault(user.Id) ?? new List<BucketProjection>();
            var userSelectedBuckets = selectedBucketsByUser.GetValueOrDefault(user.Id) ?? new List<BucketProjection>();
            var userSelectedActions = selectedActionsByUser.GetValueOrDefault(user.Id) ?? new List<ActionProjection>();
            var userTrackedActions = trackedActionsByUser.GetValueOrDefault(user.Id) ?? new List<ActionProjection>();

            var createdDate = DateOnly.FromDateTime(_time.ToIst(user.CreatedUtc).Date);
            var effectiveTrackingStart = LatestOf(selectedStartDate, trackingInceptionDate, createdDate);
            var trackedActivityDates = userTrackedBuckets
                .Select(bucket => bucket.ActivityDateIst)
                .Concat(userTrackedActions.Select(ToIstDate))
                .ToHashSet();
            var selectedTrackedActivityDates = trackedActivityDates
                .Where(date => date >= selectedStartDate)
                .ToHashSet();
            var hasTrackedActivity = trackedActivityDates.Count > 0;
            trackedActivityByUser[user.Id] = hasTrackedActivity;

            var availableWorkingDays = selectedWorkingDays
                .Where(date => date >= effectiveTrackingStart)
                .ToArray();
            var activeWorkingDays = availableWorkingDays.Count(selectedTrackedActivityDates.Contains);
            var activePercentage = availableWorkingDays.Length == 0
                ? 0
                : (int)Math.Round(
                    activeWorkingDays * 100d / availableWorkingDays.Length,
                    MidpointRounding.AwayFromZero);

            var userRegularClassificationAvailable = regularClassificationAvailable
                && availableWorkingDays.Length >= 7;
            var exposedSevenDays = lastSevenTrackingWorkingDays
                .Where(date => date >= createdDate)
                .ToArray();
            var noUsageSevenWorkingDays = sevenDayReviewAvailable
                && exposedSevenDays.Length == 7
                && !exposedSevenDays.Any(trackedActivityDates.Contains);
            var noUsageThirtyDays = thirtyDayReviewAvailable
                && LatestOf(trackingInceptionDate, createdDate) <= thirtyCalendarCutoff
                && !trackedActivityDates.Any(date => date >= thirtyCalendarCutoff);
            inactivityByUser[user.Id] = new UserInactivityState(
                noUsageSevenWorkingDays,
                noUsageThirtyDays);

            var posture = ResolvePosture(
                hasTrackedActivity,
                noUsageSevenWorkingDays,
                activePercentage,
                options.RegularUserThresholdPercent,
                userRegularClassificationAvailable);

            var lastActive = lastActiveByUser.GetValueOrDefault(user.Id);
            var distinctBuckets = userSelectedBuckets
                .Select(bucket => bucket.BucketStartUtc)
                .Distinct()
                .Count();
            var moduleLabels = userSelectedBuckets
                .Select(bucket => _modules.Find(bucket.ModuleKey)?.Label ?? bucket.ModuleKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var operationalActionCount = userSelectedActions.Count(action => action.Kind == ErpUsageActionKind.Operational);
            var administrativeActionCount = userSelectedActions.Count(action => action.Kind == ErpUsageActionKind.Administrative);

            var heatmap = BuildHeatmap(
                heatmapDates,
                user,
                effectiveTrackingStart,
                userSelectedBuckets,
                userSelectedActions,
                configuredWorkingDays,
                nonWorkingDates);

            return new ErpUsageUserRow(
                user.Id,
                user.UserName,
                user.FullName,
                user.Rank,
                rolesByUser.GetValueOrDefault(user.Id) ?? Array.Empty<string>(),
                AccountState(user),
                user.AccountKind,
                effectiveTrackingStart,
                trackedActivityDates.Contains(today),
                lastActive,
                activeWorkingDays,
                availableWorkingDays.Length,
                activePercentage,
                distinctBuckets * options.BucketMinutes,
                moduleLabels,
                operationalActionCount,
                administrativeActionCount,
                posture.Label,
                posture.Tone,
                heatmap);
        }).ToList();

        if (!string.IsNullOrWhiteSpace(query.Posture))
        {
            var posture = NormaliseKey(query.Posture);
            rows = rows.Where(row => NormaliseKey(row.Posture) == posture).ToList();
        }

        rows = ApplyQuickFilter(rows, query.QuickFilter, inactivityByUser, trackedActivityByUser);
        rows = regularClassificationAvailable
            ? rows
                .OrderBy(row => PostureSortOrder(row.Posture))
                .ThenBy(row => row.LastActiveUtc ?? DateTime.MinValue)
                .ThenBy(row => row.FullName, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : rows
                .OrderBy(row => row.Posture == "Use recorded" ? 0 : 1)
                .ThenByDescending(row => row.OperationalActionCount)
                .ThenByDescending(row => row.LastActiveUtc ?? DateTime.MinValue)
                .ThenBy(row => row.FullName, StringComparer.OrdinalIgnoreCase)
                .ToList();

        var effectiveUserIds = rows.Select(row => row.UserId).ToHashSet(StringComparer.Ordinal);
        var summary = new ErpUsageSummary(
            rows.Count,
            rows.Count(row => row.UsedToday),
            rows.Count(row => trackedActivityByUser.GetValueOrDefault(row.UserId)),
            regularClassificationAvailable ? rows.Count(row => row.Posture == "Regular user") : 0,
            regularClassificationAvailable ? rows.Count(row => row.Posture == "Occasional user") : 0,
            sevenDayReviewAvailable
                ? rows.Count(row => inactivityByUser.GetValueOrDefault(row.UserId)?.NoUsageSevenWorkingDays == true)
                : 0,
            thirtyDayReviewAvailable
                ? rows.Count(row => inactivityByUser.GetValueOrDefault(row.UserId)?.NoUsageThirtyDays == true)
                : 0,
            rows.Count(row => row.OperationalActionCount > 0));

        var moduleSummary = selectedBuckets
            .Where(bucket => effectiveUserIds.Contains(bucket.UserId))
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
            selectedStartDate,
            today,
            days,
            options.RegularUserThresholdPercent,
            trackingInceptionUtc,
            trackingWorkingDayCount,
            regularClassificationAvailable,
            sevenDayReviewAvailable,
            thirtyDayReviewAvailable,
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
        var result = await GetAsync(
            new ErpUsageQuery
            {
                Days = 30,
                Page = 1,
                PageSize = 10,
                IncludeDisabledAccounts = false,
                IncludeNonHumanAccounts = false
            },
            cancellationToken);

        return new ErpUsageCommandSummary(
            result.Summary.UserCount,
            result.Summary.ActiveToday,
            result.Summary.RegularUsers,
            result.Summary.NoUsageSevenWorkingDays,
            result.RegularClassificationAvailable,
            result.SevenDayReviewAvailable);
    }

    public async Task<ErpActivityStripVm> GetActivityStripAsync(
        string userId,
        int days = 14,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("A user id is required.", nameof(userId));
        }

        var displayDays = Math.Clamp(days, 7, 31);
        var today = _time.TodayIst;
        var startDate = today.AddDays(-(displayDays - 1));
        var dates = EnumerateDates(startDate, today).ToArray();
        var options = _options.Value;
        var trackingInceptionUtc = options.TrackingInceptionUtc.ToUniversalTime();
        var trackingInceptionDate = DateOnly.FromDateTime(_time.ToIst(trackingInceptionUtc).DateTime);
        var endUtc = _time.EndExclusiveOfIstDayUtc(today).UtcDateTime;
        var startUtc = _time.StartOfIstDayUtc(startDate).UtcDateTime;

        var user = await _db.Users
            .AsNoTracking()
            .Where(candidate => candidate.Id == userId)
            .Select(candidate => new UserProjection(
                candidate.Id,
                candidate.UserName ?? string.Empty,
                candidate.FullName,
                candidate.Rank,
                candidate.CreatedUtc,
                candidate.IsDisabled,
                candidate.PendingDeletion,
                candidate.AccountKind))
            .SingleOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            return new ErpActivityStripVm
            {
                StartDate = startDate,
                EndDate = today,
                Days = dates.Select(date => new ErpActivityDayVm(
                    date,
                    0,
                    false,
                    false,
                    false,
                    $"{date:dd MMM yyyy}: User account unavailable",
                    date == today)).ToArray()
            };
        }

        var buckets = _time.UtcNow < trackingInceptionUtc
            ? new List<BucketProjection>()
            : await _db.UserActivityBuckets
                .AsNoTracking()
                .Where(bucket =>
                    bucket.UserId == userId
                    && bucket.ActivityDateIst >= startDate
                    && bucket.ActivityDateIst <= today
                    && bucket.BucketStartUtc >= trackingInceptionUtc.UtcDateTime)
                .Select(bucket => new BucketProjection(
                    bucket.UserId,
                    bucket.ActivityDateIst,
                    bucket.BucketStartUtc,
                    bucket.ModuleKey,
                    bucket.HadNavigation,
                    bucket.HadInteractiveHeartbeat,
                    bucket.LastSeenUtc))
                .ToListAsync(cancellationToken);

        var rawActions = await CandidateAuditQuery()
            .Where(audit =>
                audit.UserId == userId
                && audit.TimeUtc >= startUtc
                && audit.TimeUtc < endUtc)
            .Select(audit => new RawActionProjection(audit.UserId!, audit.TimeUtc, audit.Action))
            .ToListAsync(cancellationToken);

        var actions = rawActions
            .Select(action => new ActionProjection(
                action.UserId,
                action.TimeUtc,
                action.Action,
                ErpUsageActionClassifier.Classify(action.Action)))
            .Where(action => action.Kind != ErpUsageActionKind.Ignored)
            .ToList();

        var nonWorkingDates = await _officeCalendar.GetNonWorkingDatesAsync(
            startDate,
            today.AddDays(1),
            cancellationToken);
        var configuredWorkingDays = options.WorkingDays.ToHashSet();
        var createdDate = DateOnly.FromDateTime(_time.ToIst(user.CreatedUtc).Date);
        var effectiveTrackingStart = LatestOf(startDate, trackingInceptionDate, createdDate);
        var heatmap = BuildHeatmap(
            dates,
            user,
            effectiveTrackingStart,
            buckets,
            actions,
            configuredWorkingDays,
            nonWorkingDates);

        var dayRows = heatmap.Select(cell =>
        {
            var isWorkingDay = configuredWorkingDays.Contains(cell.Date.DayOfWeek)
                && !nonWorkingDates.Contains(cell.Date);
            var isHistoricalAudit = cell.Label.StartsWith(
                "Historical audited",
                StringComparison.OrdinalIgnoreCase);
            var isMonitored = _time.UtcNow >= trackingInceptionUtc
                && cell.Date >= effectiveTrackingStart;
            var level = cell.State switch
            {
                ErpUsageHeatmapState.Navigation => 1,
                ErpUsageHeatmapState.Interactive => 2,
                ErpUsageHeatmapState.AdministrativeAction => 2,
                ErpUsageHeatmapState.BusinessAction => 3,
                _ => 0
            };
            var tooltip = $"{cell.Date:ddd, dd MMM yyyy}\n{cell.Label.Replace("\n", "; ", StringComparison.Ordinal)}";

            return new ErpActivityDayVm(
                cell.Date,
                level,
                isWorkingDay,
                isMonitored,
                isHistoricalAudit,
                tooltip,
                cell.Date == today);
        }).ToArray();

        var monitoredWorkingDays = dayRows.Count(day => day.IsWorkingDay && day.IsMonitored);
        var activeWorkingDays = dayRows.Count(day => day.IsWorkingDay && day.IsMonitored && day.HasActivity);
        var lastActiveDate = dayRows
            .Where(day => day.HasActivity)
            .Select(day => (DateOnly?)day.Date)
            .LastOrDefault();

        return new ErpActivityStripVm
        {
            StartDate = startDate,
            EndDate = today,
            Days = dayRows,
            ActiveWorkingDays = activeWorkingDays,
            MonitoredWorkingDays = monitoredWorkingDays,
            LastActiveDate = lastActiveDate
        };
    }

    private IReadOnlyList<ErpUsageHeatmapCell> BuildHeatmap(
        IReadOnlyList<DateOnly> dates,
        UserProjection user,
        DateOnly effectiveTrackingStart,
        IReadOnlyList<BucketProjection> buckets,
        IReadOnlyList<ActionProjection> actions,
        IReadOnlySet<DayOfWeek> configuredWorkingDays,
        IReadOnlySet<DateOnly> nonWorkingDates)
    {
        var bucketsByDate = buckets
            .GroupBy(bucket => bucket.ActivityDateIst)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var actionsByDate = actions
            .GroupBy(ToIstDate)
            .ToDictionary(group => group.Key, group => group.ToArray());

        return dates.Select(date =>
        {
            var dateActions = actionsByDate.GetValueOrDefault(date) ?? Array.Empty<ActionProjection>();
            var dateBuckets = bucketsByDate.GetValueOrDefault(date) ?? Array.Empty<BucketProjection>();
            var operationalCount = dateActions.Count(action => action.Kind == ErpUsageActionKind.Operational);
            var administrativeCount = dateActions.Count(action => action.Kind == ErpUsageActionKind.Administrative);
            var trackedActionCount = dateActions.Count(action => action.TimeUtc >= _options.Value.TrackingInceptionUtc.UtcDateTime);
            var moduleLabels = dateBuckets
                .Select(bucket => _modules.Find(bucket.ModuleKey)?.Label ?? bucket.ModuleKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var beforeComprehensiveMonitoring = date < effectiveTrackingStart
                || (date == DateOnly.FromDateTime(_time.ToIst(_options.Value.TrackingInceptionUtc).DateTime)
                    && trackedActionCount == 0
                    && dateBuckets.Length == 0);

            if (operationalCount > 0)
            {
                var prefix = beforeComprehensiveMonitoring ? "Historical audited operational action" : "Operational action";
                return new ErpUsageHeatmapCell(
                    date,
                    ErpUsageHeatmapState.BusinessAction,
                    BuildSignalLabel(prefix, operationalCount, moduleLabels, beforeComprehensiveMonitoring));
            }

            if (administrativeCount > 0)
            {
                var prefix = beforeComprehensiveMonitoring ? "Historical audited administrative action" : "Administrative action";
                return new ErpUsageHeatmapCell(
                    date,
                    ErpUsageHeatmapState.AdministrativeAction,
                    BuildSignalLabel(prefix, administrativeCount, moduleLabels, beforeComprehensiveMonitoring));
            }

            if (date < effectiveTrackingStart)
            {
                var reason = date < DateOnly.FromDateTime(_time.ToIst(user.CreatedUtc).Date)
                    ? "Account not yet created"
                    : "Comprehensive usage monitoring not active";
                return new ErpUsageHeatmapCell(date, ErpUsageHeatmapState.NotObserved, reason);
            }

            if (dateBuckets.Any(bucket => bucket.HadInteractiveHeartbeat))
            {
                return new ErpUsageHeatmapCell(
                    date,
                    ErpUsageHeatmapState.Interactive,
                    BuildSignalLabel("Interactive ERP use", 0, moduleLabels, false));
            }

            if (dateBuckets.Any(bucket => bucket.HadNavigation))
            {
                return new ErpUsageHeatmapCell(
                    date,
                    ErpUsageHeatmapState.Navigation,
                    BuildSignalLabel("ERP navigation", 0, moduleLabels, false));
            }

            if (!configuredWorkingDays.Contains(date.DayOfWeek) || nonWorkingDates.Contains(date))
            {
                return new ErpUsageHeatmapCell(date, ErpUsageHeatmapState.NonWorkingDay, "Non-working day");
            }

            return new ErpUsageHeatmapCell(date, ErpUsageHeatmapState.NoActivity, "No recorded use");
        }).ToArray();
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

    private static List<ErpUsageUserRow> ApplyQuickFilter(
        List<ErpUsageUserRow> rows,
        string? quickFilter,
        IReadOnlyDictionary<string, UserInactivityState> inactivityByUser,
        IReadOnlyDictionary<string, bool> trackedActivityByUser)
    {
        return NormaliseKey(quickFilter) switch
        {
            "usedtoday" => rows.Where(row => row.UsedToday).ToList(),
            "nousein7workingdays" or "nouse7" => rows
                .Where(row => inactivityByUser.GetValueOrDefault(row.UserId)?.NoUsageSevenWorkingDays == true)
                .ToList(),
            "neverrecorded" or "norecordeduse" => rows
                .Where(row => !trackedActivityByUser.GetValueOrDefault(row.UserId))
                .ToList(),
            _ => rows
        };
    }

    private static (string Label, string Tone) ResolvePosture(
        bool hasTrackedActivity,
        bool noUsageSevenWorkingDays,
        int percentage,
        int regularThreshold,
        bool regularClassificationAvailable)
    {
        if (!regularClassificationAvailable)
        {
            return hasTrackedActivity
                ? ("Use recorded", "info")
                : ("Not yet recorded", "neutral");
        }

        if (!hasTrackedActivity)
        {
            return ("No recorded use", "danger");
        }

        if (noUsageSevenWorkingDays)
        {
            return ("No recent use", "warning");
        }

        if (regularClassificationAvailable && percentage >= regularThreshold)
        {
            return ("Regular user", "success");
        }

        return ("Occasional user", "info");
    }

    private static string BuildSignalLabel(
        string signal,
        int count,
        IReadOnlyCollection<string> modules,
        bool historical)
    {
        var parts = new List<string>
        {
            count > 1 ? $"{signal}s: {count}" : signal
        };
        if (modules.Count > 0)
        {
            parts.Add($"Modules: {string.Join(", ", modules)}");
        }
        if (historical)
        {
            parts.Add("Read-only navigation monitoring was not active");
        }
        return string.Join("\n", parts);
    }

    private static int PostureSortOrder(string posture) => posture switch
    {
        "No recent use" => 0,
        "No recorded use" => 1,
        "Not yet recorded" => 2,
        "Use recorded" => 3,
        "Occasional user" => 4,
        "Regular user" => 5,
        _ => 4
    };

    private static string AccountState(UserProjection user) =>
        user.PendingDeletion
            ? "Pending deletion"
            : user.IsDisabled
                ? "Disabled"
                : "Active";

    private DateOnly ToIstDate(ActionProjection action) =>
        DateOnly.FromDateTime(_time.ToIst(action.TimeUtc).Date);

    private static DateTime? LaterOf(DateTime? first, DateTime? second)
    {
        if (!first.HasValue) return second;
        if (!second.HasValue) return first;
        return first.Value >= second.Value ? first : second;
    }

    private static DateOnly LatestOf(params DateOnly[] dates) => dates.Max();

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

    private static string NormaliseKey(string? value) =>
        (value ?? string.Empty)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

    private sealed record UserProjection(
        string Id,
        string UserName,
        string FullName,
        string Rank,
        DateTime CreatedUtc,
        bool IsDisabled,
        bool PendingDeletion,
        UserAccountKind AccountKind);

    private sealed record BucketProjection(
        string UserId,
        DateOnly ActivityDateIst,
        DateTime BucketStartUtc,
        string ModuleKey,
        bool HadNavigation,
        bool HadInteractiveHeartbeat,
        DateTime LastSeenUtc);

    private sealed record LastActiveProjection(
        string UserId,
        DateTime LastActiveUtc);

    private sealed record RawActionProjection(
        string UserId,
        DateTime TimeUtc,
        string Action);

    private sealed record ActionProjection(
        string UserId,
        DateTime TimeUtc,
        string Action,
        ErpUsageActionKind Kind);

    private sealed record UserInactivityState(
        bool NoUsageSevenWorkingDays,
        bool NoUsageThirtyDays);
}
