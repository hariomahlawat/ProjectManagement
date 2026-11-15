using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ProjectManagement.Pages.Tools.Collage;

/// <summary>
/// Collage Studio (docs/ux/collage-studio.md). Users make collages of their own photos entirely in
/// the browser; the page has no handlers that accept files, and nothing is stored or uploaded to
/// PRISM.
/// </summary>
[Authorize]
public sealed class IndexModel : PageModel
{
    public void OnGet()
    {
    }
}
