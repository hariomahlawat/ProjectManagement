using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Proliferation;

[Authorize(Policy = ProjectOfficeReportsPolicies.ViewProliferationTracker)]
public sealed class ProjectDetailsModel : PageModel
{
    // SECTION: Display content
    public string Lede { get; } = "Review proliferation totals across projects, years, and units.";

    // SECTION: Request handlers
    public void OnGet()
    {
    }
}
