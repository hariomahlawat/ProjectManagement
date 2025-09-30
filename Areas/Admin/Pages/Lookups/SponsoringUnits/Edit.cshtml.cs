using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ProjectManagement.Areas.Admin.Pages.Lookups.SponsoringUnits;

[Authorize(Roles = "Admin")]
public class EditModel : PageModel
{
    public IActionResult OnGet(int id, string? q, string? status, int? pageNumber)
    {
        return RedirectToPage("./Index", new { open = "edit", editId = id, q, status, pageNumber });
    }
}
