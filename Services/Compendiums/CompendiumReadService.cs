using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Projects;
using ProjectManagement.Services;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.Compendiums;

/// <summary>
/// Authoritative read model for the Simulators Compendium.
///
/// Phase 22 deliberately separates candidate eligibility from publication membership. Every normal
/// Active/Completed PRISM project is selectable. Availability for proliferation remains a live
/// project fact and filter; it is not an inclusion gate for a user-authored Compendium.
/// </summary>
public sealed class CompendiumReadService : ICompendiumReadService
{
    public const string BuildStamp = "CompendiumPdf_2026-08-13_phase22";
    private const int MaximumSelectedProjects = 500;

    private readonly ApplicationDbContext _db;
    private readonly CompendiumPdfOptions _options;
    private readonly IClock _clock;

    public CompendiumReadService(
        ApplicationDbContext db,
        IOptions<CompendiumPdfOptions> options,
        IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
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

        var photos = await _db.ProjectPhotos
            .AsNoTracking()
            .Where(photo => projectIds.Contains(photo.ProjectId))
            .Select(photo => new PhotoCandidate(
                photo.Id,
                photo.ProjectId,
                photo.IsCover,
                photo.IsLowResolution,
                photo.Ordinal,
                photo.UpdatedUtc))
            .ToListAsync(cancellationToken);

        var photosByProject = photos
            .GroupBy(photo => photo.ProjectId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        return projects
            .Select(project =>
            {
                availability.TryGetValue(project.Id, out var availableForProliferation);
                productionCosts.TryGetValue(project.Id, out var productionCost);
                var projectPhotos = photosByProject.GetValueOrDefault(project.Id)
                                    ?? Array.Empty<PhotoCandidate>();
                var defaultPhotoId = ResolveDefaultPhoto(project.CoverPhotoId, projectPhotos);
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
                    projectPhotos.Length,
                    defaultPhotoId,
                    completionDisplay);
            })
            .ToArray();
    }

    public async Task<CompendiumPdfDataDto> GetPublicationAsync(
        CompendiumPublicationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestedIds = NormalizeProjectIds(request.ProjectIds);
        var generatedAtUtc = _clock.UtcNow.ToUniversalTime();
        var candidateCount = await CountCandidatesAsync(cancellationToken);

        if (requestedIds.Length == 0)
        {
            var noSelection = CompendiumPreflightDto.Empty with
            {
                CandidateProjectCount = candidateCount,
                SelectedProjectCount = 0,
                BlockerCount = 1,
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

        var rows = await _db.Projects
            .AsNoTracking()
            .Where(project => requestedIds.Contains(project.Id)
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

        var photos = availableProjectIds.Length == 0
            ? new List<PhotoCandidate>()
            : await _db.ProjectPhotos
                .AsNoTracking()
                .Where(photo => availableProjectIds.Contains(photo.ProjectId))
                .Select(photo => new PhotoCandidate(
                    photo.Id,
                    photo.ProjectId,
                    photo.IsCover,
                    photo.IsLowResolution,
                    photo.Ordinal,
                    photo.UpdatedUtc))
                .ToListAsync(cancellationToken);

        var photosByProject = photos
            .GroupBy(photo => photo.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PhotoCandidate>)group.ToArray());

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
        for (var sortOrder = 0; sortOrder < requestedIds.Length; sortOrder++)
        {
            var projectId = requestedIds[sortOrder];
            if (!rowsById.TryGetValue(projectId, out var project))
            {
                continue;
            }

            availability.TryGetValue(project.Id, out var availableForProliferation);
            costs.TryGetValue(project.Id, out var cost);
            var projectPhotos = photosByProject.GetValueOrDefault(project.Id)
                                ?? Array.Empty<PhotoCandidate>();
            var selectedPhoto = SelectPhoto(project.CoverPhotoId, projectPhotos);
            var completionYear = ResolveCompletionYear(project.CompletedYear, project.CompletedOn);
            var issues = BuildIssues(
                project.Name,
                project.LifecycleStatus,
                completionYear,
                project.ArmService,
                project.Description,
                cost?.Cost,
                selectedPhoto.PhotoId,
                availableForProliferation == true);

            findings.AddRange(issues.Select(issue =>
                ToFinding(issue, project.Id, project.Name)));

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
                selectedPhoto.PhotoId,
                selectedPhoto.Source,
                NormalizeDisplay(project.Description, "Not recorded"),
                issues)
            {
                LifecycleDisplay = LifecycleDisplay(project.LifecycleStatus),
                ProjectCategoryName = NormalizeOptional(project.ProjectCategory),
                IsAvailableForProliferation = availableForProliferation == true,
                PhotoCount = projectPhotos.Count,
                SortOrder = sortOrder
            });
        }

        var groups = GroupInPublicationOrder(publicationProjects);
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
            BlockerCount = findings.Count(finding => finding.Severity == CompendiumFindingSeverity.Blocker),
            InformationCount = findings.Count(finding => finding.Severity == CompendiumFindingSeverity.Information),
            Findings = findings
        };

        return CreateResult(generatedAtUtc, request, groups, preflight);
    }

    public async Task<CompendiumPdfDataDto> GetProliferationCompendiumAsync(
        CancellationToken cancellationToken = default)
    {
        // Compatibility path for /Projects/Compendium and existing integrations. The new
        // Publications workspace never uses this automatic proliferation selection.
        var completed = await _db.Projects
            .AsNoTracking()
            .Where(project => !project.IsDeleted
                              && !project.IsArchived
                              && project.LifecycleStatus == ProjectLifecycleStatus.Completed)
            .Select(project => new
            {
                project.Id,
                Category = project.TechnicalCategory != null
                    ? project.TechnicalCategory.Name
                    : null,
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

        var data = await GetPublicationAsync(
            new CompendiumPublicationRequest(eligibleProjectIds),
            cancellationToken);

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
                MissingAvailabilityStatusCount = missingStatusCount
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

    private static int[] NormalizeProjectIds(IReadOnlyList<int>? projectIds)
    {
        if (projectIds is null || projectIds.Count == 0)
        {
            return Array.Empty<int>();
        }

        var seen = new HashSet<int>();
        return projectIds
            .Where(projectId => projectId > 0 && seen.Add(projectId))
            .Take(MaximumSelectedProjects)
            .ToArray();
    }

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

    private static IReadOnlyList<CompendiumPublicationIssue> BuildIssues(
        string projectName,
        ProjectLifecycleStatus lifecycleStatus,
        int? completionYear,
        string? armService,
        string? description,
        decimal? proliferationCost,
        int? photoId,
        bool availableForProliferation)
    {
        var issues = new List<CompendiumPublicationIssue>();

        if (!photoId.HasValue)
        {
            issues.Add(CompendiumPublicationIssue.MissingPhoto);
        }
        if (string.IsNullOrWhiteSpace(armService))
        {
            issues.Add(CompendiumPublicationIssue.MissingArmService);
        }
        if (availableForProliferation && !proliferationCost.HasValue)
        {
            issues.Add(CompendiumPublicationIssue.MissingProliferationCost);
        }
        else if (availableForProliferation && proliferationCost == 0)
        {
            issues.Add(CompendiumPublicationIssue.ZeroProliferationCost);
        }
        if (string.IsNullOrWhiteSpace(description))
        {
            issues.Add(CompendiumPublicationIssue.MissingDescription);
        }
        if (lifecycleStatus == ProjectLifecycleStatus.Completed && !completionYear.HasValue)
        {
            issues.Add(CompendiumPublicationIssue.MissingCompletionYear);
        }
        if (LooksLikeAiWasEnteredAsAl(projectName))
        {
            issues.Add(CompendiumPublicationIssue.PossibleTitleTypo);
        }

        return issues;
    }

    private static CompendiumFindingDto ToFinding(
        CompendiumPublicationIssue issue,
        int projectId,
        string projectName)
        => issue switch
        {
            CompendiumPublicationIssue.MissingPhoto => new CompendiumFindingDto(
                CompendiumFindingSeverity.Warning,
                "missingPhoto",
                "No publication photograph is available.",
                projectId,
                projectName),
            CompendiumPublicationIssue.MissingArmService => new CompendiumFindingDto(
                CompendiumFindingSeverity.Warning,
                "missingArmService",
                "Arm/Service is not recorded.",
                projectId,
                projectName),
            CompendiumPublicationIssue.MissingProliferationCost => new CompendiumFindingDto(
                CompendiumFindingSeverity.Warning,
                "missingCost",
                "This project is marked available for proliferation but no proliferation cost is recorded.",
                projectId,
                projectName),
            CompendiumPublicationIssue.ZeroProliferationCost => new CompendiumFindingDto(
                CompendiumFindingSeverity.Warning,
                "zeroCost",
                "Proliferation cost is zero; verify that this is intentional.",
                projectId,
                projectName),
            CompendiumPublicationIssue.MissingDescription => new CompendiumFindingDto(
                CompendiumFindingSeverity.Warning,
                "missingDescription",
                "Project description is not recorded.",
                projectId,
                projectName),
            CompendiumPublicationIssue.MissingCompletionYear => new CompendiumFindingDto(
                CompendiumFindingSeverity.Warning,
                "missingCompletionYear",
                "Completed project has no completion year.",
                projectId,
                projectName),
            CompendiumPublicationIssue.PossibleTitleTypo => new CompendiumFindingDto(
                CompendiumFindingSeverity.Warning,
                "possibleTitleTypo",
                "Project title may contain “Al” where “AI” was intended.",
                projectId,
                projectName),
            _ => new CompendiumFindingDto(
                CompendiumFindingSeverity.Information,
                "information",
                "Review project publication data.",
                projectId,
                projectName)
        };

    private static PhotoSelection SelectPhoto(
        int? explicitPhotoId,
        IReadOnlyList<PhotoCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return PhotoSelection.None;
        }

        if (explicitPhotoId.HasValue
            && candidates.Any(candidate => candidate.Id == explicitPhotoId.Value))
        {
            return new PhotoSelection(
                explicitPhotoId,
                CompendiumPhotoSelectionSource.ExplicitCover);
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

    private static int? ResolveDefaultPhoto(
        int? explicitPhotoId,
        IReadOnlyList<PhotoCandidate> candidates)
        => SelectPhoto(explicitPhotoId, candidates).PhotoId;

    private static int? ResolveCompletionYear(int? completedYear, DateOnly? completedOn)
        => completedYear ?? completedOn?.Year;

    private static string LifecycleDisplay(ProjectLifecycleStatus lifecycleStatus)
        => lifecycleStatus == ProjectLifecycleStatus.Completed ? "Completed" : "Ongoing";

    private static string NormalizeDisplay(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool LooksLikeAiWasEnteredAsAl(string value)
    {
        var normalized = value.TrimStart();
        return normalized.StartsWith("Al Based", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("Al-based", StringComparison.OrdinalIgnoreCase);
    }

    private static int CountIssue(
        IEnumerable<CompendiumProjectDto> projects,
        CompendiumPublicationIssue issue)
        => projects.Count(project => project.PublicationIssues.Contains(issue));

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
        string? TechnicalCategory);

    private sealed record CostRow(
        int ProjectId,
        decimal? Cost,
        string? Remarks);

    private sealed record PhotoCandidate(
        int Id,
        int ProjectId,
        bool IsCover,
        bool IsLowResolution,
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
}
