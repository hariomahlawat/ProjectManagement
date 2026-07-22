using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC;

[Authorize]
public sealed class MapBoardModel : PageModel
{
    public IActionResult OnGet(
        short? year = null,
        long? countryId = null,
        string? q = null,
        string? sort = null)
        => RedirectToPage(
            "/FFC/Footprint",
            new
            {
                area = "ProjectOfficeReports",
                view = "cards",
                year,
                countryId,
                q,
                sort
            });
}
