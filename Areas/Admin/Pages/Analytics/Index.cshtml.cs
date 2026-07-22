using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;

namespace ProjectManagement.Areas.Admin.Pages.Analytics;

[Authorize(Policy = AdminPolicies.SecurityView)]
[ResponseCache(NoStore = true)]
public sealed class IndexModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("./Logins");
}
