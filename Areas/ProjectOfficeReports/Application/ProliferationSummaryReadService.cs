using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public sealed class ProliferationSummaryReadService : IProliferationSummaryReadService
{
    private readonly ProliferationAggregateReadService _aggregateReadService;

    public ProliferationSummaryReadService(ProliferationAggregateReadService aggregateReadService)
    {
        _aggregateReadService = aggregateReadService ?? throw new ArgumentNullException(nameof(aggregateReadService));
    }

    public async Task<ProliferationSummaryViewModel> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var aggregates = await _aggregateReadService.GetApprovedAggregatesAsync(
            projectId: null,
            cancellationToken);

        var active = aggregates
            .Where(x => x.ReportedTotal > 0)
            .ToList();

        if (active.Count == 0)
        {
            return ProliferationSummaryViewModel.Empty;
        }

        var byProject = active
            .GroupBy(x => new { x.ProjectId, x.ProjectName, x.ProjectCode })
            .Select(group => new ProliferationSummaryProjectRow(
                group.Key.ProjectId,
                group.Key.ProjectName,
                group.Key.ProjectCode,
                BuildSourceTotals(group)))
            .OrderByDescending(x => x.Totals.Total)
            .ThenBy(x => x.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ProjectCode ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var maximumChronologicalYear = DateTime.UtcNow.Year + 1;
        var chronological = active
            .Where(x => x.Year is >= 2000 && x.Year <= maximumChronologicalYear)
            .ToList();

        var byYear = chronological
            .GroupBy(x => x.Year)
            .Select(group => new ProliferationSummaryYearRow(
                group.Key,
                BuildSourceTotals(group)))
            .OrderByDescending(x => x.Year)
            .ToList();

        var byProjectYear = chronological
            .GroupBy(x => new { x.ProjectId, x.ProjectName, x.ProjectCode, x.Year })
            .Select(group => new ProliferationSummaryProjectYearRow(
                group.Key.ProjectId,
                group.Key.ProjectName,
                group.Key.ProjectCode,
                group.Key.Year,
                BuildSourceTotals(group)))
            .OrderByDescending(x => x.Year)
            .ThenBy(x => x.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ProjectCode ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ProliferationSummaryViewModel(byProject, byYear, byProjectYear);
    }

    private static ProliferationSummarySourceTotals BuildSourceTotals(
        IEnumerable<ProliferationAggregateRow> rows)
    {
        var sdd = 0;
        var abw = 0;

        foreach (var row in rows)
        {
            if (row.Source == ProliferationSource.Sdd)
            {
                sdd = checked(sdd + row.ReportedTotal);
            }
            else if (row.Source == ProliferationSource.Abw515)
            {
                abw = checked(abw + row.ReportedTotal);
            }
        }

        return new ProliferationSummarySourceTotals(
            checked(sdd + abw),
            sdd,
            abw);
    }
}
