using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Models;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
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

        // SECTION: The current procurement workflow, read from code so the landing page never drifts from it.
        public IReadOnlyList<WorkflowStageDefinition> Stages { get; } =
            ProcurementWorkflow.StageDefinitionsFor(PlanConstants.DefaultStageTemplateVersion);

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
