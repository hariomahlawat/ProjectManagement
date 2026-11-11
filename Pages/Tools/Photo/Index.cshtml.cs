using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ProjectManagement.Pages.Tools.Photo;

/// <summary>
/// Standalone Photo Tools workspace. Users change their own photos entirely in the browser;
/// the page has no handlers that accept files, and nothing is stored or uploaded to PRISM.
/// </summary>
[Authorize]
public sealed class IndexModel : PageModel
{
    public void OnGet()
    {
    }
}
