using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Projects;
using ProjectManagement.Services;

namespace ProjectManagement.Services.Compendiums;

// SECTION: Read model for the publication-ready Simulators Compendium.
public sealed class CompendiumReadService : ICompendiumReadService
{
    public const string BuildStamp = "CompendiumPdf_2026-07-21_preflight";

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

    public async Task<CompendiumPdfDataDto> GetProliferationCompendiumAsync(
        CancellationToken cancellationToken = default)
    {
        var generatedAtUtc = _clock.UtcNow;

        // SECTION: Completed projects that are still valid portfolio records.
        var completedProjects = await _db.Projects
            .AsNoTracking()
            .Where(project =>
                project.LifecycleStatus == ProjectLifecycleStatus.Completed
                && !project.IsDeleted
                && !project.IsArchived)
            .Select(project => new
            {
                project.Id,
                project.Name,
                project.Description,
                project.CaseFileNumber,
                TechnicalCategoryName = project.TechnicalCategory != null
                    ? project.TechnicalCategory.Name
                    : null,
                project.ArmService,
                project.CompletedYear,
                project.CompletedOn,
                project.CoverPhotoId
            })
            .ToListAsync(cancellationToken);

        if (completedProjects.Count == 0)
        {
            return CreateEmptyResult(generatedAtUtc);
        }

        var completedProjectIds = completedProjects
            .Select(project => project.Id)
            .ToArray();

        // ProjectTechStatus and ProjectProductionCostFact are one-to-one project facts.
        var technicalStatuses = await _db.ProjectTechStatuses
            .AsNoTracking()
            .Where(status => completedProjectIds.Contains(status.ProjectId))
            .ToDictionaryAsync(status => status.ProjectId, cancellationToken);

        var eligibleProjectIds = technicalStatuses
            .Where(item => item.Value.AvailableForProliferation)
            .Select(item => item.Key)
            .ToHashSet();

        var missingAvailabilityStatusCount = completedProjects.Count(project =>
            !technicalStatuses.ContainsKey(project.Id));

        var excludedNotAvailableCount = completedProjects.Count(project =>
            technicalStatuses.TryGetValue(project.Id, out var status)
            && !status.AvailableForProliferation);

        if (eligibleProjectIds.Count == 0)
        {
            var preflight = CompendiumPreflightDto.Empty with
            {
                CompletedProjectCount = completedProjects.Count,
                ExcludedNotAvailableCount = excludedNotAvailableCount,
                MissingAvailabilityStatusCount = missingAvailabilityStatusCount
            };

            return CreateResult(
                generatedAtUtc,
                Array.Empty<CompendiumCategoryGroupDto>(),
                preflight);
        }

        var costs = await _db.ProjectProductionCostFacts
            .AsNoTracking()
            .Where(cost => eligibleProjectIds.Contains(cost.ProjectId))
            .ToDictionaryAsync(cost => cost.ProjectId, cancellationToken);

        // SECTION: Load photo metadata once and select the strongest available candidate.
        var photoRows = await _db.ProjectPhotos
            .AsNoTracking()
            .Where(photo => eligibleProjectIds.Contains(photo.ProjectId))
            .Select(photo => new PhotoCandidate(
                photo.Id,
                photo.ProjectId,
                photo.IsCover,
                photo.IsLowResolution,
                photo.Ordinal,
                photo.UpdatedUtc))
            .ToListAsync(cancellationToken);

        var photosByProjectId = photoRows
            .GroupBy(photo => photo.ProjectId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var projects = new List<CompendiumProjectDto>(eligibleProjectIds.Count);

        foreach (var project in completedProjects)
        {
            if (!eligibleProjectIds.Contains(project.Id))
            {
                continue;
            }

            costs.TryGetValue(project.Id, out var cost);
            photosByProjectId.TryGetValue(project.Id, out var photoCandidates);

            var selectedPhoto = SelectPhoto(project.CoverPhotoId, photoCandidates ?? Array.Empty<PhotoCandidate>());
            var completionYear = ResolveCompletionYear(project.CompletedYear, project.CompletedOn);
            var categoryName = NormalizeDisplay(project.TechnicalCategoryName, "Not recorded");
            var armService = NormalizeDisplay(project.ArmService, "Not recorded");
            var description = NormalizeDisplay(project.Description, "Not recorded");
            var projectName = NormalizeDisplay(project.Name, $"Project {project.Id.ToString(CultureInfo.InvariantCulture)}");

            var issues = BuildIssues(
                projectName,
                completionYear,
                project.ArmService,
                project.Description,
                cost?.ApproxProductionCost,
                selectedPhoto.PhotoId);

            projects.Add(new CompendiumProjectDto(
                ProjectId: project.Id,
                ProjectName: projectName,
                CaseFileNumber: NormalizeOptional(project.CaseFileNumber),
                TechnicalCategoryName: categoryName,
                CompletionYearValue: completionYear,
                CompletionYearDisplay: completionYear?.ToString(CultureInfo.InvariantCulture) ?? "Not recorded",
                ArmServiceDisplay: armService,
                ProliferationCostLakhs: cost?.ApproxProductionCost,
                ProliferationCostRemarks: NormalizeOptional(cost?.Remarks),
                CoverPhotoId: selectedPhoto.PhotoId,
                CoverPhotoSource: selectedPhoto.Source,
                DescriptionMarkdown: description,
                PublicationIssues: issues));
        }

        var groups = GroupAndSort(projects, _options);
        var readiness = projects
            .Select(project => new CompendiumProjectReadinessDto(
                project.ProjectId,
                project.ProjectName,
                project.TechnicalCategoryName,
                project.CompletionYearDisplay,
                project.PublicationIssues))
            .OrderByDescending(project => project.HasWarnings)
            .ThenBy(project => project.TechnicalCategoryName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(project => project.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var preflightResult = new CompendiumPreflightDto(
            CompletedProjectCount: completedProjects.Count,
            EligibleProjectCount: projects.Count,
            CategoryCount: groups.Count,
            ExcludedNotAvailableCount: excludedNotAvailableCount,
            MissingAvailabilityStatusCount: missingAvailabilityStatusCount,
            PhotoSelectedCount: projects.Count(project => project.CoverPhotoId.HasValue),
            MissingPhotoCount: CountIssue(projects, CompendiumPublicationIssue.MissingPhoto),
            MissingArmServiceCount: CountIssue(projects, CompendiumPublicationIssue.MissingArmService),
            MissingCostCount: CountIssue(projects, CompendiumPublicationIssue.MissingProliferationCost),
            ZeroCostCount: CountIssue(projects, CompendiumPublicationIssue.ZeroProliferationCost),
            MissingDescriptionCount: CountIssue(projects, CompendiumPublicationIssue.MissingDescription),
            MissingCompletionYearCount: CountIssue(projects, CompendiumPublicationIssue.MissingCompletionYear),
            PossibleTitleTypoCount: CountIssue(projects, CompendiumPublicationIssue.PossibleTitleTypo),
            Projects: readiness);

        return CreateResult(generatedAtUtc, groups, preflightResult);
    }

    private CompendiumPdfDataDto CreateEmptyResult(DateTimeOffset generatedAtUtc)
        => CreateResult(
            generatedAtUtc,
            Array.Empty<CompendiumCategoryGroupDto>(),
            CompendiumPreflightDto.Empty);

    private CompendiumPdfDataDto CreateResult(
        DateTimeOffset generatedAtUtc,
        IReadOnlyList<CompendiumCategoryGroupDto> groups,
        CompendiumPreflightDto preflight)
        => new(
            Title: NormalizeDisplay(_options.Title, "Simulators Compendium"),
            Subtitle: NormalizeDisplay(_options.Subtitle, "Available for Proliferation"),
            UnitDisplayName: NormalizeDisplay(_options.UnitDisplayName, "Simulator Development Division"),
            IssuerDisplayName: NormalizeDisplay(_options.IssuerDisplayName, "Simulator Development Division"),
            GeneratedAtUtc: generatedAtUtc,
            Groups: groups,
            Preflight: preflight);

    private static IReadOnlyList<CompendiumPublicationIssue> BuildIssues(
        string projectName,
        int? completionYear,
        string? armService,
        string? description,
        decimal? cost,
        int? photoId)
    {
        var issues = new List<CompendiumPublicationIssue>(7);

        if (!photoId.HasValue)
        {
            issues.Add(CompendiumPublicationIssue.MissingPhoto);
        }

        if (string.IsNullOrWhiteSpace(armService))
        {
            issues.Add(CompendiumPublicationIssue.MissingArmService);
        }

        if (!cost.HasValue)
        {
            issues.Add(CompendiumPublicationIssue.MissingProliferationCost);
        }
        else if (cost.Value == 0m)
        {
            issues.Add(CompendiumPublicationIssue.ZeroProliferationCost);
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            issues.Add(CompendiumPublicationIssue.MissingDescription);
        }

        if (!completionYear.HasValue)
        {
            issues.Add(CompendiumPublicationIssue.MissingCompletionYear);
        }

        if (LooksLikeAiWasEnteredAsAl(projectName))
        {
            issues.Add(CompendiumPublicationIssue.PossibleTitleTypo);
        }

        return issues;
    }

    private static bool LooksLikeAiWasEnteredAsAl(string projectName)
    {
        var value = projectName.TrimStart();
        return value.StartsWith("Al Based", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("Al-based", StringComparison.OrdinalIgnoreCase);
    }

    private static int CountIssue(
        IEnumerable<CompendiumProjectDto> projects,
        CompendiumPublicationIssue issue)
        => projects.Count(project => project.PublicationIssues.Contains(issue));

    private static PhotoSelection SelectPhoto(
        int? explicitCoverPhotoId,
        IReadOnlyList<PhotoCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return PhotoSelection.None;
        }

        if (explicitCoverPhotoId.HasValue)
        {
            var explicitCover = candidates.FirstOrDefault(photo => photo.Id == explicitCoverPhotoId.Value);
            if (explicitCover is not null)
            {
                return new PhotoSelection(explicitCover.Id, CompendiumPhotoSelectionSource.ExplicitCover);
            }
        }

        var markedCover = candidates
            .Where(photo => photo.IsCover)
            .OrderBy(photo => photo.IsLowResolution)
            .ThenBy(photo => photo.Ordinal)
            .ThenByDescending(photo => photo.UpdatedUtc)
            .ThenByDescending(photo => photo.Id)
            .FirstOrDefault();

        if (markedCover is not null)
        {
            return new PhotoSelection(markedCover.Id, CompendiumPhotoSelectionSource.MarkedCover);
        }

        var firstAvailable = candidates
            .OrderBy(photo => photo.IsLowResolution)
            .ThenBy(photo => photo.Ordinal)
            .ThenByDescending(photo => photo.UpdatedUtc)
            .ThenByDescending(photo => photo.Id)
            .First();

        return new PhotoSelection(firstAvailable.Id, CompendiumPhotoSelectionSource.FirstAvailable);
    }

    private static int? ResolveCompletionYear(int? completedYear, DateOnly? completedOn)
        => completedYear ?? completedOn?.Year;

    private static string NormalizeDisplay(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<CompendiumCategoryGroupDto> GroupAndSort(
        IReadOnlyList<CompendiumProjectDto> projects,
        CompendiumPdfOptions options)
    {
        var miscNames = (options.MiscCategoryNames ?? new List<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToArray();

        bool IsMisc(string categoryName)
            => miscNames.Any(name => string.Equals(name, categoryName, StringComparison.OrdinalIgnoreCase));

        return projects
            .GroupBy(project => project.TechnicalCategoryName, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                CategoryName = group.First().TechnicalCategoryName,
                IsMisc = IsMisc(group.First().TechnicalCategoryName),
                Projects = group.ToList()
            })
            .OrderBy(group => group.IsMisc ? 1 : 0)
            .ThenBy(group => group.CategoryName, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CompendiumCategoryGroupDto(
                group.CategoryName,
                group.Projects
                    .OrderByDescending(project => project.CompletionYearValue.HasValue)
                    .ThenByDescending(project => project.CompletionYearValue)
                    .ThenBy(project => project.ProjectName, StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .ToArray();
    }

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
        public static PhotoSelection None { get; } = new(null, CompendiumPhotoSelectionSource.None);
    }
}
