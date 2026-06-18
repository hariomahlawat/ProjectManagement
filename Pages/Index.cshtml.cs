using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Models;

namespace ProjectManagement.Pages
{
    [AllowAnonymous]
    public class IndexModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public IndexModel(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        // SECTION: Authenticated users skip the public landing page and enter their daily workspace.
        public async Task<IActionResult> OnGetAsync()
        {
            if (User.Identity?.IsAuthenticated != true)
            {
                return Page();
            }

            var user = await _userManager.GetUserAsync(User);
            if (user is null)
            {
                return Page();
            }

            if (await _userManager.IsInRoleAsync(user, RoleNames.ProjectOfficer))
            {
                return RedirectToPage("/Workspace/Index");
            }

            return RedirectToPage("/Dashboard/Index");
        }
    }
}
