using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;
using ProjectManagement.Data;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Proliferation;

[Authorize(Policy = ProjectOfficeReportsPolicies.ViewProliferationTracker)]
public sealed class SummaryModel : PageModel
{
    private readonly IProliferationSummaryReadService _summaryService;
    private readonly IProliferationCardExportService _cardExportService;
    private readonly ApplicationDbContext _db;
    private readonly IAuthorizationService _authorizationService;

    public SummaryModel(
        IProliferationSummaryReadService summaryService,
        IProliferationCardExportService cardExportService,
        ApplicationDbContext db,
        IAuthorizationService authorizationService)
    {
        _summaryService = summaryService ?? throw new ArgumentNullException(nameof(summaryService));
        _cardExportService = cardExportService ?? throw new ArgumentNullException(nameof(cardExportService));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
    }

    public ProliferationSummaryViewModel Summary { get; private set; } = ProliferationSummaryViewModel.Empty;
    public int ProjectsTotal { get; private set; }
    public int YearsTotal { get; private set; }
    public int GrandTotal { get; private set; }
    public int GrandAbw { get; private set; }
    public int GrandSdd { get; private set; }
    public string Lede { get; private set; } = string.Empty;
    public bool CanManageRecords { get; private set; }

    public IReadOnlyList<TechnicalCategoryBreakdownRow> TechnicalCategoryBreakdown { get; private set; } =
        Array.Empty<TechnicalCategoryBreakdownRow>();

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

        TechnicalCategoryBreakdown = await BuildTechnicalCategoryBreakdownAsync(Summary, cancellationToken);

        var submitResult = await _authorizationService.AuthorizeAsync(
            User,
            resource: null,
            ProjectOfficeReportsPolicies.SubmitProliferationTracker);
        CanManageRecords = submitResult.Succeeded;
    }

    public async Task<FileResult> OnGetExportProjectsAsync(CancellationToken cancellationToken)
    {
        var summary = await _summaryService.GetSummaryAsync(cancellationToken);
        var bytes = _cardExportService.BuildProjectsRanking(summary);

        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "ProliferationProjects.xlsx");
    }

    public async Task<FileResult> OnGetExportYearBreakdownAsync(CancellationToken cancellationToken)
    {
        var summary = await _summaryService.GetSummaryAsync(cancellationToken);
        var bytes = _cardExportService.BuildYearBreakdown(summary);

        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "ProliferationYearBreakdown.xlsx");
    }

    private async Task<IReadOnlyList<TechnicalCategoryBreakdownRow>> BuildTechnicalCategoryBreakdownAsync(
        ProliferationSummaryViewModel summary,
        CancellationToken cancellationToken)
    {
        if (summary.ByProject.Count == 0)
        {
            return Array.Empty<TechnicalCategoryBreakdownRow>();
        }

        var totalsByProject = summary.ByProject.ToDictionary(x => x.ProjectId, x => x.Totals.Total);
        var projectIds = totalsByProject.Keys.ToArray();

        var rows = await _db.Projects
            .AsNoTracking()
            .Where(x => projectIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.TechnicalCategoryId,
                TechnicalCategoryName = x.TechnicalCategory != null
                    ? x.TechnicalCategory.Name
                    : "Uncategorised"
            })
            .ToListAsync(cancellationToken);

        return rows
            .Where(x => totalsByProject.ContainsKey(x.Id))
            .GroupBy(x => new
            {
                x.TechnicalCategoryId,
                Name = string.IsNullOrWhiteSpace(x.TechnicalCategoryName)
                    ? "Uncategorised"
                    : x.TechnicalCategoryName
            })
            .Select(group => new TechnicalCategoryBreakdownRow(
                group.Key.TechnicalCategoryId,
                group.Key.Name,
                group.Sum(x => totalsByProject[x.Id])))
            .Where(x => x.Total > 0)
            .OrderByDescending(x => x.Total)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static SummaryTotals CalculateTotals(ProliferationSummaryViewModel summary)
    {
        if (summary.ByProject.Count > 0)
        {
            return new SummaryTotals(
                summary.ByProject.Count,
                summary.ByYear.Count,
                summary.ByProject.Sum(x => x.Totals.Total),
                summary.ByProject.Sum(x => x.Totals.Abw515),
                summary.ByProject.Sum(x => x.Totals.Sdd));
        }

        return SummaryTotals.Empty;
    }

    private static string BuildLede(SummaryTotals totals)
    {
        if (totals == SummaryTotals.Empty)
        {
            return "No approved proliferation data is available yet.";
        }

        return $"Approved proliferation across {totals.ProjectsTotal.ToString("N0", CultureInfo.InvariantCulture)} " +
               $"{(totals.ProjectsTotal == 1 ? "project" : "projects")} and " +
               $"{totals.YearsTotal.ToString("N0", CultureInfo.InvariantCulture)} " +
               $"{(totals.YearsTotal == 1 ? "year" : "years")}.";
    }

    private sealed record SummaryTotals(
        int ProjectsTotal,
        int YearsTotal,
        int GrandTotal,
        int GrandAbw,
        int GrandSdd)
    {
        public static SummaryTotals Empty { get; } = new(0, 0, 0, 0, 0);
    }
}
