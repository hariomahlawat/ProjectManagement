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


    public async Task<IReadOnlyList<int>> GetCompletionYearsAsync(
        CancellationToken cancellationToken = default)
    {
        var completionValues = await _db.Projects
            .AsNoTracking()
            .Where(p =>
                p.LifecycleStatus == ProjectLifecycleStatus.Completed
                && !p.IsDeleted
                && !p.IsArchived
                && (p.CompletedOn.HasValue || p.CompletedYear.HasValue))
            .Select(p => new
            {
                p.CompletedOn,
                p.CompletedYear
            })
            .ToListAsync(cancellationToken);

        return completionValues
            .Select(x => x.CompletedOn?.Year ?? x.CompletedYear)
            .Where(year => year.HasValue)
            .Select(year => year.GetValueOrDefault())
            .Distinct()
            .OrderByDescending(year => year)
            .ToList();
    }

    public async Task<IReadOnlyList<CompletedProjectSummaryDto>> GetAsync(
        int? technicalCategoryId,
        string? techStatus,
        bool? availableForProliferation,
        bool? totCompleted,
        int? completedYear,
        string? search,
        string? build,
        string? portfolioStatus,
        string sortKey,
        string sortDir,
        CancellationToken cancellationToken = default)
    {
        // SECTION: Base project selection
        var query = _db.Projects
            .AsNoTracking()
            .Include(p => p.TechnicalCategory)
            .Where(p =>
                p.LifecycleStatus == ProjectLifecycleStatus.Completed
                && !p.IsDeleted
                && !p.IsArchived)
            .AsQueryable();

        if (technicalCategoryId.HasValue)
        {
            query = query.Where(p => p.TechnicalCategoryId == technicalCategoryId.Value);
        }

        // SECTION: Build type filter
        if (!string.IsNullOrWhiteSpace(build))
        {
            if (string.Equals(build, "Rebuild", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(p => p.IsBuild);
            }
            else if (string.Equals(build, "New", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(p => !p.IsBuild);
            }
        }

        var projects = await query.ToListAsync(cancellationToken);

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
            var technologyRemarks = Normalize(tech?.Remarks);
            var proliferationRemarks = Normalize(tech?.ProliferationRemarks);
            var notAvailableReason = Normalize(tech?.NotAvailableReason);
            var proliferationCostRemarks = Normalize(cost?.Remarks);
            var remarks = BuildRemarks(
                technologyRemarks,
                proliferationRemarks,
                notAvailableReason,
                proliferationCostRemarks);

            var dto = new CompletedProjectSummaryDto
            {
                ProjectId = p.Id,
                Name = p.Name,
                TechnicalCategoryName = p.TechnicalCategory?.Name,
                BuildType = p.IsBuild ? "Rebuild" : "New",
                RdCostLakhs = p.CostLakhs,
                ApproxProductionCost = cost?.ApproxProductionCost,
                TechStatus = tech?.TechStatus,
                AvailableForProliferation = tech?.AvailableForProliferation,
                TechnologyRemarks = technologyRemarks,
                ProliferationRemarks = proliferationRemarks,
                NotAvailableReason = notAvailableReason,
                ProliferationCostRemarks = proliferationCostRemarks,
                Remarks = remarks,
                CompletedOn = p.CompletedOn,
                CompletedYear = p.CompletedOn?.Year ?? p.CompletedYear,
                CompletedMonth = p.CompletedMonth,
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

        var normalisedPortfolioStatus = CompletedProjectPortfolioStatusCodes.Normalise(portfolioStatus);
        if (normalisedPortfolioStatus is not null)
        {
            filtered = filtered.Where(r =>
                CompletedProjectPortfolioPolicy.MatchesPortfolioStatus(r, normalisedPortfolioStatus));
        }

        return ApplySorting(filtered, sortKey, sortDir).ToList();
    }

    // SECTION: Remarks helpers
    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? BuildRemarks(
        string? technologyRemarks,
        string? proliferationRemarks,
        string? notAvailableReason,
        string? proliferationCostRemarks)
    {
        var remarks = new List<string>(4);

        AddRemark(remarks, "Technology", technologyRemarks);
        AddRemark(remarks, "Availability for proliferation", proliferationRemarks);
        AddRemark(remarks, "Reason not available", notAvailableReason);
        AddRemark(remarks, "Proliferation cost", proliferationCostRemarks);

        return remarks.Count == 0
            ? null
            : string.Join(Environment.NewLine, remarks);
    }

    private static void AddRemark(List<string> remarks, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            remarks.Add($"{label}: {value}");
        }
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
                ? source.OrderByDescending(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.ProjectId)
                : source.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.ProjectId),

            "rd" => ApplyNullableSort(source, x => x.RdCostLakhs, desc).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase),
            "prod" => ApplyNullableSort(source, x => x.ProliferationCostLakhs, desc).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase),
            "completed" or "year" => CompletedProjectCompletionOrdering.Apply(source, desc),
            "avail" => ApplyNullableSort(source, x => x.AvailableForProliferation, desc).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase),
            "tot" => ApplyNullableSort(source, x => x.TotStatus, desc).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase),
            "lpp" => ApplyNullableSort(source, x => x.LatestLppDate, desc).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase),

            "tech" => ApplyNullableStringSort(source, x => x.TechStatus, desc).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase),
            "category" => ApplyNullableStringSort(source, x => x.TechnicalCategoryName, desc).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase),
            "build" => desc
                ? source.OrderByDescending(x => x.BuildType, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                : source.OrderBy(x => x.BuildType, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase),

            "quality" => desc
                ? source.OrderByDescending(CompletedProjectPortfolioPolicy.GetCriticalMissingCount)
                    .ThenByDescending(CompletedProjectPortfolioPolicy.GetSupplementaryMissingCount)
                    .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                : source.OrderBy(CompletedProjectPortfolioPolicy.GetCriticalMissingCount)
                    .ThenBy(CompletedProjectPortfolioPolicy.GetSupplementaryMissingCount)
                    .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase),

            _ => CompletedProjectCompletionOrdering.Apply(source, descending: true)
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
            ? source.OrderBy(x => keySelector(x).HasValue ? 0 : 1)
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
            ? source.OrderBy(x => string.IsNullOrWhiteSpace(keySelector(x)) ? 1 : 0)
                .ThenByDescending(x => keySelector(x), StringComparer.OrdinalIgnoreCase)
            : source.OrderBy(x => string.IsNullOrWhiteSpace(keySelector(x)) ? 1 : 0)
                .ThenBy(x => keySelector(x), StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class CompletedProjectSummaryDto
{
    public int ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? TechnicalCategoryName { get; set; }
    public string BuildType { get; set; } = "New";
    public decimal? RdCostLakhs { get; set; }
    // Legacy persistence naming is retained internally; presentation code should use the business term.
    public decimal? ApproxProductionCost { get; set; }
    public decimal? ProliferationCostLakhs => ApproxProductionCost;
    public string? TechStatus { get; set; }
    public bool? AvailableForProliferation { get; set; }
    public string? TechnologyRemarks { get; set; }
    public string? ProliferationRemarks { get; set; }
    public string? NotAvailableReason { get; set; }
    public string? ProliferationCostRemarks { get; set; }
    public string? Remarks { get; set; }
    public DateOnly? CompletedOn { get; set; }
    public int? CompletedYear { get; set; }
    public short? CompletedMonth { get; set; }
    public ProjectTotStatus? TotStatus { get; set; }
    public DateOnly? LatestLppDate { get; set; }
    public LatestLppViewModel? LatestLpp { get; set; }

    public string FormatCompletion(string unknownText = "—") =>
        ProjectCompletionFormatter.Format(CompletedOn, CompletedYear, CompletedMonth, unknownText);
}

// SECTION: Completion chronology
// Partial dates are compared only by components that are actually recorded.
// More precise values are placed before less precise values within the same
// year/month, and records without any completion date always remain last.
public static class CompletedProjectCompletionOrdering
{
    public static IOrderedEnumerable<CompletedProjectSummaryDto> Apply(
        IEnumerable<CompletedProjectSummaryDto> source,
        bool descending)
    {
        ArgumentNullException.ThrowIfNull(source);

        IOrderedEnumerable<CompletedProjectSummaryDto> ordered = source
            .OrderBy(item => GetYear(item).HasValue ? 0 : 1);

        ordered = descending
            ? ordered.ThenByDescending(GetYear)
            : ordered.ThenBy(GetYear);

        ordered = ordered.ThenBy(item => GetMonth(item).HasValue ? 0 : 1);
        ordered = descending
            ? ordered.ThenByDescending(GetMonth)
            : ordered.ThenBy(GetMonth);

        ordered = ordered.ThenBy(item => GetDay(item).HasValue ? 0 : 1);
        ordered = descending
            ? ordered.ThenByDescending(GetDay)
            : ordered.ThenBy(GetDay);

        return ordered
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ProjectId);
    }

    private static int? GetYear(CompletedProjectSummaryDto item) =>
        item.CompletedOn?.Year ?? item.CompletedYear;

    private static int? GetMonth(CompletedProjectSummaryDto item)
    {
        if (item.CompletedOn.HasValue)
        {
            return item.CompletedOn.Value.Month;
        }

        return item.CompletedMonth is >= 1 and <= 12
            ? item.CompletedMonth.Value
            : null;
    }

    private static int? GetDay(CompletedProjectSummaryDto item) =>
        item.CompletedOn?.Day;
}

public sealed class LatestLppViewModel
{
    public decimal Amount { get; set; }
    public DateOnly? Date { get; set; }
}
