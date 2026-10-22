using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Services.Workspace;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Pages.Workspace;

/// <summary>
/// Read-only Command desk for the HoD and the Comdt. The Dashboard stays their landing page;
/// this page sits under Command oversight in the workspace.
/// </summary>
[Authorize(Roles = RoleNames.Comdt + "," + RoleNames.HoD)]
public sealed class CommandDeskModel : PageModel
{
    private readonly CommandDeskService _desk;
    private readonly CommandWorkspaceService _commandWorkspaceService;
    private readonly ProjectOfficerWorkspaceService _projectOfficerWorkspaceService;
    private readonly UserManager<ApplicationUser> _users;

    public CommandDeskModel(
        CommandDeskService desk,
        CommandWorkspaceService commandWorkspaceService,
        ProjectOfficerWorkspaceService projectOfficerWorkspaceService,
        UserManager<ApplicationUser> users)
    {
        _desk = desk ?? throw new ArgumentNullException(nameof(desk));
        _commandWorkspaceService = commandWorkspaceService ?? throw new ArgumentNullException(nameof(commandWorkspaceService));
        _projectOfficerWorkspaceService = projectOfficerWorkspaceService ?? throw new ArgumentNullException(nameof(projectOfficerWorkspaceService));
        _users = users ?? throw new ArgumentNullException(nameof(users));
    }

    [BindProperty(SupportsGet = true)] public int? Window { get; set; }

    public CommandDeskVm Desk { get; private set; } = new();
    public CommandWorkspaceRailVm CommandRail { get; private set; } = new() { HasCommandAccess = true, ActiveLens = "command", ActiveItem = "command-desk" };

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var userId = _users.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        Desk = await _desk.GetAsync(User, userId, Window, ct);

        var navigation = await _commandWorkspaceService.GetNavigationShellAsync("desk", ct);
        var hasProjectOfficerAccess = User.IsInRole(RoleNames.ProjectOfficer);
        var personal = hasProjectOfficerAccess
            ? await _projectOfficerWorkspaceService.GetProjectOfficerWorkspaceAsync(
                userId,
                User,
                ProjectOfficerWorkspaceView.Conference,
                includeDocuments: false,
                ct: ct)
            : new ProjectOfficerWorkspaceVm();

        CommandRail = new CommandWorkspaceRailVm
        {
            HasCommandAccess = true,
            HasProjectOfficerAccess = hasProjectOfficerAccess,
            CanViewDocuments = hasProjectOfficerAccess,
            ActiveLens = "command",
            ActiveItem = "command-desk",
            ProjectOfficerCount = navigation.ProjectOfficerCount,
            TotalOngoingProjects = navigation.TotalOngoingProjects,
            ActionQueueCount = personal.ActionQueueBadgeCount,
            AssignedProjectCount = personal.AssignedProjectCount,
            AssignedTaskCount = personal.OfficialTaskCount,
            AssignedIdeaCount = personal.AssignedIdeaCount,
            FollowUpCount = personal.FollowUpCount,
            AotsUnreadCount = personal.AotsUnreadCount
        };

        return Page();
    }
}
