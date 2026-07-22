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
    private readonly IClock _clock;

    public ProjectBriefingDataService(
        ApplicationDbContext db,
        IProjectBriefingCostResolver costResolver,
        IProjectBriefingExternalStatusService externalStatusService,
        IProjectBriefingPhotoLoader photoLoader,
        IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _costResolver = costResolver ?? throw new ArgumentNullException(nameof(costResolver));
        _externalStatusService = externalStatusService ?? throw new ArgumentNullException(nameof(externalStatusService));
        _photoLoader = photoLoader ?? throw new ArgumentNullException(nameof(photoLoader));
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

        var projects = await BuildProjectsAsync(snapshot.Items, cancellationToken);
        return new ProjectBriefingDeckVm
        {
            Id = snapshot.Id,
            Name = snapshot.Name,
            Description = snapshot.Description,
            PresentationMode = snapshot.PresentationMode,
            CostMode = snapshot.CostMode,
            IncludeStageSummary = snapshot.IncludeStageSummary,
            IncludeProjectCategorySummary = snapshot.IncludeProjectCategorySummary,
            IncludeTechnicalCategorySummary = snapshot.IncludeTechnicalCategorySummary,
            HandlingMarking = snapshot.HandlingMarking,
            RowVersion = Encode(snapshot.RowVersion),
            UpdatedAtUtc = snapshot.UpdatedAtUtc,
            CreatedByDisplay = snapshot.CreatedByDisplay,
            LastModifiedByDisplay = snapshot.LastModifiedByDisplay,
            Projects = projects,
            Readiness = BuildReadiness(projects),
            SlideEstimate = BuildSlideEstimate(snapshot.PresentationMode, snapshot.IncludeStageSummary,
                snapshot.IncludeProjectCategorySummary, snapshot.IncludeTechnicalCategorySummary,
                snapshot.CostMode, projects)
        };
    }

    public async Task<ProjectBriefingPresentationData> BuildPresentationDataAsync(
        long deckId,
        string requestingUserId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await LoadSnapshotAsync(deckId, requestingUserId, cancellationToken)
            ?? throw new KeyNotFoundException("The saved deck was not found.");
        var projectVms = await BuildProjectsAsync(snapshot.Items, cancellationToken);
        if (projectVms.Count == 0)
        {
            throw new InvalidOperationException("Add at least one project before generating the PowerPoint deck.");
        }

        var itemByProject = snapshot.Items.ToDictionary(item => item.ProjectId);
        var projects = projectVms.Select(project =>
        {
            var item = itemByProject[project.ProjectId];
            return new ProjectBriefingPresentationProject
            {
                ProjectId = project.ProjectId,
                ProjectName = project.ProjectName,
                LifecycleStatus = project.LifecycleStatus,
                LifecycleDisplay = project.LifecycleDisplay,
                PresentStageCode = ResolveStageCode(item),
                PresentStage = project.PresentStage,
                PresentStageOrder = ResolveStageOrder(item),
                ProjectCategory = project.ProjectCategory,
                TechnicalCategory = project.TechnicalCategory,
                CostRd = project.CostRd,
                ProliferationCost = project.ProliferationCost,
                ExternalStatus = project.ExternalStatus ?? "No external status recorded",
                ExternalStatusDate = project.ExternalStatusDate,
                BriefDescription = project.BriefDescription,
                SortOrder = project.SortOrder,
                CoverPhotoId = project.CoverPhotoId,
                CoverPhotoIsReady = project.HasCoverPhoto
            };
        }).ToList();

        var summary = BuildPresentationSummary(projects);
        return new ProjectBriefingPresentationData
        {
            DeckId = snapshot.Id,
            DeckName = snapshot.Name,
            DeckDescription = snapshot.Description,
            PresentationMode = snapshot.PresentationMode,
            CostMode = snapshot.CostMode,
            IncludeStageSummary = snapshot.IncludeStageSummary,
            IncludeProjectCategorySummary = snapshot.IncludeProjectCategorySummary,
            IncludeTechnicalCategorySummary = snapshot.IncludeTechnicalCategorySummary,
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
                candidate.PresentationMode,
                candidate.CostMode,
                candidate.IncludeStageSummary,
                candidate.IncludeProjectCategorySummary,
                candidate.IncludeTechnicalCategorySummary,
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

        var itemRows = await _db.Set<ProjectBriefingDeckItem>()
            .AsNoTracking()
            .Where(item => item.DeckId == deckId
                && !item.Project.IsDeleted
                && !item.Project.IsArchived
                && item.Project.LifecycleStatus != ProjectLifecycleStatus.Cancelled)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Id)
            .Select(item => new DeckItemBaseSnapshot(
                item.Id,
                item.ProjectId,
                item.SortOrder,
                item.BriefDescriptionOverride,
                item.Project.Name,
                item.Project.Description,
                item.Project.LifecycleStatus,
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
                deck.PresentationMode,
                deck.CostMode,
                deck.IncludeStageSummary,
                deck.IncludeProjectCategorySummary,
                deck.IncludeTechnicalCategorySummary,
                deck.HandlingMarking,
                deck.UpdatedAtUtc,
                deck.CreatedByDisplay,
                deck.LastModifiedByDisplay,
                deck.RowVersion,
                Array.Empty<DeckItemSnapshot>());
        }

        var projectIds = itemRows.Select(item => item.ProjectId).Distinct().ToArray();
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
                item.LifecycleStatus,
                item.WorkflowVersion,
                item.ProjectCategory,
                item.TechnicalCategory,
                item.CoverPhotoId,
                stagesByProject.GetValueOrDefault(item.ProjectId) ?? Array.Empty<StageSnapshot>(),
                photosByProject.GetValueOrDefault(item.ProjectId) ?? Array.Empty<PhotoSnapshot>()))
            .ToList();

        return new DeckSnapshot(
            deck.Id,
            deck.Name,
            deck.Description,
            deck.PresentationMode,
            deck.CostMode,
            deck.IncludeStageSummary,
            deck.IncludeProjectCategorySummary,
            deck.IncludeTechnicalCategorySummary,
            deck.HandlingMarking,
            deck.UpdatedAtUtc,
            deck.CreatedByDisplay,
            deck.LastModifiedByDisplay,
            deck.RowVersion,
            items);
    }

    private async Task<IReadOnlyList<ProjectBriefingProjectVm>> BuildProjectsAsync(
        IReadOnlyList<DeckItemSnapshot> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return Array.Empty<ProjectBriefingProjectVm>();
        }

        var projectIds = items.Select(item => item.ProjectId).Distinct().ToArray();
        var costRd = await _costResolver.ResolveCostRdAsync(projectIds, cancellationToken);
        var proliferation = await _costResolver.ResolveProliferationCostAsync(projectIds, cancellationToken);
        var externalStatuses = await _externalStatusService.GetLatestAsync(projectIds, cancellationToken);

        var coverByProject = items.ToDictionary(item => item.ProjectId, ResolveCoverPhotoId);
        var photoReferences = coverByProject
            .Where(pair => pair.Value.HasValue)
            .Select(pair => new ProjectBriefingPhotoReference(pair.Key, pair.Value!.Value))
            .ToArray();
        var photoProbes = await _photoLoader.ProbeAsync(photoReferences, cancellationToken);

        return items
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.ItemId)
            .Select(item =>
            {
                var coverPhotoId = coverByProject[item.ProjectId];
                var probe = coverPhotoId.HasValue
                    ? photoProbes.GetValueOrDefault(coverPhotoId.Value)
                    : null;
                var external = externalStatuses.GetValueOrDefault(item.ProjectId);
                return new ProjectBriefingProjectVm
                {
                    ProjectId = item.ProjectId,
                    ProjectName = item.ProjectName,
                    LifecycleStatus = item.LifecycleStatus,
                    LifecycleDisplay = item.LifecycleStatus == ProjectLifecycleStatus.Completed ? "Completed" : "Ongoing",
                    PresentStage = ResolveStageName(item),
                    ProjectCategory = item.ProjectCategory,
                    TechnicalCategory = item.TechnicalCategory,
                    CostRd = costRd.GetValueOrDefault(item.ProjectId) ?? ProjectBriefingCostValue.Missing(),
                    ProliferationCost = proliferation.GetValueOrDefault(item.ProjectId)
                        ?? ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.Proliferation),
                    ExternalStatus = external?.Body,
                    ExternalStatusDate = external?.EventDate,
                    HasSelectedCoverPhoto = coverPhotoId.HasValue,
                    HasCoverPhoto = probe?.IsReady == true,
                    CoverPhotoId = coverPhotoId,
                    CoverPhotoReadinessReason = probe?.FailureReason,
                    BriefDescription = ProjectBriefingTextNormalizer.NormalizeFull(
                        item.BriefDescriptionOverride ?? item.ProjectDescription),
                    BriefDescriptionOverride = item.BriefDescriptionOverride,
                    SortOrder = item.SortOrder,
                    OpenUrl = $"/Projects/Overview/{item.ProjectId}"
                };
            })
            .ToList();
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
            DescriptionAvailableCount = projects.Count(project =>
                !string.Equals(
                    project.BriefDescription,
                    "Brief description not recorded.",
                    StringComparison.Ordinal))
        };

    private static ProjectBriefingPresentationSummary BuildPresentationSummary(
        IReadOnlyList<ProjectBriefingPresentationProject> projects)
    {
        var stageSummary = projects
            .GroupBy(project => new { project.PresentStage, project.PresentStageOrder })
            .Select(group => new ProjectBriefingSummaryPoint(group.Key.PresentStage, group.Count(), group.Key.PresentStageOrder))
            .OrderByDescending(point => point.Order)
            .ThenBy(point => point.Label)
            .ToList();

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
        ProjectBriefingPresentationMode presentationMode,
        bool includeStageSummary,
        bool includeProjectCategorySummary,
        bool includeTechnicalCategorySummary,
        ProjectBriefingCostMode costMode,
        IReadOnlyList<ProjectBriefingProjectVm> projects)
    {
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
        var detailSlides = includesDetailedSlides ? projects.Count : 0;
        var capabilityContinuationSlides = includesDetailedSlides
            ? projects.Sum(project =>
                ProjectBriefingCapabilityPaginator
                    .Paginate(project.BriefDescription)
                    .ContinuationSlideCount)
            : 0;

        return new ProjectBriefingSlideEstimateVm
        {
            CoverAndPortfolioSlides = 2,
            SummarySlides = summarySlides,
            ExecutiveTableSlides = executiveSlides,
            DetailedProjectSlides = detailSlides,
            CapabilityContinuationSlides = capabilityContinuationSlides,
            TotalSlides = 2
                + summarySlides
                + executiveSlides
                + detailSlides
                + capabilityContinuationSlides
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
            return "COMPLETED";
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

    private static int ResolveStageOrder(DeckItemSnapshot item)
    {
        if (item.LifecycleStatus == ProjectLifecycleStatus.Completed)
        {
            return 10_000;
        }

        return ProcurementWorkflow.OrderOf(item.WorkflowVersion, ResolveStageCode(item));
    }

    private static string Encode(byte[] value)
        => value is { Length: > 0 } ? Convert.ToBase64String(value) : string.Empty;

    private sealed record DeckHeaderSnapshot(
        long Id,
        string Name,
        string? Description,
        ProjectBriefingPresentationMode PresentationMode,
        ProjectBriefingCostMode CostMode,
        bool IncludeStageSummary,
        bool IncludeProjectCategorySummary,
        bool IncludeTechnicalCategorySummary,
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
        ProjectLifecycleStatus LifecycleStatus,
        string WorkflowVersion,
        string? ProjectCategory,
        string? TechnicalCategory,
        int? CoverPhotoId);

    private sealed record DeckSnapshot(
        long Id,
        string Name,
        string? Description,
        ProjectBriefingPresentationMode PresentationMode,
        ProjectBriefingCostMode CostMode,
        bool IncludeStageSummary,
        bool IncludeProjectCategorySummary,
        bool IncludeTechnicalCategorySummary,
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
        ProjectLifecycleStatus LifecycleStatus,
        string WorkflowVersion,
        string? ProjectCategory,
        string? TechnicalCategory,
        int? CoverPhotoId,
        IReadOnlyList<StageSnapshot> Stages,
        IReadOnlyList<PhotoSnapshot> Photos);

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
