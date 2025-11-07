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

    public SummaryModel(
        IProliferationSummaryReadService summaryService,
        IProliferationCardExportService cardExportService,
        ApplicationDbContext db)
    {
        _summaryService = summaryService ?? throw new ArgumentNullException(nameof(summaryService));
        _cardExportService = cardExportService ?? throw new ArgumentNullException(nameof(cardExportService));
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public ProliferationSummaryViewModel Summary { get; private set; } = ProliferationSummaryViewModel.Empty;

    public int ProjectsTotal { get; private set; }
    public int YearsTotal { get; private set; }
    public int GrandTotal { get; private set; }
    public int GrandAbw { get; private set; }
    public int GrandSdd { get; private set; }
    public string Lede { get; private set; } = string.Empty;

    public IReadOnlyList<TechnicalCategoryBreakdownRow> TechnicalCategoryBreakdown { get; private set; } =
        Array.Empty<TechnicalCategoryBreakdownRow>();

    [BindProperty(SupportsGet = true)]
    public bool Expand { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Open { get; set; }

    public HashSet<int> OpenYears { get; private set; } = new();

    private sealed class ProjectRow
    {
        public int Id { get; init; }
        public string? Name { get; init; }
        public int? TechnicalCategoryId { get; init; }
        public string? TechnicalCategoryName { get; init; }
    }

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

        InitOpenYears();
    }

    private async Task<IReadOnlyList<TechnicalCategoryBreakdownRow>> BuildTechnicalCategoryBreakdownAsync(
        ProliferationSummaryViewModel summary,
        CancellationToken cancellationToken)
    {
        if (summary is null || summary.ByProject.Count == 0)
        {
            return Array.Empty<TechnicalCategoryBreakdownRow>();
        }

        // totals by project id as present in the summary
        var totalsById = summary.ByProject
            .Where(p => p.ProjectId > 0)
            .ToDictionary(p => p.ProjectId, p => p.Totals.Total);

        // project names we may have to match in DB
        var namesNeedingMatch = summary.ByProject
            .Where(p => p.ProjectId <= 0 || !totalsById.ContainsKey(p.ProjectId))
            .Select(p => p.ProjectName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var projectsFromDb = new List<ProjectRow>();

        // 1. fetch by Id
        if (totalsById.Count > 0)
        {
            var idList = totalsById.Keys.ToList();

            var byId = await _db.Projects
                .AsNoTracking()
                .Where(p => idList.Contains(p.Id))
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.TechnicalCategoryId,
                    TechnicalCategoryName = p.TechnicalCategory != null ? p.TechnicalCategory.Name : null
                })
                .ToListAsync(cancellationToken);

            projectsFromDb.AddRange(byId.Select(x => new ProjectRow
            {
                Id = x.Id,
                Name = x.Name,
                TechnicalCategoryId = x.TechnicalCategoryId,
                TechnicalCategoryName = x.TechnicalCategoryName
            }));
        }

        // 2. fetch by Name for those that we couldn’t match by Id
        if (namesNeedingMatch.Count > 0)
        {
            var byName = await _db.Projects
                .AsNoTracking()
                .Where(p => namesNeedingMatch.Contains(p.Name))
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.TechnicalCategoryId,
                    TechnicalCategoryName = p.TechnicalCategory != null ? p.TechnicalCategory.Name : null
                })
                .ToListAsync(cancellationToken);

            foreach (var x in byName)
            {
                if (projectsFromDb.Any(y => y.Id == x.Id))
                {
                    continue;
                }

                projectsFromDb.Add(new ProjectRow
                {
                    Id = x.Id,
                    Name = x.Name,
                    TechnicalCategoryId = x.TechnicalCategoryId,
                    TechnicalCategoryName = x.TechnicalCategoryName
                });
            }
        }

        if (projectsFromDb.Count == 0)
        {
            return Array.Empty<TechnicalCategoryBreakdownRow>();
        }

        // join summary totals with db rows and group by technical category
        var result = projectsFromDb
            .Select(dbProj =>
            {
                // prefer total by Id
                if (totalsById.TryGetValue(dbProj.Id, out var total))
                {
                    return new
                    {
                        dbProj.TechnicalCategoryId,
                        Name = string.IsNullOrWhiteSpace(dbProj.TechnicalCategoryName)
                            ? "Uncategorised"
                            : dbProj.TechnicalCategoryName,
                        Total = total
                    };
                }

                // fallback: look up by name in summary
                var match = summary.ByProject.FirstOrDefault(sp =>
                    sp.ProjectId == dbProj.Id ||
                    string.Equals(sp.ProjectName, dbProj.Name, StringComparison.OrdinalIgnoreCase));

                var fallbackTotal = match?.Totals.Total ?? 0;

                return new
                {
                    dbProj.TechnicalCategoryId,
                    Name = string.IsNullOrWhiteSpace(dbProj.TechnicalCategoryName)
                        ? "Uncategorised"
                        : dbProj.TechnicalCategoryName,
                    Total = fallbackTotal
                };
            })
            .Where(x => x.Total > 0)
            .GroupBy(x => new { x.TechnicalCategoryId, x.Name })
            .Select(g => new TechnicalCategoryBreakdownRow(
                g.Key.TechnicalCategoryId,
                g.Key.Name,
                g.Sum(x => x.Total)))
            .OrderByDescending(r => r.Total)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return result;
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

    // ---------------- helpers below ----------------

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

    public sealed record TechnicalCategoryBreakdownRow(
        int? TechnicalCategoryId,
        string Name,
        int Total);
}
