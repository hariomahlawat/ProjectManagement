using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ProjectManagement.Pages.Tools.Pdf;

/// <summary>
/// Standalone PDF Tools workspace. Users edit their own PDFs entirely in the browser; the
/// page has no handlers that accept files, and nothing is stored or uploaded to PRISM.
/// </summary>
[Authorize]
public sealed class IndexModel : PageModel
{
    public void OnGet()
    {
    }
}
