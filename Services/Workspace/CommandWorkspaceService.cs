using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Usage;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Services.Workspace;

public sealed class CommandWorkspaceService
{
    private readonly ApplicationDbContext _db;
    private readonly IOfficerWorkloadReadService _officerWorkloadReadService;
    private readonly IErpUsageQueryService? _usageQueryService;
    private readonly IErpCommandAdoptionQueryService? _adoptionQueryService;
    private readonly IErpUsagePatternQueryService? _usagePatternQueryService;
    private readonly ILogger<CommandWorkspaceService>? _logger;

    public CommandWorkspaceService(
        ApplicationDbContext db,
        IOfficerWorkloadReadService officerWorkloadReadService,
        IErpUsageQueryService? usageQueryService = null,
        ILogger<CommandWorkspaceService>? logger = null,
        IErpCommandAdoptionQueryService? adoptionQueryService = null,
        IErpUsagePatternQueryService? usagePatternQueryService = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _officerWorkloadReadService = officerWorkloadReadService
            ?? throw new ArgumentNullException(nameof(officerWorkloadReadService));
        _usageQueryService = usageQueryService;
        _adoptionQueryService = adoptionQueryService;
        _usagePatternQueryService = usagePatternQueryService;
        _logger = logger;
    }

    public async Task<CommandWorkspaceVm> GetAsync(
        CommandWorkspaceQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (string.Equals(query.View, "adoption", StringComparison.OrdinalIgnoreCase))
        {
            return await BuildAdoptionAsync(cancellationToken);
        }

        if (string.Equals(query.View, "usage-pattern", StringComparison.OrdinalIgnoreCase))
        {
            return await BuildUsagePatternAsync(query, cancellationToken);
        }

        if (string.Equals(query.View, "my-activity", StringComparison.OrdinalIgnoreCase))
        {
            return await BuildNavigationShellAsync("my-activity", cancellationToken);
        }

        return await BuildOfficerWorkloadAsync(query, cancellationToken);
    }

    public Task<CommandWorkspaceVm> GetNavigationShellAsync(
        string activeView,
        CancellationToken cancellationToken = default)
        => BuildNavigationShellAsync(activeView, cancellationToken);

    private async Task<CommandWorkspaceVm> BuildNavigationShellAsync(
        string activeView,
        CancellationToken cancellationToken)
    {
        var totalOngoingProjects = await _db.Projects
            .AsNoTracking()
            .CountAsync(project =>
                !project.IsDeleted
                && !project.IsArchived
                && project.LifecycleStatus == ProjectLifecycleStatus.Active,
                cancellationToken);
        var officerCount = await _officerWorkloadReadService.CountActiveOfficersAsync(
            cancellationToken);

        return new CommandWorkspaceVm
        {
            GeneratedAtUtc = DateTime.UtcNow,
            ActiveView = activeView,
            TotalOngoingProjects = totalOngoingProjects,
            ProjectOfficerCount = officerCount
        };
    }

    /// <summary>
    /// Backward-compatible forwarding method retained for callers that still depend on
    /// the command workspace service. New callers should use IOfficerWorkloadReadService.
    /// </summary>
    public Task<CommandOfficerWorkloadVm?> GetOfficerWorkloadCardAsync(
        string officerUserId,
        CancellationToken cancellationToken)
        => _officerWorkloadReadService.GetOfficerAsync(officerUserId, cancellationToken);

    private async Task<CommandWorkspaceVm> BuildAdoptionAsync(
        CancellationToken cancellationToken)
    {
        var totalOngoingProjects = await _db.Projects
            .AsNoTracking()
            .CountAsync(project =>
                !project.IsDeleted
                && !project.IsArchived
                && project.LifecycleStatus == ProjectLifecycleStatus.Active,
                cancellationToken);
        var officerCount = await _officerWorkloadReadService.CountActiveOfficersAsync(
            cancellationToken);

        return new CommandWorkspaceVm
        {
            GeneratedAtUtc = DateTime.UtcNow,
            ActiveView = "adoption",
            TotalOngoingProjects = totalOngoingProjects,
            Officers = Array.Empty<CommandOfficerWorkloadVm>(),
            ProjectOfficerCount = officerCount,
            UsageSummary = await BuildUsageSummaryAsync(cancellationToken)
        };
    }

    private async Task<CommandWorkspaceVm> BuildUsagePatternAsync(
        CommandWorkspaceQuery query,
        CancellationToken cancellationToken)
    {
        var totalOngoingProjects = await _db.Projects
            .AsNoTracking()
            .CountAsync(project =>
                !project.IsDeleted
                && !project.IsArchived
                && project.LifecycleStatus == ProjectLifecycleStatus.Active,
                cancellationToken);
        var officerCount = await _officerWorkloadReadService.CountActiveOfficersAsync(
            cancellationToken);

        ErpUsagePatternResult? pattern = null;
        if (_usagePatternQueryService is not null)
        {
            try
            {
                pattern = await _usagePatternQueryService.GetAsync(
                    new ErpUsagePatternQuery
                    {
                        Days = query.PatternDays,
                        UserId = query.PatternUserId,
                        Role = query.PatternRole,
                        Module = query.PatternModule,
                        Signal = query.PatternSignal
                    },
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger?.LogWarning(
                    exception,
                    "ERP usage pattern could not be loaded for the Command Workspace.");
            }
        }

        return new CommandWorkspaceVm
        {
            GeneratedAtUtc = DateTime.UtcNow,
            ActiveView = "usage-pattern",
            TotalOngoingProjects = totalOngoingProjects,
            ProjectOfficerCount = officerCount,
            UsagePattern = pattern is null
                ? new CommandUsagePatternVm
                {
                    RequestedDays = query.PatternDays,
                    SelectedUserId = query.PatternUserId,
                    SelectedRole = query.PatternRole,
                    SelectedModule = query.PatternModule,
                    SelectedSignal = query.PatternSignal ?? ErpUsagePatternSignalNames.All
                }
                : new CommandUsagePatternVm
                {
                    StartDate = pattern.StartDate,
                    EndDate = pattern.EndDate,
                    TrackingInceptionUtc = pattern.TrackingInceptionUtc,
                    RequestedDays = pattern.RequestedDays,
                    AggregationMinutes = pattern.AggregationMinutes,
                    TotalUsersInScope = pattern.TotalUsersInScope,
                    ActiveUsers = pattern.ActiveUsers,
                    ActivityIntervals = pattern.ActivityIntervals,
                    InteractiveIntervals = pattern.InteractiveIntervals,
                    OperationalIntervals = pattern.OperationalIntervals,
                    OperationalActionCount = pattern.OperationalActionCount,
                    ModulesRepresented = pattern.ModulesRepresented,
                    SelectedUserId = query.PatternUserId,
                    SelectedRole = query.PatternRole,
                    SelectedModule = query.PatternModule,
                    SelectedSignal = query.PatternSignal ?? ErpUsagePatternSignalNames.All,
                    UserOptions = pattern.UserOptions
                        .Select(option => new CommandStringOptionVm(option.Value, option.Label))
                        .ToArray(),
                    RoleOptions = pattern.RoleOptions
                        .Select(option => new CommandStringOptionVm(option.Value, option.Label))
                        .ToArray(),
                    ModuleOptions = pattern.ModuleOptions
                        .Select(option => new CommandStringOptionVm(option.Value, option.Label))
                        .ToArray(),
                    Points = pattern.Points
                        .Select(point => new CommandUsagePatternPointVm(
                            point.TimestampUtcMilliseconds,
                            point.TimestampIstLabel,
                            point.UserId,
                            point.DisplayName,
                            point.Rank,
                            point.UserName,
                            point.Signal,
                            point.Modules,
                            point.NavigationCount,
                            point.HeartbeatCount,
                            point.OperationalActionCount))
                        .ToArray(),
                    Users = pattern.Users
                        .Select(user => new CommandUsagePatternUserVm(
                            user.UserId,
                            user.DisplayName,
                            user.Rank,
                            user.UserName,
                            user.ActiveDays,
                            user.ActivityIntervals,
                            user.InteractiveIntervals,
                            user.OperationalActionCount,
                            user.Modules,
                            user.LastActivityUtc))
                        .ToArray()
                }
        };
    }

    private async Task<CommandWorkspaceVm> BuildOfficerWorkloadAsync(
        CommandWorkspaceQuery query,
        CancellationToken cancellationToken)
    {
        var officers = await _officerWorkloadReadService.GetAllAsync(
            query.RequestingUserId,
            cancellationToken);
        var totalOngoingProjects = await _db.Projects
            .AsNoTracking()
            .CountAsync(project =>
                !project.IsDeleted
                && !project.IsArchived
                && project.LifecycleStatus == ProjectLifecycleStatus.Active,
                cancellationToken);

        return new CommandWorkspaceVm
        {
            GeneratedAtUtc = DateTime.UtcNow,
            ActiveView = "officers",
            TotalOngoingProjects = totalOngoingProjects,
            Officers = officers,
            ProjectOfficerCount = officers.Count
        };
    }

    private async Task<CommandUsageSummaryVm> BuildUsageSummaryAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            if (_adoptionQueryService is not null)
            {
                var snapshot = await _adoptionQueryService.GetAsync(
                    monitoredWorkingDays: 7,
                    cancellationToken: cancellationToken);

                return new CommandUsageSummaryVm
                {
                    PeriodStart = snapshot.PeriodStart,
                    PeriodEnd = snapshot.PeriodEnd,
                    TrackingInceptionUtc = snapshot.TrackingInceptionUtc,
                    TrackingWorkingDays = snapshot.TrackingWorkingDays,
                    RequiredWorkingDays = snapshot.RequiredWorkingDays,
                    ReviewAvailable = snapshot.ReviewAvailable,
                    TotalUsers = snapshot.TotalUsers,
                    ActiveToday = snapshot.ActiveToday,
                    UsedErpUsers = snapshot.UsedErpUsers,
                    OperationalContributors = snapshot.OperationalContributors,
                    ModulesUsed = snapshot.ModulesUsed,
                    ReviewCaseCount = snapshot.ReviewCaseCount,
                    RegularClassificationAvailable = snapshot.ReviewAvailable,
                    SevenDayReviewAvailable = snapshot.ReviewAvailable,
                    NoUsageSevenWorkingDays = snapshot.ReviewCaseCount,
                    Trend = snapshot.Trend
                        .Select(point => new CommandAdoptionTrendPointVm(
                            point.Date,
                            point.UsedErpUsers,
                            point.OperationalContributors,
                            point.IsWorkingDay))
                        .ToArray(),
                    Attention = snapshot.Attention
                        .Select(row => new CommandAdoptionAttentionVm(
                            row.UserId,
                            row.DisplayName,
                            row.Rank,
                            row.UserName,
                            row.Observation,
                            row.LastRecordedUseUtc))
                        .ToArray()
                };
            }

            if (_usageQueryService is null)
            {
                return new CommandUsageSummaryVm();
            }

            var summary = await _usageQueryService.GetCommandSummaryAsync(cancellationToken);
            return new CommandUsageSummaryVm
            {
                TotalUsers = summary.TotalUsers,
                ActiveToday = summary.ActiveToday,
                UsedErpUsers = summary.ActiveToday,
                RegularUsers = summary.RegularUsers,
                NoUsageSevenWorkingDays = summary.NoUsageSevenWorkingDays,
                ReviewCaseCount = summary.NoUsageSevenWorkingDays,
                RegularClassificationAvailable = summary.RegularClassificationAvailable,
                SevenDayReviewAvailable = summary.SevenDayReviewAvailable,
                ReviewAvailable = summary.SevenDayReviewAvailable
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Adoption intelligence is advisory. A telemetry/query failure must never
            // make the operational Command Workspace unavailable.
            _logger?.LogWarning(
                exception,
                "ERP adoption summary could not be loaded for the Command Workspace.");
            return new CommandUsageSummaryVm();
        }
    }
}

public sealed class CommandWorkspaceQuery
{
    public string View { get; init; } = "officers";
    public int PatternDays { get; init; } = 7;
    public string? PatternUserId { get; init; }
    public string? PatternRole { get; init; }
    public string? PatternModule { get; init; }
    public string? PatternSignal { get; init; }
    public string RequestingUserId { get; init; } = string.Empty;
}
