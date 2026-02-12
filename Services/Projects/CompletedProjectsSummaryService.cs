using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Projects;

namespace ProjectManagement.Services.Projects;

public sealed class CompletedProjectsSummaryService
{
    private readonly ApplicationDbContext _db;

    public CompletedProjectsSummaryService(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<IReadOnlyList<CompletedProjectSummaryDto>> GetAsync(
        int? technicalCategoryId,
        string? techStatus,
        bool? availableForProliferation,
        bool? totCompleted,
        int? completedYear,
        string? search,
        string sortKey,
        string sortDir,
        CancellationToken cancellationToken = default)
    {
        // SECTION: Base project selection
        var projects = await _db.Projects
            .AsNoTracking()
            .Where(p =>
                p.LifecycleStatus == ProjectLifecycleStatus.Completed
                && !p.IsDeleted
                && !p.IsArchived)
            .ToListAsync(cancellationToken);

        if (technicalCategoryId.HasValue)
        {
            projects = projects
                .Where(p => p.TechnicalCategoryId == technicalCategoryId.Value)
                .ToList();
        }

        var projectIds = projects.Select(p => p.Id).ToList();

        // SECTION: Related data lookups
        var costFacts = await _db.ProjectProductionCostFacts
            .AsNoTracking()
            .Where(c => projectIds.Contains(c.ProjectId))
            .ToListAsync(cancellationToken);

        var techStatuses = await _db.ProjectTechStatuses
            .AsNoTracking()
            .Where(t => projectIds.Contains(t.ProjectId))
            .ToListAsync(cancellationToken);

        var lppRecords = await _db.ProjectLppRecords
            .AsNoTracking()
            .Where(l => projectIds.Contains(l.ProjectId))
            .ToListAsync(cancellationToken);

        var totStatusLookup = await _db.ProjectTots
            .AsNoTracking()
            .Where(t => projectIds.Contains(t.ProjectId))
            .Select(t => new { t.ProjectId, t.Id, t.Status })
            .ToListAsync(cancellationToken);

        // SECTION: Deterministic related-entity dictionaries
        var costByProjectId = costFacts
            .GroupBy(x => x.ProjectId)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(x => x.UpdatedAtUtc)
                    .First());

        var techByProjectId = techStatuses
            .GroupBy(x => x.ProjectId)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(x => x.MarkedAtUtc)
                    .First());

        var latestLppByProjectId = lppRecords
            .GroupBy(l => l.ProjectId)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(x => x.LppDate)
                    .ThenByDescending(x => x.CreatedAtUtc)
                    .First());

        var totStatusByProjectId = totStatusLookup
            .GroupBy(t => t.ProjectId)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(x => x.Id)
                    .First().Status);

        // SECTION: DTO mapping
        var result = new List<CompletedProjectSummaryDto>(projects.Count);

        foreach (var p in projects)
        {
            costByProjectId.TryGetValue(p.Id, out var cost);
            techByProjectId.TryGetValue(p.Id, out var tech);
            latestLppByProjectId.TryGetValue(p.Id, out var latestLpp);

            ProjectTotStatus? totStatus = null;
            if (totStatusByProjectId.TryGetValue(p.Id, out var foundStatus))
            {
                totStatus = foundStatus;
            }

            // SECTION: Remarks projection
            string? remarks = null;
            var hasTechRemarks = !string.IsNullOrWhiteSpace(tech?.Remarks);
            var hasCostRemarks = !string.IsNullOrWhiteSpace(cost?.Remarks);

            if (hasTechRemarks && hasCostRemarks)
            {
                remarks = $"Tech: {tech!.Remarks}\nProd: {cost!.Remarks}";
            }
            else
            {
                remarks = hasTechRemarks ? tech!.Remarks : cost?.Remarks;
            }

            var dto = new CompletedProjectSummaryDto
            {
                ProjectId = p.Id,
                Name = p.Name,
                RdCostLakhs = p.CostLakhs,
                ApproxProductionCost = cost?.ApproxProductionCost,
                TechStatus = tech?.TechStatus,
                AvailableForProliferation = tech?.AvailableForProliferation,
                Remarks = remarks,
                CompletedYear = p.CompletedYear,
                TotStatus = totStatus,
                LatestLppDate = latestLpp?.LppDate,
                LatestLpp = latestLpp != null
                    ? new LatestLppViewModel
                    {
                        Amount = latestLpp.LppAmount,
                        Date = latestLpp.LppDate
                    }
                    : null
            };

            result.Add(dto);
        }

        IEnumerable<CompletedProjectSummaryDto> filtered = result;

        // SECTION: Filters
        if (!string.IsNullOrWhiteSpace(techStatus))
        {
            filtered = filtered.Where(r =>
                string.Equals(r.TechStatus, techStatus, StringComparison.OrdinalIgnoreCase));
        }

        if (availableForProliferation.HasValue)
        {
            filtered = filtered.Where(r =>
                r.AvailableForProliferation == availableForProliferation);
        }

        if (totCompleted.HasValue)
        {
            if (totCompleted.Value)
            {
                filtered = filtered.Where(r => r.TotStatus == ProjectTotStatus.Completed);
            }
            else
            {
                filtered = filtered.Where(r =>
                    r.TotStatus.HasValue
                    && r.TotStatus != ProjectTotStatus.Completed);
            }
        }

        if (completedYear.HasValue)
        {
            filtered = filtered.Where(r => r.CompletedYear == completedYear);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = filtered.Where(r =>
                r.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        return ApplySorting(filtered, sortKey, sortDir).ToList();
    }

    // SECTION: Deterministic sort application
    private static IOrderedEnumerable<CompletedProjectSummaryDto> ApplySorting(
        IEnumerable<CompletedProjectSummaryDto> source,
        string sortKey,
        string sortDir)
    {
        var desc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);

        return sortKey switch
        {
            "name" => desc
                ? source.OrderByDescending(x => x.Name).ThenBy(x => x.ProjectId)
                : source.OrderBy(x => x.Name).ThenBy(x => x.ProjectId),

            "rd" => ApplyNullableSort(source, x => x.RdCostLakhs, desc).ThenBy(x => x.Name),
            "prod" => ApplyNullableSort(source, x => x.ApproxProductionCost, desc).ThenBy(x => x.Name),
            "year" => ApplyNullableSort(source, x => x.CompletedYear, desc).ThenBy(x => x.Name),
            "avail" => ApplyNullableSort(source, x => x.AvailableForProliferation, desc).ThenBy(x => x.Name),
            "tot" => ApplyNullableSort(source, x => x.TotStatus, desc).ThenBy(x => x.Name),
            "lpp" => ApplyNullableSort(source, x => x.LatestLppDate, desc).ThenBy(x => x.Name),

            "tech" => ApplyNullableStringSort(source, x => x.TechStatus, desc).ThenBy(x => x.Name),

            _ => source.OrderBy(x => x.Name).ThenBy(x => x.ProjectId)
        };
    }

    // SECTION: Nullable sort helpers
    private static IOrderedEnumerable<T> ApplyNullableSort<T, TKey>(
        IEnumerable<T> source,
        Func<T, TKey?> keySelector,
        bool desc)
        where TKey : struct, IComparable
    {
        return desc
            ? source.OrderBy(x => keySelector(x).HasValue ? 1 : 0)
                .ThenByDescending(x => keySelector(x))
            : source.OrderBy(x => keySelector(x).HasValue ? 0 : 1)
                .ThenBy(x => keySelector(x));
    }

    private static IOrderedEnumerable<T> ApplyNullableStringSort<T>(
        IEnumerable<T> source,
        Func<T, string?> keySelector,
        bool desc)
    {
        return desc
            ? source.OrderBy(x => string.IsNullOrWhiteSpace(keySelector(x)) ? 0 : 1)
                .ThenByDescending(x => keySelector(x), StringComparer.OrdinalIgnoreCase)
            : source.OrderBy(x => string.IsNullOrWhiteSpace(keySelector(x)) ? 1 : 0)
                .ThenBy(x => keySelector(x), StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class CompletedProjectSummaryDto
{
    public int ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal? RdCostLakhs { get; set; }
    public decimal? ApproxProductionCost { get; set; }
    public string? TechStatus { get; set; }
    public bool? AvailableForProliferation { get; set; }
    public string? Remarks { get; set; }
    public int? CompletedYear { get; set; }
    public ProjectTotStatus? TotStatus { get; set; }
    public DateOnly? LatestLppDate { get; set; }
    public LatestLppViewModel? LatestLpp { get; set; }
}

public sealed class LatestLppViewModel
{
    public decimal Amount { get; set; }
    public DateOnly? Date { get; set; }
}
