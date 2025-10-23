using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;
using ProjectManagement.Data;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public sealed class ProliferationSummaryReadService : IProliferationSummaryReadService
{
    private readonly ApplicationDbContext _db;
    private readonly ProliferationTrackerReadService _trackerReadService;

    public ProliferationSummaryReadService(
        ApplicationDbContext db,
        ProliferationTrackerReadService trackerReadService)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _trackerReadService = trackerReadService ?? throw new ArgumentNullException(nameof(trackerReadService));
    }

    public async Task<ProliferationSummaryViewModel> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var yearlyCombos = await _db.ProliferationYearlies
            .AsNoTracking()
            .Where(y => y.ApprovalStatus == ApprovalStatus.Approved)
            .Select(y => new { y.ProjectId, y.Source, y.Year })
            .ToListAsync(cancellationToken);

        var granularCombos = await _db.ProliferationGranularEntries
            .AsNoTracking()
            .Where(g => g.ApprovalStatus == ApprovalStatus.Approved)
            .Select(g => new { g.ProjectId, g.Source, Year = g.ProliferationDate.Year })
            .ToListAsync(cancellationToken);

        var combos = new HashSet<Combination>();
        foreach (var item in yearlyCombos)
        {
            combos.Add(new Combination(item.ProjectId, item.Source, item.Year));
        }

        foreach (var item in granularCombos)
        {
            combos.Add(new Combination(item.ProjectId, item.Source, item.Year));
        }

        if (combos.Count == 0)
        {
            return ProliferationSummaryViewModel.Empty;
        }

        var projectIds = combos.Select(c => c.ProjectId).Distinct().ToArray();

        var projectInfos = await _db.Projects
            .AsNoTracking()
            .Where(p => projectIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.CaseFileNumber })
            .ToListAsync(cancellationToken);

        var projectLookup = projectInfos.ToDictionary(
            p => p.Id,
            p => new ProjectInfo(p.Id, p.Name, p.CaseFileNumber));

        var totals = new List<CombinationTotal>(combos.Count);
        foreach (var combo in combos)
        {
            var total = await _trackerReadService.GetEffectiveTotalAsync(
                combo.ProjectId,
                combo.Source,
                combo.Year,
                cancellationToken);

            totals.Add(new CombinationTotal(combo.ProjectId, combo.Source, combo.Year, total));
        }

        var activeTotals = totals.Where(t => t.Total > 0).ToList();
        if (activeTotals.Count == 0)
        {
            return ProliferationSummaryViewModel.Empty;
        }

        var filteredTotals = activeTotals
            .Where(t => projectLookup.ContainsKey(t.ProjectId))
            .ToList();

        if (filteredTotals.Count == 0)
        {
            return ProliferationSummaryViewModel.Empty;
        }

        var byProject = BuildByProject(filteredTotals, projectLookup);
        var byYear = BuildByYear(filteredTotals);
        var byProjectYear = BuildByProjectYear(filteredTotals, projectLookup);

        return new ProliferationSummaryViewModel(byProject, byYear, byProjectYear);
    }

    private static IReadOnlyList<ProliferationSummaryProjectRow> BuildByProject(
        IReadOnlyCollection<CombinationTotal> totals,
        IReadOnlyDictionary<int, ProjectInfo> projects)
    {
        var rows = new List<ProliferationSummaryProjectRow>();

        foreach (var group in totals.GroupBy(t => t.ProjectId))
        {
            if (!projects.TryGetValue(group.Key, out var project))
            {
                continue;
            }

            var splits = BuildSourceTotals(group);
            rows.Add(new ProliferationSummaryProjectRow(project.Id, project.Name, project.Code, splits));
        }

        return rows
            .OrderByDescending(r => r.Totals.Total)
            .ThenBy(r => r.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.ProjectCode ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<ProliferationSummaryYearRow> BuildByYear(
        IReadOnlyCollection<CombinationTotal> totals)
    {
        return totals
            .GroupBy(t => t.Year)
            .Select(group => new ProliferationSummaryYearRow(group.Key, BuildSourceTotals(group)))
            .OrderByDescending(row => row.Year)
            .ToList();
    }

    private static IReadOnlyList<ProliferationSummaryProjectYearRow> BuildByProjectYear(
        IReadOnlyCollection<CombinationTotal> totals,
        IReadOnlyDictionary<int, ProjectInfo> projects)
    {
        var rows = new List<ProliferationSummaryProjectYearRow>();

        foreach (var group in totals.GroupBy(t => new { t.ProjectId, t.Year }))
        {
            if (!projects.TryGetValue(group.Key.ProjectId, out var project))
            {
                continue;
            }

            var splits = BuildSourceTotals(group);
            rows.Add(new ProliferationSummaryProjectYearRow(
                project.Id,
                project.Name,
                project.Code,
                group.Key.Year,
                splits));
        }

        return rows
            .OrderByDescending(r => r.Year)
            .ThenBy(r => r.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.ProjectCode ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ProliferationSummarySourceTotals BuildSourceTotals(IEnumerable<CombinationTotal> totals)
    {
        var total = 0;
        var sdd = 0;
        var abw = 0;

        foreach (var item in totals)
        {
            total += item.Total;

            if (item.Source == ProliferationSource.Sdd)
            {
                sdd += item.Total;
            }
            else if (item.Source == ProliferationSource.Abw515)
            {
                abw += item.Total;
            }
        }

        return new ProliferationSummarySourceTotals(total, sdd, abw);
    }

    private readonly record struct Combination(int ProjectId, ProliferationSource Source, int Year);

    private readonly record struct CombinationTotal(int ProjectId, ProliferationSource Source, int Year, int Total);

    private readonly record struct ProjectInfo(int Id, string Name, string? Code);
}
