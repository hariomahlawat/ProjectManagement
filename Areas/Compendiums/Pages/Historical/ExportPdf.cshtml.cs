using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Compendiums.Application;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Areas.Compendiums.Pages.Historical;

[Authorize]
public sealed class ExportPdfModel : PageModel
{
    private readonly ICompendiumReadService _readService;
    private readonly IHistoricalCompendiumPdfBuilder _pdfBuilder;
    private readonly IWebHostEnvironment _environment;

    public ExportPdfModel(ICompendiumReadService readService, IHistoricalCompendiumPdfBuilder pdfBuilder, IWebHostEnvironment environment)
    {
        _readService = readService;
        _pdfBuilder = pdfBuilder;
        _environment = environment;
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var details = await _readService.GetEligibleProjectDetailsAsync(includeHistoricalExtras: true, cancellationToken);

        var logoPath = Path.Combine(_environment.WebRootPath, "img", "logos", "sdd.png");
        var logoBytes = System.IO.File.Exists(logoPath) ? await System.IO.File.ReadAllBytesAsync(logoPath, cancellationToken) : null;
        var file = _pdfBuilder.Build(new HistoricalCompendiumPdfContext(details, DateOnly.FromDateTime(DateTime.Today), logoBytes));
        return File(file, "application/pdf", $"historical-compendium-{DateTime.UtcNow:yyyyMMddHHmmss}.pdf");
    }
}
