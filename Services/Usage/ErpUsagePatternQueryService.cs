using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;

namespace ProjectManagement.Services.Usage;

public static class ErpUsagePatternSignalNames
{
    public const string All = "all";
    public const string Interactive = "interactive";
    public const string Operational = "operational";
}

public sealed class ErpUsagePatternQuery
{
    public int Days { get; init; } = 7;
    public string? UserId { get; init; }
    public string? Role { get; init; }
    public string? Module { get; init; }
    public string? Signal { get; init; }
}

public sealed record ErpUsagePatternOption(string Value, string Label);

public sealed record ErpUsagePatternPoint(
    long TimestampUtcMilliseconds,
    string TimestampIstLabel,
    string UserId,
    string DisplayName,
    string Rank,
    string UserName,
    string Signal,
    IReadOnlyList<string> Modules,
    int NavigationCount,
    int HeartbeatCount,
    int OperationalActionCount);

public sealed record ErpUsagePatternUserSummary(
    string UserId,
    string DisplayName,
    string Rank,
    string UserName,
    int ActiveDays,
    int ActivityIntervals,
    int InteractiveIntervals,
    int OperationalActionCount,
    IReadOnlyList<string> Modules,
    DateTime? LastActivityUtc);

public sealed record ErpUsagePatternResult(
    DateOnly StartDate,
    DateOnly EndDate,
    DateTimeOffset TrackingInceptionUtc,
    int RequestedDays,
    int AggregationMinutes,
    int TotalUsersInScope,
    int ActiveUsers,
    int ActivityIntervals,
    int InteractiveIntervals,
    int OperationalIntervals,
    int OperationalActionCount,
    int ModulesRepresented,
    IReadOnlyList<ErpUsagePatternOption> UserOptions,
    IReadOnlyList<ErpUsagePatternOption> RoleOptions,
    IReadOnlyList<ErpUsagePatternOption> ModuleOptions,
    IReadOnlyList<ErpUsagePatternPoint> Points,
    IReadOnlyList<ErpUsagePatternUserSummary> Users);

public interface IErpUsagePatternQueryService
{
    Task<ErpUsagePatternResult> GetAsync(
        ErpUsagePatternQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Produces a privacy-safe user-versus-time view of monitored ERP activity. Activity
/// buckets are consolidated before rendering so the chart shows meaningful usage
/// intervals rather than individual requests or heartbeat events.
/// </summary>
public sealed class ErpUsagePatternQueryService : IErpUsagePatternQueryService
{
    private static readonly int[] AllowedPeriods = [1, 7, 14, 30];

    private readonly ApplicationDbContext _db;
    private readonly IAdminTimeService _time;
    private readonly IErpUsageModuleCatalog _modules;
    private readonly IOptions<ErpUsageOptions> _options;

    public ErpUsagePatternQueryService(
        ApplicationDbContext db,
        IAdminTimeService time,
        IErpUsageModuleCatalog modules,
        IOptions<ErpUsageOptions> options)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _modules = modules ?? throw new ArgumentNullException(nameof(modules));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<ErpUsagePatternResult> GetAsync(
        ErpUsagePatternQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var options = _options.Value;
        var requestedDays = NormaliseDays(query.Days);
        var aggregationMinutes = requestedDays > 14 ? 30 : 15;
        var nowUtc = _time.UtcNow;
        var today = _time.TodayIst;
        var trackingInceptionUtc = options.TrackingInceptionUtc.ToUniversalTime();
        var trackingInceptionDate = DateOnly.FromDateTime(_time.ToIst(trackingInceptionUtc).DateTime);
        var requestedStart = today.AddDays(-(requestedDays - 1));
        var startDate = requestedStart < trackingInceptionDate ? trackingInceptionDate : requestedStart;
        var startUtc = LaterOf(_time.StartOfIstDayUtc(startDate), trackingInceptionUtc);
        var endUtcExclusive = _time.EndExclusiveOfIstDayUtc(today);

        var baseUsers = await _db.Users
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
                user.UserName ?? string.Empty))
            .ToListAsync(cancellationToken);

        var baseUserIds = baseUsers.Select(user => user.Id).ToArray();
        var roleRows = baseUserIds.Length == 0
            ? new List<RoleProjection>()
            : await (
                from userRole in _db.UserRoles.AsNoTracking()
                join role in _db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                where baseUserIds.Contains(userRole.UserId)
                select new RoleProjection(userRole.UserId, role.Name ?? string.Empty))
                .ToListAsync(cancellationToken);

        var rolesByUser = roleRows
            .Where(row => !string.IsNullOrWhiteSpace(row.RoleName))
            .GroupBy(row => row.UserId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => row.RoleName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                StringComparer.Ordinal);

        var roleOptions = roleRows
            .Select(row => row.RoleName)
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
            .Select(role => new ErpUsagePatternOption(role, role))
            .ToArray();

        var userOptions = baseUsers
            .Select(user => new ErpUsagePatternOption(
                user.Id,
                DisplayName(user)))
            .ToArray();

        var selectedUsers = baseUsers.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(query.UserId))
        {
            var selectedUserId = query.UserId.Trim();
            selectedUsers = selectedUsers.Where(user =>
                string.Equals(user.Id, selectedUserId, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            var selectedRole = query.Role.Trim();
            selectedUsers = selectedUsers.Where(user =>
                rolesByUser.TryGetValue(user.Id, out var roles)
                && roles.Contains(selectedRole, StringComparer.OrdinalIgnoreCase));
        }

        var users = selectedUsers.ToArray();
        var userMap = users.ToDictionary(user => user.Id, StringComparer.Ordinal);
        var userIds = users.Select(user => user.Id).ToArray();
        var moduleOptions = _modules.Modules
            .Select(module => new ErpUsagePatternOption(module.Key, module.Label))
            .ToArray();

        if (userIds.Length == 0 || nowUtc < trackingInceptionUtc || startDate > today)
        {
            return EmptyResult(
                startDate,
                today,
                trackingInceptionUtc,
                requestedDays,
                aggregationMinutes,
                users.Length,
                userOptions,
                roleOptions,
                moduleOptions);
        }

        var selectedModule = string.IsNullOrWhiteSpace(query.Module)
            ? null
            : query.Module.Trim().ToLowerInvariant();

        var bucketQuery = _db.UserActivityBuckets
            .AsNoTracking()
            .Where(bucket =>
                userIds.Contains(bucket.UserId)
                && bucket.BucketStartUtc >= startUtc.UtcDateTime
                && bucket.BucketStartUtc < endUtcExclusive.UtcDateTime
                && bucket.BucketStartUtc >= trackingInceptionUtc.UtcDateTime);

        if (selectedModule is not null)
        {
            bucketQuery = bucketQuery.Where(bucket => bucket.ModuleKey == selectedModule);
        }

        var buckets = await bucketQuery
            .Select(bucket => new BucketProjection(
                bucket.UserId,
                bucket.BucketStartUtc,
                bucket.LastSeenUtc,
                bucket.ModuleKey,
                bucket.HadNavigation,
                bucket.HadInteractiveHeartbeat,
                bucket.NavigationCount,
                bucket.HeartbeatCount))
            .ToListAsync(cancellationToken);

        var rawActions = await CandidateAuditQuery()
            .Where(audit =>
                audit.UserId != null
                && userIds.Contains(audit.UserId)
                && audit.TimeUtc >= startUtc.UtcDateTime
                && audit.TimeUtc < endUtcExclusive.UtcDateTime
                && audit.TimeUtc >= trackingInceptionUtc.UtcDateTime)
            .Select(audit => new RawActionProjection(
                audit.UserId!,
                audit.TimeUtc,
                audit.Action))
            .ToListAsync(cancellationToken);

        var operationalActions = rawActions
            .Where(action =>
                ErpUsageActionClassifier.Classify(action.Action)
                == ErpUsageActionKind.Operational)
            .ToArray();

        var aggregates = new Dictionary<IntervalKey, IntervalAggregate>();
        foreach (var bucket in buckets)
        {
            var intervalStart = FloorUtc(bucket.BucketStartUtc, aggregationMinutes);
            var key = new IntervalKey(bucket.UserId, intervalStart);
            if (!aggregates.TryGetValue(key, out var aggregate))
            {
                aggregate = new IntervalAggregate(bucket.UserId, intervalStart);
                aggregates.Add(key, aggregate);
            }

            aggregate.HadNavigation |= bucket.HadNavigation;
            aggregate.HadInteractiveHeartbeat |= bucket.HadInteractiveHeartbeat;
            if (bucket.LastSeenUtc > aggregate.LastActivityUtc)
            {
                aggregate.LastActivityUtc = bucket.LastSeenUtc;
            }
            aggregate.NavigationCount += bucket.NavigationCount;
            aggregate.HeartbeatCount += bucket.HeartbeatCount;
            if (!string.IsNullOrWhiteSpace(bucket.ModuleKey))
            {
                aggregate.ModuleKeys.Add(bucket.ModuleKey);
            }
        }

        foreach (var action in operationalActions)
        {
            var intervalStart = FloorUtc(action.TimeUtc, aggregationMinutes);
            var key = new IntervalKey(action.UserId, intervalStart);
            if (!aggregates.TryGetValue(key, out var aggregate))
            {
                if (selectedModule is not null)
                {
                    // Audit rows do not carry a stable module key. When a module filter is
                    // active, include an action only where a matching activity bucket exists.
                    continue;
                }

                aggregate = new IntervalAggregate(action.UserId, intervalStart);
                aggregates.Add(key, aggregate);
            }

            aggregate.OperationalActionCount++;
            if (action.TimeUtc > aggregate.LastActivityUtc)
            {
                aggregate.LastActivityUtc = action.TimeUtc;
            }
        }

        var requestedSignal = NormaliseSignal(query.Signal);
        var filteredAggregates = aggregates.Values
            .Where(aggregate => requestedSignal switch
            {
                ErpUsagePatternSignalNames.Operational => aggregate.OperationalActionCount > 0,
                ErpUsagePatternSignalNames.Interactive =>
                    aggregate.HadInteractiveHeartbeat || aggregate.OperationalActionCount > 0,
                _ => true
            })
            .OrderBy(aggregate => aggregate.IntervalStartUtc)
            .ThenBy(aggregate => DisplayName(userMap[aggregate.UserId]), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var points = filteredAggregates
            .Select(aggregate => ToPoint(aggregate, userMap[aggregate.UserId], aggregationMinutes))
            .ToArray();

        var userSummaries = filteredAggregates
            .GroupBy(aggregate => aggregate.UserId, StringComparer.Ordinal)
            .Select(group =>
            {
                var user = userMap[group.Key];
                var rows = group.ToArray();
                return new ErpUsagePatternUserSummary(
                    user.Id,
                    DisplayName(user),
                    user.Rank,
                    user.UserName,
                    rows.Select(row => DateOnly.FromDateTime(_time.ToIst(row.IntervalStartUtc).Date))
                        .Distinct()
                        .Count(),
                    rows.Length,
                    rows.Count(row => row.HadInteractiveHeartbeat || row.OperationalActionCount > 0),
                    rows.Sum(row => row.OperationalActionCount),
                    rows.SelectMany(row => row.ModuleKeys)
                        .Select(ModuleLabel)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    rows.Max(row => (DateTime?)row.LastActivityUtc));
            })
            .OrderByDescending(user => user.ActivityIntervals)
            .ThenBy(user => user.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var modulesRepresented = filteredAggregates
            .SelectMany(aggregate => aggregate.ModuleKeys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return new ErpUsagePatternResult(
            startDate,
            today,
            trackingInceptionUtc,
            requestedDays,
            aggregationMinutes,
            users.Length,
            userSummaries.Length,
            points.Length,
            filteredAggregates.Count(row => row.HadInteractiveHeartbeat || row.OperationalActionCount > 0),
            filteredAggregates.Count(row => row.OperationalActionCount > 0),
            filteredAggregates.Sum(row => row.OperationalActionCount),
            modulesRepresented,
            userOptions,
            roleOptions,
            moduleOptions,
            points,
            userSummaries);
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

    private ErpUsagePatternPoint ToPoint(
        IntervalAggregate aggregate,
        UserProjection user,
        int aggregationMinutes)
    {
        var localStart = _time.ToIst(aggregate.IntervalStartUtc);
        var localEnd = localStart.AddMinutes(aggregationMinutes);
        var signal = aggregate.OperationalActionCount > 0
            ? ErpUsagePatternSignalNames.Operational
            : aggregate.HadInteractiveHeartbeat
                ? ErpUsagePatternSignalNames.Interactive
                : "navigation";

        return new ErpUsagePatternPoint(
            new DateTimeOffset(aggregate.IntervalStartUtc).ToUnixTimeMilliseconds(),
            $"{localStart:dd MMM yyyy · HH:mm}–{localEnd:HH:mm} IST",
            user.Id,
            DisplayName(user),
            user.Rank,
            user.UserName,
            signal,
            aggregate.ModuleKeys
                .Select(ModuleLabel)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            aggregate.NavigationCount,
            aggregate.HeartbeatCount,
            aggregate.OperationalActionCount);
    }

    private string ModuleLabel(string moduleKey) =>
        _modules.Find(moduleKey)?.Label ?? moduleKey;

    private static string DisplayName(UserProjection user)
    {
        var name = string.IsNullOrWhiteSpace(user.FullName)
            ? user.UserName
            : user.FullName.Trim();
        return string.IsNullOrWhiteSpace(user.Rank)
            ? name
            : $"{user.Rank.Trim()} {name}";
    }

    private static int NormaliseDays(int requested) =>
        AllowedPeriods.Contains(requested) ? requested : 7;

    private static string NormaliseSignal(string? signal) =>
        signal?.Trim().ToLowerInvariant() switch
        {
            ErpUsagePatternSignalNames.Interactive => ErpUsagePatternSignalNames.Interactive,
            ErpUsagePatternSignalNames.Operational => ErpUsagePatternSignalNames.Operational,
            _ => ErpUsagePatternSignalNames.All
        };

    private static DateTime FloorUtc(DateTime utc, int intervalMinutes)
    {
        var normalized = utc.Kind == DateTimeKind.Utc
            ? utc
            : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        var ticks = TimeSpan.FromMinutes(intervalMinutes).Ticks;
        return new DateTime(normalized.Ticks - normalized.Ticks % ticks, DateTimeKind.Utc);
    }

    private static DateTimeOffset LaterOf(DateTimeOffset first, DateTimeOffset second) =>
        first >= second ? first : second;

    private static ErpUsagePatternResult EmptyResult(
        DateOnly startDate,
        DateOnly endDate,
        DateTimeOffset trackingInceptionUtc,
        int requestedDays,
        int aggregationMinutes,
        int totalUsers,
        IReadOnlyList<ErpUsagePatternOption> userOptions,
        IReadOnlyList<ErpUsagePatternOption> roleOptions,
        IReadOnlyList<ErpUsagePatternOption> moduleOptions) =>
        new(
            startDate,
            endDate,
            trackingInceptionUtc,
            requestedDays,
            aggregationMinutes,
            totalUsers,
            0,
            0,
            0,
            0,
            0,
            0,
            userOptions,
            roleOptions,
            moduleOptions,
            Array.Empty<ErpUsagePatternPoint>(),
            Array.Empty<ErpUsagePatternUserSummary>());

    private sealed record UserProjection(
        string Id,
        string FullName,
        string Rank,
        string UserName);

    private sealed record RoleProjection(string UserId, string RoleName);

    private sealed record BucketProjection(
        string UserId,
        DateTime BucketStartUtc,
        DateTime LastSeenUtc,
        string ModuleKey,
        bool HadNavigation,
        bool HadInteractiveHeartbeat,
        int NavigationCount,
        int HeartbeatCount);

    private sealed record RawActionProjection(
        string UserId,
        DateTime TimeUtc,
        string Action);

    private readonly record struct IntervalKey(string UserId, DateTime IntervalStartUtc);

    private sealed class IntervalAggregate
    {
        public IntervalAggregate(string userId, DateTime intervalStartUtc)
        {
            UserId = userId;
            IntervalStartUtc = intervalStartUtc;
            LastActivityUtc = intervalStartUtc;
        }

        public string UserId { get; }
        public DateTime IntervalStartUtc { get; }
        public DateTime LastActivityUtc { get; set; }
        public bool HadNavigation { get; set; }
        public bool HadInteractiveHeartbeat { get; set; }
        public int NavigationCount { get; set; }
        public int HeartbeatCount { get; set; }
        public int OperationalActionCount { get; set; }
        public HashSet<string> ModuleKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
