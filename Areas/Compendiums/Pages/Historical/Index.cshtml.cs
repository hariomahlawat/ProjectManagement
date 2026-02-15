using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Compendiums.Application;
using ProjectManagement.Areas.Compendiums.Application.Dto;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Areas.Compendiums.Pages.Historical;

[Authorize]
public sealed class IndexModel : PageModel
{
    private readonly ICompendiumReadService _readService;
    private readonly IHistoricalCompendiumPdfBuilder _pdfBuilder;
    private readonly IWebHostEnvironment _environment;

    public IndexModel(ICompendiumReadService readService, IHistoricalCompendiumPdfBuilder pdfBuilder, IWebHostEnvironment environment)
    {
        _readService = readService;
        _pdfBuilder = pdfBuilder;
        _environment = environment;
    }

    public IReadOnlyList<CompendiumProjectCardDto> Projects { get; private set; } = Array.Empty<CompendiumProjectCardDto>();
    public IReadOnlyDictionary<int, int> TotalsByProject { get; private set; } = new Dictionary<int, int>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Projects = await _readService.GetEligibleProjectsAsync(cancellationToken);

        var details = await _readService.GetEligibleProjectDetailsAsync(includeHistoricalExtras: true, cancellationToken);
        TotalsByProject = details.ToDictionary(
            detail => detail.ProjectId,
            detail => detail.HistoricalExtras?.ProliferationTotalAllTime ?? 0);
    }

    public async Task<IActionResult> OnGetExportPdfAsync(CancellationToken cancellationToken)
    {
        var details = await _readService.GetEligibleProjectDetailsAsync(includeHistoricalExtras: true, cancellationToken);

        var logoPath = Path.Combine(_environment.WebRootPath, "img", "logos", "sdd.png");
        var logoBytes = System.IO.File.Exists(logoPath) ? await System.IO.File.ReadAllBytesAsync(logoPath, cancellationToken) : null;
        var file = _pdfBuilder.Build(new HistoricalCompendiumPdfContext(details, DateOnly.FromDateTime(DateTime.Today), logoBytes));
        return File(file, "application/pdf", $"historical-compendium-{DateTime.UtcNow:yyyyMMddHHmmss}.pdf");
    }
}
