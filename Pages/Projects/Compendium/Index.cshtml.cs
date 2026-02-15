using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Services.Compendiums;

namespace ProjectManagement.Pages.Projects.Compendium;

[Authorize]
public sealed class IndexModel : PageModel
{
    private readonly ICompendiumExportService _exportService;

    public IndexModel(ICompendiumExportService exportService)
    {
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostGenerateAsync(CancellationToken cancellationToken)
    {
        var result = await _exportService.GenerateAsync(cancellationToken);
        return File(result.Bytes, "application/pdf", result.FileName);
    }
}
