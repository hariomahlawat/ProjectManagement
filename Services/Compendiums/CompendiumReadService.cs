using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Projects;
using ProjectManagement.Services;
using ProjectManagement.Services.Publications;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.Compendiums;

/// <summary>
/// Authoritative read model for the Simulators Compendium.
///
/// Phase 23 keeps publication membership user-authored while making project review, publication
/// imagery and readiness deterministic. Saved/working publication choices are overlaid on live
/// PRISM facts; project facts are never copied into the Compendium configuration.
/// </summary>
public sealed class CompendiumReadService : ICompendiumReadService
{
    public const string BuildStamp = "CompendiumPdf_2026-08-13_phase23";
    private const int MaximumSelectedProjects = 500;

    private readonly ApplicationDbContext _db;
    private readonly CompendiumPdfOptions _options;
    private readonly IClock _clock;
    private readonly IBrochurePhotoService _photoService;
    private readonly ICompendiumReadinessPolicy _readinessPolicy;

    public CompendiumReadService(
        ApplicationDbContext db,
        IOptions<CompendiumPdfOptions> options,
        IClock clock,
        IBrochurePhotoService photoService,
        ICompendiumReadinessPolicy readinessPolicy)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _photoService = photoService ?? throw new ArgumentNullException(nameof(photoService));
        _readinessPolicy = readinessPolicy ?? throw new ArgumentNullException(nameof(readinessPolicy));
    }

    public async Task<IReadOnlyList<CompendiumCandidateProjectVm>> GetCandidateProjectsAsync(
        CancellationToken cancellationToken = default)
    {
        var projects = await _db.Projects
            .AsNoTracking()
            .Where(project => !project.IsDeleted
                              && !project.IsArchived
                              && (project.LifecycleStatus == ProjectLifecycleStatus.Active
                                  || project.LifecycleStatus == ProjectLifecycleStatus.Completed))
            .OrderBy(project => project.Name)
            .Select(project => new CandidateRow(
                project.Id,
                project.Name,
                project.LifecycleStatus,
                project.Category != null ? project.Category.Name : null,
                project.TechnicalCategory != null ? project.TechnicalCategory.Name : null,
                project.Description,
                project.ArmService,
                project.CompletedYear,
                project.CompletedOn,
                project.CoverPhotoId))
            .ToListAsync(cancellationToken);

        if (projects.Count == 0)
        {
            return Array.Empty<CompendiumCandidateProjectVm>();
        }

        var projectIds = projects.Select(project => project.Id).ToArray();
        var availability = await _db.ProjectTechStatuses
            .AsNoTracking()
            .Where(status => projectIds.Contains(status.ProjectId))
            .ToDictionaryAsync(
                status => status.ProjectId,
                status => status.AvailableForProliferation,
                cancellationToken);

        var productionCosts = await _db.ProjectProductionCostFacts
            .AsNoTracking()
            .Where(cost => projectIds.Contains(cost.ProjectId))
            .ToDictionaryAsync(
                cost => cost.ProjectId,
                cost => cost.ApproxProductionCost,
                cancellationToken);

        var photos = await LoadPhotoCandidatesAsync(projectIds, cancellationToken);
        var photosByProject = photos
            .GroupBy(photo => photo.ProjectId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<PhotoCandidate>)group.ToArray());

        return projects
            .Select(project =>
            {
                availability.TryGetValue(project.Id, out var availableForProliferation);
                productionCosts.TryGetValue(project.Id, out var productionCost);
                var projectPhotos = photosByProject.GetValueOrDefault(project.Id)
                                    ?? Array.Empty<PhotoCandidate>();
                var defaultPhotoId = SelectAutomaticPhoto(project.CoverPhotoId, projectPhotos).PhotoId;
                var completionDisplay = project.LifecycleStatus == ProjectLifecycleStatus.Completed
                    ? ResolveCompletionYear(project.CompletedYear, project.CompletedOn)
                          ?.ToString(CultureInfo.InvariantCulture)
                      ?? "Year not recorded"
                    : "Ongoing";

                return new CompendiumCandidateProjectVm(
                    project.Id,
                    project.Name,
                    LifecycleDisplay(project.LifecycleStatus),
                    project.ProjectCategory,
                    project.TechnicalCategory,
                    availableForProliferation == true,
                    !string.IsNullOrWhiteSpace(project.Description),
                    !string.IsNullOrWhiteSpace(project.ArmService),
                    productionCost.HasValue,
                    projectPhotos.Count,
                    defaultPhotoId,
                    completionDisplay)
                {
                    ProliferationAvailability = availableForProliferation
                };
            })
            .ToArray();
    }

    public async Task<CompendiumPdfDataDto> GetPublicationAsync(
        CompendiumPublicationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var selections = NormalizeSelections(request.Projects);
        var requestedIds = selections.Select(selection => selection.ProjectId).ToArray();
        var generatedAtUtc = _clock.UtcNow.ToUniversalTime();
        var candidateCount = await CountCandidatesAsync(cancellationToken);

        if (requestedIds.Length == 0)
        {
            var noSelection = CompendiumPreflightDto.Empty with
            {
                CandidateProjectCount = candidateCount,
                SelectedProjectCount = 0,
                BlockerCount = 1,
                WarningCount = 0,
                Findings = new[]
                {
                    new CompendiumFindingDto(
                        CompendiumFindingSeverity.Blocker,
                        "noSelection",
                        "Select at least one project to create a Compendium.")
                }
            };
            return CreateResult(
                generatedAtUtc,
                request,
                Array.Empty<CompendiumCategoryGroupDto>(),
                noSelection);
        }

        var rows = await LoadPublicationRowsAsync(requestedIds, cancellationToken);
        var rowsById = rows.ToDictionary(project => project.Id);
        var availableProjectIds = rows.Select(project => project.Id).ToArray();

        var availability = availableProjectIds.Length == 0
            ? new Dictionary<int, bool?>()
            : await _db.ProjectTechStatuses
                .AsNoTracking()
                .Where(status => availableProjectIds.Contains(status.ProjectId))
                .ToDictionaryAsync(
                    status => status.ProjectId,
                    status => status.AvailableForProliferation,
                    cancellationToken);

        var costs = availableProjectIds.Length == 0
            ? new Dictionary<int, CostRow>()
            : await _db.ProjectProductionCostFacts
                .AsNoTracking()
                .Where(cost => availableProjectIds.Contains(cost.ProjectId))
                .Select(cost => new CostRow(cost.ProjectId, cost.ApproxProductionCost, cost.Remarks))
                .ToDictionaryAsync(cost => cost.ProjectId, cancellationToken);

        var photos = await LoadPhotoCandidatesAsync(availableProjectIds, cancellationToken);
        var photosByProject = photos
            .GroupBy(photo => photo.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PhotoCandidate>)group.ToArray());

        var resolvedSelections = new List<ResolvedSelection>(rows.Count);
        foreach (var selection in selections)
        {
            if (!rowsById.TryGetValue(selection.ProjectId, out var project))
            {
                continue;
            }

            var projectPhotos = photosByProject.GetValueOrDefault(project.Id)
                                ?? Array.Empty<PhotoCandidate>();
            resolvedSelections.Add(ResolveSelection(project, selection, projectPhotos));
        }

        var probes = await ProbeResolvedPhotosAsync(resolvedSelections, cancellationToken);
        var findings = new List<CompendiumFindingDto>();
        foreach (var unavailableProjectId in requestedIds.Where(id => !rowsById.ContainsKey(id)))
        {
            findings.Add(new CompendiumFindingDto(
                CompendiumFindingSeverity.Blocker,
                "projectUnavailable",
                "A selected project is no longer available for publication.",
                unavailableProjectId));
        }

        var publicationProjects = new List<CompendiumProjectDto>(rows.Count);
        for (var sortOrder = 0; sortOrder < selections.Count; sortOrder++)
        {
            var selection = selections[sortOrder];
            if (!rowsById.TryGetValue(selection.ProjectId, out var project))
            {
                continue;
            }

            availability.TryGetValue(project.Id, out var availableForProliferation);
            costs.TryGetValue(project.Id, out var cost);
            var projectPhotos = photosByProject.GetValueOrDefault(project.Id)
                                ?? Array.Empty<PhotoCandidate>();
            var resolved = resolvedSelections.First(item => item.Project.ProjectId == project.Id);
            var probe = resolved.ResolvedPhotoId.HasValue
                ? probes.GetValueOrDefault(resolved.ResolvedPhotoId.Value)
                : null;
            var effectiveDpi = probe is { IsReady: true }
                ? CompendiumPublicationImagePolicy.CalculateEffectiveDpi(probe.Width, probe.Height, project.Description)
                : null;
            var imageQuality = CompendiumPublicationImagePolicy.Classify(effectiveDpi);
            var completionYear = ResolveCompletionYear(project.CompletedYear, project.CompletedOn);
            var fingerprint = CompendiumReviewFingerprint.Create(new CompendiumReviewFingerprintInput(
                project.Id,
                project.Name,
                project.LifecycleStatus,
                project.ProjectCategory,
                project.TechnicalCategory,
                project.ArmService,
                completionYear,
                availableForProliferation,
                cost?.Cost,
                project.Description,
                resolved.ResolvedPhotoId,
                resolved.Selection.ImageSelectionMode,
                resolved.Selection.FocalX,
                resolved.Selection.FocalY));

            var assessment = _readinessPolicy.Evaluate(new CompendiumProjectReadinessContext(
                project.Id,
                project.Name,
                project.LifecycleStatus,
                completionYear,
                project.ArmService,
                project.Description,
                cost?.Cost,
                availableForProliferation,
                resolved.ResolvedPhotoId,
                probe?.IsReady == true,
                resolved.Selection.ImageSelectionMode,
                effectiveDpi,
                resolved.ExplicitPhotoUnavailable,
                fingerprint,
                resolved.Selection.ReviewFingerprint));

            findings.AddRange(assessment.Findings);

            publicationProjects.Add(new CompendiumProjectDto(
                project.Id,
                NormalizeDisplay(project.Name, $"Project {project.Id}"),
                NormalizeOptional(project.CaseFileNumber),
                NormalizeDisplay(project.TechnicalCategory, "Not recorded"),
                completionYear,
                project.LifecycleStatus == ProjectLifecycleStatus.Completed
                    ? completionYear?.ToString(CultureInfo.InvariantCulture) ?? "Not recorded"
                    : "Ongoing",
                NormalizeDisplay(project.ArmService, "Not recorded"),
                cost?.Cost,
                NormalizeOptional(cost?.Remarks),
                resolved.ResolvedPhotoId,
                resolved.PhotoSelectionSource,
                NormalizeOptional(project.Description) ?? string.Empty,
                assessment.PublicationIssues)
            {
                LifecycleDisplay = LifecycleDisplay(project.LifecycleStatus),
                ProjectCategoryName = NormalizeOptional(project.ProjectCategory),
                IsAvailableForProliferation = availableForProliferation == true,
                ProliferationAvailability = availableForProliferation,
                PhotoCount = projectPhotos.Count,
                SortOrder = sortOrder,
                ImageSelectionMode = resolved.Selection.ImageSelectionMode,
                PrimaryFocalX = resolved.Selection.FocalX,
                PrimaryFocalY = resolved.Selection.FocalY,
                EffectiveDpi = effectiveDpi,
                ImageQuality = imageQuality,
                ReviewFingerprint = fingerprint,
                IsReviewed = assessment.IsReviewed,
                IsReviewStale = assessment.IsReviewStale,
                ExplicitPhotoUnavailable = resolved.ExplicitPhotoUnavailable
            });
        }

        var groups = GroupInPublicationOrder(publicationProjects);
        var projectOrder = publicationProjects.ToDictionary(project => project.ProjectId, project => project.SortOrder);
        var orderedFindings = findings
            .OrderByDescending(finding => finding.Severity)
            .ThenBy(finding => finding.ProjectId.HasValue
                ? projectOrder.GetValueOrDefault(finding.ProjectId.Value, int.MaxValue)
                : -1)
            .ThenBy(finding => finding.Code, StringComparer.Ordinal)
            .ToArray();
        var readinessProjects = publicationProjects
            .Select(project => new CompendiumProjectReadinessDto(
                project.ProjectId,
                project.ProjectName,
                project.TechnicalCategoryName,
                project.CompletionYearDisplay,
                project.PublicationIssues))
            .ToArray();

        var preflight = new CompendiumPreflightDto(
            CompletedProjectCount: await CountCompletedAsync(cancellationToken),
            EligibleProjectCount: publicationProjects.Count,
            CategoryCount: groups.Count,
            ExcludedNotAvailableCount: 0,
            MissingAvailabilityStatusCount: 0,
            PhotoSelectedCount: publicationProjects.Count(project => project.CoverPhotoId.HasValue),
            MissingPhotoCount: CountIssue(publicationProjects, CompendiumPublicationIssue.MissingPhoto),
            MissingArmServiceCount: CountIssue(publicationProjects, CompendiumPublicationIssue.MissingArmService),
            MissingCostCount: CountIssue(publicationProjects, CompendiumPublicationIssue.MissingProliferationCost),
            ZeroCostCount: CountIssue(publicationProjects, CompendiumPublicationIssue.ZeroProliferationCost),
            MissingDescriptionCount: CountIssue(publicationProjects, CompendiumPublicationIssue.MissingDescription),
            MissingCompletionYearCount: CountIssue(publicationProjects, CompendiumPublicationIssue.MissingCompletionYear),
            PossibleTitleTypoCount: CountIssue(publicationProjects, CompendiumPublicationIssue.PossibleTitleTypo),
            Projects: readinessProjects)
        {
            CandidateProjectCount = candidateCount,
            SelectedProjectCount = requestedIds.Length,
            BlockerCount = orderedFindings.Count(finding => finding.Severity == CompendiumFindingSeverity.Blocker),
            WarningCount = orderedFindings.Count(finding => finding.Severity == CompendiumFindingSeverity.Warning),
            InformationCount = orderedFindings.Count(finding => finding.Severity == CompendiumFindingSeverity.Information),
            Findings = orderedFindings
        };

        return CreateResult(generatedAtUtc, request, groups, preflight);
    }

    public async Task<CompendiumReviewProjectDto?> GetReviewProjectAsync(
        CompendiumProjectSelection selection,
        CancellationToken cancellationToken = default)
    {
        selection = NormalizeSelection(selection);
        if (selection.ProjectId <= 0)
        {
            return null;
        }

        var rows = await LoadPublicationRowsAsync(new[] { selection.ProjectId }, cancellationToken);
        var project = rows.SingleOrDefault();
        if (project is null)
        {
            return null;
        }

        var availability = await _db.ProjectTechStatuses
            .AsNoTracking()
            .Where(status => status.ProjectId == project.Id)
            .Select(status => status.AvailableForProliferation)
            .SingleOrDefaultAsync(cancellationToken);

        var cost = await _db.ProjectProductionCostFacts
            .AsNoTracking()
            .Where(row => row.ProjectId == project.Id)
            .Select(row => new CostRow(row.ProjectId, row.ApproxProductionCost, row.Remarks))
            .SingleOrDefaultAsync(cancellationToken);

        var photoCandidates = await LoadPhotoCandidatesAsync(new[] { project.Id }, cancellationToken);
        var resolved = ResolveSelection(project, selection, photoCandidates);
        var photoReferences = photoCandidates
            .Select(photo => new BrochurePhotoReference(project.Id, photo.Id))
            .ToArray();
        var probes = photoReferences.Length == 0
            ? new Dictionary<int, BrochurePhotoProbe>()
            : (await _photoService.ProbeAsync(photoReferences, cancellationToken)).ToDictionary(pair => pair.Key, pair => pair.Value);

        var photos = photoCandidates
            .OrderBy(photo => photo.Ordinal)
            .ThenByDescending(photo => photo.UpdatedUtc)
            .Select(photo =>
            {
                var probe = probes.GetValueOrDefault(photo.Id);
                var dpi = probe is { IsReady: true }
                    ? CompendiumPublicationImagePolicy.CalculateEffectiveDpi(probe.Width, probe.Height, project.Description)
                    : null;
                return new CompendiumReviewPhotoVm(
                    photo.Id,
                    NormalizeOptional(photo.Caption),
                    probe?.Width ?? photo.Width,
                    probe?.Height ?? photo.Height,
                    photo.IsCover,
                    photo.IsLowResolution,
                    photo.Version,
                    probe?.IsReady == true,
                    probe?.SourceVariant,
                    CompendiumPublicationImagePolicy.Classify(dpi));
            })
            .ToArray();

        var selectedProbe = resolved.ResolvedPhotoId.HasValue
            ? probes.GetValueOrDefault(resolved.ResolvedPhotoId.Value)
            : null;
        var effectiveDpi = selectedProbe is { IsReady: true }
            ? CompendiumPublicationImagePolicy.CalculateEffectiveDpi(selectedProbe.Width, selectedProbe.Height, project.Description)
            : null;
        var completionYear = ResolveCompletionYear(project.CompletedYear, project.CompletedOn);
        var fingerprint = CompendiumReviewFingerprint.Create(new CompendiumReviewFingerprintInput(
            project.Id,
            project.Name,
            project.LifecycleStatus,
            project.ProjectCategory,
            project.TechnicalCategory,
            project.ArmService,
            completionYear,
            availability,
            cost?.Cost,
            project.Description,
            resolved.ResolvedPhotoId,
            resolved.Selection.ImageSelectionMode,
            resolved.Selection.FocalX,
            resolved.Selection.FocalY));
        var assessment = _readinessPolicy.Evaluate(new CompendiumProjectReadinessContext(
            project.Id,
            project.Name,
            project.LifecycleStatus,
            completionYear,
            project.ArmService,
            project.Description,
            cost?.Cost,
            availability,
            resolved.ResolvedPhotoId,
            selectedProbe?.IsReady == true,
            resolved.Selection.ImageSelectionMode,
            effectiveDpi,
            resolved.ExplicitPhotoUnavailable,
            fingerprint,
            resolved.Selection.ReviewFingerprint));

        return new CompendiumReviewProjectDto(
            project.Id,
            NormalizeDisplay(project.Name, $"Project {project.Id}"),
            LifecycleDisplay(project.LifecycleStatus),
            NormalizeOptional(project.ProjectCategory),
            NormalizeDisplay(project.TechnicalCategory, "Not recorded"),
            NormalizeDisplay(project.ArmService, "Not recorded"),
            project.LifecycleStatus == ProjectLifecycleStatus.Completed
                ? completionYear?.ToString(CultureInfo.InvariantCulture) ?? "Not recorded"
                : string.Empty,
            availability,
            cost?.Cost,
            CompendiumPublicationImagePolicy.FormatCost(cost?.Cost),
            NormalizeOptional(project.Description) ?? string.Empty,
            photos,
            resolved.ResolvedPhotoId,
            resolved.PhotoSelectionSource,
            resolved.Selection.ImageSelectionMode,
            resolved.Selection.FocalX,
            resolved.Selection.FocalY,
            effectiveDpi,
            CompendiumPublicationImagePolicy.Classify(effectiveDpi),
            fingerprint,
            assessment.IsReviewed,
            assessment.IsReviewStale,
            resolved.ExplicitPhotoUnavailable)
        {
            ImageFrameWidthPoints = CompendiumPublicationImagePolicy.FrameWidthPoints,
            ImageFrameHeightPoints = CompendiumPublicationImagePolicy.ResolveFrameHeightPoints(project.Description)
        };
    }

    public async Task<CompendiumPdfDataDto> GetProliferationCompendiumAsync(
        CancellationToken cancellationToken = default)
    {
        // Compatibility path for /Projects/Compendium and existing integrations. The authored
        // Publications workspace never uses this automatic proliferation selection.
        var completed = await _db.Projects
            .AsNoTracking()
            .Where(project => !project.IsDeleted
                              && !project.IsArchived
                              && project.LifecycleStatus == ProjectLifecycleStatus.Completed)
            .Select(project => new
            {
                project.Id,
                Category = project.TechnicalCategory != null ? project.TechnicalCategory.Name : null,
                project.Name
            })
            .ToListAsync(cancellationToken);

        var completedIds = completed.Select(project => project.Id).ToArray();
        var statuses = completedIds.Length == 0
            ? new Dictionary<int, bool?>()
            : await _db.ProjectTechStatuses
                .AsNoTracking()
                .Where(status => completedIds.Contains(status.ProjectId))
                .ToDictionaryAsync(
                    status => status.ProjectId,
                    status => status.AvailableForProliferation,
                    cancellationToken);

        var eligibleProjectIds = completed
            .Where(project => statuses.GetValueOrDefault(project.Id) == true)
            .OrderBy(project => project.Category ?? "Not recorded", StringComparer.OrdinalIgnoreCase)
            .ThenBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
            .Select(project => project.Id)
            .ToArray();

        // The compatibility exporter should remain non-interactive; mark the live fingerprint as
        // reviewed only inside this transient request so the legacy route does not gain review warnings.
        var legacySelections = eligibleProjectIds
            .Select(projectId => new CompendiumProjectSelection(projectId))
            .ToArray();
        var data = await GetPublicationAsync(
            new CompendiumPublicationRequest(legacySelections),
            cancellationToken);

        var nonReviewFindings = data.Preflight.Findings
            .Where(finding => finding.Code is not "reviewRequired" and not "projectChangedAfterReview")
            .ToArray();
        var missingStatusCount = completed.Count(project =>
            !statuses.TryGetValue(project.Id, out var available) || !available.HasValue);
        var excludedCount = completed.Count(project =>
            statuses.TryGetValue(project.Id, out var available) && available == false);

        return data with
        {
            Preflight = data.Preflight with
            {
                CompletedProjectCount = completed.Count,
                ExcludedNotAvailableCount = excludedCount,
                MissingAvailabilityStatusCount = missingStatusCount,
                WarningCount = nonReviewFindings.Count(finding => finding.Severity == CompendiumFindingSeverity.Warning),
                InformationCount = nonReviewFindings.Count(finding => finding.Severity == CompendiumFindingSeverity.Information),
                Findings = nonReviewFindings
            }
        };
    }

    private CompendiumPdfDataDto CreateResult(
        DateTimeOffset generatedAtUtc,
        CompendiumPublicationRequest request,
        IReadOnlyList<CompendiumCategoryGroupDto> groups,
        CompendiumPreflightDto preflight)
    {
        var istYear = TimeZoneInfo.ConvertTime(generatedAtUtc, TimeZoneHelper.GetIst()).Year;
        return new CompendiumPdfDataDto(
            NormalizeDisplay(request.Title, _options.Title ?? "Simulators Compendium"),
            NormalizeDisplay(request.Subtitle, _options.Subtitle ?? "Detailed Project Reference"),
            NormalizeDisplay(_options.UnitDisplayName, "Simulator Development Division"),
            NormalizeDisplay(_options.IssuerDisplayName, "Simulator Development Division"),
            generatedAtUtc,
            groups,
            preflight)
        {
            Edition = NormalizeDisplay(request.Edition, $"Capability Edition · {istYear}")
        };
    }

    private async Task<List<PublicationRow>> LoadPublicationRowsAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken)
        => await _db.Projects
            .AsNoTracking()
            .Where(project => projectIds.Contains(project.Id)
                              && !project.IsDeleted
                              && !project.IsArchived
                              && (project.LifecycleStatus == ProjectLifecycleStatus.Active
                                  || project.LifecycleStatus == ProjectLifecycleStatus.Completed))
            .Select(project => new PublicationRow(
                project.Id,
                project.Name,
                project.CaseFileNumber,
                project.LifecycleStatus,
                project.Description,
                project.ArmService,
                project.CompletedYear,
                project.CompletedOn,
                project.CoverPhotoId,
                project.Category != null ? project.Category.Name : null,
                project.TechnicalCategory != null ? project.TechnicalCategory.Name : null))
            .ToListAsync(cancellationToken);

    private async Task<List<PhotoCandidate>> LoadPhotoCandidatesAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken)
    {
        if (projectIds.Count == 0)
        {
            return new List<PhotoCandidate>();
        }

        return await _db.ProjectPhotos
            .AsNoTracking()
            .Where(photo => projectIds.Contains(photo.ProjectId))
            .Select(photo => new PhotoCandidate(
                photo.Id,
                photo.ProjectId,
                photo.Caption,
                photo.Width,
                photo.Height,
                photo.IsCover,
                photo.IsLowResolution,
                photo.Version,
                photo.Ordinal,
                photo.UpdatedUtc))
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyDictionary<int, BrochurePhotoProbe>> ProbeResolvedPhotosAsync(
        IReadOnlyList<ResolvedSelection> resolvedSelections,
        CancellationToken cancellationToken)
    {
        var references = resolvedSelections
            .Where(item => item.ResolvedPhotoId.HasValue)
            .Select(item => new BrochurePhotoReference(item.Project.ProjectId, item.ResolvedPhotoId!.Value))
            .GroupBy(reference => reference.PhotoId)
            .Select(group => group.First())
            .ToArray();

        return references.Length == 0
            ? new Dictionary<int, BrochurePhotoProbe>()
            : await _photoService.ProbeAsync(references, cancellationToken);
    }

    private static ResolvedSelection ResolveSelection(
        PublicationRow project,
        CompendiumProjectSelection rawSelection,
        IReadOnlyList<PhotoCandidate> candidates)
    {
        var selection = NormalizeSelection(rawSelection);
        if (selection.ImageSelectionMode == CompendiumImageSelectionMode.Explicit)
        {
            if (selection.PrimaryPhotoId.HasValue
                && candidates.Any(candidate => candidate.Id == selection.PrimaryPhotoId.Value))
            {
                return new ResolvedSelection(
                    project,
                    selection,
                    selection.PrimaryPhotoId,
                    CompendiumPhotoSelectionSource.ExplicitPublication,
                    false);
            }

            var automaticFallback = SelectAutomaticPhoto(project.CoverPhotoId, candidates);
            return new ResolvedSelection(
                project,
                selection,
                automaticFallback.PhotoId,
                automaticFallback.Source,
                true);
        }

        var automatic = SelectAutomaticPhoto(project.CoverPhotoId, candidates);
        return new ResolvedSelection(
            project,
            selection with { PrimaryPhotoId = null },
            automatic.PhotoId,
            automatic.Source,
            false);
    }

    private static PhotoSelection SelectAutomaticPhoto(
        int? projectCoverPhotoId,
        IReadOnlyList<PhotoCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return PhotoSelection.None;
        }

        if (projectCoverPhotoId.HasValue
            && candidates.Any(candidate => candidate.Id == projectCoverPhotoId.Value))
        {
            return new PhotoSelection(
                projectCoverPhotoId,
                CompendiumPhotoSelectionSource.ProjectCover);
        }

        var markedCover = candidates
            .Where(candidate => candidate.IsCover)
            .OrderBy(candidate => candidate.IsLowResolution)
            .ThenBy(candidate => candidate.Ordinal)
            .ThenByDescending(candidate => candidate.UpdatedUtc)
            .FirstOrDefault();
        if (markedCover is not null)
        {
            return new PhotoSelection(
                markedCover.Id,
                CompendiumPhotoSelectionSource.MarkedCover);
        }

        var firstAvailable = candidates
            .OrderBy(candidate => candidate.IsLowResolution)
            .ThenBy(candidate => candidate.Ordinal)
            .ThenByDescending(candidate => candidate.UpdatedUtc)
            .First();
        return new PhotoSelection(
            firstAvailable.Id,
            CompendiumPhotoSelectionSource.FirstAvailable);
    }

    private async Task<int> CountCandidatesAsync(CancellationToken cancellationToken)
        => await _db.Projects
            .AsNoTracking()
            .CountAsync(project => !project.IsDeleted
                                   && !project.IsArchived
                                   && (project.LifecycleStatus == ProjectLifecycleStatus.Active
                                       || project.LifecycleStatus == ProjectLifecycleStatus.Completed),
                cancellationToken);

    private async Task<int> CountCompletedAsync(CancellationToken cancellationToken)
        => await _db.Projects
            .AsNoTracking()
            .CountAsync(project => !project.IsDeleted
                                   && !project.IsArchived
                                   && project.LifecycleStatus == ProjectLifecycleStatus.Completed,
                cancellationToken);

    private static IReadOnlyList<CompendiumProjectSelection> NormalizeSelections(
        IReadOnlyList<CompendiumProjectSelection>? selections)
    {
        if (selections is null || selections.Count == 0)
        {
            return Array.Empty<CompendiumProjectSelection>();
        }

        var seen = new HashSet<int>();
        return selections
            .Where(selection => selection.ProjectId > 0 && seen.Add(selection.ProjectId))
            .Take(MaximumSelectedProjects)
            .Select(NormalizeSelection)
            .ToArray();
    }

    private static CompendiumProjectSelection NormalizeSelection(CompendiumProjectSelection selection)
        => selection with
        {
            PrimaryPhotoId = selection.PrimaryPhotoId is > 0 ? selection.PrimaryPhotoId : null,
            FocalX = ClampFocal(selection.FocalX),
            FocalY = ClampFocal(selection.FocalY),
            ImageSelectionMode = Enum.IsDefined(selection.ImageSelectionMode)
                ? selection.ImageSelectionMode
                : CompendiumImageSelectionMode.Automatic,
            ReviewFingerprint = CleanFingerprint(selection.ReviewFingerprint)
        };

    private static IReadOnlyList<CompendiumCategoryGroupDto> GroupInPublicationOrder(
        IReadOnlyList<CompendiumProjectDto> projects)
    {
        var categoryOrder = new List<string>();
        foreach (var project in projects.OrderBy(project => project.SortOrder))
        {
            if (!categoryOrder.Any(category => string.Equals(
                    category,
                    project.TechnicalCategoryName,
                    StringComparison.OrdinalIgnoreCase)))
            {
                categoryOrder.Add(project.TechnicalCategoryName);
            }
        }

        return categoryOrder
            .Select(category => new CompendiumCategoryGroupDto(
                category,
                projects
                    .Where(project => string.Equals(
                        project.TechnicalCategoryName,
                        category,
                        StringComparison.OrdinalIgnoreCase))
                    .OrderBy(project => project.SortOrder)
                    .ToArray()))
            .ToArray();
    }

    private static int? ResolveCompletionYear(int? completedYear, DateOnly? completedOn)
        => completedYear ?? completedOn?.Year;

    private static string LifecycleDisplay(ProjectLifecycleStatus lifecycleStatus)
        => lifecycleStatus == ProjectLifecycleStatus.Completed ? "Completed" : "Ongoing";

    private static string NormalizeDisplay(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int CountIssue(
        IEnumerable<CompendiumProjectDto> projects,
        CompendiumPublicationIssue issue)
        => projects.Count(project => project.PublicationIssues.Contains(issue));

    private static double ClampFocal(double value)
        => double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : .5d;

    private static string? CleanFingerprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var clean = value.Trim();
        return clean.Length <= 128 ? clean : clean[..128];
    }

    private sealed record CandidateRow(
        int Id,
        string Name,
        ProjectLifecycleStatus LifecycleStatus,
        string? ProjectCategory,
        string? TechnicalCategory,
        string? Description,
        string? ArmService,
        int? CompletedYear,
        DateOnly? CompletedOn,
        int? CoverPhotoId);

    private sealed record PublicationRow(
        int Id,
        string Name,
        string? CaseFileNumber,
        ProjectLifecycleStatus LifecycleStatus,
        string? Description,
        string? ArmService,
        int? CompletedYear,
        DateOnly? CompletedOn,
        int? CoverPhotoId,
        string? ProjectCategory,
        string? TechnicalCategory)
    {
        public int ProjectId => Id;
    }

    private sealed record CostRow(
        int ProjectId,
        decimal? Cost,
        string? Remarks);

    private sealed record PhotoCandidate(
        int Id,
        int ProjectId,
        string? Caption,
        int Width,
        int Height,
        bool IsCover,
        bool IsLowResolution,
        int Version,
        int Ordinal,
        DateTime UpdatedUtc);

    private sealed record PhotoSelection(
        int? PhotoId,
        CompendiumPhotoSelectionSource Source)
    {
        public static PhotoSelection None { get; } = new(
            null,
            CompendiumPhotoSelectionSource.None);
    }

    private sealed record ResolvedSelection(
        PublicationRow Project,
        CompendiumProjectSelection Selection,
        int? ResolvedPhotoId,
        CompendiumPhotoSelectionSource PhotoSelectionSource,
        bool ExplicitPhotoUnavailable);
}
