using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Models;
using ProjectManagement.Services.Navigation;

namespace ProjectManagement.Pages
{
    [AllowAnonymous]
    public class IndexModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly DefaultLandingPageResolver _landingPageResolver;

        public IndexModel(
            UserManager<ApplicationUser> userManager,
            DefaultLandingPageResolver landingPageResolver)
        {
            _userManager = userManager;
            _landingPageResolver = landingPageResolver;
        }

        // SECTION: Authenticated users skip the public landing page and enter the role-appropriate application home.
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

            var landingPage = await _landingPageResolver.ResolveAsync(user);
            return RedirectToPage(landingPage);
        }
    }
}
