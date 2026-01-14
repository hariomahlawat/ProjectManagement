using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Services.IndustryPartners;
using ProjectManagement.ViewModels.Projects.IndustryPartners;

namespace ProjectManagement.Pages.Projects.IndustryPartners
{
    [Authorize]
    public class PartnerDetailModel : PageModel
    {
        private readonly IIndustryPartnerService _industryPartnerService;
        private readonly IAuthorizationService _authorizationService;

        public PartnerDetailModel(
            IIndustryPartnerService industryPartnerService,
            IAuthorizationService authorizationService)
        {
            _industryPartnerService = industryPartnerService;
            _authorizationService = authorizationService;
        }

        // Section: Query parameters
        [BindProperty(SupportsGet = true, Name = "partnerId")]
        public int PartnerId { get; set; }

        // Section: Detail data
        public PartnerDetailViewModel? Partner { get; private set; }

        public async Task<IActionResult> OnGetAsync()
        {
            if (PartnerId <= 0)
            {
                return BadRequest();
            }

            Partner = await _industryPartnerService.GetPartnerDetailAsync(PartnerId);

            if (Partner is null)
            {
                return NotFound();
            }

            var canManage = await CanManagePartnersAsync();
            Partner.CanManage = canManage;

            // Section: Link project feedback
            if (TempData.ContainsKey("LinkProjectSuccess"))
            {
                TempData.Remove("LinkProjectSuccess");
                Response.Headers["HX-Trigger"] = "link-project-saved";
            }

            // Section: Archive/reactivate feedback
            if (TempData.ContainsKey("ArchivePartnerSuccess"))
            {
                TempData.Remove("ArchivePartnerSuccess");
                Response.Headers["HX-Trigger"] = "industry-partner-archived";
            }

            if (TempData.ContainsKey("ReactivatePartnerSuccess"))
            {
                TempData.Remove("ReactivatePartnerSuccess");
                Response.Headers["HX-Trigger"] = "industry-partner-reactivated";
            }

            return Page();
        }

        // Section: Permission helpers
        private async Task<bool> CanManagePartnersAsync()
        {
            var result = await _authorizationService.AuthorizeAsync(User, IndustryPartnerPolicies.Manage);
            return result.Succeeded;
        }
    }
}
