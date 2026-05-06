using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models;

namespace ProjectManagement.Services.ActionTasks;

public sealed class ActionTaskQueryService
{
    // SECTION: Compose all read-model slices used by Action Tasks pages.
    public ActionTaskReadModel BuildReadModel(
        IReadOnlyList<ActionTaskItem> sourceTasks,
        ActionTaskQueryRequest request,
        IReadOnlyDictionary<string, string> assigneeNames,
        IReadOnlyDictionary<int, DateTime?> activityByTaskId)
    {
        var tasks = request.IsMyTasksView
            ? sourceTasks.Where(t => string.Equals(t.AssignedToUserId, request.CurrentUserId, StringComparison.Ordinal)).ToList()
            : sourceTasks.ToList();

        var taskList = request.IsTaskListView ? ApplyTaskListFilters(tasks, request, assigneeNames).ToList() : tasks;

        return new ActionTaskReadModel
        {
            ScopeTasks = tasks,
            TaskListTasks = taskList,
            CriticalOpenTasks = tasks.Where(t => IsCriticalOpen(t)).OrderBy(t => t.DueDate).Take(5).ToList(),
            OverdueTasks = tasks.Where(t => IsOpen(t) && t.DueDate.Date < DateTime.UtcNow.Date).OrderBy(t => t.DueDate).Take(5).ToList(),
            RecentlySubmittedTasks = tasks.Where(t => string.Equals(t.Status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase)).OrderByDescending(t => t.SubmittedOn ?? DateTime.MinValue).Take(5).ToList(),
            RecentlyUpdatedTasks = tasks.OrderByDescending(t => ResolveLastActivityUtc(t, activityByTaskId) ?? DateTime.MinValue).ThenByDescending(t => t.Id).Take(5).ToList(),
            KanbanAssignedTasks = tasks.Where(t => string.Equals(t.Status, ActionTaskStatuses.Assigned, StringComparison.OrdinalIgnoreCase)).ToList(),
            KanbanInProgressTasks = tasks.Where(t => string.Equals(t.Status, ActionTaskStatuses.InProgress, StringComparison.OrdinalIgnoreCase)).ToList(),
            KanbanBlockedTasks = tasks.Where(t => string.Equals(t.Status, ActionTaskStatuses.Blocked, StringComparison.OrdinalIgnoreCase)).ToList(),
            KanbanSubmittedTasks = tasks.Where(t => string.Equals(t.Status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase)).ToList(),
            KanbanClosedTasks = tasks.Where(t => string.Equals(t.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase)).ToList(),
            DueBuckets = BuildDueBuckets(tasks),
            Reports = BuildReportModel(tasks, assigneeNames)
        };
    }

    public ActionTaskItem? SelectTask(IReadOnlyList<ActionTaskItem> visibleTasks, int? taskId)
        => !taskId.HasValue ? null : visibleTasks.FirstOrDefault(t => t.Id == taskId.Value);

    private static bool IsOpen(ActionTaskItem task) => !string.Equals(task.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase);
    private static bool IsCriticalOpen(ActionTaskItem task) => IsOpen(task) && string.Equals(task.Priority, "Critical", StringComparison.OrdinalIgnoreCase);

    private static DateTime? ResolveLastActivityUtc(ActionTaskItem task, IReadOnlyDictionary<int, DateTime?> activityByTaskId)
    {
        activityByTaskId.TryGetValue(task.Id, out var activityTimestampUtc);
        return activityTimestampUtc ?? task.SubmittedOn ?? task.AssignedOn;
    }

    private static ActionTaskDueBuckets BuildDueBuckets(IReadOnlyList<ActionTaskItem> tasks)
    {
        var today = DateTime.UtcNow.Date;
        var endOfWeek = today.AddDays(7);
        return new ActionTaskDueBuckets
        {
            Overdue = tasks.Where(t => IsOpen(t) && t.DueDate.Date < today).OrderBy(t => t.DueDate).ToList(),
            Today = tasks.Where(t => IsOpen(t) && t.DueDate.Date == today).OrderBy(t => t.DueDate).ToList(),
            ThisWeek = tasks.Where(t => IsOpen(t) && t.DueDate.Date > today && t.DueDate.Date <= endOfWeek).OrderBy(t => t.DueDate).ToList(),
            Later = tasks.Where(t => IsOpen(t) && t.DueDate.Date > endOfWeek).OrderBy(t => t.DueDate).ToList()
        };
    }

    private static ActionTaskReportReadModel BuildReportModel(IReadOnlyList<ActionTaskItem> tasks, IReadOnlyDictionary<string, string> assigneeNames)
    {
        var utcToday = DateTime.UtcNow.Date;
        var openTasks = tasks.Where(IsOpen).ToList();
        var submittedTasks = tasks.Where(t => string.Equals(t.Status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase)).ToList();
        string Assignee(string id) => assigneeNames.TryGetValue(id, out var name) ? name : "User";

        return new ActionTaskReportReadModel
        {
            AssigneePendingCounts = openTasks.GroupBy(t => Assignee(t.AssignedToUserId)).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => new CountSummary(g.Key, g.Count())).ToList(),
            PriorityCounts = tasks.GroupBy(t => t.Priority).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => new CountSummary(g.Key, g.Count())).ToList(),
            StatusCounts = tasks.GroupBy(t => t.Status).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => new CountSummary(g.Key, g.Count())).ToList(),
            OpenAgeingBuckets = new[] { new CountSummary("0 to 3 days", openTasks.Count(t => (utcToday - t.AssignedOn.Date).TotalDays is >= 0 and <= 3)), new CountSummary("4 to 7 days", openTasks.Count(t => (utcToday - t.AssignedOn.Date).TotalDays is >= 4 and <= 7)), new CountSummary("8 to 14 days", openTasks.Count(t => (utcToday - t.AssignedOn.Date).TotalDays is >= 8 and <= 14)), new CountSummary("15+ days", openTasks.Count(t => (utcToday - t.AssignedOn.Date).TotalDays >= 15)) },
            OverdueAgeingBuckets = new[] { new CountSummary("1 to 3 days overdue", openTasks.Count(t => (utcToday - t.DueDate.Date).TotalDays is >= 1 and <= 3)), new CountSummary("4 to 7 days overdue", openTasks.Count(t => (utcToday - t.DueDate.Date).TotalDays is >= 4 and <= 7)), new CountSummary("8+ days overdue", openTasks.Count(t => (utcToday - t.DueDate.Date).TotalDays >= 8)) },
            SubmittedPendingClosureAgeingBuckets = new[] { new CountSummary("0 to 1 day", submittedTasks.Count(t => (utcToday - (t.SubmittedOn ?? t.AssignedOn).Date).TotalDays is >= 0 and <= 1)), new CountSummary("2 to 3 days", submittedTasks.Count(t => (utcToday - (t.SubmittedOn ?? t.AssignedOn).Date).TotalDays is >= 2 and <= 3)), new CountSummary("4+ days", submittedTasks.Count(t => (utcToday - (t.SubmittedOn ?? t.AssignedOn).Date).TotalDays >= 4)) }
        };
    }

    private static IEnumerable<ActionTaskItem> ApplyTaskListFilters(IReadOnlyList<ActionTaskItem> tasks, ActionTaskQueryRequest request, IReadOnlyDictionary<string, string> assigneeNames)
    {
        var query = tasks.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(request.FilterStatus)) query = query.Where(t => string.Equals(t.Status, request.FilterStatus, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(request.FilterPriority)) query = query.Where(t => string.Equals(t.Priority, request.FilterPriority, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(request.FilterAssigneeUserId)) query = query.Where(t => string.Equals(t.AssignedToUserId, request.FilterAssigneeUserId, StringComparison.Ordinal));
        if (request.FilterDueDate.HasValue) { var d = request.FilterDueDate.Value.Date; query = query.Where(t => t.DueDate.Date == d); }
        if (!string.IsNullOrWhiteSpace(request.FilterSearch)) { var s = request.FilterSearch.Trim(); query = query.Where(t => t.Title.Contains(s, StringComparison.OrdinalIgnoreCase)); }

        var sortBy = (request.SortBy ?? "due").Trim().ToLowerInvariant();
        var descending = string.Equals(request.SortDir, "desc", StringComparison.OrdinalIgnoreCase);
        Func<ActionTaskItem, string> assignee = t => assigneeNames.TryGetValue(t.AssignedToUserId, out var n) ? n : "User";
        Func<ActionTaskItem, int> statusOrder = task => task.Status switch { ActionTaskStatuses.Assigned => 1, ActionTaskStatuses.InProgress => 2, ActionTaskStatuses.Blocked => 3, ActionTaskStatuses.Submitted => 4, ActionTaskStatuses.Closed => 5, _ => 99 };
        Func<ActionTaskItem, int> priorityOrder = task => task.Priority switch { "Critical" => 1, "High" => 2, "Normal" => 3, "Low" => 4, _ => 99 };
        var ordered = sortBy switch
        {
            "title" => descending ? query.OrderByDescending(t => t.Title) : query.OrderBy(t => t.Title),
            "assignee" => descending ? query.OrderByDescending(assignee) : query.OrderBy(assignee),
            "status" => descending ? query.OrderByDescending(statusOrder).ThenBy(t => t.DueDate) : query.OrderBy(statusOrder).ThenBy(t => t.DueDate),
            "priority" => descending ? query.OrderByDescending(priorityOrder).ThenBy(t => t.DueDate) : query.OrderBy(priorityOrder).ThenBy(t => t.DueDate),
            "id" => descending ? query.OrderByDescending(t => t.Id) : query.OrderBy(t => t.Id),
            _ => descending ? query.OrderByDescending(t => t.DueDate) : query.OrderBy(t => t.DueDate)
        };
        return ordered.ThenBy(t => t.Id);
    }

    public sealed record ActionTaskQueryRequest(
        string CurrentUserId,
        bool IsMyTasksView,
        bool IsTaskListView,
        string? FilterStatus,
        string? FilterPriority,
        string? FilterAssigneeUserId,
        DateTime? FilterDueDate,
        string? FilterSearch,
        string? SortBy,
        string? SortDir);

    public sealed class ActionTaskReadModel
    {
        public IReadOnlyList<ActionTaskItem> ScopeTasks { get; init; } = Array.Empty<ActionTaskItem>();
        public IReadOnlyList<ActionTaskItem> TaskListTasks { get; init; } = Array.Empty<ActionTaskItem>();
        public IReadOnlyList<ActionTaskItem> CriticalOpenTasks { get; init; } = Array.Empty<ActionTaskItem>();
        public IReadOnlyList<ActionTaskItem> OverdueTasks { get; init; } = Array.Empty<ActionTaskItem>();
        public IReadOnlyList<ActionTaskItem> RecentlySubmittedTasks { get; init; } = Array.Empty<ActionTaskItem>();
        public IReadOnlyList<ActionTaskItem> RecentlyUpdatedTasks { get; init; } = Array.Empty<ActionTaskItem>();
        public IReadOnlyList<ActionTaskItem> KanbanAssignedTasks { get; init; } = Array.Empty<ActionTaskItem>();
        public IReadOnlyList<ActionTaskItem> KanbanInProgressTasks { get; init; } = Array.Empty<ActionTaskItem>();
        public IReadOnlyList<ActionTaskItem> KanbanBlockedTasks { get; init; } = Array.Empty<ActionTaskItem>();
        public IReadOnlyList<ActionTaskItem> KanbanSubmittedTasks { get; init; } = Array.Empty<ActionTaskItem>();
        public IReadOnlyList<ActionTaskItem> KanbanClosedTasks { get; init; } = Array.Empty<ActionTaskItem>();
        public ActionTaskDueBuckets DueBuckets { get; init; } = new();
        public ActionTaskReportReadModel Reports { get; init; } = new();
    }

    public sealed class ActionTaskDueBuckets { public IReadOnlyList<ActionTaskItem> Overdue { get; init; } = Array.Empty<ActionTaskItem>(); public IReadOnlyList<ActionTaskItem> Today { get; init; } = Array.Empty<ActionTaskItem>(); public IReadOnlyList<ActionTaskItem> ThisWeek { get; init; } = Array.Empty<ActionTaskItem>(); public IReadOnlyList<ActionTaskItem> Later { get; init; } = Array.Empty<ActionTaskItem>(); }
    public sealed class ActionTaskReportReadModel { public IReadOnlyList<CountSummary> AssigneePendingCounts { get; init; } = Array.Empty<CountSummary>(); public IReadOnlyList<CountSummary> PriorityCounts { get; init; } = Array.Empty<CountSummary>(); public IReadOnlyList<CountSummary> StatusCounts { get; init; } = Array.Empty<CountSummary>(); public IReadOnlyList<CountSummary> OpenAgeingBuckets { get; init; } = Array.Empty<CountSummary>(); public IReadOnlyList<CountSummary> OverdueAgeingBuckets { get; init; } = Array.Empty<CountSummary>(); public IReadOnlyList<CountSummary> SubmittedPendingClosureAgeingBuckets { get; init; } = Array.Empty<CountSummary>(); }
    public sealed record CountSummary(string Name, int Count);
}
