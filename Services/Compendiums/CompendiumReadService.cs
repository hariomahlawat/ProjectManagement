using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure.Data;
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
    public const string BuildStamp = "CompendiumPdf_2026-08-16_editorial-constraints-v21";
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
                project.TechnicalCategory != null ? project.TechnicalCategory.SortOrder : int.MaxValue,
                project.ProjectBrief,
                project.Description,
                project.SponsoringLineDirectorate != null ? project.SponsoringLineDirectorate.Name : null,
                project.YearOfDevelopment,
                project.CompletedYear,
                project.CompletedOn,
                project.CreatedAt,
                project.CoverPhotoId))
            .ToListAsync(cancellationToken);

        if (projects.Count == 0)
        {
            return Array.Empty<CompendiumCandidateProjectVm>();
        }

        var projectIds = projects.Select(project => project.Id).ToArray();
        var capabilitiesByProject = await LoadCapabilityStatementsAsync(projectIds, cancellationToken);
        var technicalSpecificationCounts = await _db.ProjectTechnicalSpecificationItems
            .AsNoTracking()
            .Where(item => projectIds.Contains(item.ProjectId))
            .GroupBy(item => item.ProjectId)
            .ToDictionaryAsync(group => group.Key, group => group.Count(), cancellationToken);
        var iprProjectIds = await _db.IprRecords
            .AsNoTracking()
            .Where(item => item.ProjectId.HasValue
                           && projectIds.Contains(item.ProjectId.Value)
                           && (item.Status == IprStatus.Filed || item.Status == IprStatus.Granted))
            .Select(item => item.ProjectId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        var iprProjectSet = iprProjectIds.ToHashSet();
        var totProjectIds = await _db.ProjectTots
            .AsNoTracking()
            .Where(item => projectIds.Contains(item.ProjectId)
                           && (item.Status == ProjectTotStatus.InProgress || item.Status == ProjectTotStatus.Completed))
            .Select(item => item.ProjectId)
            .ToListAsync(cancellationToken);
        var totProjectSet = totProjectIds.ToHashSet();
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
                    !string.IsNullOrWhiteSpace(project.SponsoringLineDirectorate),
                    productionCost.HasValue,
                    projectPhotos.Count,
                    defaultPhotoId,
                    completionDisplay)
                {
                    ProliferationAvailability = availableForProliferation,
                    HasProjectBrief = !string.IsNullOrWhiteSpace(project.ProjectBrief),
                    HasCapabilityOverview = capabilitiesByProject.GetValueOrDefault(project.Id)?.Count > 0,
                    ProjectBriefWordCount = CountWords(project.ProjectBrief),
                    CapabilityStatementCount = capabilitiesByProject.GetValueOrDefault(project.Id)?.Count ?? 0,
                    DescriptionWordCount = CountWords(project.Description),
                    PublicationYear = ResolvePublicationYear(project.LifecycleStatus, project.YearOfDevelopment, project.CompletedYear, project.CompletedOn, project.CreatedAt),
                    TechnicalCategorySortOrder = project.TechnicalCategorySortOrder,
                    SponsoringLineDirectorateDisplay = NormalizeDisplay(project.SponsoringLineDirectorate, "Not recorded"),
                    ProliferationCostDisplay = CompendiumPublicationImagePolicy.FormatCost(productionCost),
                    TechnicalSpecificationCount = technicalSpecificationCounts.GetValueOrDefault(project.Id),
                    HasIpr = iprProjectSet.Contains(project.Id),
                    HasTechnologyTransfer = totProjectSet.Contains(project.Id)
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
        var sections = NormalizeSections(request.Sections);
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
        var capabilitiesByProject = await LoadCapabilityStatementsAsync(availableProjectIds, cancellationToken);
        var specificationsByProject = await LoadTechnicalSpecificationsAsync(availableProjectIds, cancellationToken);
        var iprByProject = await LoadIprCredentialsAsync(availableProjectIds, cancellationToken);
        var totByProject = await LoadTechnologyTransferAsync(availableProjectIds, cancellationToken);

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
            var capabilities = capabilitiesByProject.GetValueOrDefault(project.Id) ?? Array.Empty<string>();
            var effectiveNarrativeSource = selection.NarrativeSourceOverride ?? NormalizeNarrativeSource(request.NarrativeSource);
            var narrative = ResolveNarrative(project, capabilities, effectiveNarrativeSource);
            var effectiveNarrativeAlignment = selection.NarrativeAlignmentOverride
                                             ?? CompendiumNarrativeTypographyPolicy.Normalize(request.DefaultNarrativeAlignment);
            var specifications = specificationsByProject.GetValueOrDefault(project.Id) ?? Array.Empty<string>();
            var iprCredentials = iprByProject.GetValueOrDefault(project.Id) ?? Array.Empty<CompendiumIprCredentialDto>();
            var technologyTransfer = totByProject.GetValueOrDefault(project.Id);
            var sponsoringLineDirectorate = NormalizeOptional(project.SponsoringLineDirectorate) ?? string.Empty;
            var dossierImages = ResolveDossierImages(project, selection, projectPhotos, resolved);
            var programmeModules = CompendiumProgrammeInformation.Resolve(
                sponsoringLineDirectorate,
                CompendiumPublicationImagePolicy.FormatCost(cost?.Cost),
                iprCredentials,
                technologyTransfer);
            var programmeModuleCount = programmeModules.Count;
            var dossierPhotoCount = dossierImages.Count(item => item.PhotoId.HasValue);
            var dossierDecision = CompendiumDossierLayoutPlanner.Resolve(
                selection.DossierLayout,
                dossierPhotoCount,
                narrative.Text,
                specifications,
                programmeModuleCount,
                project.Name);
            var probe = resolved.ResolvedPhotoId.HasValue
                ? probes.GetValueOrDefault(resolved.ResolvedPhotoId.Value)
                : null;
            var preferredFrameWidth = CompendiumDossierPaginationPlanner.ResolvePrimaryFrameWidthPoints(dossierDecision.Layout, dossierPhotoCount);
            var preferredFrameHeight = CompendiumDossierPaginationPlanner.PreferredImageHeight(dossierDecision.Layout, dossierPhotoCount);
            var planningDpi = probe is { IsReady: true }
                ? CompendiumPublicationImagePolicy.CalculateEffectiveDpi(
                    probe.Width, probe.Height, preferredFrameWidth, preferredFrameHeight, resolved.Selection.ImageFitMode)
                : null;
            var paginationDecision = CompendiumDossierPaginationPlanner.Resolve(
                selection.DossierLayout,
                dossierDecision.Layout,
                dossierPhotoCount,
                narrative.Text,
                specifications,
                programmeModuleCount,
                project.Name,
                planningDpi,
                selection.BalancedTextFlowMode,
                probe?.Width,
                probe?.Height,
                resolved.Selection.ImageFitMode);
            var dossierFrameWidth = CompendiumDossierPaginationPlanner.ResolvePrimaryFrameWidthPoints(
                paginationDecision.Layout,
                dossierPhotoCount);
            var dossierFrameHeight = paginationDecision.PrimaryImageHeightPoints;
            var sectionAssignment = ResolveSectionAssignment(selection, sections);
            var effectiveDpi = probe is { IsReady: true }
                ? CompendiumPublicationImagePolicy.CalculateEffectiveDpi(
                    probe.Width,
                    probe.Height,
                    dossierFrameWidth,
                    dossierFrameHeight,
                    resolved.Selection.ImageFitMode)
                : null;
            var imageQuality = CompendiumPublicationImagePolicy.Classify(effectiveDpi);
            var narrativeFlow = CompendiumDossierNarrativeFlowPlanner.Resolve(
                narrative.Text,
                selection.BalancedTextFlowMode,
                paginationDecision.Layout,
                dossierPhotoCount > 0,
                paginationDecision.PrimaryImageHeightPoints,
                paginationDecision.NarrativeFontScale,
                paginationDecision.FirstPageNarrativeBudget,
                effectiveNarrativeAlignment,
                CompendiumDossierPaginationPlanner.ResolveBalancedSideWidthPoints(dossierPhotoCount));
            var completionYear = ResolveCompletionYear(project.CompletedYear, project.CompletedOn);
            var fingerprint = CompendiumReviewFingerprint.Create(new CompendiumReviewFingerprintInput(
                project.Id,
                project.Name,
                project.LifecycleStatus,
                project.ProjectCategory,
                project.TechnicalCategory,
                sponsoringLineDirectorate,
                completionYear,
                availableForProliferation,
                cost?.Cost,
                narrative.Text,
                resolved.ResolvedPhotoId,
                resolved.Selection.ImageSelectionMode,
                resolved.Selection.FocalX,
                resolved.Selection.FocalY)
            {
                NarrativeSource = effectiveNarrativeSource,
                PublicationSectionKey = sectionAssignment.SectionKey,
                PublicationSectionName = sectionAssignment.SectionName,
                ImageFitMode = resolved.Selection.ImageFitMode,
                DossierLayout = selection.DossierLayout,
                BalancedTextFlowMode = selection.BalancedTextFlowMode,
                NarrativeAlignment = effectiveNarrativeAlignment,
                DossierImages = dossierImages,
                TechnicalSpecifications = specifications,
                IprCredentials = iprCredentials,
                TechnologyTransfer = technologyTransfer
            });

            var assessment = _readinessPolicy.Evaluate(new CompendiumProjectReadinessContext(
                project.Id,
                project.Name,
                project.LifecycleStatus,
                completionYear,
                sponsoringLineDirectorate,
                narrative.Text,
                cost?.Cost,
                availableForProliferation,
                resolved.ResolvedPhotoId,
                probe?.IsReady == true,
                resolved.Selection.ImageSelectionMode,
                effectiveDpi,
                resolved.ExplicitPhotoUnavailable,
                fingerprint,
                resolved.Selection.ReviewFingerprint)
            {
                NarrativeLabel = narrative.Label,
                DossierEditorialWarning = paginationDecision.EditorialWarning
            });

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
                NormalizeDisplay(project.SponsoringLineDirectorate, "Not recorded"),
                cost?.Cost,
                NormalizeOptional(cost?.Remarks),
                resolved.ResolvedPhotoId,
                resolved.PhotoSelectionSource,
                narrative.Text,
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
                ExplicitPhotoUnavailable = resolved.ExplicitPhotoUnavailable,
                NarrativeSource = effectiveNarrativeSource,
                NarrativeLabel = narrative.Label,
                CustomSectionKey = sectionAssignment.SectionKey,
                CustomSectionName = sectionAssignment.SectionName,
                UsesNarrativeOverride = selection.NarrativeSourceOverride.HasValue,
                PublicationYear = ResolvePublicationYear(project.LifecycleStatus, project.YearOfDevelopment, project.CompletedYear, project.CompletedOn, project.CreatedAt),
                TechnicalCategorySortOrder = project.TechnicalCategorySortOrder,
                ImageFitMode = resolved.Selection.ImageFitMode,
                ProgrammeModules = programmeModules,
                IprCredentials = iprCredentials,
                TechnologyTransfer = technologyTransfer,
                TechnicalSpecifications = specifications,
                DossierLayoutOverride = selection.DossierLayout,
                EffectiveDossierLayout = paginationDecision.Layout,
                DossierLayoutReason = dossierDecision.Reason,
                DossierPressureScore = dossierDecision.PressureScore,
                DossierPrimaryImageHeightPoints = paginationDecision.PrimaryImageHeightPoints,
                DossierNarrativeFontScale = paginationDecision.NarrativeFontScale,
                DossierFirstPageNarrativeBudget = paginationDecision.FirstPageNarrativeBudget,
                DossierFirstPageSpecificationCount = paginationDecision.FirstPageSpecificationCount,
                DossierSpecificationColumns = paginationDecision.SpecificationColumns,
                DossierProgrammeColumns = paginationDecision.ProgrammeColumns,
                BalancedTextFlowMode = selection.BalancedTextFlowMode,
                NarrativeAlignment = effectiveNarrativeAlignment,
                UsesNarrativeAlignmentOverride = selection.NarrativeAlignmentOverride.HasValue,
                NarrativeFlow = narrativeFlow,
                EstimatedDossierPageCount = Math.Max(paginationDecision.EstimatedPageCount, narrativeFlow.EstimatedPageCount),
                DossierPaginationNote = paginationDecision.PaginationNote,
                DossierPaginationReason = paginationDecision.Reason,
                DossierEditorialWarning = paginationDecision.EditorialWarning,
                DossierImageCount = dossierPhotoCount,
                DossierImages = dossierImages
            });
        }

        if (NormalizeGroupingMode(request.GroupingMode) == CompendiumGroupingMode.CustomSections)
        {
            var assignedKeys = sections.Select(section => section.SectionKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var project in publicationProjects.Where(project => string.IsNullOrWhiteSpace(project.CustomSectionKey)
                                                                         || !assignedKeys.Contains(project.CustomSectionKey!)))
            {
                findings.Add(new CompendiumFindingDto(
                    CompendiumFindingSeverity.Warning,
                    "customSectionUnassigned",
                    $"{project.ProjectName} is not assigned to a custom publication section and will appear under Other Projects.",
                    project.ProjectId,
                    project.ProjectName));
            }
        }

        var structure = BuildPublicationStructure(publicationProjects, request.GroupingMode, request.SortMode, sections);
        var orderedPublicationProjects = structure.Projects;
        var groups = structure.Groups;
        var projectOrder = orderedPublicationProjects.ToDictionary(project => project.ProjectId, project => project.SortOrder);
        var orderedFindings = findings
            .OrderByDescending(finding => finding.Severity)
            .ThenBy(finding => finding.ProjectId.HasValue
                ? projectOrder.GetValueOrDefault(finding.ProjectId.Value, int.MaxValue)
                : -1)
            .ThenBy(finding => finding.Code, StringComparer.Ordinal)
            .ToArray();
        var readinessProjects = orderedPublicationProjects
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
            MissingSponsoringLineDirectorateCount: CountIssue(publicationProjects, CompendiumPublicationIssue.MissingSponsoringLineDirectorate),
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

    public Task<CompendiumReviewProjectDto?> GetReviewProjectAsync(
        CompendiumProjectSelection selection,
        CancellationToken cancellationToken = default)
        => GetReviewProjectAsync(selection, CompendiumNarrativeSource.ProjectBrief, CompendiumNarrativeAlignment.Left, cancellationToken);

    public Task<CompendiumReviewProjectDto?> GetReviewProjectAsync(
        CompendiumProjectSelection selection,
        CompendiumNarrativeSource narrativeSource,
        CancellationToken cancellationToken = default)
        => GetReviewProjectAsync(selection, narrativeSource, CompendiumNarrativeAlignment.Left, cancellationToken);

    public async Task<CompendiumReviewProjectDto?> GetReviewProjectAsync(
        CompendiumProjectSelection selection,
        CompendiumNarrativeSource narrativeSource,
        CompendiumNarrativeAlignment defaultNarrativeAlignment,
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

        narrativeSource = selection.NarrativeSourceOverride ?? NormalizeNarrativeSource(narrativeSource);
        var capabilitiesByProject = await LoadCapabilityStatementsAsync(new[] { project.Id }, cancellationToken);
        var capabilities = capabilitiesByProject.GetValueOrDefault(project.Id) ?? Array.Empty<string>();
        var narrative = ResolveNarrative(project, capabilities, narrativeSource);
        var effectiveNarrativeAlignment = selection.NarrativeAlignmentOverride
                                         ?? CompendiumNarrativeTypographyPolicy.Normalize(defaultNarrativeAlignment);
        var specifications = (await LoadTechnicalSpecificationsAsync(new[] { project.Id }, cancellationToken)).GetValueOrDefault(project.Id) ?? Array.Empty<string>();
        var iprCredentials = (await LoadIprCredentialsAsync(new[] { project.Id }, cancellationToken)).GetValueOrDefault(project.Id) ?? Array.Empty<CompendiumIprCredentialDto>();
        var technologyTransfer = (await LoadTechnologyTransferAsync(new[] { project.Id }, cancellationToken)).GetValueOrDefault(project.Id);
        var sponsoringLineDirectorate = NormalizeOptional(project.SponsoringLineDirectorate) ?? string.Empty;

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
        var dossierImages = ResolveDossierImages(project, selection, photoCandidates, resolved);
        var programmeModules = CompendiumProgrammeInformation.Resolve(
            sponsoringLineDirectorate,
            CompendiumPublicationImagePolicy.FormatCost(cost?.Cost),
            iprCredentials,
            technologyTransfer);
        var programmeModuleCount = programmeModules.Count;
        var dossierPhotoCount = dossierImages.Count(item => item.PhotoId.HasValue);
        var dossierDecision = CompendiumDossierLayoutPlanner.Resolve(
            selection.DossierLayout,
            dossierPhotoCount,
            narrative.Text, specifications, programmeModuleCount, project.Name);
        var photoReferences = photoCandidates
            .Select(photo => new BrochurePhotoReference(project.Id, photo.Id))
            .ToArray();
        var probes = photoReferences.Length == 0
            ? new Dictionary<int, BrochurePhotoProbe>()
            : (await _photoService.ProbeAsync(photoReferences, cancellationToken)).ToDictionary(pair => pair.Key, pair => pair.Value);

        var selectedProbe = resolved.ResolvedPhotoId.HasValue
            ? probes.GetValueOrDefault(resolved.ResolvedPhotoId.Value)
            : null;
        var preferredFrameWidth = CompendiumDossierPaginationPlanner.ResolvePrimaryFrameWidthPoints(
            dossierDecision.Layout,
            dossierPhotoCount);
        var preferredFrameHeight = CompendiumDossierPaginationPlanner.PreferredImageHeight(
            dossierDecision.Layout,
            dossierPhotoCount);
        var planningDpi = selectedProbe is { IsReady: true }
            ? CompendiumPublicationImagePolicy.CalculateEffectiveDpi(
                selectedProbe.Width,
                selectedProbe.Height,
                preferredFrameWidth,
                preferredFrameHeight,
                resolved.Selection.ImageFitMode)
            : null;
        var paginationDecision = CompendiumDossierPaginationPlanner.Resolve(
            selection.DossierLayout,
            dossierDecision.Layout,
            dossierPhotoCount,
            narrative.Text,
            specifications,
            programmeModuleCount,
            project.Name,
            planningDpi,
            selection.BalancedTextFlowMode,
            selectedProbe?.Width,
            selectedProbe?.Height,
            resolved.Selection.ImageFitMode);
        var dossierFrameWidth = CompendiumDossierPaginationPlanner.ResolvePrimaryFrameWidthPoints(
            paginationDecision.Layout,
            dossierPhotoCount);
        var dossierFrameHeight = paginationDecision.PrimaryImageHeightPoints;
        var effectiveDpi = selectedProbe is { IsReady: true }
            ? CompendiumPublicationImagePolicy.CalculateEffectiveDpi(
                selectedProbe.Width,
                selectedProbe.Height,
                dossierFrameWidth,
                dossierFrameHeight,
                resolved.Selection.ImageFitMode)
            : null;

        var photos = photoCandidates
            .OrderBy(photo => photo.Ordinal)
            .ThenByDescending(photo => photo.UpdatedUtc)
            .Select(photo =>
            {
                var probe = probes.GetValueOrDefault(photo.Id);
                var dpi = probe is { IsReady: true }
                    ? CompendiumPublicationImagePolicy.CalculateEffectiveDpi(
                        probe.Width,
                        probe.Height,
                        dossierFrameWidth,
                        dossierFrameHeight,
                        selection.ImageFitMode)
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

        var narrativeFlow = CompendiumDossierNarrativeFlowPlanner.Resolve(
            narrative.Text, selection.BalancedTextFlowMode, paginationDecision.Layout, dossierPhotoCount > 0,
            paginationDecision.PrimaryImageHeightPoints, paginationDecision.NarrativeFontScale, paginationDecision.FirstPageNarrativeBudget,
            effectiveNarrativeAlignment, CompendiumDossierPaginationPlanner.ResolveBalancedSideWidthPoints(dossierPhotoCount));
        var completionYear = ResolveCompletionYear(project.CompletedYear, project.CompletedOn);
        var fingerprint = CompendiumReviewFingerprint.Create(new CompendiumReviewFingerprintInput(
            project.Id,
            project.Name,
            project.LifecycleStatus,
            project.ProjectCategory,
            project.TechnicalCategory,
            sponsoringLineDirectorate,
            completionYear,
            availability,
            cost?.Cost,
            narrative.Text,
            resolved.ResolvedPhotoId,
            resolved.Selection.ImageSelectionMode,
            resolved.Selection.FocalX,
            resolved.Selection.FocalY)
        {
            NarrativeSource = narrativeSource,
            PublicationSectionKey = selection.CustomSectionKey,
            PublicationSectionName = selection.CustomSectionName,
            ImageFitMode = resolved.Selection.ImageFitMode,
            DossierLayout = selection.DossierLayout,
            BalancedTextFlowMode = selection.BalancedTextFlowMode,
            NarrativeAlignment = effectiveNarrativeAlignment,
            DossierImages = dossierImages,
            TechnicalSpecifications = specifications,
            IprCredentials = iprCredentials,
            TechnologyTransfer = technologyTransfer
        });
        var assessment = _readinessPolicy.Evaluate(new CompendiumProjectReadinessContext(
            project.Id,
            project.Name,
            project.LifecycleStatus,
            completionYear,
            sponsoringLineDirectorate,
            narrative.Text,
            cost?.Cost,
            availability,
            resolved.ResolvedPhotoId,
            selectedProbe?.IsReady == true,
            resolved.Selection.ImageSelectionMode,
            effectiveDpi,
            resolved.ExplicitPhotoUnavailable,
            fingerprint,
            resolved.Selection.ReviewFingerprint)
        {
            NarrativeLabel = narrative.Label,
            DossierEditorialWarning = paginationDecision.EditorialWarning
        });

        return new CompendiumReviewProjectDto(
            project.Id,
            NormalizeDisplay(project.Name, $"Project {project.Id}"),
            LifecycleDisplay(project.LifecycleStatus),
            NormalizeOptional(project.ProjectCategory),
            NormalizeDisplay(project.TechnicalCategory, "Not recorded"),
            NormalizeDisplay(project.SponsoringLineDirectorate, "Not recorded"),
            project.LifecycleStatus == ProjectLifecycleStatus.Completed
                ? completionYear?.ToString(CultureInfo.InvariantCulture) ?? "Not recorded"
                : string.Empty,
            availability,
            cost?.Cost,
            CompendiumPublicationImagePolicy.FormatCost(cost?.Cost),
            NormalizeOptional(narrative.Text) ?? string.Empty,
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
            ImageFrameWidthPoints = dossierFrameWidth,
            ImageFrameHeightPoints = dossierFrameHeight,
            NarrativeSource = narrativeSource,
            NarrativeLabel = narrative.Label,
            HasProjectBrief = !string.IsNullOrWhiteSpace(project.ProjectBrief),
            HasCapabilityOverview = capabilities.Count > 0,
            HasProjectDescription = !string.IsNullOrWhiteSpace(project.Description),
            ProjectBriefWordCount = CountWords(project.ProjectBrief),
            CapabilityStatementCount = capabilities.Count,
            DescriptionWordCount = CountWords(project.Description),
            CustomSectionKey = NormalizeSectionKey(selection.CustomSectionKey),
            CustomSectionName = NormalizeCustomSection(selection.CustomSectionName),
            UsesNarrativeOverride = selection.NarrativeSourceOverride.HasValue,
            ImageFitMode = resolved.Selection.ImageFitMode,
            ProgrammeModules = programmeModules,
            IprCredentials = iprCredentials,
            TechnologyTransfer = technologyTransfer,
            TechnicalSpecifications = specifications,
            DossierLayoutOverride = selection.DossierLayout,
            EffectiveDossierLayout = paginationDecision.Layout,
            DossierLayoutReason = dossierDecision.Reason,
            DossierPressureScore = dossierDecision.PressureScore,
            DossierPrimaryImageHeightPoints = paginationDecision.PrimaryImageHeightPoints,
            DossierNarrativeFontScale = paginationDecision.NarrativeFontScale,
            DossierFirstPageNarrativeBudget = paginationDecision.FirstPageNarrativeBudget,
            DossierFirstPageSpecificationCount = paginationDecision.FirstPageSpecificationCount,
            DossierSpecificationColumns = paginationDecision.SpecificationColumns,
            DossierProgrammeColumns = paginationDecision.ProgrammeColumns,
            BalancedTextFlowMode = selection.BalancedTextFlowMode,
            NarrativeAlignment = effectiveNarrativeAlignment,
            UsesNarrativeAlignmentOverride = selection.NarrativeAlignmentOverride.HasValue,
            NarrativeFlow = narrativeFlow,
            EstimatedDossierPageCount = Math.Max(paginationDecision.EstimatedPageCount, narrativeFlow.EstimatedPageCount),
            DossierPaginationNote = paginationDecision.PaginationNote,
            DossierPaginationReason = paginationDecision.Reason,
            DossierEditorialWarning = paginationDecision.EditorialWarning,
            DossierImageCount = dossierPhotoCount,
            DossierImages = dossierImages
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
            new CompendiumPublicationRequest(legacySelections)
            {
                NarrativeSource = CompendiumNarrativeSource.ProjectDescription,
                GroupingMode = CompendiumGroupingMode.TechnicalCategory,
                SortMode = CompendiumSortMode.Manual
            },
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
            Edition = NormalizeDisplay(request.Edition, $"Capability Edition · {istYear}"),
            NarrativeSource = NormalizeNarrativeSource(request.NarrativeSource),
            DefaultNarrativeAlignment = CompendiumNarrativeTypographyPolicy.Normalize(request.DefaultNarrativeAlignment),
            GroupingMode = NormalizeGroupingMode(request.GroupingMode),
            SortMode = NormalizeSortMode(request.SortMode),
            CoverDesign = request.CoverDesign
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
                project.ProjectBrief,
                project.Description,
                project.YearOfDevelopment,
                project.CompletedYear,
                project.CompletedOn,
                project.CreatedAt,
                project.CoverPhotoId,
                project.SponsoringLineDirectorate != null ? project.SponsoringLineDirectorate.Name : null,
                project.Category != null ? project.Category.Name : null,
                project.TechnicalCategory != null ? project.TechnicalCategory.Name : null,
                project.TechnicalCategory != null ? project.TechnicalCategory.SortOrder : int.MaxValue))
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

    private static IReadOnlyList<CompendiumDossierImageSelection> ResolveDossierImages(
        PublicationRow project,
        CompendiumProjectSelection rawSelection,
        IReadOnlyList<PhotoCandidate> candidates,
        ResolvedSelection primary)
    {
        var selection = NormalizeSelection(rawSelection);
        var requestedCount = Math.Clamp(selection.DossierImageCount, 1, 3);
        var result = new List<CompendiumDossierImageSelection>(requestedCount)
        {
            new(
                CompendiumDossierImageRole.Primary,
                primary.ResolvedPhotoId,
                selection.FocalX,
                selection.FocalY,
                selection.ImageFitMode,
                primary.PhotoSelectionSource)
        };

        if (requestedCount == 1)
        {
            return result;
        }

        var used = result.Where(item => item.PhotoId.HasValue).Select(item => item.PhotoId!.Value).ToHashSet();
        var ranked = RankAutomaticPhotos(project.CoverPhotoId, candidates).ToList();

        CompendiumDossierImageSelection ResolveSupporting(
            CompendiumDossierImageRole role,
            int? explicitPhotoId,
            double focalX,
            double focalY,
            CompendiumImageFitMode fitMode)
        {
            int? photoId = null;
            var source = CompendiumPhotoSelectionSource.None;
            if (explicitPhotoId is > 0 && candidates.Any(item => item.Id == explicitPhotoId.Value) && !used.Contains(explicitPhotoId.Value))
            {
                photoId = explicitPhotoId;
                source = CompendiumPhotoSelectionSource.ExplicitPublication;
            }
            else
            {
                var fallback = ranked.FirstOrDefault(item => !used.Contains(item.Id));
                if (fallback is not null)
                {
                    photoId = fallback.Id;
                    source = fallback.Id == project.CoverPhotoId
                        ? CompendiumPhotoSelectionSource.ProjectCover
                        : fallback.IsCover
                            ? CompendiumPhotoSelectionSource.MarkedCover
                            : CompendiumPhotoSelectionSource.FirstAvailable;
                }
            }

            if (photoId.HasValue)
            {
                used.Add(photoId.Value);
            }

            return new CompendiumDossierImageSelection(
                role,
                photoId,
                ClampFocal(focalX),
                ClampFocal(focalY),
                Enum.IsDefined(fitMode) ? fitMode : CompendiumImageFitMode.Fill,
                source);
        }

        result.Add(ResolveSupporting(
            CompendiumDossierImageRole.Supporting1,
            selection.SupportingPhoto1Id,
            selection.SupportingPhoto1FocalX,
            selection.SupportingPhoto1FocalY,
            selection.SupportingPhoto1FitMode));

        if (requestedCount >= 3)
        {
            result.Add(ResolveSupporting(
                CompendiumDossierImageRole.Supporting2,
                selection.SupportingPhoto2Id,
                selection.SupportingPhoto2FocalX,
                selection.SupportingPhoto2FocalY,
                selection.SupportingPhoto2FitMode));
        }

        return result;
    }

    private static IEnumerable<PhotoCandidate> RankAutomaticPhotos(
        int? projectCoverPhotoId,
        IReadOnlyList<PhotoCandidate> candidates)
        => candidates
            .OrderBy(candidate => projectCoverPhotoId.HasValue && candidate.Id == projectCoverPhotoId.Value ? 0 : candidate.IsCover ? 1 : 2)
            .ThenBy(candidate => candidate.IsLowResolution)
            .ThenBy(candidate => candidate.Ordinal)
            .ThenByDescending(candidate => candidate.UpdatedUtc);

    private async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> LoadTechnicalSpecificationsAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken)
    {
        if (projectIds.Count == 0)
        {
            return new Dictionary<int, IReadOnlyList<string>>();
        }

        var rows = await _db.ProjectTechnicalSpecificationItems
            .AsNoTracking()
            .Where(item => projectIds.Contains(item.ProjectId))
            .OrderBy(item => item.ProjectId)
            .ThenBy(item => item.DisplayOrder)
            .Select(item => new { item.ProjectId, item.Text })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(item => item.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(item => item.Text.Trim()).Where(text => text.Length > 0).Take(ProjectFieldLimits.TechnicalSpecificationMaximumCount).ToArray());
    }

    private async Task<IReadOnlyDictionary<int, IReadOnlyList<CompendiumIprCredentialDto>>> LoadIprCredentialsAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken)
    {
        if (projectIds.Count == 0)
        {
            return new Dictionary<int, IReadOnlyList<CompendiumIprCredentialDto>>();
        }

        var rows = await _db.IprRecords
            .AsNoTracking()
            .Where(item => item.ProjectId.HasValue
                           && projectIds.Contains(item.ProjectId.Value)
                           && (item.Status == IprStatus.Filed || item.Status == IprStatus.Granted))
            .Select(item => new
            {
                ProjectId = item.ProjectId!.Value,
                item.Type,
                item.Status,
                item.FiledAtUtc,
                item.GrantedAtUtc
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(item => item.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<CompendiumIprCredentialDto>)group
                    .OrderByDescending(item => item.Status == IprStatus.Granted)
                    .ThenBy(item => item.Type)
                    .Select(item => new CompendiumIprCredentialDto(
                        item.Type == IprType.Copyright ? "Copyright" : "Patent",
                        item.Status == IprStatus.Granted ? "Granted" : "Filed",
                        (item.Status == IprStatus.Granted ? item.GrantedAtUtc : item.FiledAtUtc)?.Year))
                    .ToArray());
    }

    private async Task<IReadOnlyDictionary<int, CompendiumTechnologyTransferDto>> LoadTechnologyTransferAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken)
    {
        if (projectIds.Count == 0)
        {
            return new Dictionary<int, CompendiumTechnologyTransferDto>();
        }

        var rows = await _db.ProjectTots
            .AsNoTracking()
            .Where(item => projectIds.Contains(item.ProjectId)
                           && (item.Status == ProjectTotStatus.InProgress || item.Status == ProjectTotStatus.Completed))
            .Select(item => new { item.ProjectId, item.Status, item.CompletedOn })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            item => item.ProjectId,
            item => new CompendiumTechnologyTransferDto(
                item.Status == ProjectTotStatus.Completed ? "Completed" : "In progress",
                item.Status == ProjectTotStatus.Completed ? item.CompletedOn.HasValue ? item.CompletedOn.Value.Year : null : null));
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
            ReviewFingerprint = CleanFingerprint(selection.ReviewFingerprint),
            CustomSectionKey = NormalizeSectionKey(selection.CustomSectionKey),
            CustomSectionName = NormalizeCustomSection(selection.CustomSectionName),
            NarrativeSourceOverride = selection.NarrativeSourceOverride.HasValue
                ? NormalizeNarrativeSource(selection.NarrativeSourceOverride.Value)
                : null,
            ImageFitMode = Enum.IsDefined(selection.ImageFitMode)
                ? selection.ImageFitMode
                : CompendiumImageFitMode.Fill,
            DossierLayout = Enum.IsDefined(selection.DossierLayout) ? selection.DossierLayout : CompendiumDossierLayout.Automatic,
            BalancedTextFlowMode = Enum.IsDefined(selection.BalancedTextFlowMode)
                ? selection.BalancedTextFlowMode
                : CompendiumBalancedTextFlowMode.FlowBelowImage,
            NarrativeAlignmentOverride = selection.NarrativeAlignmentOverride.HasValue
                                         && Enum.IsDefined(selection.NarrativeAlignmentOverride.Value)
                ? selection.NarrativeAlignmentOverride
                : null,
            DossierImageCount = Math.Clamp(selection.DossierImageCount, 1, 3),
            SupportingPhoto1Id = selection.SupportingPhoto1Id is > 0 ? selection.SupportingPhoto1Id : null,
            SupportingPhoto1FocalX = ClampFocal(selection.SupportingPhoto1FocalX),
            SupportingPhoto1FocalY = ClampFocal(selection.SupportingPhoto1FocalY),
            SupportingPhoto1FitMode = Enum.IsDefined(selection.SupportingPhoto1FitMode) ? selection.SupportingPhoto1FitMode : CompendiumImageFitMode.Fill,
            SupportingPhoto2Id = selection.SupportingPhoto2Id is > 0 ? selection.SupportingPhoto2Id : null,
            SupportingPhoto2FocalX = ClampFocal(selection.SupportingPhoto2FocalX),
            SupportingPhoto2FocalY = ClampFocal(selection.SupportingPhoto2FocalY),
            SupportingPhoto2FitMode = Enum.IsDefined(selection.SupportingPhoto2FitMode) ? selection.SupportingPhoto2FitMode : CompendiumImageFitMode.Fill
        };

    private static PublicationStructureResult BuildPublicationStructure(
        IReadOnlyList<CompendiumProjectDto> projects,
        CompendiumGroupingMode groupingMode,
        CompendiumSortMode sortMode,
        IReadOnlyList<CompendiumPublicationSection> sections)
    {
        groupingMode = NormalizeGroupingMode(groupingMode);
        sortMode = NormalizeSortMode(sortMode);
        if (projects.Count == 0)
        {
            return new PublicationStructureResult(Array.Empty<CompendiumProjectDto>(), Array.Empty<CompendiumCategoryGroupDto>());
        }

        var authoredOrder = projects.OrderBy(project => project.SortOrder).ToArray();
        var grouped = new List<(string Name, IReadOnlyList<CompendiumProjectDto> Projects)>();

        if (groupingMode == CompendiumGroupingMode.None)
        {
            grouped.Add(("Projects", SortProjects(authoredOrder, sortMode)));
        }
        else if (groupingMode == CompendiumGroupingMode.CustomSections)
        {
            var normalizedSections = NormalizeSections(sections);
            foreach (var section in normalizedSections.OrderBy(section => section.SortOrder))
            {
                var members = authoredOrder
                    .Where(project => string.Equals(project.CustomSectionKey, section.SectionKey, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (members.Length > 0)
                {
                    grouped.Add((section.Name, SortProjects(members, sortMode)));
                }
            }

            var knownKeys = normalizedSections.Select(section => section.SectionKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unassigned = authoredOrder
                .Where(project => string.IsNullOrWhiteSpace(project.CustomSectionKey)
                                  || !knownKeys.Contains(project.CustomSectionKey!))
                .ToArray();
            if (unassigned.Length > 0)
            {
                grouped.Add(("Other Projects", SortProjects(unassigned, sortMode)));
            }
        }
        else
        {
            // Technical-category mode follows authoritative master-data ordering. Manual/latest/A-Z
            // affects projects inside a category, never the catalogue taxonomy itself.
            var technicalGroups = authoredOrder
                .GroupBy(project => NormalizeDisplay(project.TechnicalCategoryName, "Not recorded"), StringComparer.OrdinalIgnoreCase)
                .Select(group => new
                {
                    Name = group.First().TechnicalCategoryName is null ? "Not recorded" : NormalizeDisplay(group.First().TechnicalCategoryName, "Not recorded"),
                    SortOrder = group.Min(project => project.TechnicalCategorySortOrder),
                    Projects = group.ToArray()
                })
                .OrderBy(group => group.SortOrder)
                .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var group in technicalGroups)
            {
                grouped.Add((group.Name, SortProjects(group.Projects, sortMode)));
            }
        }

        var flattened = new List<CompendiumProjectDto>(projects.Count);
        var finalGroups = new List<CompendiumCategoryGroupDto>(grouped.Count);
        var nextOrder = 0;
        foreach (var group in grouped)
        {
            var groupProjects = group.Projects
                .Select(project => project with { SortOrder = nextOrder++ })
                .ToArray();
            flattened.AddRange(groupProjects);
            finalGroups.Add(new CompendiumCategoryGroupDto(group.Name, groupProjects));
        }

        return new PublicationStructureResult(flattened, finalGroups);
    }

    private static IReadOnlyList<CompendiumProjectDto> SortProjects(
        IReadOnlyList<CompendiumProjectDto> projects,
        CompendiumSortMode sortMode)
        => NormalizeSortMode(sortMode) switch
        {
            CompendiumSortMode.LatestFirst => projects
                .OrderByDescending(project => project.PublicationYear)
                .ThenByDescending(project => project.CompletionYearValue ?? 0)
                .ThenBy(project => project.ProjectName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(project => project.SortOrder)
                .ToArray(),
            CompendiumSortMode.Alphabetical => projects
                .OrderBy(project => project.ProjectName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(project => project.SortOrder)
                .ToArray(),
            _ => projects.OrderBy(project => project.SortOrder).ToArray()
        };

    private static IReadOnlyList<CompendiumPublicationSection> NormalizeSections(
        IReadOnlyList<CompendiumPublicationSection>? sections)
    {
        if (sections is null || sections.Count == 0)
        {
            return Array.Empty<CompendiumPublicationSection>();
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<CompendiumPublicationSection>();
        foreach (var section in sections.OrderBy(section => section.SortOrder))
        {
            var key = NormalizeSectionKey(section.SectionKey);
            var name = NormalizeCustomSection(section.Name);
            if (key is null || name is null || !keys.Add(key) || !names.Add(name))
            {
                continue;
            }

            result.Add(new CompendiumPublicationSection(key, name, result.Count));
        }

        return result;
    }

    private static SectionAssignment ResolveSectionAssignment(
        CompendiumProjectSelection selection,
        IReadOnlyList<CompendiumPublicationSection> sections)
    {
        var key = NormalizeSectionKey(selection.CustomSectionKey);
        if (key is not null)
        {
            var byKey = sections.FirstOrDefault(section => string.Equals(section.SectionKey, key, StringComparison.OrdinalIgnoreCase));
            if (byKey is not null)
            {
                return new SectionAssignment(byKey.SectionKey, byKey.Name);
            }
        }

        var legacyName = NormalizeCustomSection(selection.CustomSectionName);
        if (legacyName is not null)
        {
            var byName = sections.FirstOrDefault(section => string.Equals(section.Name, legacyName, StringComparison.OrdinalIgnoreCase));
            if (byName is not null)
            {
                return new SectionAssignment(byName.SectionKey, byName.Name);
            }
        }

        return new SectionAssignment(null, legacyName);
    }

    private static int? ResolveCompletionYear(int? completedYear, DateOnly? completedOn)
        => completedYear ?? completedOn?.Year;

    private static string LifecycleDisplay(ProjectLifecycleStatus lifecycleStatus)
        => lifecycleStatus == ProjectLifecycleStatus.Completed ? "Completed" : "Ongoing";

    private static string NormalizeDisplay(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> LoadCapabilityStatementsAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken)
    {
        if (projectIds.Count == 0)
        {
            return new Dictionary<int, IReadOnlyList<string>>();
        }

        var rows = await _db.ProjectCapabilityStatements
            .AsNoTracking()
            .Where(statement => projectIds.Contains(statement.ProjectId))
            .OrderBy(statement => statement.ProjectId)
            .ThenBy(statement => statement.DisplayOrder)
            .ThenBy(statement => statement.Id)
            .Select(statement => new CapabilityRow(statement.ProjectId, statement.Statement))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(row => NormalizeOptional(row.Statement))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!)
                    .ToArray());
    }

    private static NarrativeResolution ResolveNarrative(
        PublicationRow project,
        IReadOnlyList<string> capabilities,
        CompendiumNarrativeSource source)
    {
        source = NormalizeNarrativeSource(source);
        return source switch
        {
            CompendiumNarrativeSource.ProjectBrief => new NarrativeResolution(
                NormalizeOptional(project.ProjectBrief) ?? string.Empty,
                "Project Brief"),
            CompendiumNarrativeSource.CapabilityOverview => new NarrativeResolution(
                capabilities.Count == 0
                    ? string.Empty
                    : string.Join("\n", capabilities.Select(statement => $"- {statement}")),
                "Capability Overview"),
            CompendiumNarrativeSource.ProjectDescription => new NarrativeResolution(
                NormalizeOptional(project.Description) ?? string.Empty,
                "Project Description"),
            _ => throw new InvalidOperationException("The selected Compendium narrative source is invalid.")
        };
    }

    private static CompendiumNarrativeSource NormalizeNarrativeSource(CompendiumNarrativeSource source)
        => Enum.IsDefined(source) ? source : CompendiumNarrativeSource.ProjectBrief;

    private static CompendiumGroupingMode NormalizeGroupingMode(CompendiumGroupingMode mode)
        => Enum.IsDefined(mode) ? mode : CompendiumGroupingMode.TechnicalCategory;

    private static CompendiumSortMode NormalizeSortMode(CompendiumSortMode mode)
        => Enum.IsDefined(mode) ? mode : CompendiumSortMode.Manual;

    private static string? NormalizeCustomSection(string? value)
    {
        var clean = NormalizeOptional(value);
        if (clean is null)
        {
            return null;
        }

        return clean.Length <= 120 ? clean : clean[..120].TrimEnd();
    }

    private static string? NormalizeSectionKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var clean = new string(value.Trim()
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .Take(40)
            .ToArray());
        return clean.Length == 0 ? null : clean;
    }

    private static int ResolvePublicationYear(
        ProjectLifecycleStatus lifecycleStatus,
        short? yearOfDevelopment,
        int? completedYear,
        DateOnly? completedOn,
        DateTime createdAt)
    {
        // "Latest" means the most recent meaningful lifecycle chronology: completion for
        // completed projects, project/development year for ongoing work. Database import time
        // is used only as a final deterministic fallback.
        if (lifecycleStatus == ProjectLifecycleStatus.Completed)
        {
            return ResolveCompletionYear(completedYear, completedOn)
                   ?? (yearOfDevelopment.HasValue ? yearOfDevelopment.Value : createdAt.Year);
        }

        return yearOfDevelopment.HasValue ? yearOfDevelopment.Value : createdAt.Year;
    }

    private static int CountWords(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? 0
            : value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

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

    private sealed record PublicationStructureResult(
        IReadOnlyList<CompendiumProjectDto> Projects,
        IReadOnlyList<CompendiumCategoryGroupDto> Groups);

    private sealed record SectionAssignment(string? SectionKey, string? SectionName);

    private sealed record CandidateRow(
        int Id,
        string Name,
        ProjectLifecycleStatus LifecycleStatus,
        string? ProjectCategory,
        string? TechnicalCategory,
        int TechnicalCategorySortOrder,
        string? ProjectBrief,
        string? Description,
        string? SponsoringLineDirectorate,
        short? YearOfDevelopment,
        int? CompletedYear,
        DateOnly? CompletedOn,
        DateTime CreatedAt,
        int? CoverPhotoId);

    private sealed record PublicationRow(
        int Id,
        string Name,
        string? CaseFileNumber,
        ProjectLifecycleStatus LifecycleStatus,
        string? ProjectBrief,
        string? Description,
        short? YearOfDevelopment,
        int? CompletedYear,
        DateOnly? CompletedOn,
        DateTime CreatedAt,
        int? CoverPhotoId,
        string? SponsoringLineDirectorate,
        string? ProjectCategory,
        string? TechnicalCategory,
        int TechnicalCategorySortOrder)
    {
        public int ProjectId => Id;
    }

    private sealed record CapabilityRow(int ProjectId, string Statement);

    private sealed record NarrativeResolution(string Text, string Label);

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
