using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
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

    public SelectList UserOptions { get; private set; } = default!;

    public SelectList StatusOptions { get; } = new(ProjectIdeaStatuses.All.Select(x => new { Value = x, Text = ProjectIdeaStatuses.ToDisplay(x) }), "Value", "Text");

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
        if (!_permissions.CanEditIdea(User, idea)) return Forbid();

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
        if (!_permissions.CanEditIdea(User, idea)) return Forbid();

        var requestedStatus = Input.Status;
        var statusChanged = !string.Equals(idea.Status, requestedStatus, StringComparison.OrdinalIgnoreCase);
        var archiveStateChanged = statusChanged && (string.Equals(idea.Status, ProjectIdeaStatuses.Archived, StringComparison.OrdinalIgnoreCase) || string.Equals(requestedStatus, ProjectIdeaStatuses.Archived, StringComparison.OrdinalIgnoreCase));

        if (!ProjectIdeaStatuses.All.Contains(requestedStatus))
        {
            ModelState.AddModelError("Input.Status", "Select a valid status.");
        }

        if (archiveStateChanged && !_permissions.CanArchiveIdea(User))
        {
            ModelState.AddModelError("Input.Status", "Only Admin, HoD, or Comdt users can archive or restore project ideas.");
        }

        if (!ModelState.IsValid)
        {
            await LoadUsersAsync();
            return Page();
        }

        idea.Title = Input.Title;
        idea.Description = Input.Description;
        idea.AssignedProjectOfficerUserId = Input.AssignedProjectOfficerUserId;
        idea.AssignedHodUserId = Input.AssignedHodUserId;
        idea.Status = requestedStatus;

        if (statusChanged && requestedStatus == ProjectIdeaStatuses.Archived)
        {
            idea.ArchivedAt = DateTime.UtcNow;
        }
        else if (requestedStatus != ProjectIdeaStatuses.Archived)
        {
            idea.ArchivedAt = null;
            idea.ArchiveReason = null;
        }

        await _commands.UpdateAsync(idea);
        return RedirectToPage("Details", new { id = idea.Id });
    }

    // SECTION: Lookup loading
    private async Task LoadUsersAsync()
    {
        var list = await _users.Users.OrderBy(u => u.FullName).Select(u => new { u.Id, Name = string.IsNullOrWhiteSpace(u.FullName) ? u.UserName : u.FullName }).ToListAsync();
        UserOptions = new SelectList(list, "Id", "Name");
    }
}
