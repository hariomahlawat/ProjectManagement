using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Ffc;
using ProjectManagement.Services.Ffc.Presentation;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.ProjectBriefings.Presentation;

namespace ProjectManagement.Services.ProjectBriefings;

public interface IProjectBriefingDataService
{
    Task<ProjectBriefingDeckVm?> GetDeckAsync(
        long deckId,
        string requestingUserId,
        CancellationToken cancellationToken = default,
        bool includeFfcFootprintSummary = false);

    Task<ProjectBriefingPresentationData> BuildPresentationDataAsync(
        long deckId,
        string requestingUserId,
        CancellationToken cancellationToken = default);

    // The project's photographs, for choosing the one a deck shows; null when the project is not
    // in the deck.
    Task<IReadOnlyList<ProjectBriefingPhotoOptionVm>?> GetPhotoOptionsAsync(
        long deckId,
        int projectId,
        CancellationToken cancellationToken = default);

    // What each project's slides would show today (stage, external status, costs), from the same
    // resolvers as generation, for telling when a deck's data moved. Deleted and archived
    // projects are left out: generation leaves them out too.
    Task<IReadOnlyDictionary<int, ProjectBriefingCurrentProjectFacts>> GetCurrentSlideFactsAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<int>? milestoneProjectIds = null);

    // The decks with DataMovement set wherever their last generation kept slide facts: one pass
    // over every deck's projects, for the deck library.
    Task<IReadOnlyList<ProjectBriefingDeckSummaryVm>> WithDataMovementAsync(
        IReadOnlyList<ProjectBriefingDeckSummaryVm> decks,
        CancellationToken cancellationToken = default);
}

public sealed class ProjectBriefingDataService : IProjectBriefingDataService
{
    private readonly ApplicationDbContext _db;
    private readonly IProjectBriefingCostResolver _costResolver;
    private readonly IProjectBriefingExternalStatusService _externalStatusService;
    private readonly IProjectBriefingPhotoLoader _photoLoader;
    private readonly IProjectBriefingUpdateSheetFactsResolver _updateSheetFactsResolver;
    private readonly IProjectBriefingInstitutionalProfileService _institutionalProfileService;
    private readonly IFfcFootprintService _ffcFootprintService;
    private readonly IFfcPresentationMapRenderer _ffcMapRenderer;
    private readonly IClock _clock;
    private readonly ILogger<ProjectBriefingDataService> _logger;

    public ProjectBriefingDataService(
        ApplicationDbContext db,
        IProjectBriefingCostResolver costResolver,
        IProjectBriefingExternalStatusService externalStatusService,
        IProjectBriefingPhotoLoader photoLoader,
        IProjectBriefingUpdateSheetFactsResolver updateSheetFactsResolver,
        IProjectBriefingInstitutionalProfileService institutionalProfileService,
        IFfcFootprintService ffcFootprintService,
        IFfcPresentationMapRenderer ffcMapRenderer,
        IClock clock,
        ILogger<ProjectBriefingDataService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _costResolver = costResolver ?? throw new ArgumentNullException(nameof(costResolver));
        _externalStatusService = externalStatusService ?? throw new ArgumentNullException(nameof(externalStatusService));
        _photoLoader = photoLoader ?? throw new ArgumentNullException(nameof(photoLoader));
        _updateSheetFactsResolver = updateSheetFactsResolver ?? throw new ArgumentNullException(nameof(updateSheetFactsResolver));
        _institutionalProfileService = institutionalProfileService ?? throw new ArgumentNullException(nameof(institutionalProfileService));
        _ffcFootprintService = ffcFootprintService ?? throw new ArgumentNullException(nameof(ffcFootprintService));
        _ffcMapRenderer = ffcMapRenderer ?? throw new ArgumentNullException(nameof(ffcMapRenderer));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ProjectBriefingDeckVm?> GetDeckAsync(
        long deckId,
        string requestingUserId,
        CancellationToken cancellationToken = default,
        bool includeFfcFootprintSummary = false)
    {
        var snapshot = await LoadSnapshotAsync(deckId, requestingUserId, cancellationToken);
        if (snapshot is null)
        {
            return null;
        }

        var projects = await BuildProjectsAsync(snapshot.Items, snapshot.Layout, snapshot.UpdateSheetOptions, cancellationToken);
        var presentedProjects = projects.Where(project => project.IsPresented).ToArray();
        // The global FFC footprint is only shown in the page's FFC slide editor; in-place saves and
        // preview rebuilds do not need it.
        var ffcPreview = includeFfcFootprintSummary
            ? await LoadFfcFootprintPreviewAsync(snapshot.Id, cancellationToken)
            : new FfcFootprintSummary(0, 0, 0, 0, 0, 0);
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
            ClosingSlideType = snapshot.ClosingSlideType,
            InstitutionalProfileOptions = snapshot.InstitutionalProfileOptions,
            RoleCharterOptions = snapshot.RoleCharterOptions,
            FfcGlobalFootprintOptions = snapshot.FfcGlobalFootprintOptions,
            FfcFootprintPreviewSummary = ffcPreview,
            AdditionalSlideOrder = snapshot.AdditionalSlideOrder,
            BrandingScope = snapshot.BrandingScope,
            IncludeCoverSlide = snapshot.IncludeCoverSlide,
            IncludePortfolioSummarySlide = snapshot.IncludePortfolioSummarySlide,
            IncludeStageSummary = snapshot.IncludeStageSummary,
            IncludeProjectCategorySummary = snapshot.IncludeProjectCategorySummary,
            IncludeTechnicalCategorySummary = snapshot.IncludeTechnicalCategorySummary,
            UpdateSheetOptions = snapshot.UpdateSheetOptions,
            HandlingMarking = snapshot.HandlingMarking,
            Preset = snapshot.Preset,
            PresetAdjusted = snapshot.Preset is { } preset
                && !ProjectBriefingDeckPresets.Matches(preset, new ProjectBriefingDeckPresetState(
                    snapshot.Layout,
                    snapshot.PresentationMode,
                    snapshot.NarrativeMode,
                    snapshot.CostMode,
                    snapshot.StandardSlideOptions.ShowPresentStage,
                    snapshot.StandardSlideOptions.ShowPresentStatus,
                    snapshot.IncludeStageSummary,
                    snapshot.PresentationTheme,
                    snapshot.ClosingSlideType,
                    snapshot.UpdateSheetOptions.Rows,
                    snapshot.UpdateSheetOptions.HideEmptyValues,
                    snapshot.IncludeCoverSlide,
                    snapshot.IncludePortfolioSummarySlide,
                    snapshot.AdditionalSlideOrder.Contains(ProjectBriefingAdditionalSlideType.InstitutionalProfile)
                        && snapshot.InstitutionalProfileOptions.IncludeSlide)),
            RowVersion = Encode(snapshot.RowVersion),
            UpdatedAtUtc = snapshot.UpdatedAtUtc,
            CreatedByDisplay = snapshot.CreatedByDisplay,
            LastModifiedByDisplay = snapshot.LastModifiedByDisplay,
            Projects = projects,
            Readiness = BuildReadiness(presentedProjects),
            ContentGaps = ProjectBriefingContentGaps.Resolve(
                presentedProjects,
                new ProjectBriefingContentGapSettings(
                    snapshot.Layout,
                    snapshot.PresentationMode,
                    snapshot.CostMode,
                    snapshot.NarrativeMode,
                    snapshot.StandardSlideOptions,
                    snapshot.UpdateSheetOptions)),
            SlideEstimate = BuildSlideEstimate(snapshot.Layout, snapshot.IncludeCoverSlide, snapshot.IncludePortfolioSummarySlide,
                snapshot.PresentationMode, snapshot.IncludeStageSummary, snapshot.IncludeProjectCategorySummary,
                snapshot.IncludeTechnicalCategorySummary, snapshot.StandardSlideOptions,
                snapshot.UpdateSheetOptions,
                snapshot.InstitutionalProfileOptions, snapshot.RoleCharterOptions,
                snapshot.FfcGlobalFootprintOptions, ffcPreview.CountryCount,
                snapshot.AdditionalSlideOrder,
                snapshot.CostMode, snapshot.NarrativeMode, presentedProjects)
        };
    }

    public async Task<IReadOnlyList<ProjectBriefingPhotoOptionVm>?> GetPhotoOptionsAsync(
        long deckId,
        int projectId,
        CancellationToken cancellationToken = default)
    {
        var inDeck = await _db.Set<ProjectBriefingDeckItem>()
            .AsNoTracking()
            .AnyAsync(item => item.DeckId == deckId && item.ProjectId == projectId, cancellationToken);
        if (!inDeck)
        {
            return null;
        }

        var coverPhotoId = await _db.Projects
            .AsNoTracking()
            .Where(project => project.Id == projectId)
            .Select(project => project.CoverPhotoId)
            .FirstOrDefaultAsync(cancellationToken);
        var photos = await _db.ProjectPhotos
            .AsNoTracking()
            .Where(photo => photo.ProjectId == projectId)
            .OrderByDescending(photo => photo.IsCover)
            .ThenBy(photo => photo.Ordinal)
            .ThenBy(photo => photo.Id)
            .Select(photo => new { photo.Id, photo.Caption, photo.IsCover, photo.IsLowResolution, photo.Ordinal, photo.Version })
            .ToListAsync(cancellationToken);
        // The photo a deck shows when it has no choice of its own: the same rule as the slides.
        var defaultPhotoId = coverPhotoId is { } cover && photos.Any(photo => photo.Id == cover)
            ? cover
            : photos
                .OrderByDescending(photo => photo.IsCover)
                .ThenBy(photo => photo.IsLowResolution)
                .ThenBy(photo => photo.Ordinal)
                .ThenBy(photo => photo.Id)
                .Select(photo => (int?)photo.Id)
                .FirstOrDefault();
        return photos
            .Select(photo => new ProjectBriefingPhotoOptionVm
            {
                PhotoId = photo.Id,
                Caption = string.IsNullOrWhiteSpace(photo.Caption) ? null : photo.Caption.Trim(),
                IsDefault = photo.Id == defaultPhotoId,
                IsLowResolution = photo.IsLowResolution,
                ThumbnailUrl = $"/Projects/{projectId}/Photos/{photo.Id}/View/sm?v={photo.Version}"
            })
            .ToArray();
    }

    public async Task<IReadOnlyList<ProjectBriefingDeckSummaryVm>> WithDataMovementAsync(
        IReadOnlyList<ProjectBriefingDeckSummaryVm> decks,
        CancellationToken cancellationToken = default)
    {
        var projectIds = decks
            .Where(deck => deck.GeneratedFacts is { Facts.Count: > 0 })
            .SelectMany(deck => deck.GeneratedFacts!.Facts.Select(fact => fact.ProjectId))
            .Distinct()
            .ToArray();
        if (projectIds.Length == 0)
        {
            return decks
                .Select(deck => deck.GeneratedFacts is null ? deck : deck with { DataMovement = ProjectBriefingDataMovement.None })
                .ToList();
        }

        // The update-sheet facts (PDC date) are resolved only where a generation drew that row.
        var milestoneProjectIds = decks
            .Where(deck => deck.GeneratedFacts is not null)
            .SelectMany(deck => deck.GeneratedFacts!.Facts)
            .Where(fact => fact.Shown.HasFlag(ProjectBriefingShownFacts.Milestone))
            .Select(fact => fact.ProjectId)
            .Distinct()
            .ToArray();
        var current = await GetCurrentSlideFactsAsync(projectIds, cancellationToken, milestoneProjectIds);
        return decks
            .Select(deck => deck.GeneratedFacts is null
                ? deck
                : deck with { DataMovement = ProjectBriefingSlideFacts.Compare(deck.GeneratedFacts, current) })
            .ToList();
    }

    public async Task<IReadOnlyDictionary<int, ProjectBriefingCurrentProjectFacts>> GetCurrentSlideFactsAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<int>? milestoneProjectIds = null)
    {
        var ids = projectIds.Where(id => id > 0).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<int, ProjectBriefingCurrentProjectFacts>();
        }

        var projects = await _db.Projects
            .AsNoTracking()
            .Where(project => ids.Contains(project.Id) && !project.IsDeleted && !project.IsArchived)
            .Select(project => new
            {
                project.Id,
                project.Name,
                project.LifecycleStatus,
                project.WorkflowVersion,
                project.CompletedOn,
                project.CompletedYear,
                project.CompletedMonth
            })
            .ToListAsync(cancellationToken);
        if (projects.Count == 0)
        {
            return new Dictionary<int, ProjectBriefingCurrentProjectFacts>();
        }

        var liveIds = projects.Select(project => project.Id).ToArray();
        var stagesByProject = (await _db.ProjectStages
                .AsNoTracking()
                .Where(stage => liveIds.Contains(stage.ProjectId))
                .Select(stage => new StageDatabaseSnapshot(stage.ProjectId, stage.StageCode, stage.Status, stage.SortOrder))
                .ToListAsync(cancellationToken))
            .GroupBy(stage => stage.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<StageSnapshot>)group
                    .Select(stage => new StageSnapshot(stage.StageCode, stage.Status, stage.SortOrder))
                    .OrderBy(stage => stage.SortOrder)
                    .ToList());
        var costs = await _costResolver.ResolveCostsAsync(liveIds, cancellationToken);
        var proliferation = await _costResolver.ResolveProliferationCostAsync(liveIds, cancellationToken);
        var externalStatuses = await _externalStatusService.GetLatestAsync(liveIds, cancellationToken);
        var milestoneIds = milestoneProjectIds?.Where(liveIds.Contains).Distinct().ToArray() ?? Array.Empty<int>();
        var updateSheetFacts = milestoneIds.Length == 0
            ? new Dictionary<int, ProjectBriefingUpdateSheetFacts>()
            : await _updateSheetFactsResolver.ResolveAsync(milestoneIds, cancellationToken);

        return projects.ToDictionary(
            project => project.Id,
            project =>
            {
                var external = externalStatuses.GetValueOrDefault(project.Id);
                return new ProjectBriefingCurrentProjectFacts(
                    project.Id,
                    project.Name,
                    project.LifecycleStatus,
                    ResolveStageCode(
                        project.LifecycleStatus,
                        project.WorkflowVersion,
                        stagesByProject.GetValueOrDefault(project.Id) ?? Array.Empty<StageSnapshot>()),
                    external?.Body,
                    external?.EventDate,
                    costs.CostRd.GetValueOrDefault(project.Id) ?? ProjectBriefingCostValue.Missing(),
                    costs.Ipa.GetValueOrDefault(project.Id) ?? ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.IPA),
                    proliferation.GetValueOrDefault(project.Id) ?? ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.Proliferation),
                    updateSheetFacts.GetValueOrDefault(project.Id)?.DevelopmentPdcDate,
                    BuildCompletionStatusDisplay(project.LifecycleStatus, project.CompletedOn, project.CompletedYear, project.CompletedMonth));
            });
    }

    public async Task<ProjectBriefingPresentationData> BuildPresentationDataAsync(
        long deckId,
        string requestingUserId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await LoadSnapshotAsync(deckId, requestingUserId, cancellationToken)
            ?? throw new KeyNotFoundException("The saved deck was not found.");
        var allProjectVms = await BuildProjectsAsync(snapshot.Items, snapshot.Layout, snapshot.UpdateSheetOptions, cancellationToken);
        var projectVms = allProjectVms.Where(project => project.IsPresented).ToArray();
        if (projectVms.Length == 0)
        {
            throw new InvalidOperationException(allProjectVms.Count == 0
                ? "Add at least one project before generating the PowerPoint deck."
                : allProjectVms.All(project => project.IsDeletedOrArchived)
                    ? "Every project in this deck is deleted or archived. Add a current project before generating the PowerPoint deck."
                    : "Every project in this deck is left out. Show at least one as slides or a table row before generating the PowerPoint deck.");
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
                TableRowOnly = project.Presentation == ProjectBriefingItemPresentation.TableRowOnly,
                SpeakerNotes = project.SpeakerNotes,
                CoverPhotoId = project.CoverPhotoId,
                CoverPhotoIsReady = project.HasCoverPhoto
            }));

        var summary = BuildPresentationSummary(projects);
        var institutionalProfile = snapshot.AdditionalSlideOrder.Contains(ProjectBriefingAdditionalSlideType.InstitutionalProfile)
            ? await _institutionalProfileService.BuildAsync(
                snapshot.InstitutionalProfileOptions,
                cancellationToken)
            : null;
        var roleCharter = snapshot.AdditionalSlideOrder.Contains(ProjectBriefingAdditionalSlideType.RoleAndCharter)
            ? BuildRoleCharterData(snapshot.RoleCharterOptions)
            : null;
        var ffcGlobalFootprint = snapshot.AdditionalSlideOrder.Contains(ProjectBriefingAdditionalSlideType.FfcGlobalFootprint)
            ? await BuildFfcGlobalFootprintDataAsync(snapshot.FfcGlobalFootprintOptions, cancellationToken)
            : null;
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
            ClosingSlideType = snapshot.ClosingSlideType,
            InstitutionalProfileOptions = snapshot.InstitutionalProfileOptions,
            RoleCharterOptions = snapshot.RoleCharterOptions,
            FfcGlobalFootprintOptions = snapshot.FfcGlobalFootprintOptions,
            AdditionalSlideOrder = snapshot.AdditionalSlideOrder,
            BrandingScope = snapshot.BrandingScope,
            IncludeCoverSlide = snapshot.IncludeCoverSlide,
            IncludePortfolioSummarySlide = snapshot.IncludePortfolioSummarySlide,
            IncludeStageSummary = snapshot.Layout == ProjectBriefingLayout.StandardBriefing && snapshot.IncludeStageSummary,
            IncludeProjectCategorySummary = snapshot.Layout == ProjectBriefingLayout.StandardBriefing
                && snapshot.IncludeProjectCategorySummary
                && ShouldRenderCategorySummary(summary.ProjectCategorySummary),
            IncludeTechnicalCategorySummary = snapshot.Layout == ProjectBriefingLayout.StandardBriefing
                && snapshot.IncludeTechnicalCategorySummary
                && ShouldRenderCategorySummary(summary.TechnicalCategorySummary),
            UpdateSheetOptions = snapshot.UpdateSheetOptions,
            HandlingMarking = snapshot.HandlingMarking,
            GeneratedAtUtc = _clock.UtcNow.ToUniversalTime(),
            Projects = projects,
            Summary = summary,
            InstitutionalProfile = institutionalProfile,
            RoleCharter = roleCharter,
            FfcGlobalFootprint = ffcGlobalFootprint
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

        var deck = await _db.Set<ProjectBriefingDeck>().Live()
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
        var closingSlideType = deckConfiguration.ClosingSlideType;
        var institutionalProfileOptions = deckConfiguration.InstitutionalProfileOptions;
        var roleCharterOptions = deckConfiguration.RoleCharterOptions;
        var ffcGlobalFootprintOptions = deckConfiguration.FfcGlobalFootprintOptions;
        var additionalSlideOrder = deckConfiguration.AdditionalSlideOrder;

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
                item.Presentation,
                item.PhotoId,
                item.SpeakerNotes,
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
                closingSlideType,
                institutionalProfileOptions,
                roleCharterOptions,
                ffcGlobalFootprintOptions,
                additionalSlideOrder,
                deck.HandlingMarking,
                deck.UpdatedAtUtc,
                deck.CreatedByDisplay,
                deck.LastModifiedByDisplay,
                deck.RowVersion,
                Array.Empty<DeckItemSnapshot>())
            {
                Preset = deckConfiguration.Preset
            };
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
                item.Presentation,
                item.PhotoId,
                item.SpeakerNotes,
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
            closingSlideType,
            institutionalProfileOptions,
            roleCharterOptions,
            ffcGlobalFootprintOptions,
            additionalSlideOrder,
            deck.HandlingMarking,
            deck.UpdatedAtUtc,
            deck.CreatedByDisplay,
            deck.LastModifiedByDisplay,
            deck.RowVersion,
            items)
            {
                Preset = deckConfiguration.Preset
            };
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
                    IsDeletedOrArchived = item.IsDeleted || item.IsArchived,
                    Presentation = item.Presentation,
                    ChosenPhotoId = ResolveChosenPhotoId(item),
                    SpeakerNotes = item.SpeakerNotes,
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
        => BuildCompletionStatusDisplay(item.LifecycleStatus, item.CompletedOn, item.CompletedYear, item.CompletedMonth);

    private static string? BuildCompletionStatusDisplay(
        ProjectLifecycleStatus lifecycleStatus,
        DateOnly? completedOn,
        int? completedYear,
        short? completedMonth)
    {
        if (lifecycleStatus != ProjectLifecycleStatus.Completed) return null;

        var completion = ProjectCompletionFormatter.Format(
            completedOn,
            completedYear,
            completedMonth,
            unknownText: string.Empty);
        if (string.IsNullOrWhiteSpace(completion)) return "Project completed";

        var precision = ProjectCompletionFormatter.InferPrecision(
            completedOn,
            completedYear,
            completedMonth);
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
        var ongoingStageSummary = ProjectBriefingStageOrder.BuildSummary(
            projects
                .Where(project => project.LifecycleStatus == ProjectLifecycleStatus.Active)
                .Select(project => project.PresentStageOrder));

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
            CancelledCount = projects.Count(project => project.LifecycleStatus == ProjectLifecycleStatus.Cancelled),
            TotalCostRdInRupees = projects.Sum(project => project.CostRd.AmountInRupees ?? 0m),
            CostRdRecordedCount = projects.Count(project => project.CostRd.IsAvailable),
            TotalIpaCostInRupees = projects.Sum(project => project.IpaCost.AmountInRupees ?? 0m),
            IpaCostRecordedCount = projects.Count(project => project.IpaCost.IsAvailable),
            TotalProliferationCostInRupees = projects.Sum(project => project.ProliferationCost.AmountInRupees ?? 0m),
            ProliferationCostRecordedCount = projects.Count(project => project.ProliferationCost.IsAvailable),
            MissingExternalStatusCount = projects.Count(project => string.Equals(project.ExternalStatus, "No external status recorded", StringComparison.Ordinal)),
            MissingPhotoCount = projects.Count(project => !project.TableRowOnly && !project.CoverPhotoIsReady),
            StageSummary = stageSummary,
            OngoingStageSummary = ongoingStageSummary,
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
        ProjectBriefingStandardSlideOptions standardSlideOptions,
        ProjectBriefingUpdateSheetOptions updateSheetOptions,
        ProjectBriefingInstitutionalProfileOptions institutionalProfileOptions,
        ProjectBriefingRoleCharterOptions roleCharterOptions,
        ProjectBriefingFfcGlobalFootprintOptions ffcGlobalFootprintOptions,
        int ffcCountryCount,
        IReadOnlyList<ProjectBriefingAdditionalSlideType> additionalSlideOrder,
        ProjectBriefingCostMode costMode,
        ProjectBriefingNarrativeMode narrativeMode,
        IReadOnlyList<ProjectBriefingProjectVm> projects)
    {
        var coverAndPortfolioSlides = (includeCoverSlide ? 1 : 0)
            + (includePortfolioSummarySlide ? 1 : 0);
        var institutionalProfileSlides = institutionalProfileOptions.IncludeSlide
            && additionalSlideOrder.Contains(ProjectBriefingAdditionalSlideType.InstitutionalProfile)
                ? 1
                : 0;
        var roleCharterSlides = additionalSlideOrder.Contains(ProjectBriefingAdditionalSlideType.RoleAndCharter)
            ? ProjectBriefingRoleCharterPaginator.EstimateSlideCount(roleCharterOptions)
            : 0;
        var ffcGlobalFootprintSlides = additionalSlideOrder.Contains(ProjectBriefingAdditionalSlideType.FfcGlobalFootprint)
            ? ffcGlobalFootprintOptions.EstimateSlideCount(ffcCountryCount)
            : 0;
        var ffcCountryWiseBreakdownSlides = Math.Max(0, ffcGlobalFootprintSlides - (ffcGlobalFootprintSlides > 0 ? 1 : 0));
        var configuredSlides = coverAndPortfolioSlides + institutionalProfileSlides + roleCharterSlides + ffcGlobalFootprintSlides;
        // Table row only projects get no slides of their own; a deck without a status table lists
        // them in a short table of their own (the composer's AddTableRowOnlySlides).
        var slideProjects = projects
            .Where(project => project.Presentation != ProjectBriefingItemPresentation.TableRowOnly)
            .ToArray();
        int CountTablePages(IReadOnlyList<ProjectBriefingProjectVm> tableProjects) => tableProjects.Count == 0
            ? 0
            : ProjectBriefingTablePagination.Paginate(
                tableProjects,
                ProjectBriefingContentGaps.EffectiveTableCostMode(layout, costMode),
                project => ProjectBriefingTablePagination.Measure(
                    project.ProjectName,
                    project.PresentStage,
                    project.ExternalStatus,
                    project.CostRd.IsAvailable && !string.IsNullOrWhiteSpace(project.CostRd.BasisDisplay),
                    hasProliferationCostBasis: false))
                .Count;
        var tableRowOnlyPages = CountTablePages(projects
            .Where(project => project.Presentation == ProjectBriefingItemPresentation.TableRowOnly)
            .ToArray());
        if (layout == ProjectBriefingLayout.ProjectUpdateSheet)
        {
            var projectUpdateSheetSlides = slideProjects.Sum(project =>
                ProjectBriefingUpdateSheetPlanner.EstimateSlideCount(project, updateSheetOptions));
            return new ProjectBriefingSlideEstimateVm
            {
                CoverAndPortfolioSlides = coverAndPortfolioSlides,
                ProjectUpdateSheetSlides = projectUpdateSheetSlides,
                InstitutionalProfileSlides = institutionalProfileSlides,
                RoleCharterSlides = roleCharterSlides,
                FfcGlobalFootprintSlides = ffcGlobalFootprintSlides,
                FfcCountryWiseBreakdownSlides = ffcCountryWiseBreakdownSlides,
                ExecutiveTableSlides = tableRowOnlyPages,
                ClosingSlides = 1,
                TotalSlides = configuredSlides + projectUpdateSheetSlides + tableRowOnlyPages + 1
            };
        }

        var summarySlides = includeStageSummary
            ? 1 + (standardSlideOptions.IncludeStageDistributionTable ? 1 : 0)
            : 0;
        var projectCategoryCount = projects
            .Select(project => project.ProjectCategory ?? "Not categorised")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        if (includeProjectCategorySummary)
        {
            summarySlides += ProjectBriefingSummaryPlanning.EstimateCategorySlideCount(projectCategoryCount);
        }

        var technicalCategoryCount = projects
            .Select(project => project.TechnicalCategory ?? "Not categorised")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        if (includeTechnicalCategorySummary)
        {
            summarySlides += ProjectBriefingSummaryPlanning.EstimateCategorySlideCount(technicalCategoryCount);
        }

        var executiveSlides = presentationMode is ProjectBriefingPresentationMode.ExecutiveTable
            or ProjectBriefingPresentationMode.Combined
            ? CountTablePages(projects)
            : tableRowOnlyPages;

        var includesDetailedSlides = presentationMode is ProjectBriefingPresentationMode.DetailedProjects
            or ProjectBriefingPresentationMode.Combined;
        var includeCapabilities = narrativeMode is ProjectBriefingNarrativeMode.CapabilityOverview
            or ProjectBriefingNarrativeMode.Both;
        var includeProjectBrief = narrativeMode is ProjectBriefingNarrativeMode.ProjectBrief
            or ProjectBriefingNarrativeMode.Both;
        var detailSlides = includesDetailedSlides && includeCapabilities ? slideProjects.Length : 0;
        var capabilityContinuationSlides = includesDetailedSlides && includeCapabilities
            ? slideProjects.Sum(project =>
                ProjectBriefingCapabilityPaginator
                    .Paginate(project.BriefDescription)
                    .ContinuationSlideCount)
            : 0;
        // A long brief continues onto extra slides; the estimate assumes the standard layout.
        var projectBriefSlides = includesDetailedSlides && includeProjectBrief
            ? slideProjects.Sum(project =>
                ProjectBriefingSlideComposer.EstimateProjectBriefSlideCount(project.ProjectBrief))
            : 0;

        return new ProjectBriefingSlideEstimateVm
        {
            CoverAndPortfolioSlides = coverAndPortfolioSlides,
            SummarySlides = summarySlides,
            ExecutiveTableSlides = executiveSlides,
            DetailedProjectSlides = detailSlides,
            CapabilityContinuationSlides = capabilityContinuationSlides,
            ProjectBriefSlides = projectBriefSlides,
            InstitutionalProfileSlides = institutionalProfileSlides,
            RoleCharterSlides = roleCharterSlides,
            FfcGlobalFootprintSlides = ffcGlobalFootprintSlides,
            FfcCountryWiseBreakdownSlides = ffcCountryWiseBreakdownSlides,
            ClosingSlides = 1,
            TotalSlides = configuredSlides
                + summarySlides
                + executiveSlides
                + detailSlides
                + capabilityContinuationSlides
                + projectBriefSlides
                + 1
        };
    }

    private static bool ShouldRenderCategorySummary(
        IReadOnlyList<ProjectBriefingSummaryPoint> points)
        => ProjectBriefingSummaryPlanning.ShouldRenderCategorySummary(
            points.Count(point => point.Count > 0));

    private async Task<FfcFootprintSummary> LoadFfcFootprintPreviewAsync(
        long deckId,
        CancellationToken cancellationToken)
    {
        try
        {
            var footprint = await _ffcFootprintService.GetAsync(
                new FfcFootprintRequest(
                    Metric: FfcFootprintMetric.TotalUnits,
                    Sort: FfcFootprintSort.TotalUnits),
                cancellationToken);
            return footprint.Summary;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "FFC footprint preview could not be loaded while calculating the briefing deck. DeckId={DeckId}",
                deckId);
            return new FfcFootprintSummary(0, 0, 0, 0, 0, 0);
        }
    }

    private async Task<ProjectBriefingFfcGlobalFootprintData?> BuildFfcGlobalFootprintDataAsync(
        ProjectBriefingFfcGlobalFootprintOptions options,
        CancellationToken cancellationToken)
    {
        var normalized = ProjectBriefingFfcGlobalFootprintOptions.Normalize(
            options.IncludeSlide,
            options.Title,
            options.Layout,
            options.MaximumCountryRows,
            options.IncludeCountryWiseBreakdown);
        if (!normalized.IncludeSlide)
        {
            return null;
        }

        var footprint = await _ffcFootprintService.GetAsync(
            new FfcFootprintRequest(
                Metric: FfcFootprintMetric.TotalUnits,
                Sort: FfcFootprintSort.TotalUnits),
            cancellationToken);
        var countries = footprint.Countries
            .OrderByDescending(country => country.TotalUnits)
            .ThenBy(country => country.CountryName, StringComparer.OrdinalIgnoreCase)
            .Select(country => new ProjectBriefingFfcCountryData(
                country.CountryName,
                country.IsoCode,
                country.ProjectCount,
                country.InstalledUnits,
                country.DeliveredNotInstalledUnits,
                country.PlannedUnits))
            .ToArray();

        var mapCountries = footprint.Countries
            .Select(country => new FfcPresentationCountry(
                country.CountryId,
                country.CountryName,
                country.IsoCode,
                country.RecordCount,
                country.ProjectCount,
                country.InstalledUnits,
                country.DeliveredNotInstalledUnits,
                country.PlannedUnits,
                country.LastUpdated,
                Array.Empty<FfcPresentationRecord>()))
            .ToArray();
        var mapImage = mapCountries.Length == 0
            ? Array.Empty<byte>()
            : normalized.Layout == ProjectBriefingFfcGlobalFootprintLayout.MapDominant
                ? _ffcMapRenderer.RenderFocused(mapCountries, width: 2400, height: 725)
                : _ffcMapRenderer.RenderFocused(mapCountries, width: 2100, height: 1065);
        var dataAsOn = footprint.Countries.Count == 0
            ? _clock.UtcNow.ToUniversalTime()
            : footprint.Countries.Max(country => country.LastUpdated).ToUniversalTime();

        return new ProjectBriefingFfcGlobalFootprintData
        {
            Title = normalized.Title,
            Layout = normalized.Layout,
            IncludeCountryWiseBreakdown = normalized.IncludeCountryWiseBreakdown,
            Summary = footprint.Summary,
            Countries = countries,
            MaximumCountryRows = normalized.MaximumCountryRows,
            MapImage = mapImage,
            DataAsOnUtc = dataAsOn
        };
    }

    private static ProjectBriefingRoleCharterData? BuildRoleCharterData(
        ProjectBriefingRoleCharterOptions options)
    {
        var normalized = ProjectBriefingRoleCharterOptions.Normalize(
            options.IncludeSlide,
            options.Title,
            options.Layout,
            options.UseSharedContent,
            options.RoleStatements,
            options.CharterItems);
        if (!normalized.IncludeSlide)
        {
            return null;
        }

        return new ProjectBriefingRoleCharterData
        {
            Title = normalized.Title,
            Layout = normalized.Layout,
            RoleStatements = normalized.Layout == ProjectBriefingRoleCharterLayout.CharterOnly
                ? Array.Empty<ProjectBriefingRoleCharterEntry>()
                : normalized.RoleStatements,
            CharterItems = normalized.CharterItems
        };
    }

    // The photo chosen for this deck, while it still belongs to the project.
    private static int? ResolveChosenPhotoId(DeckItemSnapshot item)
        => item.PhotoId is { } photoId && item.Photos.Any(photo => photo.Id == photoId) ? photoId : null;

    private static int? ResolveCoverPhotoId(DeckItemSnapshot item)
    {
        if (ResolveChosenPhotoId(item) is { } chosen)
        {
            return chosen;
        }

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
        => ResolveStageCode(item.LifecycleStatus, item.WorkflowVersion, item.Stages);

    private static string ResolveStageCode(
        ProjectLifecycleStatus lifecycleStatus,
        string workflowVersion,
        IReadOnlyList<StageSnapshot> stages)
    {
        if (lifecycleStatus == ProjectLifecycleStatus.Completed)
        {
            return ProjectBriefingStageOrder.CompletedCode;
        }

        var codes = ProcurementWorkflow.StageCodesFor(workflowVersion);
        var statusByCode = stages
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
        ProjectBriefingItemPresentation Presentation,
        int? PhotoId,
        string? SpeakerNotes,
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
        ProjectBriefingClosingSlideType ClosingSlideType,
        ProjectBriefingInstitutionalProfileOptions InstitutionalProfileOptions,
        ProjectBriefingRoleCharterOptions RoleCharterOptions,
        ProjectBriefingFfcGlobalFootprintOptions FfcGlobalFootprintOptions,
        IReadOnlyList<ProjectBriefingAdditionalSlideType> AdditionalSlideOrder,
        string? HandlingMarking,
        DateTimeOffset UpdatedAtUtc,
        string CreatedByDisplay,
        string LastModifiedByDisplay,
        byte[] RowVersion,
        IReadOnlyList<DeckItemSnapshot> Items)
    {
        public ProjectBriefingDeckPreset? Preset { get; init; }
    }

    private sealed record DeckItemSnapshot(
        long ItemId,
        int ProjectId,
        int SortOrder,
        string? BriefDescriptionOverride,
        ProjectBriefingItemPresentation Presentation,
        int? PhotoId,
        string? SpeakerNotes,
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
