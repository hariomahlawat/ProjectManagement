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
        var backlogTasks = BuildBacklogTasks(tasks, request, assigneeNames);
        var sprintReadModel = BuildSprintReadModel(tasks, request.Sprints, request.SelectedSprintId, assigneeNames);
        var activeSprintMetrics = BuildActiveSprintMetrics(tasks, sprintReadModel.ActiveSprint);

        return new ActionTaskReadModel
        {
            ScopeTasks = tasks,
            TaskListTasks = taskList,
            BacklogTasks = backlogTasks,
            SprintReadModel = sprintReadModel,
            ActiveSprintMetrics = activeSprintMetrics,
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
            Reports = BuildReportModel(tasks, request, assigneeNames)
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

    // SECTION: Backlog read-model projection where null SprintId is the backlog contract.
    private static IReadOnlyList<ActionTaskItem> BuildBacklogTasks(IReadOnlyList<ActionTaskItem> tasks, ActionTaskQueryRequest request, IReadOnlyDictionary<string, string> assigneeNames)
    {
        var backlogTasks = tasks.Where(t => t.SprintId is null).ToList();
        return request.IsBacklogView
            ? ApplyTaskListFilters(backlogTasks, request, assigneeNames).ToList()
            : backlogTasks.OrderBy(t => t.DueDate).ThenBy(t => t.Id).ToList();
    }

    // SECTION: Sprint read-model projection for selected sprint operational rendering.
    private static ActionSprintReadModel BuildSprintReadModel(IReadOnlyList<ActionTaskItem> tasks, IReadOnlyList<ActionSprint> sprints, int? selectedSprintId, IReadOnlyDictionary<string, string> assigneeNames)
    {
        var orderedSprints = sprints
            .OrderByDescending(s => s.Status == ActionSprintStatus.Active)
            .ThenByDescending(s => s.StartDate)
            .ThenByDescending(s => s.Id)
            .ToList();

        var activeSprint = orderedSprints.FirstOrDefault(s => s.Status == ActionSprintStatus.Active);
        var selectedSprint = selectedSprintId.HasValue
            ? orderedSprints.FirstOrDefault(s => s.Id == selectedSprintId.Value)
            : activeSprint ?? orderedSprints.FirstOrDefault();

        var selectedTasks = selectedSprint is null
            ? new List<ActionTaskItem>()
            : tasks.Where(t => t.SprintId == selectedSprint.Id).OrderBy(t => StatusOrder(t)).ThenBy(t => t.DueDate).ThenBy(t => t.Id).ToList();

        var backlogTasks = tasks.Where(t => t.SprintId is null).OrderBy(t => t.DueDate).ThenBy(t => t.Id).ToList();

        return new ActionSprintReadModel
        {
            Sprints = orderedSprints,
            ActiveSprint = activeSprint,
            SelectedSprint = selectedSprint,
            SelectedSprintTasks = selectedTasks,
            BacklogTasks = backlogTasks,
            Summary = selectedSprint is null
                ? ActionSprintSummary.Empty
                : BuildSprintSummary(selectedSprint, selectedTasks, backlogTasks.Count, assigneeNames),
            ClosureReview = selectedSprint is null
                ? ActionSprintClosureReview.Empty
                : BuildClosureReview(selectedSprint, selectedTasks, orderedSprints)
        };
    }

    // SECTION: Compact sprint summary suitable for low-risk UI exposure.
    private static ActionSprintSummary BuildSprintSummary(ActionSprint sprint, IReadOnlyList<ActionTaskItem> sprintTasks, int backlogCount, IReadOnlyDictionary<string, string> assigneeNames)
    {
        var openTasks = sprintTasks.Count(IsOpen);
        var closedTasks = sprintTasks.Count(t => string.Equals(t.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase));
        var blockedTasks = sprintTasks.Count(t => string.Equals(t.Status, ActionTaskStatuses.Blocked, StringComparison.OrdinalIgnoreCase));
        var submittedTasks = sprintTasks.Count(t => string.Equals(t.Status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase));
        var assigneeCount = sprintTasks.Select(t => assigneeNames.TryGetValue(t.AssignedToUserId, out var name) ? name : t.AssignedToUserId).Distinct(StringComparer.OrdinalIgnoreCase).Count();

        return new ActionSprintSummary
        {
            SprintId = sprint.Id,
            Title = sprint.Name,
            Status = sprint.Status.ToString(),
            DateRange = $"{sprint.StartDate:dd MMM yyyy} – {sprint.EndDate:dd MMM yyyy}",
            CommandFocus = sprint.Goal,
            TaskCount = sprintTasks.Count,
            OpenCount = openTasks,
            ClosedCount = closedTasks,
            BlockedCount = blockedTasks,
            SubmittedCount = submittedTasks,
            BacklogCount = backlogCount,
            AssigneeCount = assigneeCount
        };
    }

    // SECTION: Closure review read model for explicit end-of-sprint disposition.
    private static ActionSprintClosureReview BuildClosureReview(ActionSprint sprint, IReadOnlyList<ActionTaskItem> sprintTasks, IReadOnlyList<ActionSprint> sprints)
    {
        var unfinishedTasks = sprintTasks.Where(IsOpen).OrderBy(t => StatusOrder(t)).ThenBy(t => t.DueDate).ThenBy(t => t.Id).ToList();
        var targetOptions = sprints
            .Where(s => s.Id != sprint.Id && s.Status != ActionSprintStatus.Closed && s.StartDate.Date > sprint.EndDate.Date)
            .OrderBy(s => s.StartDate)
            .ThenBy(s => s.Id)
            .ToList();

        return new ActionSprintClosureReview
        {
            SprintId = sprint.Id,
            CanCloseDirectly = sprint.Status == ActionSprintStatus.Active && unfinishedTasks.Count == 0,
            CompletedTasks = sprintTasks.Where(t => string.Equals(t.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase)).OrderBy(t => t.DueDate).ThenBy(t => t.Id).ToList(),
            UnfinishedTasks = unfinishedTasks,
            BlockedTasks = sprintTasks.Where(t => string.Equals(t.Status, ActionTaskStatuses.Blocked, StringComparison.OrdinalIgnoreCase)).OrderBy(t => t.DueDate).ThenBy(t => t.Id).ToList(),
            SubmittedPendingClosureTasks = sprintTasks.Where(t => string.Equals(t.Status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase)).OrderBy(t => t.SubmittedOn ?? t.DueDate).ThenBy(t => t.Id).ToList(),
            TargetSprintOptions = targetOptions,
            RecommendedDispositionOptions = BuildRecommendedDispositionOptions(unfinishedTasks, targetOptions)
        };
    }

    private static IReadOnlyList<string> BuildRecommendedDispositionOptions(IReadOnlyList<ActionTaskItem> unfinishedTasks, IReadOnlyList<ActionSprint> targetOptions)
    {
        if (unfinishedTasks.Count == 0)
        {
            return new[] { "Close the sprint after recording closure remarks." };
        }

        var options = new List<string> { "Move unfinished tasks with continuing operational value to the next open sprint.", "Move deferred or unscheduled tasks to backlog." };
        if (targetOptions.Count == 0)
        {
            options.Add("Create or reopen a planned sprint before carrying tasks forward.");
        }

        return options;
    }

    // SECTION: Active sprint dashboard metrics scoped to operational task health.
    private static ActiveSprintOperationalMetrics BuildActiveSprintMetrics(IReadOnlyList<ActionTaskItem> tasks, ActionSprint? activeSprint)
    {
        var activeSprintTasks = activeSprint is null
            ? new List<ActionTaskItem>()
            : tasks.Where(t => t.SprintId == activeSprint.Id).ToList();
        var today = DateTime.UtcNow.Date;

        return new ActiveSprintOperationalMetrics
        {
            ActiveSprintName = activeSprint?.Name,
            ActiveSprintDateRange = activeSprint is null ? null : $"{activeSprint.StartDate:dd MMM yyyy} – {activeSprint.EndDate:dd MMM yyyy}",
            TotalTasks = activeSprintTasks.Count,
            CompletedTasks = activeSprintTasks.Count(t => string.Equals(t.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase)),
            InProgressTasks = activeSprintTasks.Count(t => string.Equals(t.Status, ActionTaskStatuses.InProgress, StringComparison.OrdinalIgnoreCase)),
            BlockedTasks = activeSprintTasks.Count(t => string.Equals(t.Status, ActionTaskStatuses.Blocked, StringComparison.OrdinalIgnoreCase)),
            OverdueTasks = activeSprintTasks.Count(t => IsOpen(t) && t.DueDate.Date < today),
            BacklogTasks = tasks.Count(t => t.SprintId is null),
            CarryForwardCandidateTasks = activeSprintTasks.Count(t => IsOpen(t))
        };
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

    // SECTION: Filter-aware report projection for management analysis without changing workflow rules.
    private static ActionTaskReportReadModel BuildReportModel(IReadOnlyList<ActionTaskItem> tasks, ActionTaskQueryRequest request, IReadOnlyDictionary<string, string> assigneeNames)
    {
        var utcToday = DateTime.UtcNow.Date;
        var reportTasks = ApplyReportFilters(tasks, request).ToList();
        var openTasks = reportTasks.Where(IsOpen).ToList();
        var submittedTasks = reportTasks.Where(t => string.Equals(t.Status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase)).ToList();
        var backlogOpenTasks = openTasks.Where(t => t.SprintId is null).ToList();
        var blockedTasks = reportTasks.Where(t => string.Equals(t.Status, ActionTaskStatuses.Blocked, StringComparison.OrdinalIgnoreCase)).ToList();
        string Assignee(string id) => assigneeNames.TryGetValue(id, out var name) ? name : "User";

        return new ActionTaskReportReadModel
        {
            TotalTaskCount = tasks.Count,
            FilteredTaskCount = reportTasks.Count,
            AssigneePendingCounts = openTasks.GroupBy(t => Assignee(t.AssignedToUserId)).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => new CountSummary(g.Key, g.Count())).ToList(),
            PriorityCounts = openTasks.GroupBy(t => t.Priority).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => new CountSummary(g.Key, g.Count())).ToList(),
            StatusCounts = reportTasks.GroupBy(t => t.Status).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => new CountSummary(g.Key, g.Count())).ToList(),
            OpenAgeingBuckets = BuildAssignedAgeingBuckets(openTasks, utcToday),
            OverdueAgeingBuckets = new[] { new CountSummary("1 to 3 days overdue", openTasks.Count(t => (utcToday - t.DueDate.Date).TotalDays is >= 1 and <= 3)), new CountSummary("4 to 7 days overdue", openTasks.Count(t => (utcToday - t.DueDate.Date).TotalDays is >= 4 and <= 7)), new CountSummary("8+ days overdue", openTasks.Count(t => (utcToday - t.DueDate.Date).TotalDays >= 8)) },
            SubmittedPendingClosureAgeingBuckets = new[] { new CountSummary("0 to 1 day", submittedTasks.Count(t => (utcToday - (t.SubmittedOn ?? t.AssignedOn).Date).TotalDays is >= 0 and <= 1)), new CountSummary("2 to 3 days", submittedTasks.Count(t => (utcToday - (t.SubmittedOn ?? t.AssignedOn).Date).TotalDays is >= 2 and <= 3)), new CountSummary("4+ days", submittedTasks.Count(t => (utcToday - (t.SubmittedOn ?? t.AssignedOn).Date).TotalDays >= 4)) },
            BacklogAgeingBuckets = BuildAssignedAgeingBuckets(backlogOpenTasks, utcToday),
            BlockedAgeingBuckets = BuildAssignedAgeingBuckets(blockedTasks, utcToday),
            CarryForwardBySprint = BuildCarryForwardBySprint(openTasks, request.Sprints)
        };
    }

    // SECTION: Reports filters are isolated from register/backlog filters to preserve other workspaces.
    private static IEnumerable<ActionTaskItem> ApplyReportFilters(IReadOnlyList<ActionTaskItem> tasks, ActionTaskQueryRequest request)
    {
        var query = tasks.AsEnumerable();
        if (request.ReportSprintId.HasValue)
        {
            query = request.ReportSprintId.Value == 0
                ? query.Where(t => t.SprintId is null)
                : query.Where(t => t.SprintId == request.ReportSprintId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.ReportAssigneeUserId)) query = query.Where(t => string.Equals(t.AssignedToUserId, request.ReportAssigneeUserId, StringComparison.Ordinal));
        if (request.ReportFromDate.HasValue) { var d = request.ReportFromDate.Value.Date; query = query.Where(t => t.DueDate.Date >= d); }
        if (request.ReportToDate.HasValue) { var d = request.ReportToDate.Value.Date; query = query.Where(t => t.DueDate.Date <= d); }
        if (!string.IsNullOrWhiteSpace(request.ReportStatus)) query = query.Where(t => string.Equals(t.Status, request.ReportStatus, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(request.ReportPriority)) query = query.Where(t => string.Equals(t.Priority, request.ReportPriority, StringComparison.OrdinalIgnoreCase));
        return query;
    }

    // SECTION: Shared assigned-age buckets used by open, backlog and blocked ageing reports.
    private static IReadOnlyList<CountSummary> BuildAssignedAgeingBuckets(IReadOnlyList<ActionTaskItem> tasks, DateTime utcToday)
        => new[] { new CountSummary("0 to 3 days", tasks.Count(t => (utcToday - t.AssignedOn.Date).TotalDays is >= 0 and <= 3)), new CountSummary("4 to 7 days", tasks.Count(t => (utcToday - t.AssignedOn.Date).TotalDays is >= 4 and <= 7)), new CountSummary("8 to 14 days", tasks.Count(t => (utcToday - t.AssignedOn.Date).TotalDays is >= 8 and <= 14)), new CountSummary("15+ days", tasks.Count(t => (utcToday - t.AssignedOn.Date).TotalDays >= 15)) };

    // SECTION: Safely-derived carry-forward candidates from unfinished tasks still associated with non-closed sprints.
    private static IReadOnlyList<CountSummary> BuildCarryForwardBySprint(IReadOnlyList<ActionTaskItem> openTasks, IReadOnlyList<ActionSprint> sprints)
    {
        var sprintLookup = sprints.ToDictionary(s => s.Id);
        return openTasks
            .Where(t => t.SprintId.HasValue && sprintLookup.TryGetValue(t.SprintId.Value, out var sprint) && sprint.Status != ActionSprintStatus.Closed)
            .GroupBy(t => sprintLookup[t.SprintId!.Value])
            .OrderBy(g => g.Key.StartDate)
            .ThenBy(g => g.Key.Id)
            .Select(g => new CountSummary(g.Key.Name, g.Count()))
            .ToList();
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
        Func<ActionTaskItem, int> statusOrder = StatusOrder;
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

    // SECTION: Shared operational status ordering for boards and filtered lists.
    private static int StatusOrder(ActionTaskItem task) => task.Status switch
    {
        ActionTaskStatuses.Assigned => 1,
        ActionTaskStatuses.InProgress => 2,
        ActionTaskStatuses.Blocked => 3,
        ActionTaskStatuses.Submitted => 4,
        ActionTaskStatuses.Closed => 5,
        _ => 99
    };

    public sealed record ActionTaskQueryRequest(
        string CurrentUserId,
        bool IsMyTasksView,
        bool IsTaskListView,
        bool IsBacklogView,
        int? SelectedSprintId,
        IReadOnlyList<ActionSprint> Sprints,
        string? FilterStatus,
        string? FilterPriority,
        string? FilterAssigneeUserId,
        DateTime? FilterDueDate,
        string? FilterSearch,
        string? SortBy,
        string? SortDir,
        int? ReportSprintId = null,
        string? ReportAssigneeUserId = null,
        DateTime? ReportFromDate = null,
        DateTime? ReportToDate = null,
        string? ReportStatus = null,
        string? ReportPriority = null);

    public sealed class ActionTaskReadModel
    {
        public IReadOnlyList<ActionTaskItem> ScopeTasks { get; init; } = Array.Empty<ActionTaskItem>();
        public IReadOnlyList<ActionTaskItem> TaskListTasks { get; init; } = Array.Empty<ActionTaskItem>();
        public IReadOnlyList<ActionTaskItem> BacklogTasks { get; init; } = Array.Empty<ActionTaskItem>();
        public ActionSprintReadModel SprintReadModel { get; init; } = new();
        public ActiveSprintOperationalMetrics ActiveSprintMetrics { get; init; } = ActiveSprintOperationalMetrics.Empty;
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

    public sealed class ActionTaskDueBuckets
    {
        public IReadOnlyList<ActionTaskItem> Overdue { get; init; } = Array.Empty<ActionTaskItem>();
        public IReadOnlyList<ActionTaskItem> Today { get; init; } = Array.Empty<ActionTaskItem>();
        public IReadOnlyList<ActionTaskItem> ThisWeek { get; init; } = Array.Empty<ActionTaskItem>();
        public IReadOnlyList<ActionTaskItem> Later { get; init; } = Array.Empty<ActionTaskItem>();
    }

    public sealed class ActionSprintReadModel
    {
        public IReadOnlyList<ActionSprint> Sprints { get; init; } = Array.Empty<ActionSprint>();
        public ActionSprint? ActiveSprint { get; init; }
        public ActionSprint? SelectedSprint { get; init; }
        public IReadOnlyList<ActionTaskItem> SelectedSprintTasks { get; init; } = Array.Empty<ActionTaskItem>();
        public IReadOnlyList<ActionTaskItem> BacklogTasks { get; init; } = Array.Empty<ActionTaskItem>();
        public ActionSprintSummary Summary { get; init; } = ActionSprintSummary.Empty;
        public ActionSprintClosureReview ClosureReview { get; init; } = ActionSprintClosureReview.Empty;
    }

    public sealed class ActionSprintSummary
    {
        public static ActionSprintSummary Empty { get; } = new();
        public int? SprintId { get; init; }
        public string Title { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string DateRange { get; init; } = string.Empty;
        public string? CommandFocus { get; init; }
        public int TaskCount { get; init; }
        public int OpenCount { get; init; }
        public int ClosedCount { get; init; }
        public int BlockedCount { get; init; }
        public int SubmittedCount { get; init; }
        public int BacklogCount { get; init; }
        public int AssigneeCount { get; init; }
    }

    public sealed class ActionSprintClosureReview
    {
        public static ActionSprintClosureReview Empty { get; } = new();
        public int? SprintId { get; init; }
        public bool CanCloseDirectly { get; init; }
        public IReadOnlyList<ActionTaskItem> CompletedTasks { get; init; } = Array.Empty<ActionTaskItem>();
        public IReadOnlyList<ActionTaskItem> UnfinishedTasks { get; init; } = Array.Empty<ActionTaskItem>();
        public IReadOnlyList<ActionTaskItem> BlockedTasks { get; init; } = Array.Empty<ActionTaskItem>();
        public IReadOnlyList<ActionTaskItem> SubmittedPendingClosureTasks { get; init; } = Array.Empty<ActionTaskItem>();
        public IReadOnlyList<ActionSprint> TargetSprintOptions { get; init; } = Array.Empty<ActionSprint>();
        public IReadOnlyList<string> RecommendedDispositionOptions { get; init; } = Array.Empty<string>();
    }

    public sealed class ActiveSprintOperationalMetrics
    {
        public static ActiveSprintOperationalMetrics Empty { get; } = new();
        public string? ActiveSprintName { get; init; }
        public string? ActiveSprintDateRange { get; init; }
        public int TotalTasks { get; init; }
        public int CompletedTasks { get; init; }
        public int InProgressTasks { get; init; }
        public int BlockedTasks { get; init; }
        public int OverdueTasks { get; init; }
        public int BacklogTasks { get; init; }
        public int CarryForwardCandidateTasks { get; init; }
    }

    public sealed class ActionTaskReportReadModel
    {
        public IReadOnlyList<CountSummary> AssigneePendingCounts { get; init; } = Array.Empty<CountSummary>();
        public IReadOnlyList<CountSummary> PriorityCounts { get; init; } = Array.Empty<CountSummary>();
        public IReadOnlyList<CountSummary> StatusCounts { get; init; } = Array.Empty<CountSummary>();
        public IReadOnlyList<CountSummary> OpenAgeingBuckets { get; init; } = Array.Empty<CountSummary>();
        public IReadOnlyList<CountSummary> OverdueAgeingBuckets { get; init; } = Array.Empty<CountSummary>();
        public IReadOnlyList<CountSummary> SubmittedPendingClosureAgeingBuckets { get; init; } = Array.Empty<CountSummary>();
        public IReadOnlyList<CountSummary> BacklogAgeingBuckets { get; init; } = Array.Empty<CountSummary>();
        public IReadOnlyList<CountSummary> CarryForwardBySprint { get; init; } = Array.Empty<CountSummary>();
        public IReadOnlyList<CountSummary> BlockedAgeingBuckets { get; init; } = Array.Empty<CountSummary>();
        public int TotalTaskCount { get; init; }
        public int FilteredTaskCount { get; init; }
    }
    public sealed record CountSummary(string Name, int Count);
}
