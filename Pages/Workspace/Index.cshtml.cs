using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Services.Workspace;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Pages.Workspace;

[Authorize]
public class IndexModel : PageModel
{
    private readonly ProjectOfficerWorkspaceService _workspaceService;
    private readonly UserManager<ApplicationUser> _userManager;
    public ProjectOfficerWorkspaceVm Workspace { get; private set; } = new();
    public IndexModel(ProjectOfficerWorkspaceService workspaceService, UserManager<ApplicationUser> userManager) { _workspaceService = workspaceService; _userManager = userManager; }

    // SECTION: Page loading
    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();
        if (!User.IsInRole(RoleNames.ProjectOfficer)) return RedirectToPage("/Dashboard/Index");
        Workspace = await _workspaceService.GetProjectOfficerWorkspaceAsync(userId, User, ct);
        return Page();
    }
}
