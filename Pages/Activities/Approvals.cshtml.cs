using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ProjectManagement.Pages.Activities;

/// <summary>
/// Compatibility route. Activity deletion decisions are now handled in the central
/// Decision Centre so every approval follows one consistent workflow.
/// </summary>
[Authorize(Roles = "Admin,HoD")]
public sealed class ApprovalsModel : PageModel
{
    public IActionResult OnGet()
        => RedirectToPage("/Approvals/Pending/Index", new { Type = "ActivityDelete" });
}
