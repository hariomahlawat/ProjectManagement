using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Tot
{
    /// <summary>
    /// The old ToT summary address. The overview (position, counts and chart) is now the top of the
    /// one ToT page (redesign PR 7, docs/ux/tot.md); bookmarks and links still land there.
    /// </summary>
    [Authorize(Policy = ProjectOfficeReportsPolicies.ViewTotTracker)]
    public sealed class SummaryModel : PageModel
    {
        public IActionResult OnGet() => RedirectToPage("/Tot/Index", new { area = "ProjectOfficeReports" });
    }
}
