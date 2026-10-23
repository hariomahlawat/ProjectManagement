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
    // Straight to the country cards (where MapBoard also leads). The page name is area-relative:
    // the old "/ProjectOfficeReports/FFC/MapBoard" named no page and answered 500.
    public IActionResult OnGet()
        => RedirectToPage("/FFC/Footprint", new { area = "ProjectOfficeReports", view = "cards" });
}
