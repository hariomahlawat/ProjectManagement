using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ProjectManagement.Areas.Admin.Pages.Lookups.LineDirectorates;

[Authorize(Roles = "Admin")]
public class CreateModel : PageModel
{
    public IActionResult OnGet(string? q, string? status, int? pageNumber)
    {
        return RedirectToPage("./Index", new { open = "create", q, status, pageNumber });
    }
}
