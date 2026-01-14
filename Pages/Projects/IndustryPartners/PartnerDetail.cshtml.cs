using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Services.IndustryPartners;
using ProjectManagement.ViewModels.Projects.IndustryPartners;

namespace ProjectManagement.Pages.Projects.IndustryPartners
{
    [Authorize]
    public class PartnerDetailModel : PageModel
    {
        private readonly IIndustryPartnerService _industryPartnerService;

        public PartnerDetailModel(IIndustryPartnerService industryPartnerService)
        {
            _industryPartnerService = industryPartnerService;
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

            return Page();
        }
    }
}
