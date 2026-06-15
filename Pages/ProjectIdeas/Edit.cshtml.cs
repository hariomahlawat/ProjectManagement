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
    private readonly ProjectIdeaReadService _read; private readonly ProjectIdeaCommandService _commands; private readonly ProjectIdeaPermissionService _permissions; private readonly UserManager<ApplicationUser> _users;
    public EditModel(ProjectIdeaReadService read, ProjectIdeaCommandService commands, ProjectIdeaPermissionService permissions, UserManager<ApplicationUser> users) { _read = read; _commands = commands; _permissions = permissions; _users = users; }
    [BindProperty] public InputModel Input { get; set; } = new(); public SelectList UserOptions { get; private set; } = default!; public SelectList StatusOptions { get; } = new(ProjectIdeaStatuses.All.Select(x => new { Value = x, Text = ProjectIdeaStatuses.ToDisplay(x) }), "Value", "Text");
    public class InputModel { public int Id { get; set; } [Required, MaxLength(200)] public string Title { get; set; } = string.Empty; [Required, MaxLength(2000)] public string Description { get; set; } = string.Empty; public string? AssignedProjectOfficerUserId { get; set; } public string? AssignedHodUserId { get; set; } [Required] public string Status { get; set; } = ProjectIdeaStatuses.Active; }
    public async Task<IActionResult> OnGetAsync(int id) { var idea = await _read.GetDetailsAsync(id); if (idea is null) return NotFound(); if (!_permissions.CanEditIdea(User, idea)) return Forbid(); Input = new() { Id = idea.Id, Title = idea.Title, Description = idea.Description, AssignedProjectOfficerUserId = idea.AssignedProjectOfficerUserId, AssignedHodUserId = idea.AssignedHodUserId, Status = idea.Status }; await LoadUsersAsync(); return Page(); }
    public async Task<IActionResult> OnPostAsync() { var idea = await _read.GetDetailsAsync(Input.Id); if (idea is null) return NotFound(); if (!_permissions.CanEditIdea(User, idea)) return Forbid(); if (!ProjectIdeaStatuses.All.Contains(Input.Status)) ModelState.AddModelError("Input.Status", "Select a valid status."); if (!ModelState.IsValid) { await LoadUsersAsync(); return Page(); } idea.Title = Input.Title; idea.Description = Input.Description; idea.AssignedProjectOfficerUserId = Input.AssignedProjectOfficerUserId; idea.AssignedHodUserId = Input.AssignedHodUserId; idea.Status = Input.Status; if (Input.Status != ProjectIdeaStatuses.Archived) { idea.ArchivedAt = null; idea.ArchiveReason = null; } await _commands.UpdateAsync(idea); return RedirectToPage("Details", new { id = idea.Id }); }
    private async Task LoadUsersAsync() { var list = await _users.Users.OrderBy(u => u.FullName).Select(u => new { u.Id, Name = string.IsNullOrWhiteSpace(u.FullName) ? u.UserName : u.FullName }).ToListAsync(); UserOptions = new SelectList(list, "Id", "Name"); }
}
