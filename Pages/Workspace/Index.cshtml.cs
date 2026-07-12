using System.Text.Json;
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
    [BindProperty(SupportsGet = true)] public string View { get; set; } = "officers";
    [BindProperty(SupportsGet = true)] public List<int> ParentCategoryIds { get; set; } = new();
    [BindProperty(SupportsGet = true)] public string? ProjectSearch { get; set; }
    [BindProperty(SupportsGet = true)] public bool PopulatedStagesOnly { get; set; }

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
            View = string.Equals(View, "portfolio", StringComparison.OrdinalIgnoreCase)
                ? "portfolio"
                : "officers";
            if (View == "portfolio" && !Request.Query.ContainsKey(nameof(PopulatedStagesOnly)))
            {
                PopulatedStagesOnly = true;
            }
            CommandWorkspace = await _commandWorkspaceService.GetAsync(new CommandWorkspaceQuery
            {
                View = View,
                ParentCategoryIds = ParentCategoryIds,
                ProjectSearch = ProjectSearch,
                PopulatedStagesOnly = PopulatedStagesOnly,
                RequestingUserId = userId
            }, ct);
        }
        else
        {
            Workspace = await _projectOfficerWorkspaceService.GetProjectOfficerWorkspaceAsync(userId, User, ct);
        }

        return Page();
    }
    public async Task<IActionResult> OnPostSaveOfficerOrderAsync([FromBody] SaveOfficerOrderRequest request, CancellationToken ct)
    {
        if (!User.IsInRole(RoleNames.Comdt) && !User.IsInRole(RoleNames.HoD)) return Forbid();
        if (request.OfficerUserIds is null || request.OfficerUserIds.Count > 250) return BadRequest();

        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var validOfficerIds = (await _userManager.GetUsersInRoleAsync(RoleNames.ProjectOfficer))
            .Where(x => !x.IsDisabled && !x.PendingDeletion)
            .Select(x => x.Id)
            .ToHashSet(StringComparer.Ordinal);

        var normalized = request.OfficerUserIds
            .Where(id => !string.IsNullOrWhiteSpace(id) && validOfficerIds.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var currentUser = await _userManager.FindByIdAsync(userId);
        if (currentUser is null) return Challenge();
        currentUser.ComdtOfficerWorkloadOrderJson = JsonSerializer.Serialize(normalized);
        var result = await _userManager.UpdateAsync(currentUser);
        if (!result.Succeeded)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "The officer order could not be saved." });
        }

        return new JsonResult(new { saved = true });
    }

    public sealed class SaveOfficerOrderRequest
    {
        public List<string> OfficerUserIds { get; init; } = new();
    }

}
