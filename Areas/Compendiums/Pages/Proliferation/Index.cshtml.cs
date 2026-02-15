using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Compendiums.Application;
using ProjectManagement.Areas.Compendiums.Application.Dto;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Areas.Compendiums.Pages.Proliferation;

[Authorize]
public sealed class IndexModel : PageModel
{
    private readonly ICompendiumReadService _readService;
    private readonly IProliferationCompendiumPdfBuilder _pdfBuilder;
    private readonly IWebHostEnvironment _environment;

    public IndexModel(ICompendiumReadService readService, IProliferationCompendiumPdfBuilder pdfBuilder, IWebHostEnvironment environment)
    {
        _readService = readService;
        _pdfBuilder = pdfBuilder;
        _environment = environment;
    }

    public IReadOnlyList<CompendiumProjectCardDto> Projects { get; private set; } = Array.Empty<CompendiumProjectCardDto>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Projects = await _readService.GetEligibleProjectsAsync(cancellationToken);
    }

    public async Task<IActionResult> OnGetExportPdfAsync(CancellationToken cancellationToken)
    {
        var details = await _readService.GetEligibleProjectDetailsAsync(includeHistoricalExtras: false, cancellationToken);

        var logoPath = Path.Combine(_environment.WebRootPath, "img", "logos", "sdd.png");
        var logoBytes = System.IO.File.Exists(logoPath) ? await System.IO.File.ReadAllBytesAsync(logoPath, cancellationToken) : null;

        var file = _pdfBuilder.Build(new ProliferationCompendiumPdfContext(details, DateOnly.FromDateTime(DateTime.Today), logoBytes));
        return File(file, "application/pdf", $"proliferation-compendium-{DateTime.UtcNow:yyyyMMddHHmmss}.pdf");
    }
}
