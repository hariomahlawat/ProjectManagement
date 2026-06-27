using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Training;

/// <summary>
/// Compatibility route. Training deletion decisions are now handled in the central
/// Decision Centre.
/// </summary>
[Authorize(Policy = ProjectOfficeReportsPolicies.ApproveTrainingTracker)]
public sealed class ApprovalsModel : PageModel
{
    public IActionResult OnGet()
        => RedirectToPage("/Approvals/Pending/Index", new { Type = "TrainingDelete" });
}
