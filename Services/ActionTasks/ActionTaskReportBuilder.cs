using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models;

namespace ProjectManagement.Services.ActionTasks;

public sealed class ActionTaskReportBuilder
{
    private readonly IActionTrackerClock _clock;

    public ActionTaskReportBuilder(IActionTrackerClock clock)
    {
        _clock = clock;
    }

    // SECTION: Filter-aware report projection for management analysis without changing workflow rules.
    public ActionTaskQueryService.ActionTaskReportReadModel BuildReportModel(IReadOnlyList<ActionTaskItem> tasks, ActionTaskQueryService.ActionTaskQueryRequest request, IReadOnlyDictionary<string, string> assigneeNames)
    {
        var utcToday = _clock.UtcToday;
        var reportTasks = ApplyReportFilters(tasks, request).ToList();
        var openTasks = reportTasks.Where(IsOpen).ToList();
        var submittedTasks = reportTasks.Where(t => string.Equals(t.Status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase)).ToList();
        var backlogOpenTasks = openTasks.Where(t => t.SprintId is null).ToList();
        var blockedTasks = reportTasks.Where(t => string.Equals(t.Status, ActionTaskStatuses.Blocked, StringComparison.OrdinalIgnoreCase)).ToList();
        string Assignee(string id) => assigneeNames.TryGetValue(id, out var name) ? name : "User";

        return new ActionTaskQueryService.ActionTaskReportReadModel
        {
            TotalTaskCount = tasks.Count,
            FilteredTaskCount = reportTasks.Count,
            AssigneePendingCounts = openTasks.GroupBy(t => Assignee(t.AssignedToUserId)).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => new ActionTaskQueryService.CountSummary(g.Key, g.Count())).ToList(),
            PriorityCounts = openTasks.GroupBy(t => t.Priority).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => new ActionTaskQueryService.CountSummary(g.Key, g.Count())).ToList(),
            StatusCounts = reportTasks.GroupBy(t => t.Status).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => new ActionTaskQueryService.CountSummary(g.Key, g.Count())).ToList(),
            OpenAgeingBuckets = BuildAssignedAgeingBuckets(openTasks, utcToday),
            OverdueAgeingBuckets = new[] { new ActionTaskQueryService.CountSummary("1 to 3 days overdue", openTasks.Count(t => (utcToday - t.DueDate.Date).TotalDays is >= 1 and <= 3)), new ActionTaskQueryService.CountSummary("4 to 7 days overdue", openTasks.Count(t => (utcToday - t.DueDate.Date).TotalDays is >= 4 and <= 7)), new ActionTaskQueryService.CountSummary("8+ days overdue", openTasks.Count(t => (utcToday - t.DueDate.Date).TotalDays >= 8)) },
            SubmittedPendingClosureAgeingBuckets = new[] { new ActionTaskQueryService.CountSummary("0 to 1 day", submittedTasks.Count(t => (utcToday - (t.SubmittedOn ?? t.AssignedOn).Date).TotalDays is >= 0 and <= 1)), new ActionTaskQueryService.CountSummary("2 to 3 days", submittedTasks.Count(t => (utcToday - (t.SubmittedOn ?? t.AssignedOn).Date).TotalDays is >= 2 and <= 3)), new ActionTaskQueryService.CountSummary("4+ days", submittedTasks.Count(t => (utcToday - (t.SubmittedOn ?? t.AssignedOn).Date).TotalDays >= 4)) },
            BacklogAgeingBuckets = BuildAssignedAgeingBuckets(backlogOpenTasks, utcToday),
            BlockedAgeingBuckets = BuildAssignedAgeingBuckets(blockedTasks, utcToday),
            CarryForwardBySprint = BuildCarryForwardBySprint(openTasks, request.Sprints)
        };
    }

    // SECTION: Reports filters are isolated from register/backlog filters to preserve other workspaces.
    private static IEnumerable<ActionTaskItem> ApplyReportFilters(IReadOnlyList<ActionTaskItem> tasks, ActionTaskQueryService.ActionTaskQueryRequest request)
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
    private static IReadOnlyList<ActionTaskQueryService.CountSummary> BuildAssignedAgeingBuckets(IReadOnlyList<ActionTaskItem> tasks, DateTime utcToday)
        => new[] { new ActionTaskQueryService.CountSummary("0 to 3 days", tasks.Count(t => (utcToday - t.AssignedOn.Date).TotalDays is >= 0 and <= 3)), new ActionTaskQueryService.CountSummary("4 to 7 days", tasks.Count(t => (utcToday - t.AssignedOn.Date).TotalDays is >= 4 and <= 7)), new ActionTaskQueryService.CountSummary("8 to 14 days", tasks.Count(t => (utcToday - t.AssignedOn.Date).TotalDays is >= 8 and <= 14)), new ActionTaskQueryService.CountSummary("15+ days", tasks.Count(t => (utcToday - t.AssignedOn.Date).TotalDays >= 15)) };

    // SECTION: Safely-derived carry-forward candidates from unfinished tasks still associated with non-closed sprints.
    private static IReadOnlyList<ActionTaskQueryService.CountSummary> BuildCarryForwardBySprint(IReadOnlyList<ActionTaskItem> openTasks, IReadOnlyList<ActionSprint> sprints)
    {
        var sprintLookup = sprints.ToDictionary(s => s.Id);
        return openTasks
            .Where(t => t.SprintId.HasValue && sprintLookup.TryGetValue(t.SprintId.Value, out var sprint) && sprint.Status != ActionSprintStatus.Closed)
            .GroupBy(t => sprintLookup[t.SprintId!.Value])
            .OrderBy(g => g.Key.StartDate)
            .ThenBy(g => g.Key.Id)
            .Select(g => new ActionTaskQueryService.CountSummary(g.Key.Name, g.Count()))
            .ToList();
    }

    private static bool IsOpen(ActionTaskItem task) => !string.Equals(task.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase);
}
