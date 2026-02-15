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

        var totals = new Dictionary<int, int>();
        foreach (var project in Projects)
        {
            var detail = await _readService.GetProjectAsync(project.ProjectId, includeHistoricalExtras: true, cancellationToken);
            totals[project.ProjectId] = detail?.HistoricalExtras?.ProliferationTotalAllTime ?? 0;
        }

        TotalsByProject = totals;
    }

    public async Task<IActionResult> OnGetExportPdfAsync(CancellationToken cancellationToken)
    {
        var cards = await _readService.GetEligibleProjectsAsync(cancellationToken);
        var details = new List<CompendiumProjectDetailDto>(cards.Count);

        foreach (var card in cards)
        {
            var detail = await _readService.GetProjectAsync(card.ProjectId, includeHistoricalExtras: true, cancellationToken);
            if (detail is not null)
            {
                details.Add(detail);
            }
        }

        var logoPath = Path.Combine(_environment.WebRootPath, "img", "logos", "sdd.png");
        var logoBytes = System.IO.File.Exists(logoPath) ? await System.IO.File.ReadAllBytesAsync(logoPath, cancellationToken) : null;
        var file = _pdfBuilder.Build(new HistoricalCompendiumPdfContext(details, DateOnly.FromDateTime(DateTime.Today), logoBytes));
        return File(file, "application/pdf", $"historical-compendium-{DateTime.UtcNow:yyyyMMddHHmmss}.pdf");
    }
}
