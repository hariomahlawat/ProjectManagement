using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ProjectManagement.Pages.Notebook;

[Authorize]
public class EditModel : PageModel
{
    // SECTION: Compatibility route; editing is handled in the split panel on Index.
    public IActionResult OnGet(Guid? id) => RedirectToPage("/Notebook/Index", new { selectedId = id });
}
