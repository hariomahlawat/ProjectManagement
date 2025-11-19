using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC;

[Authorize]
// -----------------------------------------------------------------------------
// SECTION: Redirect handler
// MapTable previously rendered a standalone view. The board feature replaced it,
// but legacy bookmarks still hit this route. Keep the handler without marking it
// obsolete to avoid compiler warnings while continuing to redirect users.
// -----------------------------------------------------------------------------
public class MapTableModel : PageModel
{
    public IActionResult OnGet()
    {
        return RedirectToPage("/ProjectOfficeReports/FFC/MapBoard");
    }
}
