using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Projects;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Pages.Projects.CompletedSummary;

[Authorize]
public sealed class IndexModel : PageModel
{
    private readonly CompletedProjectsSummaryService _summaryService;
    private readonly ICompletedProjectsSummaryExcelBuilder _excelBuilder;
    private readonly IClock _clock;
    private readonly ApplicationDbContext _db;

    public IndexModel(
        CompletedProjectsSummaryService summaryService,
        ICompletedProjectsSummaryExcelBuilder excelBuilder,
        IClock clock,
        ApplicationDbContext db)
    {
        _summaryService = summaryService ?? throw new ArgumentNullException(nameof(summaryService));
        _excelBuilder = excelBuilder ?? throw new ArgumentNullException(nameof(excelBuilder));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    // SECTION: Filter inputs
    [BindProperty(SupportsGet = true)]
    public int? TechnicalCategoryId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? TechStatus { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool? AvailableForProliferation { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool? TotCompleted { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? CompletedYear { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    // SECTION: Sorting inputs
    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Dir { get; set; }

    // SECTION: Filter option lists
    public IReadOnlyList<SelectListItem> TechnicalCategoryOptions { get; private set; } = Array.Empty<SelectListItem>();
    public IReadOnlyList<SelectListItem> TechStatusOptions { get; private set; } = Array.Empty<SelectListItem>();
    public IReadOnlyList<SelectListItem> TotStatusOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> AvailabilityOptions { get; } = new[]
    {
        new SelectListItem("All", string.Empty),
        new SelectListItem("Yes", "true"),
        new SelectListItem("No", "false")
    };

    public IReadOnlyList<CompletedProjectSummaryDto> Items { get; private set; } = Array.Empty<CompletedProjectSummaryDto>();

    // expose to the view
    public bool CanEdit { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadTechnicalCategoriesAsync(cancellationToken);
        NormaliseFilters();
        NormaliseSorting();

        TechStatusOptions = BuildTechStatusOptions(TechStatus);
        TotStatusOptions = BuildTotStatusOptions(TotCompleted);

        Items = await _summaryService.GetAsync(
            TechnicalCategoryId,
            TechStatus,
            AvailableForProliferation,
            TotCompleted,
            CompletedYear,
            Search,
            Sort!,
            Dir!,
            cancellationToken);

        CanEdit = User.IsInRole("Admin")
                  || User.IsInRole("HoD")
                  || User.IsInRole("Project Office");
    }

    public async Task<IActionResult> OnGetExportAsync(CancellationToken cancellationToken)
    {
        NormaliseFilters();
        NormaliseSorting();

        var items = await _summaryService.GetAsync(
            TechnicalCategoryId,
            TechStatus,
            AvailableForProliferation,
            TotCompleted,
            CompletedYear,
            Search,
            Sort!,
            Dir!,
            cancellationToken);

        var generatedAtUtc = _clock.UtcNow;

        var workbook = _excelBuilder.Build(
            new CompletedProjectsSummaryExportContext(
                items,
                generatedAtUtc,
                TechStatus,
                AvailableForProliferation,
                TotCompleted,
                CompletedYear,
                Search));

        var fileName = $"completed-projects-summary-{generatedAtUtc:yyyyMMddHHmmss}.xlsx";
        return File(
            workbook,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    private void NormaliseFilters()
    {
        if (!string.IsNullOrWhiteSpace(TechStatus)
            && Array.IndexOf(ProjectTechStatusCodes.All, TechStatus) < 0)
        {
            TechStatus = null;
        }

        var totCompletedRaw = Request.Query[nameof(TotCompleted)].ToString();
        if (!string.IsNullOrWhiteSpace(totCompletedRaw)
            && !bool.TryParse(totCompletedRaw, out _))
        {
            TotCompleted = null;
        }

        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
    }

    private void NormaliseSorting()
    {
        // SECTION: Normalize sorting query params
        var sort = (Sort ?? string.Empty).Trim().ToLowerInvariant();
        var dir = (Dir ?? string.Empty).Trim().ToLowerInvariant();

        Sort = sort is "name" or "rd" or "prod" or "lpp" or "tech" or "avail" or "tot" or "year"
            ? sort
            : "name";

        Dir = dir is "asc" or "desc"
            ? dir
            : "asc";
    }

    public string NextSortDirection(string sortKey)
    {
        // SECTION: Toggle direction for active sort column
        return string.Equals(Sort, sortKey, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Dir, "asc", StringComparison.OrdinalIgnoreCase)
            ? "desc"
            : "asc";
    }

    public string GetSortIndicator(string sortKey)
    {
        // SECTION: Indicator for active sort column
        if (!string.Equals(Sort, sortKey, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return string.Equals(Dir, "desc", StringComparison.OrdinalIgnoreCase) ? "▼" : "▲";
    }

    private async Task LoadTechnicalCategoriesAsync(CancellationToken cancellationToken)
    {
        var cats = await _db.TechnicalCategories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var items = new List<SelectListItem>
        {
            new("All", string.Empty)
        };

        foreach (var c in cats)
        {
            items.Add(new SelectListItem(c.Name, c.Id.ToString(), TechnicalCategoryId == c.Id));
        }

        TechnicalCategoryOptions = items;
    }

    private static IReadOnlyList<SelectListItem> BuildTechStatusOptions(string? selected)
    {
        var items = new List<SelectListItem>
        {
            new("All", string.Empty)
        };

        foreach (var status in ProjectTechStatusCodes.All)
        {
            items.Add(new SelectListItem(status, status,
                string.Equals(status, selected, StringComparison.Ordinal)));
        }

        return items;
    }

    private static IReadOnlyList<SelectListItem> BuildTotStatusOptions(bool? selected)
    {
        var items = new List<SelectListItem>
        {
            new("All", string.Empty),
            new("Completed", "true"),
            new("Not completed", "false")
        };

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Value))
            {
                item.Selected = selected is null;
                continue;
            }

            if (bool.TryParse(item.Value, out var value))
            {
                item.Selected = selected == value;
            }
        }

        return items;
    }
}
