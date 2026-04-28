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
    public IReadOnlyList<ActionTaskItem> CriticalOpenTasks { get; private set; } = Array.Empty<ActionTaskItem>();
    public IReadOnlyList<ActionTaskItem> OverdueTasks { get; private set; } = Array.Empty<ActionTaskItem>();
    public IReadOnlyList<ActionTaskItem> RecentlySubmittedTasks { get; private set; } = Array.Empty<ActionTaskItem>();
    public IReadOnlyList<ActionTaskItem> RecentlyUpdatedTasks { get; private set; } = Array.Empty<ActionTaskItem>();
    public IReadOnlyList<ActionTaskItem> KanbanAssignedTasks { get; private set; } = Array.Empty<ActionTaskItem>();
    public IReadOnlyList<ActionTaskItem> KanbanInProgressTasks { get; private set; } = Array.Empty<ActionTaskItem>();
    public IReadOnlyList<ActionTaskItem> KanbanBlockedTasks { get; private set; } = Array.Empty<ActionTaskItem>();
    public IReadOnlyList<ActionTaskItem> KanbanSubmittedTasks { get; private set; } = Array.Empty<ActionTaskItem>();
    public IReadOnlyList<ActionTaskItem> KanbanClosedTasks { get; private set; } = Array.Empty<ActionTaskItem>();
    public IReadOnlyList<ActionTaskItem> DueTodayTasks { get; private set; } = Array.Empty<ActionTaskItem>();
    public IReadOnlyList<ActionTaskItem> DueThisWeekTasks { get; private set; } = Array.Empty<ActionTaskItem>();
    public IReadOnlyList<ActionTaskItem> DueLaterTasks { get; private set; } = Array.Empty<ActionTaskItem>();
    public IReadOnlyList<ActionTaskItem> SprintOverdueTasks { get; private set; } = Array.Empty<ActionTaskItem>();
    public IReadOnlyList<ActionTaskAuditLog> SelectedTaskLogs { get; private set; } = Array.Empty<ActionTaskAuditLog>();
    public IReadOnlyList<UserOption> AssignableUsers { get; private set; } = Array.Empty<UserOption>();
    public IReadOnlyDictionary<string, string> TaskAssigneeNames { get; private set; } = new Dictionary<string, string>(StringComparer.Ordinal);
    public IReadOnlyList<CountSummary> AssigneePendingCounts { get; private set; } = Array.Empty<CountSummary>();
    public IReadOnlyList<CountSummary> PriorityCounts { get; private set; } = Array.Empty<CountSummary>();
    public IReadOnlyList<CountSummary> StatusCounts { get; private set; } = Array.Empty<CountSummary>();

    [BindProperty(SupportsGet = true)]
    public string? ViewMode { get; set; } = "Dashboard";

    [BindProperty(SupportsGet = true)]
    public int? TaskId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? FilterStatus { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? FilterPriority { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? FilterAssigneeUserId { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? FilterDueDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? FilterSearch { get; set; }

    public string CurrentRole { get; private set; } = string.Empty;
    public string CurrentUserId { get; private set; } = string.Empty;
    public bool ShowCreatePanel { get; private set; }
    public ActionTaskItem? SelectedTask { get; private set; }

    [BindProperty]
    public CreateTaskInput Input { get; set; } = new();

    // SECTION: UI state projections
    public bool CanCreate => _permission.CanCreate(CurrentRole);
    public bool CanClose => _permission.CanClose(CurrentRole);
    public string ResolvedViewMode => ResolveViewMode();
    public bool IsDashboardView => string.Equals(ResolvedViewMode, "Dashboard", StringComparison.OrdinalIgnoreCase);
    public bool IsMyTasksView => string.Equals(ResolvedViewMode, "MyTasks", StringComparison.OrdinalIgnoreCase);
    public bool IsTaskListView => string.Equals(ResolvedViewMode, "TaskList", StringComparison.OrdinalIgnoreCase);
    public bool IsKanbanView => string.Equals(ResolvedViewMode, "Kanban", StringComparison.OrdinalIgnoreCase);
    public bool IsSprintBoardView => string.Equals(ResolvedViewMode, "Sprint", StringComparison.OrdinalIgnoreCase);
    public bool IsReportsView => string.Equals(ResolvedViewMode, "Reports", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<string> AssignmentRoles => ActionTaskRoleResolver.AllowedAssignmentRoles();
    public IReadOnlyList<string> AllowedStatusOptions => new[]
    {
        ActionTaskStatuses.Assigned,
        ActionTaskStatuses.InProgress,
        ActionTaskStatuses.Blocked,
        ActionTaskStatuses.Submitted
    };

    // SECTION: Per-task action visibility helpers
    public bool CanSubmitTask(ActionTaskItem task)
    {
        return !string.Equals(task.Status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(task.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase)
            && string.Equals(task.AssignedToUserId, CurrentUserId, StringComparison.Ordinal);
    }

    public bool CanCloseTask(ActionTaskItem task)
    {
        return CanClose
            && string.Equals(task.Status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase);
    }

    public bool CanUpdateTaskStatus(ActionTaskItem task)
    {
        return !string.Equals(task.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase)
            && (_permission.CanViewAll(CurrentRole) || string.Equals(task.AssignedToUserId, CurrentUserId, StringComparison.Ordinal));
    }

    public string GetStatusBadgeClass(string status)
    {
        if (string.Equals(status, ActionTaskStatuses.InProgress, StringComparison.OrdinalIgnoreCase))
        {
            return "at-badge at-badge-status-progress";
        }

        if (string.Equals(status, ActionTaskStatuses.Blocked, StringComparison.OrdinalIgnoreCase))
        {
            return "at-badge at-badge-status-blocked";
        }

        if (string.Equals(status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase))
        {
            return "at-badge at-badge-status-submitted";
        }

        if (string.Equals(status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase))
        {
            return "at-badge at-badge-status-closed";
        }

        return "at-badge at-badge-status-assigned";
    }

    public string GetPriorityBadgeClass(string priority)
    {
        if (string.Equals(priority, "Critical", StringComparison.OrdinalIgnoreCase))
        {
            return "at-badge at-badge-priority-critical";
        }

        if (string.Equals(priority, "High", StringComparison.OrdinalIgnoreCase))
        {
            return "at-badge at-badge-priority-high";
        }

        return "at-badge at-badge-priority-normal";
    }

    public string ResolveAssigneeName(string assignedToUserId)
    {
        return TaskAssigneeNames.TryGetValue(assignedToUserId, out var assigneeName)
            ? assigneeName
            : "User";
    }

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
            ShowCreatePanel = true;
            await LoadDataAsync();
            return Page();
        }

        var assignedUser = await _users.FindByIdAsync(Input.AssignedToUserId);
        if (assignedUser is null)
        {
            ModelState.AddModelError(string.Empty, "Assigned user was not found.");
            ShowCreatePanel = true;
            await LoadDataAsync();
            return Page();
        }

        var assignedRoles = await _users.GetRolesAsync(assignedUser);
        var assignedRole = ActionTaskRoleResolver.ResolveAssignableRoleFromRoles(assignedRoles);
        if (assignedRole is null)
        {
            ModelState.AddModelError(string.Empty, "Selected user does not have an assignable Task Tracker role.");
            ShowCreatePanel = true;
            await LoadDataAsync();
            return Page();
        }

        if (!_permission.CanAssign(CurrentRole, assignedRole))
        {
            ModelState.AddModelError(string.Empty, $"Current role is not permitted to assign tasks to {assignedRole}.");
            ShowCreatePanel = true;
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
        return RedirectToPage("/ActionTasks/Index", new { viewMode = ResolveViewMode() });
    }

    // SECTION: Submit task for closure review
    public async Task<IActionResult> OnPostSubmitAsync(int id)
    {
        await ResolveIdentityAsync();
        try
        {
            await _service.SubmitTaskAsync(id, CurrentUserId, CurrentRole, "Submitted by assignee/workflow actor.");
            TempData["ToastMessage"] = "Task submitted.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ToastError"] = ex.Message;
        }

        return RedirectToPage(new { ViewMode = ResolveViewMode(), TaskId = id });
    }

    // SECTION: Close task by command role
    public async Task<IActionResult> OnPostCloseAsync(int id)
    {
        await ResolveIdentityAsync();
        try
        {
            await _service.CloseTaskAsync(id, CurrentUserId, CurrentRole, "Closed by command authority.");
            TempData["ToastMessage"] = "Task closed.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ToastError"] = ex.Message;
        }

        return RedirectToPage(new { ViewMode = ResolveViewMode(), TaskId = id });
    }

    // SECTION: Update in-flight status
    public async Task<IActionResult> OnPostUpdateStatusAsync(int id, string status)
    {
        await ResolveIdentityAsync();
        try
        {
            await _service.UpdateStatusAsync(id, status, CurrentUserId, CurrentRole);
            TempData["ToastMessage"] = "Task status updated.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ToastError"] = ex.Message;
        }

        return RedirectToPage(new { ViewMode = ResolveViewMode(), TaskId = id });
    }

    // SECTION: Shared data loading
    private async Task LoadDataAsync()
    {
        await ResolveIdentityAsync();

        var tasks = await _service.GetTasksAsync(CurrentUserId, CurrentRole);

        // SECTION: Apply view-specific filtering
        if (IsMyTasksView)
        {
            tasks = tasks
                .Where(t => string.Equals(t.AssignedToUserId, CurrentUserId, StringComparison.Ordinal))
                .ToList();
        }

        AssignableUsers = await LoadAssignableUsersAsync();
        TaskAssigneeNames = await LoadTaskAssigneeNamesAsync(tasks);

        // SECTION: Populate overview and grouping collections
        BuildDashboardCollections(tasks);
        BuildKanbanCollections(tasks);
        BuildSprintCollections(tasks);
        BuildReportCollections(tasks);

        // SECTION: Apply task-list filters only in task list mode
        Tasks = IsTaskListView
            ? ApplyTaskListFilters(tasks)
            : tasks;

        if (TaskId.HasValue)
        {
            SelectedTask = tasks.FirstOrDefault(t => t.Id == TaskId.Value);
            SelectedTaskLogs = await _service.GetTaskLogsAsync(TaskId.Value, CurrentUserId, CurrentRole);
        }
    }

    private IReadOnlyList<ActionTaskItem> ApplyTaskListFilters(IReadOnlyList<ActionTaskItem> tasks)
    {
        var query = tasks.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(FilterStatus))
        {
            query = query.Where(t => string.Equals(t.Status, FilterStatus, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(FilterPriority))
        {
            query = query.Where(t => string.Equals(t.Priority, FilterPriority, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(FilterAssigneeUserId))
        {
            query = query.Where(t => string.Equals(t.AssignedToUserId, FilterAssigneeUserId, StringComparison.Ordinal));
        }

        if (FilterDueDate.HasValue)
        {
            var dueDate = FilterDueDate.Value.Date;
            query = query.Where(t => t.DueDate.Date == dueDate);
        }

        if (!string.IsNullOrWhiteSpace(FilterSearch))
        {
            var search = FilterSearch.Trim();
            query = query.Where(t => t.Title.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        return query.ToList();
    }

    private void BuildDashboardCollections(IReadOnlyList<ActionTaskItem> tasks)
    {
        var utcNow = DateTime.UtcNow;

        CriticalOpenTasks = tasks
            .Where(t => string.Equals(t.Priority, "Critical", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(t.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.DueDate)
            .Take(5)
            .ToList();

        OverdueTasks = tasks
            .Where(t => !string.Equals(t.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase)
                        && t.DueDate.Date < utcNow.Date)
            .OrderBy(t => t.DueDate)
            .Take(5)
            .ToList();

        RecentlySubmittedTasks = tasks
            .Where(t => string.Equals(t.Status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(t => t.SubmittedOn ?? DateTime.MinValue)
            .Take(5)
            .ToList();

        RecentlyUpdatedTasks = tasks
            .OrderByDescending(t => t.SubmittedOn ?? t.AssignedOn)
            .Take(5)
            .ToList();
    }

    private void BuildKanbanCollections(IReadOnlyList<ActionTaskItem> tasks)
    {
        KanbanAssignedTasks = tasks.Where(t => string.Equals(t.Status, ActionTaskStatuses.Assigned, StringComparison.OrdinalIgnoreCase)).ToList();
        KanbanInProgressTasks = tasks.Where(t => string.Equals(t.Status, ActionTaskStatuses.InProgress, StringComparison.OrdinalIgnoreCase)).ToList();
        KanbanBlockedTasks = tasks.Where(t => string.Equals(t.Status, ActionTaskStatuses.Blocked, StringComparison.OrdinalIgnoreCase)).ToList();
        KanbanSubmittedTasks = tasks.Where(t => string.Equals(t.Status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase)).ToList();
        KanbanClosedTasks = tasks.Where(t => string.Equals(t.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void BuildSprintCollections(IReadOnlyList<ActionTaskItem> tasks)
    {
        var utcToday = DateTime.UtcNow.Date;
        var endOfWeek = utcToday.AddDays(7);

        SprintOverdueTasks = tasks
            .Where(t => !string.Equals(t.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase)
                        && t.DueDate.Date < utcToday)
            .OrderBy(t => t.DueDate)
            .ToList();

        DueTodayTasks = tasks
            .Where(t => !string.Equals(t.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase)
                        && t.DueDate.Date == utcToday)
            .OrderBy(t => t.DueDate)
            .ToList();

        DueThisWeekTasks = tasks
            .Where(t => !string.Equals(t.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase)
                        && t.DueDate.Date > utcToday
                        && t.DueDate.Date <= endOfWeek)
            .OrderBy(t => t.DueDate)
            .ToList();

        DueLaterTasks = tasks
            .Where(t => !string.Equals(t.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase)
                        && t.DueDate.Date > endOfWeek)
            .OrderBy(t => t.DueDate)
            .ToList();
    }

    private void BuildReportCollections(IReadOnlyList<ActionTaskItem> tasks)
    {
        AssigneePendingCounts = tasks
            .Where(t => !string.Equals(t.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase))
            .GroupBy(t => ResolveAssigneeName(t.AssignedToUserId))
            .OrderByDescending(group => group.Count())
            .Select(group => new CountSummary(group.Key, group.Count()))
            .ToList();

        PriorityCounts = tasks
            .GroupBy(t => t.Priority)
            .OrderByDescending(group => group.Count())
            .Select(group => new CountSummary(group.Key, group.Count()))
            .ToList();

        StatusCounts = tasks
            .GroupBy(t => t.Status)
            .OrderByDescending(group => group.Count())
            .Select(group => new CountSummary(group.Key, group.Count()))
            .ToList();
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
        var users = await _users.Users
            .OrderBy(x => x.UserName)
            .Take(200)
            .ToListAsync();

        // SECTION: Resolve assignable users with role checks
        var list = new List<UserOption>();
        foreach (var user in users)
        {
            var roles = await _users.GetRolesAsync(user);
            var matchedRole = ActionTaskRoleResolver.ResolveAssignableRoleFromRoles(roles);
            if (matchedRole is null || !_permission.CanAssign(CurrentRole, matchedRole))
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

    // SECTION: Resolve a safe, standardized view mode value for postback and redirects
    private string ResolveViewMode()
    {
        var normalized = (ViewMode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "Dashboard";
        }

        // SECTION: Normalize known aliases first so legacy links remain valid.
        if (string.Equals(normalized, "SprintBoard", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "Sprint";
        }
        else if (string.Equals(normalized, "My Tasks", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "MyTasks";
        }

        // SECTION: Explicitly whitelist supported view modes and default unsupported inputs.
        return normalized switch
        {
            _ when string.Equals(normalized, "Dashboard", StringComparison.OrdinalIgnoreCase) => "Dashboard",
            _ when string.Equals(normalized, "MyTasks", StringComparison.OrdinalIgnoreCase) => "MyTasks",
            _ when string.Equals(normalized, "TaskList", StringComparison.OrdinalIgnoreCase) => "TaskList",
            _ when string.Equals(normalized, "Kanban", StringComparison.OrdinalIgnoreCase) => "Kanban",
            _ when string.Equals(normalized, "Sprint", StringComparison.OrdinalIgnoreCase) => "Sprint",
            _ when string.Equals(normalized, "Reports", StringComparison.OrdinalIgnoreCase) => "Reports",
            _ => "Dashboard"
        };
    }

    public sealed class CreateTaskInput
    {
        [Display(Name = "Task Title")]
        [Required, StringLength(200)]
        public string Title { get; set; } = string.Empty;

        [Display(Name = "Description / Instructions")]
        [Required, StringLength(4000)]
        public string Description { get; set; } = string.Empty;

        [Required]
        public string AssignedToUserId { get; set; } = string.Empty;

        [Display(Name = "Due Date")]
        [Required]
        public DateTime DueDate { get; set; } = DateTime.UtcNow.Date.AddDays(7);

        [Required, StringLength(24)]
        public string Priority { get; set; } = "Normal";
    }

    public sealed record UserOption(string UserId, string DisplayName, string Role);
    public sealed record CountSummary(string Name, int Count);
}
