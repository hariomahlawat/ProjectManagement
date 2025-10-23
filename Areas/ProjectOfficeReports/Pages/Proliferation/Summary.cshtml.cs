using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Proliferation;

[Authorize(Policy = ProjectOfficeReportsPolicies.ViewProliferationTracker)]
public sealed class SummaryModel : PageModel
{
    private readonly IProliferationSummaryReadService _summaryService;

    public SummaryModel(IProliferationSummaryReadService summaryService)
    {
        _summaryService = summaryService ?? throw new ArgumentNullException(nameof(summaryService));
    }

    public ProliferationSummaryViewModel Summary { get; private set; } = ProliferationSummaryViewModel.Empty;

    public int ProjectsTotal { get; private set; }

    public int YearsTotal { get; private set; }

    public int GrandTotal { get; private set; }

    public int GrandAbw { get; private set; }

    public int GrandSdd { get; private set; }

    public string Lede { get; private set; } = string.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Summary = await _summaryService.GetSummaryAsync(cancellationToken);

        var totals = CalculateTotals(Summary);

        ProjectsTotal = totals.ProjectsTotal;
        YearsTotal = totals.YearsTotal;
        GrandTotal = totals.GrandTotal;
        GrandAbw = totals.GrandAbw;
        GrandSdd = totals.GrandSdd;
        Lede = BuildLede(totals);
    }

    private static SummaryTotals CalculateTotals(ProliferationSummaryViewModel summary)
    {
        if (summary is null)
        {
            return SummaryTotals.Empty;
        }

        if (summary.ByProject.Count > 0)
        {
            var projectTotals = summary.ByProject.Select(row => row.Totals);
            var yearsTotal = summary.ByYear.Count > 0
                ? summary.ByYear.Count
                : DistinctYearCount(summary.ByProjectYear);

            return new SummaryTotals(
                summary.ByProject.Count,
                yearsTotal,
                projectTotals.Sum(t => t.Total),
                projectTotals.Sum(t => t.Abw515),
                projectTotals.Sum(t => t.Sdd));
        }

        if (summary.ByYear.Count > 0)
        {
            var yearTotals = summary.ByYear.Select(row => row.Totals);
            var projectsTotal = summary.ByProjectYear.Count > 0
                ? summary.ByProjectYear.Select(row => row.ProjectId).Distinct().Count()
                : 0;

            return new SummaryTotals(
                projectsTotal,
                summary.ByYear.Count,
                yearTotals.Sum(t => t.Total),
                yearTotals.Sum(t => t.Abw515),
                yearTotals.Sum(t => t.Sdd));
        }

        if (summary.ByProjectYear.Count > 0)
        {
            return new SummaryTotals(
                summary.ByProjectYear.Select(row => row.ProjectId).Distinct().Count(),
                DistinctYearCount(summary.ByProjectYear),
                summary.ByProjectYear.Sum(row => row.Totals.Total),
                summary.ByProjectYear.Sum(row => row.Totals.Abw515),
                summary.ByProjectYear.Sum(row => row.Totals.Sdd));
        }

        return SummaryTotals.Empty;
    }

    private static int DistinctYearCount(IReadOnlyList<ProliferationSummaryProjectYearRow> rows)
    {
        return rows.Count == 0
            ? 0
            : rows.Select(row => row.Year).Distinct().Count();
    }

    private static string BuildLede(SummaryTotals totals)
    {
        if (totals == SummaryTotals.Empty)
        {
            return "No proliferation data is available yet.";
        }

        var projectsPart = totals.ProjectsTotal > 0
            ? FormatCount(totals.ProjectsTotal, "project", "projects")
            : null;
        var yearsPart = totals.YearsTotal > 0
            ? $"{FormatCount(totals.YearsTotal, "year", "years")}" 
            : null;

        string message;

        if (projectsPart is not null && yearsPart is not null)
        {
            message = $"This summary covers {projectsPart} across {yearsPart}";
        }
        else if (projectsPart is not null)
        {
            message = $"This summary covers {projectsPart}";
        }
        else if (yearsPart is not null)
        {
            message = $"This summary covers {yearsPart} of proliferation";
        }
        else
        {
            message = "This summary covers recorded proliferation";
        }

        if (totals.GrandTotal > 0)
        {
            message += $" with {totals.GrandTotal.ToString("N0", CultureInfo.InvariantCulture)} total proliferations";

            var breakdown = BuildBreakdown(totals);
            if (!string.IsNullOrEmpty(breakdown))
            {
                message += $" ({breakdown})";
            }
        }

        if (!message.EndsWith('.'))
        {
            message += '.';
        }

        return message;
    }

    private static string BuildBreakdown(SummaryTotals totals)
    {
        var breakdown = new List<string>();

        if (totals.GrandSdd > 0)
        {
            breakdown.Add($"{totals.GrandSdd.ToString("N0", CultureInfo.InvariantCulture)} from SDD");
        }

        if (totals.GrandAbw > 0)
        {
            breakdown.Add($"{totals.GrandAbw.ToString("N0", CultureInfo.InvariantCulture)} from 515 ABW");
        }

        return breakdown.Count switch
        {
            0 => string.Empty,
            1 => breakdown[0],
            _ => string.Join(" and ", breakdown)
        };
    }

    private static string FormatCount(int value, string singular, string plural)
    {
        var label = value == 1 ? singular : plural;
        return $"{value.ToString("N0", CultureInfo.InvariantCulture)} {label}";
    }

    private readonly record struct SummaryTotals(
        int ProjectsTotal,
        int YearsTotal,
        int GrandTotal,
        int GrandAbw,
        int GrandSdd)
    {
        public static SummaryTotals Empty { get; } = new(0, 0, 0, 0, 0);
    }
}
