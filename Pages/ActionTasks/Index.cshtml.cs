using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Services.ActionTasks;

namespace ProjectManagement.Pages.ActionTasks;

[Authorize(Policy = "ActionTracker.Access")]
public class IndexModel : PageModel
{
    private readonly IActionTaskService _service;
    private readonly ActionTaskPermissionService _permission;
    private readonly UserManager<ApplicationUser> _users;

    public IndexModel(IActionTaskService service, ActionTaskPermissionService permission, UserManager<ApplicationUser> users)
    {
        _service = service;
        _permission = permission;
        _users = users;
    }

    public IReadOnlyList<ActionTaskItem> Tasks { get; private set; } = Array.Empty<ActionTaskItem>();
    public IReadOnlyList<ActionTaskAuditLog> SelectedTaskLogs { get; private set; } = Array.Empty<ActionTaskAuditLog>();
    public IReadOnlyList<UserOption> AssignableUsers { get; private set; } = Array.Empty<UserOption>();
    public IReadOnlyDictionary<string, string> TaskAssigneeNames { get; private set; } = new Dictionary<string, string>(StringComparer.Ordinal);

    [BindProperty(SupportsGet = true)]
    public string ViewMode { get; set; } = "Dashboard";

    [BindProperty(SupportsGet = true)]
    public int? TaskId { get; set; }

    public string CurrentRole { get; private set; } = string.Empty;
    public string CurrentUserId { get; private set; } = string.Empty;

    [BindProperty]
    public CreateTaskInput Input { get; set; } = new();

    // SECTION: UI state projections
    public bool CanCreate => _permission.CanCreate(CurrentRole);
    public bool CanClose => _permission.CanClose(CurrentRole);
    public IReadOnlyList<string> AssignmentRoles => ActionTaskRoleResolver.AllowedAssignmentRoles();
    public IReadOnlyList<string> AllowedStatusOptions => CanClose
        ? ActionTaskStatuses.All
        : new[] { ActionTaskStatuses.Assigned, ActionTaskStatuses.InProgress, ActionTaskStatuses.Blocked, ActionTaskStatuses.Submitted };

    public async Task OnGetAsync()
    {
        await LoadDataAsync();
    }

    // SECTION: Create action task
    public async Task<IActionResult> OnPostCreateAsync()
    {
        await ResolveIdentityAsync();

        if (!_permission.CanCreate(CurrentRole))
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            await LoadDataAsync();
            return Page();
        }

        var assignedUser = await _users.FindByIdAsync(Input.AssignedToUserId);
        if (assignedUser is null)
        {
            ModelState.AddModelError(string.Empty, "Assigned user was not found.");
            await LoadDataAsync();
            return Page();
        }

        var assignedRoles = await _users.GetRolesAsync(assignedUser);
        var assignedRole = ActionTaskRoleResolver.ResolveFromRoles(assignedRoles);
        if (assignedRole is null || !_permission.CanAssign(CurrentRole, assignedRole))
        {
            ModelState.AddModelError(string.Empty, "Selected user is not eligible for task assignment.");
            await LoadDataAsync();
            return Page();
        }

        await _service.CreateTaskAsync(new ActionTaskItem
        {
            Title = Input.Title.Trim(),
            Description = Input.Description.Trim(),
            CreatedByUserId = CurrentUserId,
            AssignedToUserId = Input.AssignedToUserId,
            CreatedByRole = CurrentRole,
            AssignedToRole = assignedRole,
            DueDate = Input.DueDate,
            Priority = Input.Priority
        });

        TempData["ToastMessage"] = "Task created.";
        return RedirectToPage(new { ViewMode });
    }

    // SECTION: Submit task for closure review
    public async Task<IActionResult> OnPostSubmitAsync(int id)
    {
        await ResolveIdentityAsync();
        await _service.SubmitTaskAsync(id, CurrentUserId, CurrentRole, "Submitted by assignee/workflow actor.");
        TempData["ToastMessage"] = "Task submitted.";
        return RedirectToPage(new { ViewMode, TaskId = id });
    }

    // SECTION: Close task by command role
    public async Task<IActionResult> OnPostCloseAsync(int id)
    {
        await ResolveIdentityAsync();
        await _service.CloseTaskAsync(id, CurrentUserId, CurrentRole, "Closed by command authority.");
        TempData["ToastMessage"] = "Task closed.";
        return RedirectToPage(new { ViewMode, TaskId = id });
    }

    // SECTION: Update in-flight status
    public async Task<IActionResult> OnPostUpdateStatusAsync(int id, string status)
    {
        await ResolveIdentityAsync();
        await _service.UpdateStatusAsync(id, status, CurrentUserId, CurrentRole);
        TempData["ToastMessage"] = "Task status updated.";
        return RedirectToPage(new { ViewMode, TaskId = id });
    }

    // SECTION: Shared data loading
    private async Task LoadDataAsync()
    {
        await ResolveIdentityAsync();

        var tasks = await _service.GetTasksAsync(CurrentUserId, CurrentRole);
        Tasks = IsMyTasksView()
            ? tasks.Where(t => string.Equals(t.AssignedToUserId, CurrentUserId, StringComparison.Ordinal)).ToList()
            : tasks;

        AssignableUsers = await LoadAssignableUsersAsync();
        TaskAssigneeNames = await LoadTaskAssigneeNamesAsync(Tasks);

        if (TaskId.HasValue)
        {
            SelectedTaskLogs = await _service.GetTaskLogsAsync(TaskId.Value, CurrentUserId, CurrentRole);
        }
    }

    private async Task ResolveIdentityAsync()
    {
        CurrentUserId = _users.GetUserId(User) ?? string.Empty;
        CurrentRole = ActionTaskRoleResolver.Resolve(User) ?? string.Empty;

        await Task.CompletedTask;
    }

    private async Task<IReadOnlyList<UserOption>> LoadAssignableUsersAsync()
    {
        // SECTION: Stabilize user snapshot to avoid overlapping data-reader operations
        var targets = new HashSet<string>(AssignmentRoles, StringComparer.OrdinalIgnoreCase);
        var users = await _users.Users
            .OrderBy(x => x.UserName)
            .Take(200)
            .ToListAsync();

        // SECTION: Resolve assignable users with role checks
        var list = new List<UserOption>();
        foreach (var user in users)
        {
            var roles = await _users.GetRolesAsync(user);
            var matchedRole = roles.FirstOrDefault(targets.Contains);
            if (matchedRole is null)
            {
                continue;
            }

            list.Add(new UserOption(user.Id, user.UserName ?? user.Email ?? "User", matchedRole));
        }

        return list;
    }

    // SECTION: Resolve assignee display names for task register rendering
    private async Task<IReadOnlyDictionary<string, string>> LoadTaskAssigneeNamesAsync(IReadOnlyList<ActionTaskItem> tasks)
    {
        var userIds = tasks
            .Select(t => t.AssignedToUserId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (userIds.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var users = await _users.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName, u.Email })
            .ToListAsync();

        return users.ToDictionary(
            u => u.Id,
            u => string.IsNullOrWhiteSpace(u.UserName) ? (u.Email ?? "User") : u.UserName!,
            StringComparer.Ordinal);
    }

    // SECTION: Normalize and evaluate selected view mode
    private bool IsMyTasksView() =>
        string.Equals((ViewMode ?? string.Empty).Trim(), "MyTasks", StringComparison.OrdinalIgnoreCase);

    public sealed class CreateTaskInput
    {
        [Required, StringLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required, StringLength(4000)]
        public string Description { get; set; } = string.Empty;

        [Required]
        public string AssignedToUserId { get; set; } = string.Empty;

        [Required]
        public DateTime DueDate { get; set; } = DateTime.UtcNow.Date.AddDays(7);

        [Required, StringLength(24)]
        public string Priority { get; set; } = "Normal";
    }

    public sealed record UserOption(string UserId, string DisplayName, string Role);
}
