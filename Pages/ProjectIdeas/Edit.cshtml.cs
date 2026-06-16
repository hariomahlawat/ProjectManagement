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

    public EditModel(ProjectIdeaReadService read, ProjectIdeaCommandService commands, ProjectIdeaPermissionService permissions, UserManager<ApplicationUser> users)
    {
        _read = read;
        _commands = commands;
        _permissions = permissions;
        _users = users;
    }

    // SECTION: Bound form state
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public SelectList ProjectOfficerOptions { get; private set; } = default!;
    public SelectList HodOptions { get; private set; } = default!;

    public SelectList EditableStatusOptions { get; } = new(new[] { new { Value = ProjectIdeaStatuses.Active, Text = "Active" }, new { Value = ProjectIdeaStatuses.OnHold, Text = "On Hold" } }, "Value", "Text");

    [TempData] public string? StatusMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public class InputModel
    {
        public int Id { get; set; }

        [Required, MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required, MaxLength(2000)]
        public string Description { get; set; } = string.Empty;

        public string? AssignedProjectOfficerUserId { get; set; }

        public string? AssignedHodUserId { get; set; }

        [Required]
        public string Status { get; set; } = ProjectIdeaStatuses.Active;
    }

    // SECTION: Page handlers
    public async Task<IActionResult> OnGetAsync(int id)
    {
        var idea = await _read.GetDetailsAsync(id);
        if (idea is null) return NotFound();
        if (!_permissions.CanEditIdeaCore(User, idea)) return Forbid();

        Input = new()
        {
            Id = idea.Id,
            Title = idea.Title,
            Description = idea.Description,
            AssignedProjectOfficerUserId = idea.AssignedProjectOfficerUserId,
            AssignedHodUserId = idea.AssignedHodUserId,
            Status = idea.Status
        };

        await LoadUsersAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var idea = await _read.GetDetailsAsync(Input.Id);
        if (idea is null) return NotFound();
        if (!_permissions.CanEditIdeaCore(User, idea)) return Forbid();

        var requestedStatus = Input.Status;
        if (!IsEditableStatus(requestedStatus))
        {
            ModelState.AddModelError("Input.Status", "Select Active or On Hold. Use the archive action to archive an idea.");
        }

        if (!ModelState.IsValid)
        {
            await LoadUsersAsync();
            return Page();
        }

        idea.Title = Input.Title.Trim();
        idea.Description = Input.Description.Trim();
        idea.AssignedProjectOfficerUserId = Input.AssignedProjectOfficerUserId;
        idea.AssignedHodUserId = Input.AssignedHodUserId;
        idea.Status = requestedStatus;

        await _commands.UpdateAsync(idea);
        StatusMessage = "Idea updated.";
        return RedirectToPage("Details", new { id = idea.Id });
    }

    // SECTION: Lookup loading
    private async Task LoadUsersAsync()
    {
        ProjectOfficerOptions = BuildSelectList(await _users.GetUsersInRoleAsync(RoleNames.ProjectOfficer));
        HodOptions = BuildSelectList(await _users.GetUsersInRoleAsync(RoleNames.HoD));
    }

    private static SelectList BuildSelectList(IEnumerable<ApplicationUser> users)
    {
        return new SelectList(users.OrderBy(DisplayName).Select(u => new { u.Id, Name = DisplayName(u) }), "Id", "Name");
    }

    private static string DisplayName(ApplicationUser user) => string.IsNullOrWhiteSpace(user.FullName) ? user.UserName ?? user.Email ?? user.Id : user.FullName;
    private static bool IsEditableStatus(string status) => status == ProjectIdeaStatuses.Active || status == ProjectIdeaStatuses.OnHold;
}
