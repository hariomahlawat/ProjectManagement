using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.ProjectBriefings.Presentation;

namespace ProjectManagement.Services.ProjectBriefings;

public interface IProjectBriefingDataService
{
    Task<ProjectBriefingDeckVm?> GetDeckAsync(
        long deckId,
        string requestingUserId,
        CancellationToken cancellationToken = default);

    Task<ProjectBriefingPresentationData> BuildPresentationDataAsync(
        long deckId,
        string requestingUserId,
        CancellationToken cancellationToken = default);
}

public sealed class ProjectBriefingDataService : IProjectBriefingDataService
{
    private readonly ApplicationDbContext _db;
    private readonly IProjectBriefingCostResolver _costResolver;
    private readonly IProjectBriefingExternalStatusService _externalStatusService;
    private readonly IProjectBriefingPhotoLoader _photoLoader;
    private readonly IProjectBriefingUpdateSheetFactsResolver _updateSheetFactsResolver;
    private readonly IClock _clock;

    public ProjectBriefingDataService(
        ApplicationDbContext db,
        IProjectBriefingCostResolver costResolver,
        IProjectBriefingExternalStatusService externalStatusService,
        IProjectBriefingPhotoLoader photoLoader,
        IProjectBriefingUpdateSheetFactsResolver updateSheetFactsResolver,
        IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _costResolver = costResolver ?? throw new ArgumentNullException(nameof(costResolver));
        _externalStatusService = externalStatusService ?? throw new ArgumentNullException(nameof(externalStatusService));
        _photoLoader = photoLoader ?? throw new ArgumentNullException(nameof(photoLoader));
        _updateSheetFactsResolver = updateSheetFactsResolver ?? throw new ArgumentNullException(nameof(updateSheetFactsResolver));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<ProjectBriefingDeckVm?> GetDeckAsync(
        long deckId,
        string requestingUserId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await LoadSnapshotAsync(deckId, requestingUserId, cancellationToken);
        if (snapshot is null)
        {
            return null;
        }

        var projects = await BuildProjectsAsync(snapshot.Items, snapshot.Layout, snapshot.UpdateSheetOptions, cancellationToken);
        return new ProjectBriefingDeckVm
        {
            Id = snapshot.Id,
            Name = snapshot.Name,
            Description = snapshot.Description,
            Layout = snapshot.Layout,
            PresentationMode = snapshot.PresentationMode,
            CostMode = snapshot.CostMode,
            NarrativeMode = snapshot.NarrativeMode,
            StandardSlideOptions = snapshot.StandardSlideOptions,
            PresentationTheme = snapshot.PresentationTheme,
            BrandingScope = snapshot.BrandingScope,
            IncludeCoverSlide = snapshot.IncludeCoverSlide,
            IncludePortfolioSummarySlide = snapshot.IncludePortfolioSummarySlide,
            IncludeStageSummary = snapshot.IncludeStageSummary,
            IncludeProjectCategorySummary = snapshot.IncludeProjectCategorySummary,
            IncludeTechnicalCategorySummary = snapshot.IncludeTechnicalCategorySummary,
            UpdateSheetOptions = snapshot.UpdateSheetOptions,
            HandlingMarking = snapshot.HandlingMarking,
            RowVersion = Encode(snapshot.RowVersion),
            UpdatedAtUtc = snapshot.UpdatedAtUtc,
            CreatedByDisplay = snapshot.CreatedByDisplay,
            LastModifiedByDisplay = snapshot.LastModifiedByDisplay,
            Projects = projects,
            Readiness = BuildReadiness(projects),
            SlideEstimate = BuildSlideEstimate(snapshot.Layout, snapshot.IncludeCoverSlide, snapshot.IncludePortfolioSummarySlide,
                snapshot.PresentationMode, snapshot.IncludeStageSummary, snapshot.IncludeProjectCategorySummary,
                snapshot.IncludeTechnicalCategorySummary, snapshot.CostMode, snapshot.NarrativeMode, projects)
        };
    }

    public async Task<ProjectBriefingPresentationData> BuildPresentationDataAsync(
        long deckId,
        string requestingUserId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await LoadSnapshotAsync(deckId, requestingUserId, cancellationToken)
            ?? throw new KeyNotFoundException("The saved deck was not found.");
        var projectVms = await BuildProjectsAsync(snapshot.Items, snapshot.Layout, snapshot.UpdateSheetOptions, cancellationToken);
        if (projectVms.Count == 0)
        {
            throw new InvalidOperationException("Add at least one project before generating the PowerPoint deck.");
        }

        var projects = ProjectBriefingProjectOrdering.OrderProjects(projectVms.Select(project =>
            new ProjectBriefingPresentationProject
            {
                ProjectId = project.ProjectId,
                ProjectName = project.ProjectName,
                LifecycleStatus = project.LifecycleStatus,
                LifecycleDisplay = project.LifecycleDisplay,
                PresentStageCode = project.PresentStageCode,
                PresentStage = project.PresentStage,
                PresentStageOrder = project.PresentStageOrder,
                ProjectCategory = project.ProjectCategory,
                TechnicalCategory = project.TechnicalCategory,
                CostRd = project.CostRd,
                IpaCost = project.IpaCost,
                ProliferationCost = project.ProliferationCost,
                ExternalStatus = project.ExternalStatus ?? "No external status recorded",
                ExternalStatusDate = project.ExternalStatusDate,
                BriefDescription = project.BriefDescription,
                ProjectBrief = project.ProjectBrief,
                CapabilityStatements = project.CapabilityStatements,
                ArppReference = project.ArppReference,
                ArppPppNumberApplicable = project.ArppPppNumberApplicable,
                Fund = project.Fund,
                DfpdsSchedule = project.DfpdsSchedule,
                Cfa = project.Cfa,
                AonDate = project.AonDate,
                SupplyOrderDate = project.SupplyOrderDate,
                DevelopmentPdcDate = project.DevelopmentPdcDate,
                CompletionStatusDisplay = project.CompletionStatusDisplay,
                JdpNames = project.JdpNames,
                ProjectOfficer = project.ProjectOfficer,
                LineDirectorate = project.LineDirectorate,
                SortOrder = project.SortOrder,
                CoverPhotoId = project.CoverPhotoId,
                CoverPhotoIsReady = project.HasCoverPhoto
            }));

        var summary = BuildPresentationSummary(projects);
        return new ProjectBriefingPresentationData
        {
            DeckId = snapshot.Id,
            DeckName = snapshot.Name,
            DeckDescription = snapshot.Description,
            Layout = snapshot.Layout,
            PresentationMode = snapshot.PresentationMode,
            CostMode = snapshot.Layout == ProjectBriefingLayout.ProjectUpdateSheet
                ? ProjectBriefingCostMode.CostRdOnly
                : snapshot.CostMode,
            NarrativeMode = snapshot.Layout == ProjectBriefingLayout.ProjectUpdateSheet
                ? ProjectBriefingNarrativeMode.ProjectBrief
                : snapshot.NarrativeMode,
            StandardSlideOptions = snapshot.StandardSlideOptions,
            PresentationTheme = snapshot.PresentationTheme,
            BrandingScope = snapshot.BrandingScope,
            IncludeCoverSlide = snapshot.IncludeCoverSlide,
            IncludePortfolioSummarySlide = snapshot.IncludePortfolioSummarySlide,
            IncludeStageSummary = snapshot.Layout == ProjectBriefingLayout.StandardBriefing && snapshot.IncludeStageSummary,
            IncludeProjectCategorySummary = snapshot.Layout == ProjectBriefingLayout.StandardBriefing && snapshot.IncludeProjectCategorySummary,
            IncludeTechnicalCategorySummary = snapshot.Layout == ProjectBriefingLayout.StandardBriefing && snapshot.IncludeTechnicalCategorySummary,
            UpdateSheetOptions = snapshot.UpdateSheetOptions,
            HandlingMarking = snapshot.HandlingMarking,
            GeneratedAtUtc = _clock.UtcNow.ToUniversalTime(),
            Projects = projects,
            Summary = summary
        };
    }

    private async Task<DeckSnapshot?> LoadSnapshotAsync(
        long deckId,
        string requestingUserId,
        CancellationToken cancellationToken)
    {
        var userId = requestingUserId?.Trim();
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedAccessException("The current user could not be resolved.");
        }

        var deck = await _db.Set<ProjectBriefingDeck>()
            .AsNoTracking()
            .Where(candidate => candidate.Id == deckId)
            .Select(candidate => new DeckHeaderSnapshot(
                candidate.Id,
                candidate.Name,
                candidate.Description,
                candidate.Layout,
                candidate.PresentationMode,
                candidate.CostMode,
                candidate.NarrativeMode,
                candidate.PresentationTheme,
                candidate.BrandingScope,
                candidate.IncludeCoverSlide,
                candidate.IncludePortfolioSummarySlide,
                candidate.IncludeStageSummary,
                candidate.IncludeProjectCategorySummary,
                candidate.IncludeTechnicalCategorySummary,
                candidate.SelectionRulesJson,
                candidate.HandlingMarking,
                candidate.UpdatedAtUtc,
                candidate.OwnerUser.FullName != string.Empty
                    ? candidate.OwnerUser.FullName
                    : candidate.OwnerUser.UserName ?? "Unknown user",
                candidate.LastModifiedByUser != null
                    ? (candidate.LastModifiedByUser.FullName != string.Empty
                        ? candidate.LastModifiedByUser.FullName
                        : candidate.LastModifiedByUser.UserName ?? "Unknown user")
                    : (candidate.OwnerUser.FullName != string.Empty
                        ? candidate.OwnerUser.FullName
                        : candidate.OwnerUser.UserName ?? "Unknown user"),
                candidate.RowVersion))
            .FirstOrDefaultAsync(cancellationToken);
        if (deck is null)
        {
            return null;
        }

        var deckConfiguration = ProjectBriefingDeckConfigurationCodec.Read(deck.SelectionRulesJson);
        var updateSheetOptions = deckConfiguration.UpdateSheetOptions;
        var standardSlideOptions = deckConfiguration.StandardSlideOptions;

        var itemRows = await _db.Set<ProjectBriefingDeckItem>()
            .AsNoTracking()
            .Where(item => item.DeckId == deckId)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Id)
            .Select(item => new DeckItemBaseSnapshot(
                item.Id,
                item.ProjectId,
                item.SortOrder,
                item.BriefDescriptionOverride,
                item.Project.Name,
                item.Project.Description,
                item.Project.ProjectBrief,
                item.Project.LifecycleStatus,
                item.Project.CompletedOn,
                item.Project.CompletedYear,
                item.Project.CompletedMonth,
                item.Project.IsDeleted,
                item.Project.IsArchived,
                item.Project.WorkflowVersion,
                item.Project.Category != null ? item.Project.Category.Name : null,
                item.Project.TechnicalCategory != null ? item.Project.TechnicalCategory.Name : null,
                item.Project.CoverPhotoId))
            .ToListAsync(cancellationToken);

        if (itemRows.Count == 0)
        {
            return new DeckSnapshot(
                deck.Id,
                deck.Name,
                deck.Description,
                deck.Layout,
                deck.PresentationMode,
                deck.CostMode,
                deck.NarrativeMode,
                deck.PresentationTheme,
                deck.BrandingScope,
                deck.IncludeCoverSlide,
                deck.IncludePortfolioSummarySlide,
                deck.IncludeStageSummary,
                deck.IncludeProjectCategorySummary,
                deck.IncludeTechnicalCategorySummary,
                updateSheetOptions,
                standardSlideOptions,
                deck.HandlingMarking,
                deck.UpdatedAtUtc,
                deck.CreatedByDisplay,
                deck.LastModifiedByDisplay,
                deck.RowVersion,
                Array.Empty<DeckItemSnapshot>());
        }

        var projectIds = itemRows.Select(item => item.ProjectId).Distinct().ToArray();
        var capabilityRows = await _db.ProjectCapabilityStatements
            .AsNoTracking()
            .Where(statement => projectIds.Contains(statement.ProjectId))
            .OrderBy(statement => statement.ProjectId)
            .ThenBy(statement => statement.DisplayOrder)
            .ThenBy(statement => statement.Id)
            .Select(statement => new CapabilityDatabaseSnapshot(
                statement.ProjectId,
                statement.Statement,
                statement.DisplayOrder))
            .ToListAsync(cancellationToken);
        var capabilitiesByProject = capabilityRows
            .GroupBy(statement => statement.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .OrderBy(statement => statement.DisplayOrder)
                    .Select(statement => statement.Statement)
                    .ToList());

        var stageRows = await _db.ProjectStages
            .AsNoTracking()
            .Where(stage => projectIds.Contains(stage.ProjectId))
            .Select(stage => new StageDatabaseSnapshot(
                stage.ProjectId,
                stage.StageCode,
                stage.Status,
                stage.SortOrder))
            .ToListAsync(cancellationToken);
        var stagesByProject = stageRows
            .GroupBy(stage => stage.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<StageSnapshot>)group
                    .Select(stage => new StageSnapshot(stage.StageCode, stage.Status, stage.SortOrder))
                    .OrderBy(stage => stage.SortOrder)
                    .ToList());

        var photoRows = await _db.ProjectPhotos
            .AsNoTracking()
            .Where(photo => projectIds.Contains(photo.ProjectId))
            .Select(photo => new PhotoDatabaseSnapshot(
                photo.ProjectId,
                photo.Id,
                photo.IsCover,
                photo.IsLowResolution,
                photo.Ordinal))
            .ToListAsync(cancellationToken);
        var photosByProject = photoRows
            .GroupBy(photo => photo.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PhotoSnapshot>)group
                    .OrderByDescending(photo => photo.IsCover)
                    .ThenBy(photo => photo.IsLowResolution)
                    .ThenBy(photo => photo.Ordinal)
                    .ThenBy(photo => photo.Id)
                    .Select(photo => new PhotoSnapshot(photo.Id, photo.IsCover, photo.IsLowResolution, photo.Ordinal))
                    .ToList());

        var items = itemRows
            .Select(item => new DeckItemSnapshot(
                item.ItemId,
                item.ProjectId,
                item.SortOrder,
                item.BriefDescriptionOverride,
                item.ProjectName,
                item.ProjectDescription,
                item.ProjectBrief,
                item.LifecycleStatus,
                item.CompletedOn,
                item.CompletedYear,
                item.CompletedMonth,
                item.IsDeleted,
                item.IsArchived,
                item.WorkflowVersion,
                item.ProjectCategory,
                item.TechnicalCategory,
                item.CoverPhotoId,
                capabilitiesByProject.GetValueOrDefault(item.ProjectId) ?? Array.Empty<string>(),
                stagesByProject.GetValueOrDefault(item.ProjectId) ?? Array.Empty<StageSnapshot>(),
                photosByProject.GetValueOrDefault(item.ProjectId) ?? Array.Empty<PhotoSnapshot>()))
            .ToList();

        return new DeckSnapshot(
            deck.Id,
            deck.Name,
            deck.Description,
            deck.Layout,
            deck.PresentationMode,
            deck.CostMode,
            deck.NarrativeMode,
            deck.PresentationTheme,
            deck.BrandingScope,
            deck.IncludeCoverSlide,
            deck.IncludePortfolioSummarySlide,
            deck.IncludeStageSummary,
            deck.IncludeProjectCategorySummary,
            deck.IncludeTechnicalCategorySummary,
            updateSheetOptions,
            standardSlideOptions,
            deck.HandlingMarking,
            deck.UpdatedAtUtc,
            deck.CreatedByDisplay,
            deck.LastModifiedByDisplay,
            deck.RowVersion,
            items);
    }

    private async Task<IReadOnlyList<ProjectBriefingProjectVm>> BuildProjectsAsync(
        IReadOnlyList<DeckItemSnapshot> items,
        ProjectBriefingLayout layout,
        ProjectBriefingUpdateSheetOptions updateSheetOptions,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return Array.Empty<ProjectBriefingProjectVm>();
        }

        var projectIds = items.Select(item => item.ProjectId).Distinct().ToArray();
        var resolvedCosts = await _costResolver.ResolveCostsAsync(projectIds, cancellationToken);
        var costRd = resolvedCosts.CostRd;
        var ipaCost = resolvedCosts.Ipa;
        IReadOnlyDictionary<int, ProjectBriefingCostValue> proliferation =
            layout == ProjectBriefingLayout.ProjectUpdateSheet
                ? new Dictionary<int, ProjectBriefingCostValue>()
                : await _costResolver.ResolveProliferationCostAsync(projectIds, cancellationToken);
        var externalStatuses = await _externalStatusService.GetLatestAsync(projectIds, cancellationToken);
        var updateSheetFacts = layout == ProjectBriefingLayout.ProjectUpdateSheet
            ? await _updateSheetFactsResolver.ResolveAsync(projectIds, cancellationToken)
            : new Dictionary<int, ProjectBriefingUpdateSheetFacts>();

        var coverByProject = items.ToDictionary(item => item.ProjectId, ResolveCoverPhotoId);
        var photoReferences = coverByProject
            .Where(pair => pair.Value.HasValue)
            .Select(pair => new ProjectBriefingPhotoReference(pair.Key, pair.Value!.Value))
            .ToArray();
        var photoProbes = await _photoLoader.ProbeAsync(photoReferences, cancellationToken);

        var projectedProjects = items
            .Select(item =>
            {
                var coverPhotoId = coverByProject[item.ProjectId];
                var probe = coverPhotoId.HasValue
                    ? photoProbes.GetValueOrDefault(coverPhotoId.Value)
                    : null;
                var external = externalStatuses.GetValueOrDefault(item.ProjectId);
                var stageCode = ResolveStageCode(item);
                updateSheetFacts.TryGetValue(item.ProjectId, out var updateFacts);
                var requiresDevelopmentPdc = string.Equals(stageCode, StageCodes.DEVP, StringComparison.OrdinalIgnoreCase);
                var hasCompleteArppDetails = updateFacts?.HasCompleteFundingAuthorityDetails == true;
                var resolvedCostRd = costRd.GetValueOrDefault(item.ProjectId)
                    ?? ProjectBriefingCostValue.Missing();
                var resolvedExternalStatus = external?.Body;
                var updateSheetCoreFactsReady = layout != ProjectBriefingLayout.ProjectUpdateSheet
                    || AreSelectedUpdateSheetRowsReady(
                        updateSheetOptions,
                        item.LifecycleStatus,
                        requiresDevelopmentPdc,
                        resolvedCostRd,
                        resolvedExternalStatus,
                        updateFacts);
                return new ProjectBriefingProjectVm
                {
                    ProjectId = item.ProjectId,
                    ProjectName = item.ProjectName,
                    LifecycleStatus = item.LifecycleStatus,
                    LifecycleDisplay = ResolveLifecycleDisplay(item),
                    PresentStageCode = stageCode,
                    PresentStage = ResolveStageName(item),
                    PresentStageOrder = ProjectBriefingStageOrder.Resolve(item.LifecycleStatus, stageCode),
                    ProjectCategory = item.ProjectCategory,
                    TechnicalCategory = item.TechnicalCategory,
                    CostRd = resolvedCostRd,
                    IpaCost = ipaCost.GetValueOrDefault(item.ProjectId)
                        ?? ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.IPA),
                    ProliferationCost = proliferation.GetValueOrDefault(item.ProjectId)
                        ?? ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.Proliferation),
                    ExternalStatus = external?.Body,
                    ExternalStatusDate = external?.EventDate,
                    HasSelectedCoverPhoto = coverPhotoId.HasValue,
                    HasCoverPhoto = probe?.IsReady == true,
                    CoverPhotoId = coverPhotoId,
                    CoverPhotoReadinessReason = probe?.FailureReason,
                    BriefDescription = ProjectBriefingTextNormalizer.NormalizeFull(
                        item.BriefDescriptionOverride
                        ?? (item.CapabilityStatements.Count > 0
                            ? string.Join("\n", item.CapabilityStatements.Select(statement => $"• {statement}"))
                            : "Capability overview not recorded.")),
                    ProjectBrief = ProjectBriefingTextNormalizer.NormalizeProjectBrief(item.ProjectBrief),
                    CapabilityStatements = item.CapabilityStatements,
                    BriefDescriptionOverride = item.BriefDescriptionOverride,
                    ArppReference = updateFacts?.ArppReference,
                    ArppPppNumberApplicable = updateFacts?.IsDelistedArppPosition != true,
                    Fund = updateFacts?.Fund,
                    DfpdsSchedule = updateFacts?.DfpdsSchedule,
                    Cfa = updateFacts?.Cfa,
                    AonDate = updateFacts?.AonDate,
                    SupplyOrderDate = updateFacts?.SupplyOrderDate,
                    DevelopmentPdcDate = requiresDevelopmentPdc ? updateFacts?.DevelopmentPdcDate : null,
                    CompletionStatusDisplay = BuildCompletionStatusDisplay(item),
                    JdpNames = updateFacts?.JdpNames ?? Array.Empty<string>(),
                    ProjectOfficer = updateFacts?.ProjectOfficer,
                    ProjectOfficerIsComplete = updateFacts?.ProjectOfficerIsComplete == true,
                    LineDirectorate = updateFacts?.LineDirectorate,
                    HasCompleteArppDetails = hasCompleteArppDetails,
                    IsDevelopmentPdcRequired = requiresDevelopmentPdc,
                    IsUpdateSheetCoreFactsReady = updateSheetCoreFactsReady,
                    SortOrder = item.SortOrder,
                    OpenUrl = $"/Projects/Overview/{item.ProjectId}"
                };
            });

        return ProjectBriefingProjectOrdering.OrderProjects(projectedProjects);
    }


    private static bool AreSelectedUpdateSheetRowsReady(
        ProjectBriefingUpdateSheetOptions options,
        ProjectLifecycleStatus lifecycleStatus,
        bool requiresDevelopmentPdc,
        ProjectBriefingCostValue costRd,
        string? externalStatus,
        ProjectBriefingUpdateSheetFacts? facts)
    {
        foreach (var row in options.Rows)
        {
            var ready = row switch
            {
                ProjectBriefingUpdateSheetRow.ProjectCost => costRd.IsAvailable,
                ProjectBriefingUpdateSheetRow.ArppPppNumber => facts?.IsDelistedArppPosition == true
                    || !string.IsNullOrWhiteSpace(facts?.ArppReference),
                ProjectBriefingUpdateSheetRow.FundingAuthority => facts?.HasCompleteFundingAuthorityDetails == true,
                ProjectBriefingUpdateSheetRow.AonDate => facts?.AonDate.HasValue == true,
                ProjectBriefingUpdateSheetRow.SupplyOrder => facts is not null
                    && facts.SupplyOrderDate.HasValue
                    && facts.JdpNames.Count > 0,
                ProjectBriefingUpdateSheetRow.PdcOrCompletionStatus => lifecycleStatus is ProjectLifecycleStatus.Completed
                    or ProjectLifecycleStatus.Cancelled
                    || !requiresDevelopmentPdc
                    || facts?.DevelopmentPdcDate.HasValue == true,
                ProjectBriefingUpdateSheetRow.PresentStatus => !string.IsNullOrWhiteSpace(externalStatus),
                ProjectBriefingUpdateSheetRow.ProjectOfficer => facts?.ProjectOfficerIsComplete == true,
                ProjectBriefingUpdateSheetRow.LineDirectorate => !string.IsNullOrWhiteSpace(facts?.LineDirectorate),
                _ => true
            };

            // Hidden rows suppress only wholly empty optional values. Partially populated rows remain
            // visible and must still be reported as incomplete. The contextual PDC/completion row is
            // always retained, so a missing Development-stage PDC also continues to need attention.
            var rowWillRender = row switch
            {
                ProjectBriefingUpdateSheetRow.ProjectCost => costRd.IsAvailable,
                ProjectBriefingUpdateSheetRow.ArppPppNumber => facts?.IsDelistedArppPosition != true
                    && !string.IsNullOrWhiteSpace(facts?.ArppReference),
                ProjectBriefingUpdateSheetRow.FundingAuthority => facts is not null
                    && (!string.IsNullOrWhiteSpace(facts.Fund)
                        || !string.IsNullOrWhiteSpace(facts.DfpdsSchedule)
                        || !string.IsNullOrWhiteSpace(facts.Cfa)),
                ProjectBriefingUpdateSheetRow.AonDate => facts?.AonDate.HasValue == true,
                ProjectBriefingUpdateSheetRow.SupplyOrder => facts is not null
                    && (facts.SupplyOrderDate.HasValue || facts.JdpNames.Count > 0),
                ProjectBriefingUpdateSheetRow.PdcOrCompletionStatus => true,
                ProjectBriefingUpdateSheetRow.PresentStatus => !string.IsNullOrWhiteSpace(externalStatus),
                ProjectBriefingUpdateSheetRow.ProjectOfficer => !string.IsNullOrWhiteSpace(facts?.ProjectOfficer),
                ProjectBriefingUpdateSheetRow.LineDirectorate => !string.IsNullOrWhiteSpace(facts?.LineDirectorate),
                _ => false
            };

            if (!ready && (!options.HideEmptyValues || rowWillRender))
            {
                return false;
            }
        }

        return true;
    }

    private static string? BuildCompletionStatusDisplay(DeckItemSnapshot item)
    {
        if (item.LifecycleStatus != ProjectLifecycleStatus.Completed) return null;

        var completion = ProjectCompletionFormatter.Format(
            item.CompletedOn,
            item.CompletedYear,
            item.CompletedMonth,
            unknownText: string.Empty);
        if (string.IsNullOrWhiteSpace(completion)) return "Project completed";

        var precision = ProjectCompletionFormatter.InferPrecision(
            item.CompletedOn,
            item.CompletedYear,
            item.CompletedMonth);
        return precision == ProjectCompletionPrecision.ExactDate
            ? $"Project completed on {completion}"
            : $"Project completed in {completion}";
    }

    private static string ResolveLifecycleDisplay(DeckItemSnapshot item)
    {
        if (item.IsDeleted) return "Deleted record";
        if (item.IsArchived) return "Archived";
        return item.LifecycleStatus switch
        {
            ProjectLifecycleStatus.Completed => "Completed",
            ProjectLifecycleStatus.Cancelled => "Cancelled",
            _ => "Ongoing"
        };
    }

    private static ProjectBriefingReadinessVm BuildReadiness(IReadOnlyList<ProjectBriefingProjectVm> projects)
        => new()
        {
            ProjectCount = projects.Count,
            OngoingCount = projects.Count(project => project.LifecycleStatus == ProjectLifecycleStatus.Active),
            CompletedCount = projects.Count(project => project.LifecycleStatus == ProjectLifecycleStatus.Completed),
            ExternalStatusAvailableCount = projects.Count(project => !string.IsNullOrWhiteSpace(project.ExternalStatus)),
            CostRdAvailableCount = projects.Count(project => project.CostRd.IsAvailable),
            ProliferationCostAvailableCount = projects.Count(project => project.ProliferationCost.IsAvailable),
            CoverPhotoAvailableCount = projects.Count(project => project.HasCoverPhoto),
            SelectedCoverPhotoCount = projects.Count(project => project.HasSelectedCoverPhoto),
            DescriptionAvailableCount = projects.Count(project => HasCapabilityOverview(project.BriefDescription)),
            CapabilityOverviewAvailableCount = projects.Count(project => HasCapabilityOverview(project.BriefDescription)),
            ProjectBriefAvailableCount = projects.Count(project => HasProjectBrief(project.ProjectBrief)),
            UpdateSheetCoreFactsAvailableCount = projects.Count(project => project.IsUpdateSheetCoreFactsReady),
            ArppReferenceAvailableCount = projects.Count(project => !project.ArppPppNumberApplicable || !string.IsNullOrWhiteSpace(project.ArppReference)),
            ArppDetailsAvailableCount = projects.Count(project => project.HasCompleteArppDetails),
            AonDateAvailableCount = projects.Count(project => project.AonDate.HasValue),
            SupplyOrderDateAvailableCount = projects.Count(project => project.SupplyOrderDate.HasValue),
            JdpAvailableCount = projects.Count(project => project.JdpNames.Count > 0),
            DevelopmentProjectCount = projects.Count(project => project.IsDevelopmentPdcRequired),
            DevelopmentPdcAvailableCount = projects.Count(project => project.IsDevelopmentPdcRequired && project.DevelopmentPdcDate.HasValue),
            ProjectOfficerAvailableCount = projects.Count(project => project.ProjectOfficerIsComplete),
            LineDirectorateAvailableCount = projects.Count(project => !string.IsNullOrWhiteSpace(project.LineDirectorate))
        };

    private static bool HasCapabilityOverview(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && !string.Equals(value.Trim(), "Brief description not recorded.", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(value.Trim(), "Capability overview not recorded.", StringComparison.OrdinalIgnoreCase);

    private static bool HasProjectBrief(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && !string.Equals(value.Trim(), "Project brief not recorded.", StringComparison.OrdinalIgnoreCase);

    private static ProjectBriefingPresentationSummary BuildPresentationSummary(
        IReadOnlyList<ProjectBriefingPresentationProject> projects)
    {
        var stageSummary = ProjectBriefingStageOrder.BuildSummary(
            projects.Select(project => project.PresentStageOrder));

        var projectCategorySummary = projects
            .GroupBy(project => string.IsNullOrWhiteSpace(project.ProjectCategory) ? "Not categorised" : project.ProjectCategory!)
            .Select(group => new ProjectBriefingSummaryPoint(group.Key, group.Count()))
            .OrderByDescending(point => point.Count)
            .ThenBy(point => point.Label)
            .ToList();

        var technicalCategorySummary = projects
            .GroupBy(project => string.IsNullOrWhiteSpace(project.TechnicalCategory) ? "Not categorised" : project.TechnicalCategory!)
            .Select(group => new ProjectBriefingSummaryPoint(group.Key, group.Count()))
            .OrderByDescending(point => point.Count)
            .ThenBy(point => point.Label)
            .ToList();

        return new ProjectBriefingPresentationSummary
        {
            ProjectCount = projects.Count,
            OngoingCount = projects.Count(project => project.LifecycleStatus == ProjectLifecycleStatus.Active),
            CompletedCount = projects.Count(project => project.LifecycleStatus == ProjectLifecycleStatus.Completed),
            TotalCostRdInRupees = projects.Sum(project => project.CostRd.AmountInRupees ?? 0m),
            CostRdRecordedCount = projects.Count(project => project.CostRd.IsAvailable),
            TotalIpaCostInRupees = projects.Sum(project => project.IpaCost.AmountInRupees ?? 0m),
            IpaCostRecordedCount = projects.Count(project => project.IpaCost.IsAvailable),
            TotalProliferationCostInRupees = projects.Sum(project => project.ProliferationCost.AmountInRupees ?? 0m),
            ProliferationCostRecordedCount = projects.Count(project => project.ProliferationCost.IsAvailable),
            MissingExternalStatusCount = projects.Count(project => string.Equals(project.ExternalStatus, "No external status recorded", StringComparison.Ordinal)),
            MissingPhotoCount = projects.Count(project => !project.CoverPhotoIsReady),
            StageSummary = stageSummary,
            ProjectCategorySummary = projectCategorySummary,
            TechnicalCategorySummary = technicalCategorySummary
        };
    }

    private static ProjectBriefingSlideEstimateVm BuildSlideEstimate(
        ProjectBriefingLayout layout,
        bool includeCoverSlide,
        bool includePortfolioSummarySlide,
        ProjectBriefingPresentationMode presentationMode,
        bool includeStageSummary,
        bool includeProjectCategorySummary,
        bool includeTechnicalCategorySummary,
        ProjectBriefingCostMode costMode,
        ProjectBriefingNarrativeMode narrativeMode,
        IReadOnlyList<ProjectBriefingProjectVm> projects)
    {
        var introductorySlides = (includeCoverSlide ? 1 : 0) + (includePortfolioSummarySlide ? 1 : 0);
        if (layout == ProjectBriefingLayout.ProjectUpdateSheet)
        {
            return new ProjectBriefingSlideEstimateVm
            {
                CoverAndPortfolioSlides = introductorySlides,
                ProjectUpdateSheetSlides = projects.Count,
                TotalSlides = introductorySlides + projects.Count
            };
        }

        var summarySlides = includeStageSummary ? 2 : 0;
        if (includeProjectCategorySummary)
        {
            summarySlides += Math.Max(1, (int)Math.Ceiling(projects
                .Select(project => project.ProjectCategory ?? "Not categorised")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() / 10d));
        }
        if (includeTechnicalCategorySummary)
        {
            summarySlides += Math.Max(1, (int)Math.Ceiling(projects
                .Select(project => project.TechnicalCategory ?? "Not categorised")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() / 10d));
        }

        var executiveSlides = presentationMode is ProjectBriefingPresentationMode.ExecutiveTable
            or ProjectBriefingPresentationMode.Combined
            ? ProjectBriefingTablePagination.Paginate(
                projects,
                costMode,
                project => ProjectBriefingTablePagination.Measure(
                    project.ProjectName,
                    project.PresentStage,
                    project.ExternalStatus,
                    project.CostRd.IsAvailable && !string.IsNullOrWhiteSpace(project.CostRd.BasisDisplay),
                    hasProliferationCostBasis: false))
                .Count
            : 0;

        var includesDetailedSlides = presentationMode is ProjectBriefingPresentationMode.DetailedProjects
            or ProjectBriefingPresentationMode.Combined;
        var includeCapabilities = narrativeMode is ProjectBriefingNarrativeMode.CapabilityOverview
            or ProjectBriefingNarrativeMode.Both;
        var includeProjectBrief = narrativeMode is ProjectBriefingNarrativeMode.ProjectBrief
            or ProjectBriefingNarrativeMode.Both;
        var detailSlides = includesDetailedSlides && includeCapabilities ? projects.Count : 0;
        var capabilityContinuationSlides = includesDetailedSlides && includeCapabilities
            ? projects.Sum(project =>
                ProjectBriefingCapabilityPaginator
                    .Paginate(project.BriefDescription)
                    .ContinuationSlideCount)
            : 0;
        var projectBriefSlides = includesDetailedSlides && includeProjectBrief ? projects.Count : 0;

        return new ProjectBriefingSlideEstimateVm
        {
            CoverAndPortfolioSlides = introductorySlides,
            SummarySlides = summarySlides,
            ExecutiveTableSlides = executiveSlides,
            DetailedProjectSlides = detailSlides,
            CapabilityContinuationSlides = capabilityContinuationSlides,
            ProjectBriefSlides = projectBriefSlides,
            TotalSlides = introductorySlides
                + summarySlides
                + executiveSlides
                + detailSlides
                + capabilityContinuationSlides
                + projectBriefSlides
        };
    }

    private static int? ResolveCoverPhotoId(DeckItemSnapshot item)
    {
        if (item.CoverPhotoId.HasValue && item.Photos.Any(photo => photo.Id == item.CoverPhotoId.Value))
        {
            return item.CoverPhotoId;
        }

        return item.Photos
            .OrderByDescending(photo => photo.IsCover)
            .ThenBy(photo => photo.IsLowResolution)
            .ThenBy(photo => photo.Ordinal)
            .ThenBy(photo => photo.Id)
            .Select(photo => (int?)photo.Id)
            .FirstOrDefault();
    }

    private static string ResolveStageName(DeckItemSnapshot item)
    {
        if (item.LifecycleStatus == ProjectLifecycleStatus.Completed)
        {
            return "Completed";
        }

        var code = ResolveStageCode(item);
        return StageCodes.DisplayNameOf(item.WorkflowVersion, code);
    }

    private static string ResolveStageCode(DeckItemSnapshot item)
    {
        if (item.LifecycleStatus == ProjectLifecycleStatus.Completed)
        {
            return ProjectBriefingStageOrder.CompletedCode;
        }

        var codes = ProcurementWorkflow.StageCodesFor(item.WorkflowVersion);
        var statusByCode = item.Stages
            .GroupBy(stage => stage.StageCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(stage => stage.SortOrder).First().Status,
                StringComparer.OrdinalIgnoreCase);
        var statuses = codes
            .Select(code => statusByCode.GetValueOrDefault(code, StageStatus.NotStarted))
            .ToArray();
        var index = OngoingStagePresentationPolicy.ResolveCurrentStageIndex(statuses);
        return codes[index];
    }

    private static string Encode(byte[] value)
        => value is { Length: > 0 } ? Convert.ToBase64String(value) : string.Empty;

    private sealed record DeckHeaderSnapshot(
        long Id,
        string Name,
        string? Description,
        ProjectBriefingLayout Layout,
        ProjectBriefingPresentationMode PresentationMode,
        ProjectBriefingCostMode CostMode,
        ProjectBriefingNarrativeMode NarrativeMode,
        ProjectBriefingPresentationTheme PresentationTheme,
        ProjectBriefingBrandingScope BrandingScope,
        bool IncludeCoverSlide,
        bool IncludePortfolioSummarySlide,
        bool IncludeStageSummary,
        bool IncludeProjectCategorySummary,
        bool IncludeTechnicalCategorySummary,
        string? SelectionRulesJson,
        string? HandlingMarking,
        DateTimeOffset UpdatedAtUtc,
        string CreatedByDisplay,
        string LastModifiedByDisplay,
        byte[] RowVersion);

    private sealed record DeckItemBaseSnapshot(
        long ItemId,
        int ProjectId,
        int SortOrder,
        string? BriefDescriptionOverride,
        string ProjectName,
        string? ProjectDescription,
        string? ProjectBrief,
        ProjectLifecycleStatus LifecycleStatus,
        DateOnly? CompletedOn,
        int? CompletedYear,
        short? CompletedMonth,
        bool IsDeleted,
        bool IsArchived,
        string WorkflowVersion,
        string? ProjectCategory,
        string? TechnicalCategory,
        int? CoverPhotoId);

    private sealed record DeckSnapshot(
        long Id,
        string Name,
        string? Description,
        ProjectBriefingLayout Layout,
        ProjectBriefingPresentationMode PresentationMode,
        ProjectBriefingCostMode CostMode,
        ProjectBriefingNarrativeMode NarrativeMode,
        ProjectBriefingPresentationTheme PresentationTheme,
        ProjectBriefingBrandingScope BrandingScope,
        bool IncludeCoverSlide,
        bool IncludePortfolioSummarySlide,
        bool IncludeStageSummary,
        bool IncludeProjectCategorySummary,
        bool IncludeTechnicalCategorySummary,
        ProjectBriefingUpdateSheetOptions UpdateSheetOptions,
        ProjectBriefingStandardSlideOptions StandardSlideOptions,
        string? HandlingMarking,
        DateTimeOffset UpdatedAtUtc,
        string CreatedByDisplay,
        string LastModifiedByDisplay,
        byte[] RowVersion,
        IReadOnlyList<DeckItemSnapshot> Items);

    private sealed record DeckItemSnapshot(
        long ItemId,
        int ProjectId,
        int SortOrder,
        string? BriefDescriptionOverride,
        string ProjectName,
        string? ProjectDescription,
        string? ProjectBrief,
        ProjectLifecycleStatus LifecycleStatus,
        DateOnly? CompletedOn,
        int? CompletedYear,
        short? CompletedMonth,
        bool IsDeleted,
        bool IsArchived,
        string WorkflowVersion,
        string? ProjectCategory,
        string? TechnicalCategory,
        int? CoverPhotoId,
        IReadOnlyList<string> CapabilityStatements,
        IReadOnlyList<StageSnapshot> Stages,
        IReadOnlyList<PhotoSnapshot> Photos);

    private sealed record CapabilityDatabaseSnapshot(
        int ProjectId,
        string Statement,
        int DisplayOrder);

    private sealed record StageDatabaseSnapshot(
        int ProjectId,
        string StageCode,
        StageStatus Status,
        int SortOrder);

    private sealed record PhotoDatabaseSnapshot(
        int ProjectId,
        int Id,
        bool IsCover,
        bool IsLowResolution,
        int Ordinal);

    private sealed record StageSnapshot(string StageCode, StageStatus Status, int SortOrder);
    private sealed record PhotoSnapshot(int Id, bool IsCover, bool IsLowResolution, int Ordinal);
}

public static partial class ProjectBriefingTextNormalizer
{
    [GeneratedRegex(@"!\[[^\]]*\]\([^\)]*\)", RegexOptions.Compiled)]
    private static partial Regex MarkdownImageRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\([^\)]*\)", RegexOptions.Compiled)]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"(^|\n)\s{0,3}(#{1,6}|[-*+]\s+|\d+[.)]\s+|>\s*)", RegexOptions.Compiled)]
    private static partial Regex MarkdownPrefixRegex();

    [GeneratedRegex(@"[`*_~]{1,3}", RegexOptions.Compiled)]
    private static partial Regex MarkdownDecorationRegex();

    [GeneratedRegex(@"[^\S\r\n]+", RegexOptions.Compiled)]
    private static partial Regex HorizontalWhitespaceRegex();

    [GeneratedRegex(@"\n{3,}", RegexOptions.Compiled)]
    private static partial Regex ExcessiveNewlinesRegex();

    public static string NormalizeProjectBrief(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Project brief not recorded.";
        }

        var normalized = Normalize(value, ProjectFieldLimits.ProjectBriefMaxLength);
        return string.Equals(normalized, "Brief description not recorded.", StringComparison.OrdinalIgnoreCase)
            ? "Project brief not recorded."
            : normalized;
    }

    public static string NormalizeFull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Brief description not recorded.";
        }

        var normalized = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        normalized = MarkdownImageRegex().Replace(normalized, string.Empty);
        normalized = MarkdownLinkRegex().Replace(normalized, "$1");
        normalized = HorizontalWhitespaceRegex().Replace(normalized, " ");
        normalized = string.Join(
            "\n",
            normalized.Split('\n').Select(line => line.TrimEnd()));
        normalized = ExcessiveNewlinesRegex().Replace(normalized, "\n\n").Trim();

        return string.IsNullOrWhiteSpace(normalized)
            ? "Brief description not recorded."
            : normalized;
    }

    public static string Normalize(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Brief description not recorded.";
        }

        var normalized = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        normalized = MarkdownImageRegex().Replace(normalized, string.Empty);
        normalized = MarkdownLinkRegex().Replace(normalized, "$1");
        normalized = MarkdownPrefixRegex().Replace(normalized, "$1");
        normalized = MarkdownDecorationRegex().Replace(normalized, string.Empty);
        normalized = HorizontalWhitespaceRegex().Replace(normalized, " ");
        normalized = string.Join(
            "\n",
            normalized.Split('\n').Select(line => line.Trim()));
        normalized = ExcessiveNewlinesRegex().Replace(normalized, "\n\n").Trim();

        if (normalized.Length <= maximumLength)
        {
            return normalized;
        }

        var boundary = normalized.LastIndexOfAny(
            new[] { ' ', '\n', '.', ';', ':' },
            Math.Max(1, maximumLength - 2));
        var take = boundary >= maximumLength * .72
            ? boundary
            : Math.Max(1, maximumLength - 1);
        return normalized[..take].TrimEnd() + "…";
    }
}
