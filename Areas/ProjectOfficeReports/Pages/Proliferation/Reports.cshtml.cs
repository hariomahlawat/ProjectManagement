using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Proliferation
{
    // SECTION: Reports page model
    [Authorize(Policy = ProjectOfficeReportsPolicies.ViewProliferationTracker)]
    public sealed class ReportsModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
