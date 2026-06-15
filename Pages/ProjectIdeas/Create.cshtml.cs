using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectIdeas;
using ProjectManagement.Services.ProjectIdeas;

namespace ProjectManagement.Pages.ProjectIdeas;

[Authorize]
public class CreateModel : PageModel
{
    private readonly UserManager<ApplicationUser> _users; private readonly ProjectIdeaPermissionService _permissions; private readonly ProjectIdeaCommandService _commands;
    public CreateModel(UserManager<ApplicationUser> users, ProjectIdeaPermissionService permissions, ProjectIdeaCommandService commands) { _users = users; _permissions = permissions; _commands = commands; }
    [BindProperty] public InputModel Input { get; set; } = new();
    public SelectList UserOptions { get; private set; } = default!;
    public SelectList StatusOptions { get; } = new(ProjectIdeaStatuses.All.Select(x => new { Value = x, Text = ProjectIdeaStatuses.ToDisplay(x) }), "Value", "Text");
    public class InputModel { [Required, MaxLength(200)] public string Title { get; set; } = string.Empty; [Required, MaxLength(2000)] public string Description { get; set; } = string.Empty; public string? AssignedProjectOfficerUserId { get; set; } public string? AssignedHodUserId { get; set; } [Required] public string Status { get; set; } = ProjectIdeaStatuses.Active; }
    public async Task<IActionResult> OnGetAsync() { if (!_permissions.CanCreateIdea(User)) return Forbid(); await LoadUsersAsync(); return Page(); }
    public async Task<IActionResult> OnPostAsync() { if (!_permissions.CanCreateIdea(User)) return Forbid(); if (!ProjectIdeaStatuses.All.Contains(Input.Status)) ModelState.AddModelError("Input.Status", "Select a valid status."); if (!ModelState.IsValid) { await LoadUsersAsync(); return Page(); } var idea = await _commands.CreateAsync(new ProjectIdea { Title = Input.Title, Description = Input.Description, Status = Input.Status, AssignedProjectOfficerUserId = Input.AssignedProjectOfficerUserId, AssignedHodUserId = Input.AssignedHodUserId, CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)! }); return RedirectToPage("Details", new { id = idea.Id }); }
    private async Task LoadUsersAsync() { var list = await _users.Users.OrderBy(u => u.FullName).Select(u => new { u.Id, Name = string.IsNullOrWhiteSpace(u.FullName) ? u.UserName : u.FullName }).ToListAsync(); UserOptions = new SelectList(list, "Id", "Name"); }
}
