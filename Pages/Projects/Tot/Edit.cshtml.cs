using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ProjectManagement.Pages.Projects.Tot;

/// <summary>
/// The old standalone ToT editor. Transfer of Technology is now edited only in the Overview
/// drawer, which applies the same rules (applicability, pending requests, PO approval). This
/// address is kept so bookmarks and old links still work: it opens the project's Overview with
/// the ToT drawer in edit mode. The drawer itself decides whether the user may edit.
/// </summary>
[Authorize]
public sealed class EditModel : PageModel
{
    public IActionResult OnGet(int id) => RedirectToOverview(id);

    public IActionResult OnPost(int id) => RedirectToOverview(id);

    private IActionResult RedirectToOverview(int id)
        => RedirectToPage("/Projects/Overview", new { id, tot = "edit" });
}
