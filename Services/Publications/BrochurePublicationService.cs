using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Publications;

public interface IBrochurePublicationService
{
    Task<IReadOnlyList<BrochureProjectListItemVm>> GetProjectOptionsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BrochureProjectReviewVm>> GetReviewProjectsAsync(
        IReadOnlyCollection<int> projectIds,
        BrochureNarrativeSource narrativeSource,
        CancellationToken cancellationToken = default);

    Task<BrochurePreflight> PreflightAsync(
        IReadOnlyList<BrochureProjectSelection> selections,
        BrochureNarrativeSource narrativeSource,
        BrochureCoverStyle coverStyle,
        bool allowTextOnlyProjects,
        int? coverHeroProjectId,
        int? coverHeroPhotoId,
        CancellationToken cancellationToken = default);

    Task<BrochurePublicationData> BuildAsync(
        IReadOnlyList<BrochureProjectSelection> selections,
        BrochureBuildOptions options,
        CancellationToken cancellationToken = default);
}

public sealed class BrochurePublicationValidationException : Exception
{
    public BrochurePublicationValidationException(BrochurePreflight preflight)
        : base("The brochure did not pass publication preflight.")
    {
        Preflight = preflight ?? throw new ArgumentNullException(nameof(preflight));
    }

    public BrochurePreflight Preflight { get; }
}

/// <summary>
/// Authoritative brochure read, preflight and composition service. UI preflight and
/// final generation deliberately run through the same project/narrative/photo rules.
/// </summary>
public sealed partial class BrochurePublicationService : IBrochurePublicationService
{
    private const int MaximumProjects = 100;

    private readonly ApplicationDbContext _db;
    private readonly IBrochurePhotoService _photoService;

    public BrochurePublicationService(
        ApplicationDbContext db,
        IBrochurePhotoService photoService)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _photoService = photoService ?? throw new ArgumentNullException(nameof(photoService));
    }

    public async Task<IReadOnlyList<BrochureProjectListItemVm>> GetProjectOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        var projects = await _db.Projects
            .AsNoTracking()
            .Where(project => !project.IsDeleted
                              && !project.IsArchived
                              && (project.LifecycleStatus == ProjectLifecycleStatus.Active
                                  || project.LifecycleStatus == ProjectLifecycleStatus.Completed))
            .OrderBy(project => project.Name)
            .Select(project => new ProjectOptionRow(
                project.Id,
                project.Name,
                project.LifecycleStatus,
                project.Category != null ? project.Category.Name : null,
                project.TechnicalCategory != null ? project.TechnicalCategory.Name : null,
                project.ProjectBrief,
                project.Description,
                project.CoverPhotoId))
            .ToListAsync(cancellationToken);

        if (projects.Count == 0)
        {
            return Array.Empty<BrochureProjectListItemVm>();
        }

        var projectIds = projects.Select(project => project.ProjectId).ToArray();
        var capabilityRows = await _db.ProjectCapabilityStatements
            .AsNoTracking()
            .Where(statement => projectIds.Contains(statement.ProjectId))
            .Select(statement => new CapabilityRow(statement.ProjectId, statement.Statement))
            .ToListAsync(cancellationToken);
        var capabilityWords = capabilityRows
            .GroupBy(row => row.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(row => BrochureLayoutPlanner.CountWords(row.Statement)));

        var photoRows = await LoadPhotoRowsAsync(projectIds, cancellationToken);
        var photosByProject = GroupPhotos(photoRows);

        return projects.Select(project =>
        {
            var projectPhotos = photosByProject.GetValueOrDefault(project.ProjectId)
                                ?? Array.Empty<PublicationPhotoRow>();
            var primary = SelectDefaultPrimary(project.CoverPhotoId, projectPhotos);
            // A second brochure image is an editorial choice, not an implicit side effect
            // of a project having multiple photographs. Gallery 2 can still prompt/pick a
            // second image in the client, while Automatic uses one only when explicitly set.
            PublicationPhotoRow? secondary = null;
            var photoOptions = projectPhotos
                .Select(photo => new BrochurePhotoOptionVm(
                    photo.PhotoId,
                    photo.Version,
                    photo.Caption,
                    photo.Width,
                    photo.Height,
                    photo.IsCover,
                    photo.IsLowResolution))
                .ToArray();

            return new BrochureProjectListItemVm(
                project.ProjectId,
                project.ProjectName,
                LifecycleDisplay(project.LifecycleStatus),
                project.ProjectCategory,
                project.TechnicalCategory,
                HasMeaningfulText(project.ProjectBrief),
                capabilityWords.GetValueOrDefault(project.ProjectId) > 0,
                HasMeaningfulText(project.Description),
                BrochureLayoutPlanner.CountWords(project.ProjectBrief),
                capabilityWords.GetValueOrDefault(project.ProjectId),
                BrochureLayoutPlanner.CountWords(project.Description),
                primary?.PhotoId,
                secondary?.PhotoId,
                photoOptions);
        }).ToArray();
    }


    public async Task<IReadOnlyList<BrochureProjectReviewVm>> GetReviewProjectsAsync(
        IReadOnlyCollection<int> projectIds,
        BrochureNarrativeSource narrativeSource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectIds);
        if (!Enum.IsDefined(narrativeSource))
        {
            throw new InvalidOperationException("The selected brochure narrative source is invalid.");
        }

        var ids = projectIds
            .Where(projectId => projectId > 0)
            .Distinct()
            .Take(MaximumProjects)
            .ToArray();
        if (ids.Length == 0)
        {
            return Array.Empty<BrochureProjectReviewVm>();
        }

        var projects = await _db.Projects
            .AsNoTracking()
            .Where(project => ids.Contains(project.Id)
                              && !project.IsDeleted
                              && !project.IsArchived
                              && (project.LifecycleStatus == ProjectLifecycleStatus.Active
                                  || project.LifecycleStatus == ProjectLifecycleStatus.Completed))
            .Select(project => new ProjectOptionRow(
                project.Id,
                project.Name,
                project.LifecycleStatus,
                project.Category != null ? project.Category.Name : null,
                project.TechnicalCategory != null ? project.TechnicalCategory.Name : null,
                project.ProjectBrief,
                project.Description,
                project.CoverPhotoId))
            .ToListAsync(cancellationToken);

        var capabilityRows = await _db.ProjectCapabilityStatements
            .AsNoTracking()
            .Where(statement => ids.Contains(statement.ProjectId))
            .OrderBy(statement => statement.ProjectId)
            .ThenBy(statement => statement.DisplayOrder)
            .ThenBy(statement => statement.Id)
            .Select(statement => new CapabilityRow(statement.ProjectId, statement.Statement))
            .ToListAsync(cancellationToken);
        var capabilitiesByProject = capabilityRows
            .GroupBy(row => row.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(row => CleanLine(row.Statement))
                    .Where(HasMeaningfulText)
                    .ToArray());

        var photoRows = await LoadPhotoRowsAsync(ids, cancellationToken);
        var photosByProject = GroupPhotos(photoRows);
        var projectById = projects.ToDictionary(project => project.ProjectId);

        var result = new List<BrochureProjectReviewVm>(projects.Count);
        foreach (var id in ids)
        {
            if (!projectById.TryGetValue(id, out var project))
            {
                continue;
            }

            var capabilities = capabilitiesByProject.GetValueOrDefault(id) ?? Array.Empty<string>();
            var publicationRow = new PublicationProjectRow(
                project.ProjectId,
                project.ProjectName,
                project.ProjectBrief,
                project.Description,
                project.ProjectCategory,
                project.TechnicalCategory,
                project.CoverPhotoId);
            var (narrative, hasNarrative) = ResolveNarrative(publicationRow, capabilities, narrativeSource);
            var projectPhotos = photosByProject.GetValueOrDefault(id) ?? Array.Empty<PublicationPhotoRow>();
            var primary = SelectDefaultPrimary(project.CoverPhotoId, projectPhotos);
            var photoOptions = projectPhotos
                .Select(photo => new BrochurePhotoOptionVm(
                    photo.PhotoId,
                    photo.Version,
                    photo.Caption,
                    photo.Width,
                    photo.Height,
                    photo.IsCover,
                    photo.IsLowResolution))
                .ToArray();

            result.Add(new BrochureProjectReviewVm(
                project.ProjectId,
                project.ProjectName,
                LifecycleDisplay(project.LifecycleStatus),
                project.ProjectCategory,
                project.TechnicalCategory,
                narrative,
                hasNarrative,
                BrochureLayoutPlanner.CountWords(narrative),
                HasMeaningfulText(project.ProjectBrief),
                capabilities.Count > 0,
                HasMeaningfulText(project.Description),
                BrochureLayoutPlanner.CountWords(project.ProjectBrief),
                capabilities.Sum(statement => BrochureLayoutPlanner.CountWords(statement)),
                BrochureLayoutPlanner.CountWords(project.Description),
                primary?.PhotoId,
                photoOptions));
        }

        return result;
    }

    public async Task<BrochurePreflight> PreflightAsync(
        IReadOnlyList<BrochureProjectSelection> selections,
        BrochureNarrativeSource narrativeSource,
        BrochureCoverStyle coverStyle,
        bool allowTextOnlyProjects,
        int? coverHeroProjectId,
        int? coverHeroPhotoId,
        CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(
            selections,
            narrativeSource,
            coverStyle,
            allowTextOnlyProjects,
            coverHeroProjectId,
            coverHeroPhotoId,
            cancellationToken);
        return prepared.Preflight;
    }

    public async Task<BrochurePublicationData> BuildAsync(
        IReadOnlyList<BrochureProjectSelection> selections,
        BrochureBuildOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var prepared = await PrepareAsync(
            selections,
            options.NarrativeSource,
            options.CoverStyle,
            options.AllowTextOnlyProjects,
            options.CoverHeroProjectId,
            options.CoverHeroPhotoId,
            cancellationToken);
        if (!prepared.Preflight.CanGenerate)
        {
            throw new BrochurePublicationValidationException(prepared.Preflight);
        }

        var renderRequests = prepared.Projects
            .SelectMany(project => BuildRenderRequests(project))
            .ToArray();
        var rendered = await _photoService.RenderAsync(renderRequests, cancellationToken);

        var lateIssues = new List<BrochurePreflightIssue>();
        BrochurePublicationImage? coverHeroImage = null;
        if (options.CoverStyle == BrochureCoverStyle.Contemporary && prepared.CoverHero is not null)
        {
            coverHeroImage = await _photoService.RenderAsync(
                new BrochurePhotoRenderRequest(
                    prepared.CoverHero.ProjectId,
                    prepared.CoverHero.PhotoId,
                    options.CoverHeroFocalX,
                    options.CoverHeroFocalY,
                    1800,
                    1100),
                cancellationToken);

            if (coverHeroImage is null)
            {
                var heroProject = prepared.Projects.FirstOrDefault(project =>
                    project.Row.ProjectId == prepared.CoverHero.ProjectId);
                lateIssues.Add(new BrochurePreflightIssue(
                    BrochurePreflightIssueCode.CoverHeroUnavailable,
                    PublicationIssueSeverity.Blocker,
                    prepared.CoverHero.ProjectId,
                    heroProject?.Row.ProjectName,
                    "The selected Cover B hero photograph became unavailable during generation."));
            }
        }

        var publicationProjects = new List<BrochurePublicationProject>(prepared.Projects.Count);
        foreach (var project in prepared.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            BrochurePublicationImage? primary = null;
            if (project.PrimaryPhotoId.HasValue)
            {
                rendered.TryGetValue(project.PrimaryPhotoId.Value, out primary);
                if (primary is null)
                {
                    lateIssues.Add(new BrochurePreflightIssue(
                        BrochurePreflightIssueCode.SelectedPhotoUnavailable,
                        PublicationIssueSeverity.Blocker,
                        project.Row.ProjectId,
                        project.Row.ProjectName,
                        "The selected primary photograph became unavailable during generation."));
                }
            }

            BrochurePublicationImage? secondary = null;
            if (project.SecondaryPhotoId.HasValue && project.ImageMode != BrochureImageMode.Single)
            {
                rendered.TryGetValue(project.SecondaryPhotoId.Value, out secondary);
                if (secondary is null && project.ImageMode == BrochureImageMode.GalleryTwo)
                {
                    lateIssues.Add(new BrochurePreflightIssue(
                        BrochurePreflightIssueCode.GallerySecondPhotoUnavailable,
                        PublicationIssueSeverity.Blocker,
                        project.Row.ProjectId,
                        project.Row.ProjectName,
                        "The selected second gallery photograph became unavailable during generation."));
                }
            }

            publicationProjects.Add(new BrochurePublicationProject(
                project.Row.ProjectId,
                project.Row.ProjectName,
                project.Row.ProjectCategory,
                project.Row.TechnicalCategory,
                project.Narrative,
                project.NarrativeWordCount,
                primary,
                secondary,
                project.ImageMode));
        }

        if (lateIssues.Count > 0)
        {
            throw new BrochurePublicationValidationException(new BrochurePreflight(
                prepared.Preflight.SelectedProjectCount,
                prepared.Preflight.Issues.Concat(lateIssues).ToArray(),
                prepared.Preflight.ResolvedCoverHeroProjectId,
                prepared.Preflight.ResolvedCoverHeroPhotoId,
                prepared.Preflight.ResolvedCoverHeroWidth,
                prepared.Preflight.ResolvedCoverHeroHeight,
                prepared.Preflight.ResolvedCoverHeroQuality));
        }

        return new BrochurePublicationData(
            options,
            publicationProjects,
            prepared.Preflight,
            coverHeroImage);
    }

    private async Task<PreparedPublication> PrepareAsync(
        IReadOnlyList<BrochureProjectSelection> selections,
        BrochureNarrativeSource narrativeSource,
        BrochureCoverStyle coverStyle,
        bool allowTextOnlyProjects,
        int? requestedCoverHeroProjectId,
        int? requestedCoverHeroPhotoId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selections);
        if (!Enum.IsDefined(narrativeSource))
        {
            throw new InvalidOperationException("The selected brochure narrative source is invalid.");
        }
        if (!Enum.IsDefined(coverStyle))
        {
            throw new InvalidOperationException("The selected brochure cover style is invalid.");
        }

        var normalizedSelections = NormalizeSelections(selections);
        if (normalizedSelections.Length == 0)
        {
            return new PreparedPublication(
                Array.Empty<PreparedProject>(),
                new BrochurePreflight(0, new[]
                {
                    new BrochurePreflightIssue(
                        BrochurePreflightIssueCode.ProjectUnavailable,
                        PublicationIssueSeverity.Blocker,
                        null,
                        null,
                        "Select at least one project for the brochure.")
                }));
        }

        if (normalizedSelections.Length > MaximumProjects)
        {
            return new PreparedPublication(
                Array.Empty<PreparedProject>(),
                new BrochurePreflight(normalizedSelections.Length, new[]
                {
                    new BrochurePreflightIssue(
                        BrochurePreflightIssueCode.SelectionLimitExceeded,
                        PublicationIssueSeverity.Blocker,
                        null,
                        null,
                        $"A brochure can contain up to {MaximumProjects} selected projects.")
                }));
        }

        var projectIds = normalizedSelections.Select(selection => selection.ProjectId).ToArray();
        var rows = await _db.Projects
            .AsNoTracking()
            .Where(project => projectIds.Contains(project.Id)
                              && !project.IsDeleted
                              && !project.IsArchived
                              && (project.LifecycleStatus == ProjectLifecycleStatus.Active
                                  || project.LifecycleStatus == ProjectLifecycleStatus.Completed))
            .Select(project => new PublicationProjectRow(
                project.Id,
                project.Name,
                project.ProjectBrief,
                project.Description,
                project.Category != null ? project.Category.Name : null,
                project.TechnicalCategory != null ? project.TechnicalCategory.Name : null,
                project.CoverPhotoId))
            .ToListAsync(cancellationToken);
        var rowById = rows.ToDictionary(row => row.ProjectId);

        var validProjectIds = rows.Select(row => row.ProjectId).ToArray();
        var capabilityRows = await _db.ProjectCapabilityStatements
            .AsNoTracking()
            .Where(statement => validProjectIds.Contains(statement.ProjectId))
            .OrderBy(statement => statement.ProjectId)
            .ThenBy(statement => statement.DisplayOrder)
            .ThenBy(statement => statement.Id)
            .Select(statement => new CapabilityRow(statement.ProjectId, statement.Statement))
            .ToListAsync(cancellationToken);
        var capabilitiesByProject = capabilityRows
            .GroupBy(row => row.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(row => CleanLine(row.Statement))
                    .Where(HasMeaningfulText)
                    .ToArray());

        var photoRows = await LoadPhotoRowsAsync(validProjectIds, cancellationToken);
        var photosByProject = GroupPhotos(photoRows);

        var issues = new List<BrochurePreflightIssue>();
        var prepared = new List<PreparedProject>(normalizedSelections.Length);
        foreach (var selection in normalizedSelections)
        {
            if (!rowById.TryGetValue(selection.ProjectId, out var row))
            {
                issues.Add(new BrochurePreflightIssue(
                    BrochurePreflightIssueCode.ProjectUnavailable,
                    PublicationIssueSeverity.Blocker,
                    selection.ProjectId,
                    null,
                    $"Project {selection.ProjectId} is no longer available for publication."));
                continue;
            }

            var capabilities = capabilitiesByProject.GetValueOrDefault(selection.ProjectId)
                               ?? Array.Empty<string>();
            var (narrative, hasNarrative) = ResolveNarrative(row, capabilities, narrativeSource);
            var wordCount = BrochureLayoutPlanner.CountWords(narrative);
            if (!hasNarrative)
            {
                issues.Add(new BrochurePreflightIssue(
                    BrochurePreflightIssueCode.MissingNarrative,
                    PublicationIssueSeverity.Blocker,
                    row.ProjectId,
                    row.ProjectName,
                    $"{NarrativeSourceLabel(narrativeSource)} is not recorded for this project."));
            }
            else if (wordCount > BrochureLayoutPlanner.LongNarrativeChunkWords)
            {
                issues.Add(new BrochurePreflightIssue(
                    BrochurePreflightIssueCode.LongNarrative,
                    PublicationIssueSeverity.Information,
                    row.ProjectId,
                    row.ProjectName,
                    $"Narrative is {wordCount} words and will continue on a feature page rather than use smaller body text."));
            }

            var projectPhotos = photosByProject.GetValueOrDefault(row.ProjectId)
                                ?? Array.Empty<PublicationPhotoRow>();
            var (primaryPhotoId, primaryInvalid) = ResolvePrimaryPhotoId(
                selection.PrimaryPhotoId,
                row.CoverPhotoId,
                projectPhotos);
            if (primaryInvalid)
            {
                issues.Add(new BrochurePreflightIssue(
                    BrochurePreflightIssueCode.SelectedPhotoInvalid,
                    PublicationIssueSeverity.Blocker,
                    row.ProjectId,
                    row.ProjectName,
                    "The selected primary photograph does not belong to this project."));
            }

            var (secondaryPhotoId, secondaryInvalid) = ResolveSecondaryPhotoId(
                selection.SecondaryPhotoId,
                primaryPhotoId,
                projectPhotos);
            if (secondaryInvalid)
            {
                issues.Add(new BrochurePreflightIssue(
                    BrochurePreflightIssueCode.GallerySecondPhotoInvalid,
                    selection.ImageMode == BrochureImageMode.GalleryTwo
                        ? PublicationIssueSeverity.Blocker
                        : PublicationIssueSeverity.Warning,
                    row.ProjectId,
                    row.ProjectName,
                    "The selected second photograph is invalid or duplicates the primary photograph."));
            }

            if (!primaryPhotoId.HasValue && !primaryInvalid)
            {
                var textOnlyPermitted = allowTextOnlyProjects && selection.ImageMode != BrochureImageMode.GalleryTwo;
                issues.Add(new BrochurePreflightIssue(
                    textOnlyPermitted
                        ? BrochurePreflightIssueCode.TextOnlyProject
                        : BrochurePreflightIssueCode.MissingPrimaryPhoto,
                    textOnlyPermitted
                        ? PublicationIssueSeverity.Warning
                        : PublicationIssueSeverity.Blocker,
                    row.ProjectId,
                    row.ProjectName,
                    textOnlyPermitted
                        ? "No photograph is available; this project will be typeset as a text-only project block."
                        : "A primary photograph is required for this image treatment. Enable text-only projects only for non-gallery projects where publication without imagery is intentional."));
            }

            if (selection.ImageMode == BrochureImageMode.GalleryTwo && !secondaryPhotoId.HasValue && !secondaryInvalid)
            {
                issues.Add(new BrochurePreflightIssue(
                    BrochurePreflightIssueCode.GallerySecondPhotoRequired,
                    PublicationIssueSeverity.Blocker,
                    row.ProjectId,
                    row.ProjectName,
                    "Gallery 2 requires a second project photograph."));
            }

            prepared.Add(new PreparedProject(
                row,
                narrative,
                wordCount,
                primaryPhotoId,
                secondaryPhotoId,
                selection.PrimaryFocalX,
                selection.PrimaryFocalY,
                selection.SecondaryFocalX,
                selection.SecondaryFocalY,
                selection.ImageMode,
                selection.PrimaryPhotoConfirmed,
                selection.IsReviewed));
        }

        var references = new List<BrochurePhotoReference>();
        foreach (var project in prepared)
        {
            if (project.PrimaryPhotoId.HasValue)
            {
                references.Add(new BrochurePhotoReference(project.Row.ProjectId, project.PrimaryPhotoId.Value));
            }
            if (project.SecondaryPhotoId.HasValue && project.ImageMode != BrochureImageMode.Single)
            {
                references.Add(new BrochurePhotoReference(project.Row.ProjectId, project.SecondaryPhotoId.Value));
            }

            if (coverStyle == BrochureCoverStyle.Contemporary)
            {
                foreach (var photo in photosByProject.GetValueOrDefault(project.Row.ProjectId)
                                      ?? Array.Empty<PublicationPhotoRow>())
                {
                    references.Add(new BrochurePhotoReference(project.Row.ProjectId, photo.PhotoId));
                }
            }
        }

        var probes = await _photoService.ProbeAsync(
            references
                .GroupBy(reference => reference.PhotoId)
                .Select(group => group.First())
                .ToArray(),
            cancellationToken);

        var coverHero = ResolveCoverHero(
            prepared,
            photosByProject,
            probes,
            coverStyle,
            requestedCoverHeroProjectId,
            requestedCoverHeroPhotoId,
            issues);

        foreach (var project in prepared)
        {
            if (project.PrimaryPhotoId.HasValue)
            {
                var primaryProbe = probes.GetValueOrDefault(project.PrimaryPhotoId.Value);
                if (primaryProbe is null || !primaryProbe.IsReady)
                {
                    issues.Add(new BrochurePreflightIssue(
                        BrochurePreflightIssueCode.SelectedPhotoUnavailable,
                        PublicationIssueSeverity.Blocker,
                        project.Row.ProjectId,
                        project.Row.ProjectName,
                        primaryProbe?.FailureReason ?? "The selected primary photograph cannot be loaded from storage."));
                }
                else
                {
                    var isCoverHero = coverHero is not null
                                      && coverHero.ProjectId == project.Row.ProjectId
                                      && coverHero.PhotoId == project.PrimaryPhotoId.Value;
                    var placement = isCoverHero
                        ? PhotoPlacement.CoverHero
                        : project.ImageMode == BrochureImageMode.GalleryTwo
                          || project.NarrativeWordCount > BrochureLayoutPlanner.ThreeProjectMaximumWords
                            ? PhotoPlacement.Feature
                            : PhotoPlacement.Card;
                    AddQualityFinding(issues, project, primaryProbe, placement, isCoverHero ? "Cover hero" : "Primary");
                }
            }

            if (project.SecondaryPhotoId.HasValue && project.ImageMode != BrochureImageMode.Single)
            {
                var secondaryProbe = probes.GetValueOrDefault(project.SecondaryPhotoId.Value);
                if (secondaryProbe is null || !secondaryProbe.IsReady)
                {
                    issues.Add(new BrochurePreflightIssue(
                        BrochurePreflightIssueCode.GallerySecondPhotoUnavailable,
                        project.ImageMode == BrochureImageMode.GalleryTwo
                            ? PublicationIssueSeverity.Blocker
                            : PublicationIssueSeverity.Warning,
                        project.Row.ProjectId,
                        project.Row.ProjectName,
                        secondaryProbe?.FailureReason ?? "The selected second photograph cannot be loaded from storage."));
                }
                else
                {
                    AddQualityFinding(issues, project, secondaryProbe, PhotoPlacement.Card, "Second");
                }
            }
        }

        var coverHeroProject = coverHero is not null
            ? prepared.FirstOrDefault(project => project.Row.ProjectId == coverHero.ProjectId)
            : null;
        var coverHeroProbe = coverHero is not null
            ? probes.GetValueOrDefault(coverHero.PhotoId)
            : null;

        if (coverHero is not null
            && coverHeroProject is not null
            && coverHeroProbe is { IsReady: true }
            && coverHeroProject.PrimaryPhotoId != coverHero.PhotoId)
        {
            AddQualityFinding(issues, coverHeroProject, coverHeroProbe, PhotoPlacement.CoverHero, "Cover hero");
        }

        return new PreparedPublication(
            prepared,
            new BrochurePreflight(
                normalizedSelections.Length,
                issues,
                coverHero?.ProjectId,
                coverHero?.PhotoId,
                coverHeroProbe?.Width ?? 0,
                coverHeroProbe?.Height ?? 0,
                coverHeroProbe?.Quality),
            coverHero);
    }


    private static CoverHeroResolution? ResolveCoverHero(
        IReadOnlyList<PreparedProject> projects,
        IReadOnlyDictionary<int, IReadOnlyList<PublicationPhotoRow>> photosByProject,
        IReadOnlyDictionary<int, BrochurePhotoProbe> probes,
        BrochureCoverStyle coverStyle,
        int? requestedProjectId,
        int? requestedPhotoId,
        ICollection<BrochurePreflightIssue> issues)
    {
        if (coverStyle != BrochureCoverStyle.Contemporary)
        {
            return null;
        }

        if (requestedProjectId.HasValue || requestedPhotoId.HasValue)
        {
            PreparedProject? requestedProject = null;
            PublicationPhotoRow? requestedPhoto = null;

            if (requestedProjectId.HasValue)
            {
                requestedProject = projects.FirstOrDefault(project => project.Row.ProjectId == requestedProjectId.Value);
                if (requestedProject is null)
                {
                    issues.Add(new BrochurePreflightIssue(
                        BrochurePreflightIssueCode.CoverHeroInvalid,
                        PublicationIssueSeverity.Blocker,
                        requestedProjectId,
                        null,
                        "The selected Cover B hero project is no longer part of this brochure."));
                    return null;
                }

                var photos = photosByProject.GetValueOrDefault(requestedProject.Row.ProjectId)
                             ?? Array.Empty<PublicationPhotoRow>();
                var photoId = requestedPhotoId ?? requestedProject.PrimaryPhotoId;
                requestedPhoto = photoId.HasValue
                    ? photos.FirstOrDefault(photo => photo.PhotoId == photoId.Value)
                    : null;
            }
            else if (requestedPhotoId.HasValue)
            {
                foreach (var project in projects)
                {
                    var photo = (photosByProject.GetValueOrDefault(project.Row.ProjectId)
                                 ?? Array.Empty<PublicationPhotoRow>())
                        .FirstOrDefault(candidate => candidate.PhotoId == requestedPhotoId.Value);
                    if (photo is null)
                    {
                        continue;
                    }

                    requestedProject = project;
                    requestedPhoto = photo;
                    break;
                }
            }

            if (requestedProject is null || requestedPhoto is null)
            {
                issues.Add(new BrochurePreflightIssue(
                    BrochurePreflightIssueCode.CoverHeroInvalid,
                    PublicationIssueSeverity.Blocker,
                    requestedProject?.Row.ProjectId ?? requestedProjectId,
                    requestedProject?.Row.ProjectName,
                    "The selected Cover B hero photograph is no longer available for the selected project."));
                return null;
            }

            if (!probes.TryGetValue(requestedPhoto.PhotoId, out var requestedProbe)
                || !requestedProbe.IsReady)
            {
                issues.Add(new BrochurePreflightIssue(
                    BrochurePreflightIssueCode.CoverHeroUnavailable,
                    PublicationIssueSeverity.Blocker,
                    requestedProject.Row.ProjectId,
                    requestedProject.Row.ProjectName,
                    requestedProbe?.FailureReason ?? "The selected Cover B hero photograph cannot be loaded from storage."));
                return null;
            }

            return new CoverHeroResolution(requestedProject.Row.ProjectId, requestedPhoto.PhotoId);
        }

        var projectNames = projects.ToDictionary(
            project => project.Row.ProjectId,
            project => project.Row.ProjectName);

        var candidates = projects
            .SelectMany(project =>
                (photosByProject.GetValueOrDefault(project.Row.ProjectId)
                 ?? Array.Empty<PublicationPhotoRow>())
                .Select(photo => new
                {
                    Project = project,
                    Photo = photo,
                    Probe = probes.GetValueOrDefault(photo.PhotoId)
                }))
            .Where(candidate => candidate.Probe is { IsReady: true })
            .Select(candidate =>
            {
                var (effectiveWidth, effectiveHeight) = BrochurePhotoService.EffectiveCropDimensions(
                    candidate.Probe!.Width,
                    candidate.Probe.Height,
                    1800d / 1100d);
                return new
                {
                    candidate.Project,
                    candidate.Photo,
                    Probe = candidate.Probe!,
                    EffectivePixels = effectiveWidth * effectiveHeight
                };
            })
            .OrderByDescending(candidate => candidate.Probe.Quality)
            .ThenByDescending(candidate => candidate.Photo.IsCover)
            .ThenByDescending(candidate => candidate.EffectivePixels)
            .ThenBy(candidate => projectNames[candidate.Project.Row.ProjectId], StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Photo.Ordinal)
            .ThenBy(candidate => candidate.Photo.PhotoId)
            .ToArray();

        var resolved = candidates.FirstOrDefault();
        if (resolved is null)
        {
            issues.Add(new BrochurePreflightIssue(
                BrochurePreflightIssueCode.CoverHeroUnavailable,
                PublicationIssueSeverity.Blocker,
                null,
                null,
                "Cover B requires at least one selected project with a usable publication photograph."));
            return null;
        }

        return new CoverHeroResolution(resolved.Project.Row.ProjectId, resolved.Photo.PhotoId);
    }


    private static void AddQualityFinding(
        ICollection<BrochurePreflightIssue> issues,
        PreparedProject project,
        BrochurePhotoProbe probe,
        PhotoPlacement placement,
        string label)
    {
        var (effectiveWidth, effectiveHeight) = placement == PhotoPlacement.CoverHero
            ? BrochurePhotoService.EffectiveCropDimensions(probe.Width, probe.Height, 1800d / 1100d)
            : BrochurePhotoService.EffectiveWideCropDimensions(probe.Width, probe.Height);
        var (minimumWidth, minimumHeight, placementLabel) = placement switch
        {
            PhotoPlacement.CoverHero => (1800d, 1100d, "Cover B hero"),
            PhotoPlacement.Feature => (1400d, 788d, "large feature frame"),
            _ => (1100d, 619d, "standard project frame")
        };

        if (effectiveWidth >= minimumWidth && effectiveHeight >= minimumHeight)
        {
            return;
        }

        issues.Add(new BrochurePreflightIssue(
            BrochurePreflightIssueCode.LowResolutionPhoto,
            PublicationIssueSeverity.Warning,
            project.Row.ProjectId,
            project.Row.ProjectName,
            $"{label} photograph resolves to {probe.Width}×{probe.Height}px and may look soft in the {placementLabel}. A higher-resolution source is recommended."));
    }

    private enum PhotoPlacement
    {
        Card = 1,
        Feature = 2,
        CoverHero = 3
    }

    private static IEnumerable<BrochurePhotoRenderRequest> BuildRenderRequests(PreparedProject project)
    {
        if (project.PrimaryPhotoId.HasValue)
        {
            yield return new BrochurePhotoRenderRequest(
                project.Row.ProjectId,
                project.PrimaryPhotoId.Value,
                project.PrimaryFocalX,
                project.PrimaryFocalY);
        }

        if (project.SecondaryPhotoId.HasValue && project.ImageMode != BrochureImageMode.Single)
        {
            yield return new BrochurePhotoRenderRequest(
                project.Row.ProjectId,
                project.SecondaryPhotoId.Value,
                project.SecondaryFocalX,
                project.SecondaryFocalY);
        }
    }

    private async Task<IReadOnlyList<PublicationPhotoRow>> LoadPhotoRowsAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken)
    {
        if (projectIds.Count == 0)
        {
            return Array.Empty<PublicationPhotoRow>();
        }

        var ids = projectIds.ToArray();
        return await _db.ProjectPhotos
            .AsNoTracking()
            .Where(photo => ids.Contains(photo.ProjectId))
            .Select(photo => new PublicationPhotoRow(
                photo.ProjectId,
                photo.Id,
                photo.IsCover,
                photo.IsLowResolution,
                photo.Ordinal,
                photo.Width,
                photo.Height,
                photo.Caption,
                photo.Version))
            .ToListAsync(cancellationToken);
    }

    private static IReadOnlyDictionary<int, IReadOnlyList<PublicationPhotoRow>> GroupPhotos(
        IReadOnlyList<PublicationPhotoRow> photos)
        => photos
            .GroupBy(photo => photo.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PublicationPhotoRow>)group
                    .OrderByDescending(photo => photo.IsCover)
                    .ThenBy(photo => photo.IsLowResolution)
                    .ThenBy(photo => photo.Ordinal)
                    .ThenBy(photo => photo.PhotoId)
                    .ToArray());

    private static PublicationPhotoRow? SelectDefaultPrimary(
        int? configuredCoverPhotoId,
        IReadOnlyList<PublicationPhotoRow> photos)
    {
        if (photos.Count == 0)
        {
            return null;
        }

        if (configuredCoverPhotoId.HasValue)
        {
            var configured = photos.FirstOrDefault(photo => photo.PhotoId == configuredCoverPhotoId.Value);
            if (configured is not null)
            {
                return configured;
            }
        }

        return photos.First();
    }

    private static (int? PhotoId, bool Invalid) ResolvePrimaryPhotoId(
        int? requestedPhotoId,
        int? configuredCoverPhotoId,
        IReadOnlyList<PublicationPhotoRow> photos)
    {
        if (requestedPhotoId.HasValue)
        {
            return photos.Any(photo => photo.PhotoId == requestedPhotoId.Value)
                ? (requestedPhotoId, false)
                : (null, true);
        }

        return (SelectDefaultPrimary(configuredCoverPhotoId, photos)?.PhotoId, false);
    }

    private static (int? PhotoId, bool Invalid) ResolveSecondaryPhotoId(
        int? requestedPhotoId,
        int? primaryPhotoId,
        IReadOnlyList<PublicationPhotoRow> photos)
    {
        if (requestedPhotoId.HasValue)
        {
            var valid = requestedPhotoId.Value != primaryPhotoId
                        && photos.Any(photo => photo.PhotoId == requestedPhotoId.Value);
            return valid ? (requestedPhotoId, false) : (null, true);
        }

        // Secondary imagery is deliberately opt-in. This prevents hidden image choices,
        // unnecessary image decoding, and warnings for photographs the user never selected.
        return (null, false);
    }

    private static BrochureProjectSelection[] NormalizeSelections(
        IReadOnlyList<BrochureProjectSelection> selections)
        => selections
            .Where(selection => selection.ProjectId > 0)
            .GroupBy(selection => selection.ProjectId)
            .Select(group => group.First())
            .Select(selection => selection with
            {
                PrimaryFocalX = ClampFocal(selection.PrimaryFocalX),
                PrimaryFocalY = ClampFocal(selection.PrimaryFocalY),
                SecondaryFocalX = ClampFocal(selection.SecondaryFocalX),
                SecondaryFocalY = ClampFocal(selection.SecondaryFocalY),
                ImageMode = Enum.IsDefined(selection.ImageMode)
                    ? selection.ImageMode
                    : BrochureImageMode.Automatic
            })
            .ToArray();

    private static (string Narrative, bool HasNarrative) ResolveNarrative(
        PublicationProjectRow project,
        IReadOnlyList<string> capabilities,
        BrochureNarrativeSource source)
        => source switch
        {
            BrochureNarrativeSource.ProjectBrief => ResolveText(
                project.ProjectBrief,
                "Project brief not recorded."),
            BrochureNarrativeSource.CapabilityOverview => capabilities.Count > 0
                ? (string.Join("\n\n", capabilities.Select(statement => $"• {statement}")), true)
                : ("Capability overview not recorded.", false),
            BrochureNarrativeSource.FullDescription => ResolveText(
                project.Description,
                "Project description not recorded."),
            _ => throw new InvalidOperationException("The selected brochure narrative source is invalid.")
        };

    private static (string Text, bool HasText) ResolveText(string? value, string fallback)
    {
        if (!HasMeaningfulText(value))
        {
            return (fallback, false);
        }

        var normalized = NormalizeNarrative(value!);
        return string.IsNullOrWhiteSpace(normalized)
            ? (fallback, false)
            : (normalized, true);
    }

    private static string NarrativeSourceLabel(BrochureNarrativeSource source)
        => source switch
        {
            BrochureNarrativeSource.ProjectBrief => "Project Brief",
            BrochureNarrativeSource.CapabilityOverview => "Capability Overview",
            BrochureNarrativeSource.FullDescription => "Full Description",
            _ => "Selected narrative"
        };

    private static string NormalizeNarrative(string value)
    {
        var normalized = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        normalized = MarkdownImageRegex().Replace(normalized, string.Empty);
        normalized = MarkdownLinkRegex().Replace(normalized, "$1");
        normalized = MarkdownHeadingRegex().Replace(normalized, "$1");
        normalized = MarkdownDecorationRegex().Replace(normalized, string.Empty);
        normalized = HorizontalWhitespaceRegex().Replace(normalized, " ");

        var lines = normalized.Split('\n').Select(CleanLine).ToArray();
        normalized = string.Join("\n", lines);
        normalized = ExcessiveNewlinesRegex().Replace(normalized, "\n\n").Trim();
        return normalized;
    }

    private static string CleanLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var line = value.Trim();
        line = ListPrefixRegex().Replace(line, match =>
            string.Equals(match.Groups[1].Value, "-", StringComparison.Ordinal)
            || string.Equals(match.Groups[1].Value, "*", StringComparison.Ordinal)
            || string.Equals(match.Groups[1].Value, "+", StringComparison.Ordinal)
                ? "• "
                : match.Value);
        return line;
    }

    private static bool HasMeaningfulText(string? value)
        => !string.IsNullOrWhiteSpace(value);

    private static string LifecycleDisplay(ProjectLifecycleStatus status)
        => status switch
        {
            ProjectLifecycleStatus.Active => "Ongoing",
            ProjectLifecycleStatus.Completed => "Completed",
            _ => status.ToString()
        };

    private static double ClampFocal(double value)
        => double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : .5d;

    private sealed record ProjectOptionRow(
        int ProjectId,
        string ProjectName,
        ProjectLifecycleStatus LifecycleStatus,
        string? ProjectCategory,
        string? TechnicalCategory,
        string? ProjectBrief,
        string? Description,
        int? CoverPhotoId);

    private sealed record PublicationProjectRow(
        int ProjectId,
        string ProjectName,
        string? ProjectBrief,
        string? Description,
        string? ProjectCategory,
        string? TechnicalCategory,
        int? CoverPhotoId);

    private sealed record CapabilityRow(int ProjectId, string Statement);

    private sealed record PublicationPhotoRow(
        int ProjectId,
        int PhotoId,
        bool IsCover,
        bool IsLowResolution,
        int Ordinal,
        int Width,
        int Height,
        string? Caption,
        int Version);

    private sealed record PreparedProject(
        PublicationProjectRow Row,
        string Narrative,
        int NarrativeWordCount,
        int? PrimaryPhotoId,
        int? SecondaryPhotoId,
        double PrimaryFocalX,
        double PrimaryFocalY,
        double SecondaryFocalX,
        double SecondaryFocalY,
        BrochureImageMode ImageMode,
        bool PrimaryPhotoConfirmed,
        bool IsReviewed);

    private sealed record CoverHeroResolution(int ProjectId, int PhotoId);

    private sealed record PreparedPublication(
        IReadOnlyList<PreparedProject> Projects,
        BrochurePreflight Preflight,
        CoverHeroResolution? CoverHero = null);

    [GeneratedRegex(@"!\[[^\]]*\]\([^\)]+\)", RegexOptions.Compiled)]
    private static partial Regex MarkdownImageRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\([^\)]+\)", RegexOptions.Compiled)]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"(?m)^\s{0,3}#{1,6}\s+(.+)$", RegexOptions.Compiled)]
    private static partial Regex MarkdownHeadingRegex();

    [GeneratedRegex(@"(\*\*|__|~~|`)", RegexOptions.Compiled)]
    private static partial Regex MarkdownDecorationRegex();

    [GeneratedRegex(@"[\t ]{2,}", RegexOptions.Compiled)]
    private static partial Regex HorizontalWhitespaceRegex();

    [GeneratedRegex(@"\n{3,}", RegexOptions.Compiled)]
    private static partial Regex ExcessiveNewlinesRegex();

    [GeneratedRegex(@"^(?:\s*)([-*+]|\d+[.)])\s+", RegexOptions.Compiled)]
    private static partial Regex ListPrefixRegex();
}
