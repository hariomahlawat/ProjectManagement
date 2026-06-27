using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectIdeas;
using ProjectManagement.Services.ProjectIdeas;

namespace ProjectManagement.Pages.ProjectIdeas;

[Authorize]
public class EditModel : PageModel
{
    private readonly ProjectIdeaReadService _read;
    private readonly ProjectIdeaCommandService _commands;
    private readonly ProjectIdeaPermissionService _permissions;
    private readonly UserManager<ApplicationUser> _users;

    public EditModel(
        ProjectIdeaReadService read,
        ProjectIdeaCommandService commands,
        ProjectIdeaPermissionService permissions,
        UserManager<ApplicationUser> users)
    {
        _read = read;
        _commands = commands;
        _permissions = permissions;
        _users = users;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool CanManageCore { get; private set; }
    public string CurrentTitle { get; private set; } = string.Empty;
    public string CurrentStatus { get; private set; } = string.Empty;
    public string? CurrentProjectOfficerName { get; private set; }
    public string? CurrentHodName { get; private set; }

    public SelectList ProjectOfficerOptions { get; private set; } = default!;
    public SelectList HodOptions { get; private set; } = default!;

    public SelectList EditableStatusOptions { get; } = new(
        new[]
        {
            new { Value = ProjectIdeaStatuses.Active, Text = "Active" },
            new { Value = ProjectIdeaStatuses.OnHold, Text = "On Hold" }
        },
        "Value",
        "Text");

    [TempData] public string? StatusMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public class InputModel
    {
        public int Id { get; set; }

        [MaxLength(200)]
        public string? Title { get; set; }

        [Required, MaxLength(2000)]
        public string Description { get; set; } = string.Empty;

        public string? AssignedProjectOfficerUserId { get; set; }
        public string? AssignedHodUserId { get; set; }
        public string? Status { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var idea = await _read.GetDetailsAsync(id);
        if (idea is null) return NotFound();
        if (!_permissions.CanEditDescription(User, idea)) return Forbid();

        SetPageState(idea);
        Input = new InputModel
        {
            Id = idea.Id,
            Title = idea.Title,
            Description = idea.Description,
            AssignedProjectOfficerUserId = idea.AssignedProjectOfficerUserId,
            AssignedHodUserId = idea.AssignedHodUserId,
            Status = idea.Status
        };

        await LoadUsersIfRequiredAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var idea = await _read.GetDetailsAsync(Input.Id);
        if (idea is null) return NotFound();
        if (!_permissions.CanEditDescription(User, idea)) return Forbid();

        SetPageState(idea);

        if (CanManageCore)
        {
            ValidateCoreFields();
        }
        else
        {
            // Project Officers may update only the description. Never trust posted
            // values for title, ownership or status from a restricted editor.
            ModelState.Remove("Input.Title");
            ModelState.Remove("Input.AssignedProjectOfficerUserId");
            ModelState.Remove("Input.AssignedHodUserId");
            ModelState.Remove("Input.Status");
        }

        if (!ModelState.IsValid)
        {
            await LoadUsersIfRequiredAsync();
            return Page();
        }

        idea.Description = Input.Description.Trim();

        if (CanManageCore)
        {
            idea.Title = Input.Title!.Trim();
            idea.AssignedProjectOfficerUserId = Input.AssignedProjectOfficerUserId;
            idea.AssignedHodUserId = Input.AssignedHodUserId;
            idea.Status = Input.Status!;
        }

        await _commands.UpdateAsync(idea);
        StatusMessage = CanManageCore ? "Idea updated." : "Idea description updated.";
        return RedirectToPage("Details", new { id = idea.Id });
    }

    private void ValidateCoreFields()
    {
        if (string.IsNullOrWhiteSpace(Input.Title))
        {
            ModelState.AddModelError("Input.Title", "Title is required.");
        }

        if (string.IsNullOrWhiteSpace(Input.Status) || !IsEditableStatus(Input.Status))
        {
            ModelState.AddModelError(
                "Input.Status",
                "Select Active or On Hold. Use the archive action to archive an idea.");
        }
    }

    private void SetPageState(ProjectIdea idea)
    {
        CanManageCore = _permissions.CanEditIdeaCore(User, idea);
        CurrentTitle = idea.Title;
        CurrentStatus = ProjectIdeaStatuses.ToDisplay(idea.Status);
        CurrentProjectOfficerName = DisplayNameOrNull(idea.AssignedProjectOfficerUser);
        CurrentHodName = DisplayNameOrNull(idea.AssignedHodUser);
    }

    private async Task LoadUsersIfRequiredAsync()
    {
        if (!CanManageCore)
        {
            ProjectOfficerOptions = new SelectList(Array.Empty<object>());
            HodOptions = new SelectList(Array.Empty<object>());
            return;
        }

        ProjectOfficerOptions = BuildSelectList(await _users.GetUsersInRoleAsync(RoleNames.ProjectOfficer));
        HodOptions = BuildSelectList(await _users.GetUsersInRoleAsync(RoleNames.HoD));
    }

    private static SelectList BuildSelectList(IEnumerable<ApplicationUser> users)
    {
        return new SelectList(
            users.OrderBy(DisplayName).Select(u => new { u.Id, Name = DisplayName(u) }),
            "Id",
            "Name");
    }

    private static string DisplayName(ApplicationUser user) =>
        string.IsNullOrWhiteSpace(user.FullName)
            ? user.UserName ?? user.Email ?? user.Id
            : user.FullName;

    private static string? DisplayNameOrNull(ApplicationUser? user) =>
        user is null ? null : DisplayName(user);

    private static bool IsEditableStatus(string status) =>
        status == ProjectIdeaStatuses.Active || status == ProjectIdeaStatuses.OnHold;
}
