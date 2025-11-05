using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Proliferation;

[Authorize(Policy = ProjectOfficeReportsPolicies.ViewProliferationTracker)]
public sealed class SummaryModel : PageModel
{
    private readonly IProliferationSummaryReadService _summaryService;
    private readonly IProliferationCardExportService _cardExportService;

    public SummaryModel(
        IProliferationSummaryReadService summaryService,
        IProliferationCardExportService cardExportService)
    {
        _summaryService = summaryService ?? throw new ArgumentNullException(nameof(summaryService));
        _cardExportService = cardExportService ?? throw new ArgumentNullException(nameof(cardExportService));
    }

    public ProliferationSummaryViewModel Summary { get; private set; } = ProliferationSummaryViewModel.Empty;

    public int ProjectsTotal { get; private set; }

    public int YearsTotal { get; private set; }

    public int GrandTotal { get; private set; }

    public int GrandAbw { get; private set; }

    public int GrandSdd { get; private set; }

    public string Lede { get; private set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public bool Expand { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Open { get; set; }

    public HashSet<int> OpenYears { get; private set; } = new();

    // ------------------------------------------------------------------
    // normal GET
    // ------------------------------------------------------------------
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

        InitOpenYears();
    }

    // ------------------------------------------------------------------
    // GET /Proliferation/Summary?handler=ExportProjects
    // exports: projects ranked by proliferations
    // ------------------------------------------------------------------
    public async Task<FileResult> OnGetExportProjectsAsync(CancellationToken cancellationToken)
    {
        var summary = await _summaryService.GetSummaryAsync(cancellationToken);
        var bytes = _cardExportService.BuildProjectsRanking(summary);

        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "ProliferationProjects.xlsx");
    }

    // ------------------------------------------------------------------
    // GET /Proliferation/Summary?handler=ExportYearBreakdown
    // exports: separate sheet per year
    // ------------------------------------------------------------------
    public async Task<FileResult> OnGetExportYearBreakdownAsync(CancellationToken cancellationToken)
    {
        var summary = await _summaryService.GetSummaryAsync(cancellationToken);
        var bytes = _cardExportService.BuildYearBreakdown(summary);

        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "ProliferationYearBreakdown.xlsx");
    }

    // ---------------- existing helper logic below ---------------------

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

    private void InitOpenYears()
    {
        var yearSet = new HashSet<int>();

        IEnumerable<int> availableYears;

        if (Summary.ByYear.Count > 0)
        {
            availableYears = Summary.ByYear.Select(row => row.Year);
        }
        else if (Summary.ByProjectYear.Count > 0)
        {
            availableYears = Summary.ByProjectYear.Select(row => row.Year);
        }
        else
        {
            OpenYears = yearSet;
            return;
        }

        var orderedYears = availableYears
            .Distinct()
            .OrderByDescending(year => year)
            .ToArray();

        if (orderedYears.Length == 0)
        {
            OpenYears = yearSet;
            return;
        }

        if (Expand)
        {
            foreach (var year in orderedYears)
            {
                yearSet.Add(year);
            }

            OpenYears = yearSet;
            return;
        }

        if (Open is not null)
        {
            var validYears = new HashSet<int>(orderedYears);
            var requestedYears = Open.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var requested in requestedYears)
            {
                if (int.TryParse(requested, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    && validYears.Contains(parsed))
                {
                    yearSet.Add(parsed);
                }
            }

            OpenYears = yearSet;
            return;
        }

        yearSet.Add(orderedYears[0]);
        OpenYears = yearSet;
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
