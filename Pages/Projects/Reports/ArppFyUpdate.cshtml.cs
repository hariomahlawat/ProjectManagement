using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Services.Reports.ArppFyProjectUpdate;

namespace ProjectManagement.Pages.Projects.Reports;

[Authorize(Policy = ProjectOfficeReportsPolicies.ViewArpp)]
public sealed class ArppFyUpdateModel : PageModel
{
    private readonly IArppFyProjectUpdateService _reportService;
    private readonly IArppFyProjectUpdateExportService _exportService;

    public ArppFyUpdateModel(
        IArppFyProjectUpdateService reportService,
        IArppFyProjectUpdateExportService exportService)
    {
        _reportService = reportService ?? throw new ArgumentNullException(nameof(reportService));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
    }

    [BindProperty(SupportsGet = true)]
    public int? FinancialYearStart { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool IncludePresentStage { get; set; }

    [BindProperty(SupportsGet = true)]
    public ArppListingDateMode ListingDateMode { get; set; } = ArppListingDateMode.InitialListing;

    public IReadOnlyList<int> AvailableFinancialYears { get; private set; } = Array.Empty<int>();
    public ArppFyProjectUpdateReport? Report { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        ListingDateMode = ArppFyProjectUpdatePresentationOptions.NormalizeListingDateMode(ListingDateMode);
        AvailableFinancialYears = await _reportService.GetAvailableFinancialYearsAsync(cancellationToken);
        if (AvailableFinancialYears.Count == 0)
        {
            return Page();
        }

        var selected = FinancialYearStart.HasValue && AvailableFinancialYears.Contains(FinancialYearStart.Value)
            ? FinancialYearStart.Value
            : AvailableFinancialYears[0];
        FinancialYearStart = selected;
        Report = await _reportService.BuildAsync(selected, cancellationToken);
        return Page();
    }

    public Task<IActionResult> OnGetWordAsync(int financialYearStart, CancellationToken cancellationToken)
        => ExportAsync(financialYearStart, _exportService.BuildWord, cancellationToken);

    public Task<IActionResult> OnGetPdfAsync(int financialYearStart, CancellationToken cancellationToken)
        => ExportAsync(financialYearStart, _exportService.BuildPdf, cancellationToken);

    public Task<IActionResult> OnGetExcelAsync(int financialYearStart, CancellationToken cancellationToken)
        => ExportAsync(financialYearStart, _exportService.BuildExcel, cancellationToken);

    private async Task<IActionResult> ExportAsync(
        int financialYearStart,
        Func<ArppFyProjectUpdateReport, ArppFyProjectUpdatePresentationOptions?, ArppFyProjectUpdateFile> exporter,
        CancellationToken cancellationToken)
    {
        var available = await _reportService.GetAvailableFinancialYearsAsync(cancellationToken);
        if (!available.Contains(financialYearStart))
        {
            return NotFound();
        }

        var report = await _reportService.BuildAsync(financialYearStart, cancellationToken);
        if (report is null)
        {
            return NotFound();
        }

        if (!report.CanExport)
        {
            return BadRequest("The selected financial year has no linked approved projects to export.");
        }

        var options = new ArppFyProjectUpdatePresentationOptions(
            IncludePresentStage,
            ArppFyProjectUpdatePresentationOptions.NormalizeListingDateMode(ListingDateMode));
        var file = exporter(report, options);
        return File(file.Content, file.ContentType, file.FileName);
    }
}
