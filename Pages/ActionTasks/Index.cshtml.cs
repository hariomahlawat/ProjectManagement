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
    public IReadOnlyDictionary<string, string> TaskActorNames { get; private set; } = new Dictionary<string, string>(StringComparer.Ordinal);
    public IReadOnlyList<CountSummary> AssigneePendingCounts { get; private set; } = Array.Empty<CountSummary>();
    public IReadOnlyList<CountSummary> PriorityCounts { get; private set; } = Array.Empty<CountSummary>();
    public IReadOnlyList<CountSummary> StatusCounts { get; private set; } = Array.Empty<CountSummary>();
    public IReadOnlyList<CountSummary> OpenAgeingBuckets { get; private set; } = Array.Empty<CountSummary>();
    public IReadOnlyList<CountSummary> OverdueAgeingBuckets { get; private set; } = Array.Empty<CountSummary>();

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
    public bool ShowCreateModal { get; private set; }
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
    public string PageHeading => ResolvedViewMode switch
    {
        "MyTasks" => "My Tasks",
        "Sprint" => "Due Window Board",
        "Kanban" => "Task Kanban Board",
        "TaskList" => "Command Task Register",
        "Reports" => "Task Performance Summary",
        _ => "Command Task Dashboard"
    };
    public string PageSubtitle => ResolvedViewMode switch
    {
        "MyTasks" => "Tasks assigned to the logged-in user.",
        "Sprint" => "Tasks grouped by due date and urgency.",
        "Kanban" => "Tasks grouped by current workflow status.",
        "TaskList" => "Filterable register of all visible tasks.",
        "Reports" => "Summary of pending, critical, blocked and closed tasks.",
        _ => "Command-level visibility of active, delayed and critical tasks."
    };

    public IReadOnlyList<string> AssignmentRoles => ActionTaskRoleResolver.AllowedAssignmentRoles();
    public IReadOnlyList<string> AllowedStatusOptions => new[]
    {
        ActionTaskStatuses.Assigned,
        ActionTaskStatuses.InProgress,
        ActionTaskStatuses.Blocked,
        ActionTaskStatuses.Submitted
    };

    // SECTION: Selected-task projection helper
    public bool IsSelectedTask(ActionTaskItem task)
    {
        return TaskId.HasValue && TaskId.Value == task.Id;
    }

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

    public string ResolveActorName(string performedByUserId)
    {
        return TaskActorNames.TryGetValue(performedByUserId, out var actorName)
            ? actorName
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
            ShowCreateModal = true;
            await LoadDataAsync();
            return Page();
        }

        var assignedUser = await _users.FindByIdAsync(Input.AssignedToUserId);
        if (assignedUser is null)
        {
            ModelState.AddModelError(nameof(Input.AssignedToUserId), "Selected user was not found.");
            ShowCreateModal = true;
            await LoadDataAsync();
            return Page();
        }

        if (assignedUser.IsDisabled || assignedUser.PendingDeletion)
        {
            ModelState.AddModelError(nameof(Input.AssignedToUserId), "Selected user is inactive and cannot be assigned a task.");
            ShowCreateModal = true;
            await LoadDataAsync();
            return Page();
        }

        if (assignedUser.LockoutEnd.HasValue && assignedUser.LockoutEnd > DateTimeOffset.UtcNow)
        {
            ModelState.AddModelError(nameof(Input.AssignedToUserId), "Selected user is locked and cannot be assigned a task.");
            ShowCreateModal = true;
            await LoadDataAsync();
            return Page();
        }

        var assignedRoles = await _users.GetRolesAsync(assignedUser);
        var assignedRole = ActionTaskRoleResolver.ResolveAssignableRoleFromRoles(assignedRoles);
        if (assignedRole is null)
        {
            ModelState.AddModelError(string.Empty, "Selected user does not have an assignable Task Tracker role.");
            ShowCreateModal = true;
            await LoadDataAsync();
            return Page();
        }

        if (!_permission.CanAssign(CurrentRole, assignedRole))
        {
            ModelState.AddModelError(string.Empty, $"Current role is not permitted to assign tasks to {assignedRole}.");
            ShowCreateModal = true;
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
    public async Task<IActionResult> OnPostSubmitAsync(int id, string? remarks)
    {
        await ResolveIdentityAsync();
        try
        {
            await _service.SubmitTaskAsync(id, CurrentUserId, CurrentRole, remarks);
            TempData["ToastMessage"] = "Task submitted.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ToastError"] = ex.Message;
        }

        return RedirectToPage(new { ViewMode = ResolveViewMode(), TaskId = id });
    }

    // SECTION: Close task by command role
    public async Task<IActionResult> OnPostCloseAsync(int id, string? remarks)
    {
        await ResolveIdentityAsync();
        try
        {
            await _service.CloseTaskAsync(id, CurrentUserId, CurrentRole, remarks);
            TempData["ToastMessage"] = "Task closed.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ToastError"] = ex.Message;
        }

        return RedirectToPage(new { ViewMode = ResolveViewMode(), TaskId = id });
    }

    // SECTION: Update in-flight status
    public async Task<IActionResult> OnPostUpdateStatusAsync(int id, string status, string? remarks)
    {
        await ResolveIdentityAsync();
        try
        {
            await _service.UpdateStatusAsync(id, status, CurrentUserId, CurrentRole, remarks);
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
            TaskActorNames = await LoadTaskActorNamesAsync(SelectedTaskLogs);
        }
    }

    // SECTION: Task display projections for richer UI cards and mini-lists.
    public IReadOnlyList<TaskDisplayItem> CriticalOpenTaskDisplays =>
        ToDisplayItems(CriticalOpenTasks);

    public IReadOnlyList<TaskDisplayItem> OverdueTaskDisplays =>
        ToDisplayItems(OverdueTasks);

    public IReadOnlyList<TaskDisplayItem> RecentlySubmittedTaskDisplays =>
        ToDisplayItems(RecentlySubmittedTasks);

    public IReadOnlyList<TaskDisplayItem> RecentlyUpdatedTaskDisplays =>
        ToDisplayItems(RecentlyUpdatedTasks);

    public IReadOnlyList<TaskDisplayItem> KanbanAssignedTaskDisplays =>
        ToDisplayItems(KanbanAssignedTasks);

    public IReadOnlyList<TaskDisplayItem> KanbanInProgressTaskDisplays =>
        ToDisplayItems(KanbanInProgressTasks);

    public IReadOnlyList<TaskDisplayItem> KanbanBlockedTaskDisplays =>
        ToDisplayItems(KanbanBlockedTasks);

    public IReadOnlyList<TaskDisplayItem> KanbanSubmittedTaskDisplays =>
        ToDisplayItems(KanbanSubmittedTasks);

    public IReadOnlyList<TaskDisplayItem> KanbanClosedTaskDisplays =>
        ToDisplayItems(KanbanClosedTasks);

    public IReadOnlyList<TaskDisplayItem> DueTodayTaskDisplays =>
        ToDisplayItems(DueTodayTasks);

    public IReadOnlyList<TaskDisplayItem> DueThisWeekTaskDisplays =>
        ToDisplayItems(DueThisWeekTasks);

    public IReadOnlyList<TaskDisplayItem> DueLaterTaskDisplays =>
        ToDisplayItems(DueLaterTasks);

    public IReadOnlyList<TaskDisplayItem> SprintOverdueTaskDisplays =>
        ToDisplayItems(SprintOverdueTasks);

    // SECTION: KPI helpers for dashboard and reports.
    public int ActiveCount => Tasks.Count(t => !string.Equals(t.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase));
    public int OverdueCount => Tasks.Count(t => !string.Equals(t.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase) && t.DueDate.Date < DateTime.UtcNow.Date);
    public int SubmittedCount => CountByStatus(ActionTaskStatuses.Submitted);
    public int BlockedCount => CountByStatus(ActionTaskStatuses.Blocked);
    public int ClosedCount => CountByStatus(ActionTaskStatuses.Closed);
    public int CriticalOpenCount => CriticalOpenTasks.Count;
    public int StatusCountsMax => StatusCounts.Count == 0 ? 0 : StatusCounts.Max(x => x.Count);
    public int PriorityCountsMax => PriorityCounts.Count == 0 ? 0 : PriorityCounts.Max(x => x.Count);
    public int AssigneePendingCountsMax => AssigneePendingCounts.Count == 0 ? 0 : AssigneePendingCounts.Max(x => x.Count);
    public int OpenAgeingBucketsMax => OpenAgeingBuckets.Count == 0 ? 0 : OpenAgeingBuckets.Max(x => x.Count);
    public int OverdueAgeingBucketsMax => OverdueAgeingBuckets.Count == 0 ? 0 : OverdueAgeingBuckets.Max(x => x.Count);
    public int ActiveCriticalCount => Tasks.Count(t =>
        !string.Equals(t.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase)
        && string.Equals(t.Priority, "Critical", StringComparison.OrdinalIgnoreCase));

    public int SubmittedPendingClosureCount => Tasks.Count(t =>
        string.Equals(t.Status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase));

    public bool HasActiveFilters =>
        IsTaskListView &&
        (!string.IsNullOrWhiteSpace(FilterStatus)
         || !string.IsNullOrWhiteSpace(FilterPriority)
         || !string.IsNullOrWhiteSpace(FilterAssigneeUserId)
         || FilterDueDate.HasValue
         || !string.IsNullOrWhiteSpace(FilterSearch));

    public string CommandSummary
    {
        get
        {
            var activeTasksText = $"There {(ActiveCount == 1 ? "is" : "are")} {ActiveCount} active {(ActiveCount == 1 ? "task" : "tasks")}";
            var criticalText = $", including {ActiveCriticalCount} critical {(ActiveCriticalCount == 1 ? "task" : "tasks")}.";
            var overdueText = OverdueCount switch
            {
                0 => "No tasks are overdue.",
                1 => "1 task is overdue.",
                _ => $"{OverdueCount} tasks are overdue."
            };

            var submittedPendingText = SubmittedPendingClosureCount switch
            {
                0 => "No submitted task is pending closure.",
                1 => "1 submitted task is pending closure.",
                _ => $"{SubmittedPendingClosureCount} submitted tasks are pending closure."
            };

            return $"{activeTasksText}{criticalText} {overdueText} {submittedPendingText}";
        }
    }

    // SECTION: Percentage helper for CSS bar-width calculations.
    public int ToPercent(int value, int max) =>
        max <= 0 ? 0 : (int)Math.Round((double)value / max * 100);

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
        var utcToday = DateTime.UtcNow.Date;
        var openTasks = tasks
            .Where(t => !string.Equals(t.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase))
            .ToList();

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

        // SECTION: Open-task ageing buckets for command trend analysis.
        OpenAgeingBuckets = new[]
        {
            new CountSummary("0 to 3 days", openTasks.Count(t => (utcToday - t.AssignedOn.Date).TotalDays is >= 0 and <= 3)),
            new CountSummary("4 to 7 days", openTasks.Count(t => (utcToday - t.AssignedOn.Date).TotalDays is >= 4 and <= 7)),
            new CountSummary("8 to 14 days", openTasks.Count(t => (utcToday - t.AssignedOn.Date).TotalDays is >= 8 and <= 14)),
            new CountSummary("15+ days", openTasks.Count(t => (utcToday - t.AssignedOn.Date).TotalDays >= 15))
        };

        // SECTION: Overdue ageing buckets for late-task exposure.
        OverdueAgeingBuckets = new[]
        {
            new CountSummary("1 to 3 days overdue", openTasks.Count(t => (utcToday - t.DueDate.Date).TotalDays is >= 1 and <= 3)),
            new CountSummary("4 to 7 days overdue", openTasks.Count(t => (utcToday - t.DueDate.Date).TotalDays is >= 4 and <= 7)),
            new CountSummary("8+ days overdue", openTasks.Count(t => (utcToday - t.DueDate.Date).TotalDays >= 8))
        };
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
        var utcNow = DateTimeOffset.UtcNow;
        var users = await _users.Users
            .Where(u => !u.IsDisabled)
            .Where(u => !u.PendingDeletion)
            .Where(u => !u.LockoutEnd.HasValue || u.LockoutEnd <= utcNow)
            .OrderBy(u => u.Rank)
            .ThenBy(u => u.FullName)
            .ThenBy(u => u.UserName)
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

            list.Add(new UserOption(user.Id, BuildPersonDisplayName(user), matchedRole));
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
            .Select(u => new { u.Id, u.Rank, u.FullName, u.UserName, u.Email })
            .ToListAsync();

        return users.ToDictionary(
            u => u.Id,
            u => BuildPersonDisplayName(u.Rank, u.FullName, u.UserName, u.Email),
            StringComparer.Ordinal);
    }

    // SECTION: Resolve inspector actor names for audit visibility.
    private async Task<IReadOnlyDictionary<string, string>> LoadTaskActorNamesAsync(IReadOnlyList<ActionTaskAuditLog> logs)
    {
        var actorIds = logs
            .Select(log => log.PerformedByUserId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (actorIds.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var users = await _users.Users
            .Where(u => actorIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Rank, u.FullName, u.UserName, u.Email })
            .ToListAsync();

        return users.ToDictionary(
            u => u.Id,
            u => BuildPersonDisplayName(u.Rank, u.FullName, u.UserName, u.Email),
            StringComparer.Ordinal);
    }

    // SECTION: Person display helper for rank and full-name-first rendering.
    private static string BuildPersonDisplayName(ApplicationUser user) =>
        BuildPersonDisplayName(user.Rank, user.FullName, user.UserName, user.Email);

    // SECTION: Person display helper for query projections.
    private static string BuildPersonDisplayName(string? rank, string? fullName, string? userName, string? email)
    {
        var trimmedRank = rank?.Trim();
        var trimmedFullName = fullName?.Trim();

        if (!string.IsNullOrWhiteSpace(trimmedRank) && !string.IsNullOrWhiteSpace(trimmedFullName))
        {
            return $"{trimmedRank} {trimmedFullName}";
        }

        if (!string.IsNullOrWhiteSpace(trimmedFullName))
        {
            return trimmedFullName;
        }

        if (!string.IsNullOrWhiteSpace(userName))
        {
            return userName;
        }

        return string.IsNullOrWhiteSpace(email) ? "User" : email;
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
    public sealed class TaskDisplayItem
    {
        public ActionTaskItem Task { get; init; } = default!;
        public string AssigneeName { get; init; } = string.Empty;
    }

    private int CountByStatus(string status) =>
        Tasks.Count(t => string.Equals(t.Status, status, StringComparison.OrdinalIgnoreCase));

    private IReadOnlyList<TaskDisplayItem> ToDisplayItems(IReadOnlyList<ActionTaskItem> tasks) =>
        tasks.Select(task => new TaskDisplayItem
        {
            Task = task,
            AssigneeName = ResolveAssigneeName(task.AssignedToUserId)
        }).ToList();
}
