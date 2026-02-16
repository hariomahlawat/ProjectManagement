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

namespace ProjectManagement.Services.Compendiums;

// SECTION: Read service for Simulators Compendium export.
public sealed class CompendiumReadService : ICompendiumReadService
{
    public const string BuildStamp = "CompendiumPdf_2026-02-15_zip";

    private readonly ApplicationDbContext _db;
    private readonly CompendiumPdfOptions _options;

    public CompendiumReadService(ApplicationDbContext db, IOptions<CompendiumPdfOptions> options)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<CompendiumPdfDataDto> GetProliferationCompendiumAsync(CancellationToken cancellationToken = default)
    {
        // SECTION: Base project selection (URD eligibility: completed + not deleted + not archived)
        var baseProjects = await _db.Projects
            .AsNoTracking()
            .Where(p =>
                p.LifecycleStatus == ProjectLifecycleStatus.Completed
                && !p.IsDeleted
                && !p.IsArchived)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Description,
                p.TechnicalCategoryId,
                TechnicalCategoryName = p.TechnicalCategory != null ? p.TechnicalCategory.Name : null,
                p.ArmService,
                p.CompletedYear,
                p.CompletedOn,
                p.CoverPhotoId
            })
            .ToListAsync(cancellationToken);

        if (baseProjects.Count == 0)
        {
            return new CompendiumPdfDataDto(
                Title: string.IsNullOrWhiteSpace(_options.Title) ? "Simulators Compendium" : _options.Title,
                UnitDisplayName: _options.UnitDisplayName ?? string.Empty,
                GeneratedAtUtc: DateTimeOffset.UtcNow,
                Groups: Array.Empty<CompendiumCategoryGroupDto>());
        }

        var projectIds = baseProjects.Select(p => p.Id).ToList();

        // SECTION: Latest tech status per project (deterministic)
        var techRows = await _db.ProjectTechStatuses
            .AsNoTracking()
            .Where(t => projectIds.Contains(t.ProjectId))
            .ToListAsync(cancellationToken);

        var latestTechByProjectId = techRows
            .GroupBy(t => t.ProjectId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.MarkedAtUtc).First());

        // SECTION: URD eligibility requires AvailableForProliferation = true on latest tech status.
        var eligibleProjectIds = new HashSet<int>(
            latestTechByProjectId
                .Where(kvp => kvp.Value.AvailableForProliferation)
                .Select(kvp => kvp.Key));

        if (eligibleProjectIds.Count == 0)
        {
            return new CompendiumPdfDataDto(
                Title: string.IsNullOrWhiteSpace(_options.Title) ? "Simulators Compendium" : _options.Title,
                UnitDisplayName: _options.UnitDisplayName ?? string.Empty,
                GeneratedAtUtc: DateTimeOffset.UtcNow,
                Groups: Array.Empty<CompendiumCategoryGroupDto>());
        }

        // SECTION: Latest production cost fact per project (optional)
        var costRows = await _db.ProjectProductionCostFacts
            .AsNoTracking()
            .Where(c => eligibleProjectIds.Contains(c.ProjectId))
            .ToListAsync(cancellationToken);

        var latestCostByProjectId = costRows
            .GroupBy(c => c.ProjectId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.UpdatedAtUtc).First());

        // SECTION: Cover photo fallback.
        // Rule: if Project.CoverPhotoId is null, try latest ProjectPhoto with IsCover == true.
        var missingCoverIds = baseProjects
            .Where(p => eligibleProjectIds.Contains(p.Id) && !p.CoverPhotoId.HasValue)
            .Select(p => p.Id)
            .ToList();

        Dictionary<int, int> coverPhotoFallbackByProjectId = new();
        if (missingCoverIds.Count > 0)
        {
            var coverCandidates = await _db.ProjectPhotos
                .AsNoTracking()
                .Where(ph => missingCoverIds.Contains(ph.ProjectId) && ph.IsCover)
                .Select(ph => new { ph.ProjectId, ph.Id, ph.UpdatedUtc })
                .ToListAsync(cancellationToken);

            coverPhotoFallbackByProjectId = coverCandidates
                .GroupBy(x => x.ProjectId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.UpdatedUtc).ThenByDescending(x => x.Id).First().Id);
        }

        // SECTION: Materialise DTOs for eligible projects.
        var projects = new List<CompendiumProjectDto>(eligibleProjectIds.Count);

        foreach (var p in baseProjects)
        {
            if (!eligibleProjectIds.Contains(p.Id))
            {
                continue;
            }

            latestCostByProjectId.TryGetValue(p.Id, out var cost);

            var completionYear = ResolveCompletionYear(p.CompletedYear, p.CompletedOn);
            var completionYearDisplay = completionYear.HasValue
                ? completionYear.Value.ToString(CultureInfo.InvariantCulture)
                : "Not recorded";

            var categoryName = string.IsNullOrWhiteSpace(p.TechnicalCategoryName)
                ? "Not recorded"
                : p.TechnicalCategoryName!.Trim();

            var armDisplay = string.IsNullOrWhiteSpace(p.ArmService)
                ? "Not recorded"
                : p.ArmService.Trim();

            var descriptionMd = string.IsNullOrWhiteSpace(p.Description)
                ? "Not recorded"
                : p.Description.Trim();

            var coverPhotoId = p.CoverPhotoId;
            if (!coverPhotoId.HasValue && coverPhotoFallbackByProjectId.TryGetValue(p.Id, out var fallbackId))
            {
                coverPhotoId = fallbackId;
            }

            projects.Add(new CompendiumProjectDto(
                ProjectId: p.Id,
                ProjectName: p.Name,
                TechnicalCategoryName: categoryName,
                CompletionYearValue: completionYear,
                CompletionYearDisplay: completionYearDisplay,
                ArmServiceDisplay: armDisplay,
                ProliferationCostLakhs: cost?.ApproxProductionCost,
                CoverPhotoId: coverPhotoId,
                DescriptionMarkdown: descriptionMd));
        }

        // SECTION: Grouping and ordering per URD.
        var groups = GroupAndSort(projects, _options);

        return new CompendiumPdfDataDto(
            Title: string.IsNullOrWhiteSpace(_options.Title) ? "Simulators Compendium" : _options.Title,
            UnitDisplayName: _options.UnitDisplayName ?? string.Empty,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Groups: groups);
    }

    private static int? ResolveCompletionYear(int? completedYear, DateOnly? completedOn)
    {
        if (completedYear.HasValue)
        {
            return completedYear.Value;
        }

        if (completedOn.HasValue)
        {
            return completedOn.Value.Year;
        }

        return null;
    }

    private static IReadOnlyList<CompendiumCategoryGroupDto> GroupAndSort(
        IReadOnlyList<CompendiumProjectDto> projects,
        CompendiumPdfOptions options)
    {
        var miscNames = (options.MiscCategoryNames ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToList();

        bool IsMisc(string categoryName)
            => miscNames.Any(x => string.Equals(x, categoryName, StringComparison.OrdinalIgnoreCase));

        var groups = projects
            .GroupBy(p => p.TechnicalCategoryName)
            .Select(g => new
            {
                CategoryName = g.Key,
                IsMisc = IsMisc(g.Key),
                Projects = g.ToList()
            })
            .OrderBy(x => x.IsMisc ? 1 : 0)
            .ThenBy(x => x.CategoryName, StringComparer.OrdinalIgnoreCase)
            .Select(x => new CompendiumCategoryGroupDto(
                TechnicalCategoryName: x.CategoryName,
                Projects: x.Projects
                    .OrderByDescending(p => p.CompletionYearValue.HasValue)
                    .ThenByDescending(p => p.CompletionYearValue)
                    .ThenBy(p => p.ProjectName, StringComparer.OrdinalIgnoreCase)
                    .ToList()))
            .ToList();

        return groups;
    }
}
