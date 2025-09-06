using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ProjectManagement.Areas.Identity.Pages.Account
{
    [Authorize(Roles = "Admin")]
    public class RegisterModel : PageModel
    {
        public IActionResult OnGet() => RedirectToPage("/Users/Create", new { area = "Admin" });
    }
}
