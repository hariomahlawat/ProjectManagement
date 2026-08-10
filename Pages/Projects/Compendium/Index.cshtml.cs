using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Services.Compendiums;

namespace ProjectManagement.Pages.Projects.Compendium;

/// <summary>
/// Compatibility endpoint for existing Compendium bookmarks and legacy POSTs.
/// GET requests move users to the canonical Publications workspace; an existing
/// Generate POST remains functional so old links/forms are not broken abruptly.
/// </summary>
[Authorize]
public sealed class IndexModel : PageModel
{
    private readonly ICompendiumExportService _exportService;

    public IndexModel(ICompendiumExportService exportService)
    {
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
    }

    [BindProperty]
    public GenerateInput Input { get; set; } = new();

    public IActionResult OnGet()
        => RedirectToPage("/Projects/Publications/Compendium/Index");

    public async Task<IActionResult> OnPostGenerateAsync(CancellationToken cancellationToken)
    {
        Input.HandlingMarking = NormalizeOptional(Input.HandlingMarking);
        if (!ModelState.IsValid)
        {
            return RedirectToPage("/Projects/Publications/Compendium/Index");
        }

        var result = await _exportService.GenerateAsync(
            new CompendiumExportRequest(Input.HandlingMarking),
            cancellationToken);
        return File(result.Bytes, "application/pdf", result.FileName);
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 80 ? normalized : normalized[..80].TrimEnd();
    }

    public sealed class GenerateInput
    {
        [StringLength(80)]
        public string? HandlingMarking { get; set; }
    }
}
