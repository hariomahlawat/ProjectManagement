using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
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
public class CreateModel : PageModel
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly ProjectIdeaPermissionService _permissions;
    private readonly IProjectIdeaCommandService _commands;

    public CreateModel(UserManager<ApplicationUser> users, ProjectIdeaPermissionService permissions, IProjectIdeaCommandService commands)
    {
        _users = users;
        _permissions = permissions;
        _commands = commands;
    }

    // SECTION: Bound form state
    [BindProperty] public InputModel Input { get; set; } = new();
    [TempData] public string? StatusMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    // SECTION: Select lists
    public SelectList ProjectOfficerOptions { get; private set; } = default!;
    public SelectList HodOptions { get; private set; } = default!;
    public SelectList EditableStatusOptions { get; } = new(new[] { new { Value = ProjectIdeaStatuses.Active, Text = "Active" }, new { Value = ProjectIdeaStatuses.OnHold, Text = "On Hold" } }, "Value", "Text");

    public class InputModel
    {
        [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
        [Required, MaxLength(2000)] public string Description { get; set; } = string.Empty;
        public string? AssignedProjectOfficerUserId { get; set; }
        public string? AssignedHodUserId { get; set; }
        [Required] public string Status { get; set; } = ProjectIdeaStatuses.Active;
    }

    // SECTION: Page handlers
    public async Task<IActionResult> OnGetAsync()
    {
        if (!_permissions.CanCreateIdea(User)) return Forbid();
        await LoadUsersAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!_permissions.CanCreateIdea(User)) return Forbid();
        if (!IsEditableStatus(Input.Status)) ModelState.AddModelError("Input.Status", "Select Active or On Hold.");
        if (!ModelState.IsValid) { await LoadUsersAsync(); ErrorMessage = "Please correct the highlighted errors."; return Page(); }

        var idea = await _commands.CreateAsync(new ProjectIdea
        {
            Title = Input.Title.Trim(),
            Description = Input.Description.Trim(),
            Status = Input.Status,
            AssignedProjectOfficerUserId = Input.AssignedProjectOfficerUserId,
            AssignedHodUserId = Input.AssignedHodUserId,
            CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!
        });

        StatusMessage = "Idea created.";
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
