using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC;

[Authorize]
[Obsolete("Use /ProjectOfficeReports/FFC/MapBoard")]
public class MapTableModel : PageModel
{
    public IActionResult OnGet()
    {
        return RedirectToPage("/ProjectOfficeReports/FFC/MapBoard");
    }
}
