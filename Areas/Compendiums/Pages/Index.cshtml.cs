using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ProjectManagement.Areas.Compendiums.Pages;

[Authorize]
public sealed class IndexModel : PageModel
{
    // SECTION: Landing page model (intentionally empty)
    public void OnGet()
    {
    }
}
