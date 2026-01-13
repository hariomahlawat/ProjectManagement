using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.ViewModels.Projects.IndustryPartners;

namespace ProjectManagement.Pages.Projects.IndustryPartners
{
    [Authorize]
    public class PartnerDetailModel : PageModel
    {
        // Section: Query parameters
        [BindProperty(SupportsGet = true, Name = "partnerId")]
        public int PartnerId { get; set; }

        // Section: Detail data
        public PartnerDetailViewModel? Partner { get; private set; }

        public IActionResult OnGet()
        {
            if (PartnerId <= 0)
            {
                return BadRequest();
            }

            Partner = IndustryPartnerSampleData.BuildSamplePartners()
                .FirstOrDefault(partner => partner.Id == PartnerId);

            if (Partner is null)
            {
                return NotFound();
            }

            return Page();
        }
    }
}
