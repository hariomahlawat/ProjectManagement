using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public sealed class ProliferationProjectReadService : IProliferationProjectReadService
{
    private readonly ApplicationDbContext _db;

    // Section: Constructor
    public ProliferationProjectReadService(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    // Section: Public API
    public async Task<ProliferationProjectAggregationResult> GetAggregatesAsync(
        ProliferationProjectAggregationRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var projectInfos = await LoadProjectsAsync(request, cancellationToken);
        if (projectInfos.Count == 0)
        {
            return ProliferationProjectAggregationResult.Empty;
        }

        var projectLookup = projectInfos.ToDictionary(p => p.Id);
        var projectIds = projectInfos.Select(p => p.Id).ToArray();

        var yearlyQuery = _db.ProliferationYearlies
            .AsNoTracking()
            .Where(y => projectIds.Contains(y.ProjectId) && y.ApprovalStatus == ApprovalStatus.Approved);

        var granularQuery = _db.ProliferationGranularEntries
            .AsNoTracking()
            .Where(g => projectIds.Contains(g.ProjectId) && g.ApprovalStatus == ApprovalStatus.Approved);

        if (request.Source.HasValue)
        {
            yearlyQuery = yearlyQuery.Where(y => y.Source == request.Source.Value);
            granularQuery = granularQuery.Where(g => g.Source == request.Source.Value);
        }

        ApplyDateFilters(request, ref yearlyQuery, ref granularQuery);

        var yearlyItems = await yearlyQuery.ToListAsync(cancellationToken);
        var granularItems = await granularQuery.ToListAsync(cancellationToken);

        ApplySearchFilters(request.Search, projectLookup, ref yearlyItems, ref granularItems);

        if (yearlyItems.Count == 0 && granularItems.Count == 0)
        {
            return ProliferationProjectAggregationResult.Empty;
        }

        return new ProliferationProjectAggregationResult(
            BuildProjectTotals(yearlyItems, granularItems, projectLookup),
            BuildProjectYearTotals(yearlyItems, granularItems, projectLookup),
            BuildProjectUnitTotals(granularItems, projectLookup),
            BuildUnitProjectMap(granularItems, projectLookup));
    }

    // Section: Project loading
    private async Task<List<ProjectInfo>> LoadProjectsAsync(
        ProliferationProjectAggregationRequest request,
        CancellationToken cancellationToken)
    {
        var query = _db.Projects
            .AsNoTracking()
            .Where(p => !p.IsDeleted && !p.IsArchived && p.LifecycleStatus == ProjectLifecycleStatus.Completed);

        if (request.ProjectCategoryId.HasValue)
        {
            query = query.Where(p => p.CategoryId == request.ProjectCategoryId.Value);
        }

        if (request.TechnicalCategoryId.HasValue)
        {
            query = query.Where(p => p.TechnicalCategoryId == request.TechnicalCategoryId.Value);
        }

        return await query
            .Select(p => new ProjectInfo(p.Id, p.Name, p.CaseFileNumber))
            .ToListAsync(cancellationToken);
    }

    // Section: Filtering
    private static void ApplyDateFilters(
        ProliferationProjectAggregationRequest request,
        ref IQueryable<ProliferationYearly> yearlyQuery,
        ref IQueryable<ProliferationGranular> granularQuery)
    {
        var yearsFilter = request.Years is { Count: > 0 }
            ? new HashSet<int>(request.Years)
            : null;

        if (yearsFilter is not null)
        {
            yearlyQuery = yearlyQuery.Where(y => yearsFilter.Contains(y.Year));
            granularQuery = granularQuery.Where(g => yearsFilter.Contains(g.ProliferationDate.Year));
            return;
        }

        if (request.DateFrom.HasValue)
        {
            var from = request.DateFrom.Value;
            yearlyQuery = yearlyQuery.Where(y => y.Year >= from.Year);
            granularQuery = granularQuery.Where(g => g.ProliferationDate >= from);
        }

        if (request.DateTo.HasValue)
        {
            var to = request.DateTo.Value;
            yearlyQuery = yearlyQuery.Where(y => y.Year <= to.Year);
            granularQuery = granularQuery.Where(g => g.ProliferationDate <= to);
        }
    }

    private static void ApplySearchFilters(
        string? search,
        IReadOnlyDictionary<int, ProjectInfo> projectLookup,
        ref List<ProliferationYearly> yearlyItems,
        ref List<ProliferationGranular> granularItems)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return;
        }

        var term = search.Trim();

        yearlyItems = yearlyItems
            .Where(y => MatchesProjectSearch(projectLookup[y.ProjectId], term))
            .ToList();

        granularItems = granularItems
            .Where(g => MatchesProjectSearch(projectLookup[g.ProjectId], term)
                        || g.UnitName.Contains(term, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    // Section: Aggregation - all projects
    private static IReadOnlyList<ProliferationProjectTotalsRow> BuildProjectTotals(
        IReadOnlyCollection<ProliferationYearly> yearlyItems,
        IReadOnlyCollection<ProliferationGranular> granularItems,
        IReadOnlyDictionary<int, ProjectInfo> projectLookup)
    {
        var totals = BuildProjectTotalsLookup(yearlyItems, granularItems);

        return totals
            .Select(item => new ProliferationProjectTotalsRow(
                item.Key.ProjectId,
                projectLookup[item.Key.ProjectId].Name,
                projectLookup[item.Key.ProjectId].Code,
                item.Value.ToTotals()))
            .OrderByDescending(row => row.Totals.Total)
            .ThenBy(row => row.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.ProjectCode ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // Section: Aggregation - project by year
    private static IReadOnlyList<ProliferationProjectYearTotalsRow> BuildProjectYearTotals(
        IReadOnlyCollection<ProliferationYearly> yearlyItems,
        IReadOnlyCollection<ProliferationGranular> granularItems,
        IReadOnlyDictionary<int, ProjectInfo> projectLookup)
    {
        var totals = BuildProjectYearTotalsLookup(yearlyItems, granularItems);

        return totals
            .Select(item => new ProliferationProjectYearTotalsRow(
                item.Key.ProjectId,
                projectLookup[item.Key.ProjectId].Name,
                projectLookup[item.Key.ProjectId].Code,
                item.Key.Year,
                item.Value.ToTotals()))
            .OrderByDescending(row => row.Year)
            .ThenBy(row => row.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.ProjectCode ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // Section: Aggregation - project by unit
    private static IReadOnlyList<ProliferationProjectUnitTotalsRow> BuildProjectUnitTotals(
        IReadOnlyCollection<ProliferationGranular> granularItems,
        IReadOnlyDictionary<int, ProjectInfo> projectLookup)
    {
        var totals = BuildProjectUnitTotalsLookup(granularItems);

        return totals
            .Select(item => new ProliferationProjectUnitTotalsRow(
                item.Key.ProjectId,
                projectLookup[item.Key.ProjectId].Name,
                projectLookup[item.Key.ProjectId].Code,
                item.Key.UnitName,
                item.Value.ToTotals()))
            .OrderBy(row => row.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.UnitName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.ProjectCode ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // Section: Aggregation - unit to projects mapping
    private static IReadOnlyList<ProliferationUnitProjectMapRow> BuildUnitProjectMap(
        IReadOnlyCollection<ProliferationGranular> granularItems,
        IReadOnlyDictionary<int, ProjectInfo> projectLookup)
    {
        return granularItems
            .GroupBy(g => g.UnitName, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ProliferationUnitProjectMapRow(
                group.Key,
                group
                    .Select(g => projectLookup[g.ProjectId])
                    .Distinct()
                    .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(p => p.Code ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .Select(p => new ProliferationUnitProjectItem(p.Id, p.Name, p.Code))
                    .ToList()))
            .OrderBy(row => row.UnitName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // Section: Aggregation helpers
    private static Dictionary<ProjectKey, SourceTotals> BuildProjectTotalsLookup(
        IEnumerable<ProliferationYearly> yearlyItems,
        IEnumerable<ProliferationGranular> granularItems)
    {
        var totals = new Dictionary<ProjectKey, SourceTotals>();

        foreach (var item in yearlyItems)
        {
            var key = new ProjectKey(item.ProjectId);
            if (!totals.TryGetValue(key, out var totalsItem))
            {
                totalsItem = new SourceTotals();
                totals[key] = totalsItem;
            }

            totalsItem.Add(item.Source, item.TotalQuantity);
        }

        foreach (var item in granularItems)
        {
            var key = new ProjectKey(item.ProjectId);
            if (!totals.TryGetValue(key, out var totalsItem))
            {
                totalsItem = new SourceTotals();
                totals[key] = totalsItem;
            }

            totalsItem.Add(item.Source, item.Quantity);
        }

        return totals;
    }

    private static Dictionary<ProjectYearKey, SourceTotals> BuildProjectYearTotalsLookup(
        IEnumerable<ProliferationYearly> yearlyItems,
        IEnumerable<ProliferationGranular> granularItems)
    {
        var totals = new Dictionary<ProjectYearKey, SourceTotals>();

        foreach (var item in yearlyItems)
        {
            var key = new ProjectYearKey(item.ProjectId, item.Year);
            if (!totals.TryGetValue(key, out var totalsItem))
            {
                totalsItem = new SourceTotals();
                totals[key] = totalsItem;
            }

            totalsItem.Add(item.Source, item.TotalQuantity);
        }

        foreach (var item in granularItems)
        {
            var key = new ProjectYearKey(item.ProjectId, item.ProliferationDate.Year);
            if (!totals.TryGetValue(key, out var totalsItem))
            {
                totalsItem = new SourceTotals();
                totals[key] = totalsItem;
            }

            totalsItem.Add(item.Source, item.Quantity);
        }

        return totals;
    }

    private static Dictionary<ProjectUnitKey, SourceTotals> BuildProjectUnitTotalsLookup(
        IEnumerable<ProliferationGranular> granularItems)
    {
        var totals = new Dictionary<ProjectUnitKey, SourceTotals>();

        foreach (var item in granularItems)
        {
            var key = new ProjectUnitKey(item.ProjectId, item.UnitName);
            if (!totals.TryGetValue(key, out var totalsItem))
            {
                totalsItem = new SourceTotals();
                totals[key] = totalsItem;
            }

            totalsItem.Add(item.Source, item.Quantity);
        }

        return totals;
    }

    private static bool MatchesProjectSearch(ProjectInfo project, string term)
    {
        if (project.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(project.Code)
               && project.Code!.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    // Section: Supporting types
    private sealed class SourceTotals
    {
        public int Total { get; private set; }
        public int Sdd { get; private set; }
        public int Abw { get; private set; }

        public void Add(ProliferationSource source, int quantity)
        {
            Total += quantity;

            if (source == ProliferationSource.Sdd)
            {
                Sdd += quantity;
            }
            else if (source == ProliferationSource.Abw515)
            {
                Abw += quantity;
            }
        }

        public ProliferationSummarySourceTotals ToTotals()
        {
            return new ProliferationSummarySourceTotals(Total, Sdd, Abw);
        }
    }

    private readonly record struct ProjectInfo(int Id, string Name, string? Code);

    private readonly record struct ProjectKey(int ProjectId);

    private readonly record struct ProjectYearKey(int ProjectId, int Year);

    private readonly record struct ProjectUnitKey(int ProjectId, string UnitName);
}
