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
    private const string UnassignedStageCode = "UNASSIGNED";

    private readonly ApplicationDbContext _db;
    private readonly IOfficerWorkloadReadService _officerWorkloadReadService;
    private readonly IWorkflowStageMetadataProvider _workflowStageMetadataProvider;
    private readonly IErpUsageQueryService? _usageQueryService;
    private readonly IErpCommandAdoptionQueryService? _adoptionQueryService;
    private readonly ILogger<CommandWorkspaceService>? _logger;

    public CommandWorkspaceService(
        ApplicationDbContext db,
        IOfficerWorkloadReadService officerWorkloadReadService,
        IWorkflowStageMetadataProvider workflowStageMetadataProvider,
        IErpUsageQueryService? usageQueryService = null,
        ILogger<CommandWorkspaceService>? logger = null,
        IErpCommandAdoptionQueryService? adoptionQueryService = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _officerWorkloadReadService = officerWorkloadReadService
            ?? throw new ArgumentNullException(nameof(officerWorkloadReadService));
        _workflowStageMetadataProvider = workflowStageMetadataProvider
            ?? throw new ArgumentNullException(nameof(workflowStageMetadataProvider));
        _usageQueryService = usageQueryService;
        _adoptionQueryService = adoptionQueryService;
        _logger = logger;
    }

    public async Task<CommandWorkspaceVm> GetAsync(
        CommandWorkspaceQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (string.Equals(query.View, "portfolio", StringComparison.OrdinalIgnoreCase))
        {
            return await BuildPortfolioAsync(query, cancellationToken);
        }

        if (string.Equals(query.View, "adoption", StringComparison.OrdinalIgnoreCase))
        {
            return await BuildAdoptionAsync(cancellationToken);
        }

        return await BuildOfficerWorkloadAsync(query, cancellationToken);
    }

    /// <summary>
    /// Backward-compatible forwarding method retained for callers that still depend on
    /// the command workspace service. New callers should use IOfficerWorkloadReadService.
    /// </summary>
    public Task<CommandOfficerWorkloadVm?> GetOfficerWorkloadCardAsync(
        string officerUserId,
        CancellationToken cancellationToken)
        => _officerWorkloadReadService.GetOfficerAsync(officerUserId, cancellationToken);

    private async Task<CommandWorkspaceVm> BuildPortfolioAsync(
        CommandWorkspaceQuery query,
        CancellationToken cancellationToken)
    {
        var categories = await _db.ProjectCategories
            .AsNoTracking()
            .Where(category => category.IsActive)
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .Select(category => new CategoryRow(
                category.Id,
                category.Name,
                category.ParentId,
                category.SortOrder))
            .ToListAsync(cancellationToken);

        var parentOptions = categories
            .Where(category => category.ParentId == null)
            .Select(category => new CommandFilterOptionVm(category.Id, category.Name))
            .ToList();
        var selectedParents = query.ParentCategoryIds
            .Where(id => parentOptions.Any(option => option.Id == id))
            .Distinct()
            .ToList();

        var projectRows = await _db.Projects
            .AsNoTracking()
            .Where(project =>
                !project.IsDeleted
                && !project.IsArchived
                && project.LifecycleStatus == ProjectLifecycleStatus.Active)
            .Select(project => new ProjectRow(
                project.Id,
                project.Name,
                project.CategoryId,
                project.LeadPoUserId,
                project.LeadPoUser != null ? project.LeadPoUser.FullName : null,
                project.LeadPoUser != null ? project.LeadPoUser.Rank : null,
                project.WorkflowVersion,
                project.ProjectStages
                    .Select(stage => new StageRow(
                        stage.StageCode,
                        stage.Status,
                        stage.SortOrder,
                        stage.ActualStart,
                        stage.CompletedOn))
                    .ToList()))
            .ToListAsync(cancellationToken);

        var categoryMap = categories.ToDictionary(category => category.Id);
        var normalizedProjects = projectRows
            .Select(project => NormalizeProject(project, categoryMap))
            .ToList();

        IEnumerable<NormalizedProject> filteredProjects = normalizedProjects;
        if (selectedParents.Count > 0)
        {
            filteredProjects = filteredProjects.Where(project =>
                project.ParentCategoryId.HasValue
                && selectedParents.Contains(project.ParentCategoryId.Value));
        }

        if (!string.IsNullOrWhiteSpace(query.ProjectSearch))
        {
            var projectSearch = query.ProjectSearch.Trim();
            filteredProjects = filteredProjects.Where(project =>
                project.Name.Contains(projectSearch, StringComparison.OrdinalIgnoreCase));
        }

        var projectList = filteredProjects.ToList();
        var orderedStageCodes = StageCodes.All
            .Concat(projectList
                .Select(project => project.StageCode)
                .Where(code => !StageCodes.All.Contains(code, StringComparer.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var stageColumns = BuildStageColumns(
            projectList,
            orderedStageCodes,
            query.PopulatedStagesOnly);
        var stageSeries = projectList
            .GroupBy(project => new
            {
                project.StageCode,
                project.StageName,
                project.ParentCategoryName
            })
            .Select(group => new CommandStageSeriesPointVm(
                group.Key.StageCode,
                group.Key.StageName,
                group.Key.ParentCategoryName,
                group.Count()))
            .OrderBy(point => StageOrder(point.StageCode, orderedStageCodes))
            .ThenBy(point => point.CategoryName)
            .ToList();
        var stageOptions = orderedStageCodes
            .Select((code, index) => new CommandFilterOptionVm(
                index + 1,
                code == UnassignedStageCode
                    ? "Unassigned"
                    : StageCodes.DisplayNameOf(code)))
            .ToList();

        var officerCount = await _officerWorkloadReadService.CountActiveOfficersAsync(cancellationToken);

        return new CommandWorkspaceVm
        {
            GeneratedAtUtc = DateTime.UtcNow,
            ActiveView = "portfolio",
            TotalOngoingProjects = projectRows.Count,
            ParentCategoryOptions = parentOptions,
            SelectedParentCategoryIds = selectedParents,
            ProjectSearch = query.ProjectSearch,
            PopulatedStagesOnly = query.PopulatedStagesOnly,
            StageSeries = stageSeries,
            StageColumns = stageColumns,
            Officers = Array.Empty<CommandOfficerWorkloadVm>(),
            StageOptions = stageOptions,
            ProjectOfficerCount = officerCount,
            UsageSummary = await BuildUsageSummaryAsync(cancellationToken)
        };
    }


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
            ParentCategoryOptions = Array.Empty<CommandFilterOptionVm>(),
            SelectedParentCategoryIds = Array.Empty<int>(),
            ProjectSearch = null,
            PopulatedStagesOnly = false,
            StageSeries = Array.Empty<CommandStageSeriesPointVm>(),
            StageColumns = Array.Empty<CommandStageColumnVm>(),
            Officers = Array.Empty<CommandOfficerWorkloadVm>(),
            StageOptions = Array.Empty<CommandFilterOptionVm>(),
            ProjectOfficerCount = officerCount,
            UsageSummary = await BuildUsageSummaryAsync(cancellationToken)
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
            ParentCategoryOptions = Array.Empty<CommandFilterOptionVm>(),
            SelectedParentCategoryIds = Array.Empty<int>(),
            ProjectSearch = null,
            PopulatedStagesOnly = false,
            StageSeries = Array.Empty<CommandStageSeriesPointVm>(),
            StageColumns = Array.Empty<CommandStageColumnVm>(),
            Officers = officers,
            StageOptions = Array.Empty<CommandFilterOptionVm>(),
            ProjectOfficerCount = officers.Count,
            UsageSummary = await BuildUsageSummaryAsync(cancellationToken)
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
                    SignedInUsers = snapshot.SignedInUsers,
                    UsedErpUsers = snapshot.UsedErpUsers,
                    OperationalContributors = snapshot.OperationalContributors,
                    AdoptionGap = snapshot.AdoptionGap,
                    ReviewCaseCount = snapshot.ReviewCaseCount,
                    RegularClassificationAvailable = snapshot.ReviewAvailable,
                    SevenDayReviewAvailable = snapshot.ReviewAvailable,
                    NoUsageSevenWorkingDays = snapshot.ReviewCaseCount,
                    Trend = snapshot.Trend
                        .Select(point => new CommandAdoptionTrendPointVm(
                            point.Date,
                            point.SignedInUsers,
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
                            row.LastRecordedUseUtc,
                            row.SignedInDuringPeriod))
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

    private static IReadOnlyList<CommandStageColumnVm> BuildStageColumns(
        IReadOnlyList<NormalizedProject> projects,
        IReadOnlyList<string> orderedCodes,
        bool populatedOnly)
    {
        var columns = orderedCodes
            .Select(code =>
            {
                var stageProjects = projects
                    .Where(project => string.Equals(
                        project.StageCode,
                        code,
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();

                return new CommandStageColumnVm
                {
                    StageCode = code,
                    StageName = code == UnassignedStageCode
                        ? "Unassigned"
                        : StageCodes.DisplayNameOf(code),
                    ProjectCount = stageProjects.Count,
                    Categories = stageProjects
                        .GroupBy(project => project.ParentCategoryName)
                        .OrderBy(group => group.Key)
                        .Select(group => new CommandStageCategoryVm
                        {
                            CategoryName = group.Key,
                            ProjectCount = group.Count(),
                            Projects = group
                                .OrderBy(project => project.Name)
                                .Select(project => new CommandStageProjectVm(
                                    project.Id,
                                    project.Name,
                                    project.OfficerDisplayName,
                                    $"/Projects/Overview/{project.Id}"))
                                .ToList()
                        })
                        .ToList()
                };
            })
            .ToList();

        return populatedOnly
            ? columns.Where(column => column.ProjectCount > 0).ToList()
            : columns;
    }

    private NormalizedProject NormalizeProject(
        ProjectRow row,
        IReadOnlyDictionary<int, CategoryRow> categories)
    {
        var stageSnapshots = row.Stages
            .Select(stage => new ProjectStageStatusSnapshot(
                stage.StageCode,
                stage.Status,
                stage.SortOrder,
                stage.ActualStart,
                stage.CompletedOn))
            .ToList();
        var presentStage = PresentStageHelper.ComputePresentStageAndAge(
            stageSnapshots,
            _workflowStageMetadataProvider,
            row.WorkflowVersion);

        var stageCode = string.IsNullOrWhiteSpace(presentStage.CurrentStageCode)
            ? UnassignedStageCode
            : presentStage.CurrentStageCode.Trim();
        var stageName = stageCode == UnassignedStageCode
            ? "Unassigned"
            : presentStage.CurrentStageName
                ?? _workflowStageMetadataProvider.GetDisplayName(row.WorkflowVersion, stageCode);

        int? parentId = null;
        var parentName = "Uncategorised";
        if (row.CategoryId.HasValue
            && categories.TryGetValue(row.CategoryId.Value, out var category))
        {
            var cursor = category;
            var guard = 0;
            while (cursor.ParentId.HasValue
                   && categories.TryGetValue(cursor.ParentId.Value, out var parent)
                   && guard++ < 20)
            {
                cursor = parent;
            }

            parentId = cursor.Id;
            parentName = cursor.Name;
        }

        var officerName = string.IsNullOrWhiteSpace(row.OfficerName)
            ? "Unassigned"
            : string.Join(' ', new[] { row.OfficerRank, row.OfficerName }
                .Where(value => !string.IsNullOrWhiteSpace(value)));

        return new NormalizedProject(
            row.Id,
            row.Name,
            parentId,
            parentName,
            stageCode,
            stageName,
            row.LeadPoUserId,
            officerName);
    }

    private static int StageOrder(string code, IReadOnlyList<string> orderedCodes)
    {
        for (var index = 0; index < orderedCodes.Count; index++)
        {
            if (string.Equals(orderedCodes[index], code, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    private sealed record CategoryRow(int Id, string Name, int? ParentId, int SortOrder);

    private sealed record StageRow(
        string StageCode,
        StageStatus Status,
        int SortOrder,
        DateOnly? ActualStart,
        DateOnly? CompletedOn);

    private sealed record ProjectRow(
        int Id,
        string Name,
        int? CategoryId,
        string? LeadPoUserId,
        string? OfficerName,
        string? OfficerRank,
        string? WorkflowVersion,
        IReadOnlyList<StageRow> Stages);

    private sealed record NormalizedProject(
        int Id,
        string Name,
        int? ParentCategoryId,
        string ParentCategoryName,
        string StageCode,
        string StageName,
        string? LeadPoUserId,
        string OfficerDisplayName);
}

public sealed class CommandWorkspaceQuery
{
    public string View { get; init; } = "portfolio";
    public IReadOnlyList<int> ParentCategoryIds { get; init; } = Array.Empty<int>();
    public string? ProjectSearch { get; init; }
    public bool PopulatedStagesOnly { get; init; }
    public string RequestingUserId { get; init; } = string.Empty;
}
