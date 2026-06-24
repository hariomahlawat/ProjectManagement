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
    private readonly ProjectOfficerWorkspaceService _projectOfficerWorkspaceService;
    private readonly CommandWorkspaceService _commandWorkspaceService;
    private readonly UserManager<ApplicationUser> _userManager;

    public ProjectOfficerWorkspaceVm Workspace { get; private set; } = new();
    public CommandWorkspaceVm CommandWorkspace { get; private set; } = new();
    public bool IsCommandMode { get; private set; }
    public bool CanSwitchWorkspace { get; private set; }

    [BindProperty(SupportsGet = true)] public string? Mode { get; set; }
    [BindProperty(SupportsGet = true)] public string View { get; set; } = "portfolio";
    [BindProperty(SupportsGet = true)] public List<int> ParentCategoryIds { get; set; } = new();
    [BindProperty(SupportsGet = true)] public string? ProjectSearch { get; set; }
    [BindProperty(SupportsGet = true)] public bool PopulatedStagesOnly { get; set; }
    [BindProperty(SupportsGet = true)] public string? OfficerSearch { get; set; }
    [BindProperty(SupportsGet = true)] public string? OfficerStageCode { get; set; }
    [BindProperty(SupportsGet = true)] public string OfficerWorkType { get; set; } = "all";

    public IndexModel(ProjectOfficerWorkspaceService projectOfficerWorkspaceService, CommandWorkspaceService commandWorkspaceService, UserManager<ApplicationUser> userManager)
    {
        _projectOfficerWorkspaceService = projectOfficerWorkspaceService;
        _commandWorkspaceService = commandWorkspaceService;
        _userManager = userManager;
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var hasCommandRole = User.IsInRole(RoleNames.Comdt) || User.IsInRole(RoleNames.HoD);
        var hasProjectOfficerRole = User.IsInRole(RoleNames.ProjectOfficer);
        if (!hasCommandRole && !hasProjectOfficerRole) return RedirectToPage("/Dashboard/Index");

        CanSwitchWorkspace = hasCommandRole && hasProjectOfficerRole;
        IsCommandMode = hasCommandRole && (!string.Equals(Mode, "project-officer", StringComparison.OrdinalIgnoreCase) || !hasProjectOfficerRole);

        if (IsCommandMode)
        {
            View = string.Equals(View, "officers", StringComparison.OrdinalIgnoreCase) ? "officers" : "portfolio";
            CommandWorkspace = await _commandWorkspaceService.GetAsync(new CommandWorkspaceQuery
            {
                View = View,
                ParentCategoryIds = ParentCategoryIds,
                ProjectSearch = ProjectSearch,
                PopulatedStagesOnly = PopulatedStagesOnly,
                OfficerSearch = OfficerSearch,
                OfficerStageCode = OfficerStageCode,
                OfficerWorkType = OfficerWorkType
            }, ct);
        }
        else
        {
            Workspace = await _projectOfficerWorkspaceService.GetProjectOfficerWorkspaceAsync(userId, User, ct);
        }

        return Page();
    }
}
