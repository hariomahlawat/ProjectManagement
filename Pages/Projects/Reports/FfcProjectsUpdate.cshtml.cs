using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Services.Ffc;
using ProjectManagement.Services.Reports.FfcProjectsUpdate;
using ProjectManagement.Utilities;

namespace ProjectManagement.Pages.Projects.Reports;

[Authorize(Policy = ProjectOfficeReportsPolicies.ViewArpp)]
public sealed class FfcProjectsUpdateModel : PageModel
{
    private const string WordContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string PdfContentType = "application/pdf";
    private const string ExcelContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly IFfcQueryService _ffcQueryService;

    public FfcProjectsUpdateModel(IFfcQueryService ffcQueryService)
        => _ffcQueryService = ffcQueryService ?? throw new ArgumentNullException(nameof(ffcQueryService));

    [BindProperty(SupportsGet = true)]
    public FfcCountryYearSelectionMode SelectionMode { get; set; } =
        FfcCountryYearSelectionMode.DefaultActive;

    [BindProperty(SupportsGet = true)]
    public string? CountryYears { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool IncludeOverallStatus { get; set; }

    public FfcProjectsUpdateReport? Report { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Report = await BuildReportAsync(cancellationToken);

        // Keep the URL/export state canonical. In DefaultActive mode the factory
        // still owns the default rule; the CSV simply mirrors the current result.
        CountryYears = Report.SelectedCountryYearsCsv;
        SelectionMode = Report.SelectionMode;
    }

    public Task<IActionResult> OnGetWordAsync(CancellationToken cancellationToken)
        => ExportAsync(
            (report, options) => FfcProjectsUpdateWordBuilder.Build(report, options),
            WordContentType,
            "docx",
            cancellationToken);

    public Task<IActionResult> OnGetPdfAsync(CancellationToken cancellationToken)
        => ExportAsync(
            (report, options) => FfcProjectsUpdatePdfBuilder.Build(report, options),
            PdfContentType,
            "pdf",
            cancellationToken);

    public Task<IActionResult> OnGetExcelAsync(CancellationToken cancellationToken)
        => ExportAsync(
            (report, options) => FfcProjectsUpdateExcelBuilder.Build(report, options),
            ExcelContentType,
            "xlsx",
            cancellationToken);

    private async Task<IActionResult> ExportAsync(
        Func<FfcProjectsUpdateReport, FfcProjectsUpdatePresentationOptions, byte[]> builder,
        string contentType,
        string extension,
        CancellationToken cancellationToken)
    {
        var report = await BuildReportAsync(cancellationToken);
        if (!report.CanExport)
        {
            return BadRequest("Select at least one country-year containing one or more FFC projects.");
        }

        var options = new FfcProjectsUpdatePresentationOptions(IncludeOverallStatus);
        var generatedAtIst = TimeZoneInfo.ConvertTime(report.GeneratedAtUtc, TimeZoneHelper.GetIst());
        var fileName = string.Format(
            CultureInfo.InvariantCulture,
            "FFC_Projects_Update_{0:yyyyMMdd_HHmm}.{1}",
            generatedAtIst,
            extension);

        return File(builder(report, options), contentType, fileName);
    }

    private async Task<FfcProjectsUpdateReport> BuildReportAsync(CancellationToken cancellationToken)
    {
        var groups = await _ffcQueryService.GetDetailedGroupsAsync(
            DateOnly.MinValue,
            DateOnly.MaxValue,
            incompleteOnly: false,
            countryId: null,
            year: null,
            applyYearFilter: false,
            cancellationToken: cancellationToken);

        return FfcProjectsUpdateReportFactory.Create(
            groups,
            SelectionMode,
            CountryYears,
            DateTimeOffset.UtcNow);
    }
}
