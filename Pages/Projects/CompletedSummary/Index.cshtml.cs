using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using ProjectManagement.Models.Projects;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Pages.Projects.CompletedSummary;

[Authorize]
// SECTION: Completed projects summary read page model
public sealed class IndexModel : PageModel
{
    // SECTION: Dependencies
    private readonly CompletedProjectsSummaryService _summaryService;
    private readonly ICompletedProjectsSummaryExcelBuilder _excelBuilder;
    private readonly IClock _clock;

    public IndexModel(
        CompletedProjectsSummaryService summaryService,
        ICompletedProjectsSummaryExcelBuilder excelBuilder,
        IClock clock)
    {
        _summaryService = summaryService ?? throw new ArgumentNullException(nameof(summaryService));
        _excelBuilder = excelBuilder ?? throw new ArgumentNullException(nameof(excelBuilder));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    // SECTION: Filters
    [BindProperty(SupportsGet = true)]
    public string? TechStatus { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool? AvailableForProliferation { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? CompletedYear { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public IReadOnlyList<SelectListItem> TechStatusOptions { get; private set; } = Array.Empty<SelectListItem>();
    public IReadOnlyList<SelectListItem> AvailabilityOptions { get; } = new[]
    {
        new SelectListItem("All", string.Empty),
        new SelectListItem("Yes", "true"),
        new SelectListItem("No", "false")
    };

    // SECTION: View data
    public IReadOnlyList<CompletedProjectSummaryDto> Items { get; private set; } = Array.Empty<CompletedProjectSummaryDto>();

    // SECTION: Handlers
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        NormaliseFilters();
        TechStatusOptions = BuildTechStatusOptions(TechStatus);

        Items = await _summaryService.GetAsync(
            TechStatus,
            AvailableForProliferation,
            CompletedYear,
            Search,
            cancellationToken);
    }

    public async Task<IActionResult> OnGetExportAsync(CancellationToken cancellationToken)
    {
        NormaliseFilters();

        var items = await _summaryService.GetAsync(
            TechStatus,
            AvailableForProliferation,
            CompletedYear,
            Search,
            cancellationToken);

        var generatedAtUtc = _clock.UtcNow;
        var workbook = _excelBuilder.Build(
            new CompletedProjectsSummaryExportContext(
                items,
                generatedAtUtc,
                TechStatus,
                AvailableForProliferation,
                CompletedYear,
                Search));

        var fileName = $"completed-projects-summary-{generatedAtUtc:yyyyMMddHHmmss}.xlsx";
        return File(workbook, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    // SECTION: Helpers
    private void NormaliseFilters()
    {
        if (!string.IsNullOrWhiteSpace(TechStatus) && Array.IndexOf(ProjectTechStatusCodes.All, TechStatus) < 0)
        {
            TechStatus = null;
        }

        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
    }

    private static IReadOnlyList<SelectListItem> BuildTechStatusOptions(string? selected)
    {
        var items = new List<SelectListItem>
        {
            new("All", string.Empty)
        };

        foreach (var status in ProjectTechStatusCodes.All)
        {
            items.Add(new SelectListItem(status, status, string.Equals(status, selected, StringComparison.Ordinal)));
        }

        return items;
    }
}
