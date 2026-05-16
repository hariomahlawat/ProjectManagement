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
        var assignedOpenTasks = openTasks.Where(IsAssignedReportWork).ToList();
        var overdueAssignedTasks = assignedOpenTasks.Where(t => IsAssignedTaskOverdue(t, utcToday)).ToList();
        var submittedTasks = assignedOpenTasks.Where(IsSubmitted).ToList();
        var backlogTasks = reportTasks.Where(t => ActionTaskCategorization.ResolveBucket(t) == ActionTaskBucket.Backlog).ToList();
        var blockedTasks = assignedOpenTasks.Where(t => string.Equals(t.Status, ActionTaskStatuses.Blocked, StringComparison.OrdinalIgnoreCase)).ToList();

        return new ActionTaskQueryService.ActionTaskReportReadModel
        {
            TotalTaskCount = tasks.Count,
            FilteredTaskCount = reportTasks.Count,
            WorkloadSummary = BuildWorkloadSummary(openTasks, overdueAssignedTasks, blockedTasks, submittedTasks, backlogTasks),
            BucketDistribution = BuildBucketDistribution(reportTasks),
            ResponsiblePersonWorkloads = BuildResponsiblePersonWorkloads(assignedOpenTasks, overdueAssignedTasks, assigneeNames),
            BacklogAgeingBuckets = BuildBacklogAgeingBuckets(backlogTasks.Where(IsOpen).ToList(), utcToday),
            AssignedTaskAgeingBuckets = BuildAssignedTaskAgeingBuckets(assignedOpenTasks, utcToday),
            OverdueAgeingBuckets = BuildOverdueAgeingBuckets(overdueAssignedTasks, utcToday),
            SubmittedPendingClosureAgeingBuckets = BuildSubmittedPendingClosureAgeingBuckets(submittedTasks, utcToday),
            SprintPerformanceRows = BuildSprintPerformanceRows(reportTasks, request.Sprints, utcToday),
            InvalidStateRows = BuildInvalidStateRows(reportTasks),

            // SECTION: Legacy report summaries remain populated for existing consumers while the Reports UI uses the management model above.
            AssigneePendingCounts = BuildAssigneePendingCounts(assignedOpenTasks, assigneeNames),
            PriorityCounts = openTasks.GroupBy(t => t.Priority).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => new ActionTaskQueryService.CountSummary(g.Key, g.Count())).ToList(),
            StatusCounts = reportTasks.GroupBy(t => t.Status).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => new ActionTaskQueryService.CountSummary(g.Key, g.Count())).ToList(),
            OpenAgeingBuckets = BuildAssignedTaskAgeingBuckets(assignedOpenTasks, utcToday),
            OutsideSprintWorkloadCounts = BuildOutsideSprintWorkloadCounts(assignedOpenTasks.Where(t => ActionTaskCategorization.ResolveBucket(t) == ActionTaskBucket.OutsideSprint).ToList(), assigneeNames),
            BlockedAgeingBuckets = BuildAssignedTaskAgeingBuckets(blockedTasks, utcToday),
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
                ? query.Where(t => ActionTaskCategorization.ResolveBucket(t) == ActionTaskBucket.Backlog)
                : query.Where(t => t.SprintId == request.ReportSprintId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.ReportAssigneeUserId)) query = query.Where(t => string.Equals(t.AssignedToUserId, request.ReportAssigneeUserId, StringComparison.Ordinal));
        if (request.ReportFromDate.HasValue) { var d = request.ReportFromDate.Value.Date; query = query.Where(t => t.DueDate.Date >= d); }
        if (request.ReportToDate.HasValue) { var d = request.ReportToDate.Value.Date; query = query.Where(t => t.DueDate.Date <= d); }
        if (!string.IsNullOrWhiteSpace(request.ReportStatus)) query = query.Where(t => string.Equals(t.Status, request.ReportStatus, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(request.ReportPriority)) query = query.Where(t => string.Equals(t.Priority, request.ReportPriority, StringComparison.OrdinalIgnoreCase));
        return query;
    }

    // SECTION: Workload summary cards keep Reports at management level rather than duplicating operational boards.
    private static ActionTaskQueryService.ActionTaskReportSummary BuildWorkloadSummary(
        IReadOnlyList<ActionTaskItem> openTasks,
        IReadOnlyList<ActionTaskItem> overdueAssignedTasks,
        IReadOnlyList<ActionTaskItem> blockedTasks,
        IReadOnlyList<ActionTaskItem> submittedTasks,
        IReadOnlyList<ActionTaskItem> backlogTasks)
        => new()
        {
            OpenTasks = openTasks.Count,
            Overdue = overdueAssignedTasks.Count,
            Blocked = blockedTasks.Count,
            PendingClosure = submittedTasks.Count,
            BacklogItems = backlogTasks.Count
        };

    // SECTION: Official bucket distribution uses the central classifier and hides Invalid State when no records exist.
    private static IReadOnlyList<ActionTaskQueryService.CountSummary> BuildBucketDistribution(IReadOnlyList<ActionTaskItem> tasks)
    {
        var bucketCounts = tasks
            .GroupBy(ActionTaskCategorization.ResolveBucket)
            .ToDictionary(g => g.Key, g => g.Count());

        var buckets = new List<ActionTaskQueryService.CountSummary>
        {
            new("Backlog", bucketCounts.GetValueOrDefault(ActionTaskBucket.Backlog)),
            new("Outside Sprint", bucketCounts.GetValueOrDefault(ActionTaskBucket.OutsideSprint)),
            new("Sprint", bucketCounts.GetValueOrDefault(ActionTaskBucket.Sprint)),
            new("Closed", bucketCounts.GetValueOrDefault(ActionTaskBucket.Closed))
        };

        var invalidCount = bucketCounts.GetValueOrDefault(ActionTaskBucket.Invalid);
        if (invalidCount > 0)
        {
            buckets.Add(new ActionTaskQueryService.CountSummary("Invalid State", invalidCount));
        }

        return buckets;
    }

    // SECTION: Responsible workload excludes backlog and closed records, then sorts overload risk to the top.
    private static IReadOnlyList<ActionTaskQueryService.ResponsiblePersonWorkloadSummary> BuildResponsiblePersonWorkloads(
        IReadOnlyList<ActionTaskItem> assignedOpenTasks,
        IReadOnlyList<ActionTaskItem> overdueAssignedTasks,
        IReadOnlyDictionary<string, string> assigneeNames)
    {
        var overdueIds = overdueAssignedTasks.Select(t => t.Id).ToHashSet();
        return assignedOpenTasks
            .GroupBy(t => Assignee(t.AssignedToUserId, assigneeNames))
            .Select(g => new ActionTaskQueryService.ResponsiblePersonWorkloadSummary
            {
                ResponsiblePerson = g.Key,
                Open = g.Count(),
                Overdue = g.Count(t => overdueIds.Contains(t.Id)),
                Blocked = g.Count(t => string.Equals(t.Status, ActionTaskStatuses.Blocked, StringComparison.OrdinalIgnoreCase)),
                InProgress = g.Count(t => string.Equals(t.Status, ActionTaskStatuses.InProgress, StringComparison.OrdinalIgnoreCase)),
                Submitted = g.Count(IsSubmitted),
                Critical = g.Count(t => string.Equals(t.Priority, "Critical", StringComparison.OrdinalIgnoreCase))
            })
            .OrderByDescending(x => x.Open)
            .ThenByDescending(x => x.Overdue)
            .ThenBy(x => x.ResponsiblePerson)
            .ToList();
    }

    // SECTION: Backlog ageing is planning-inventory ageing; AssignedOn is a temporary backlog-entry proxy until a CreatedOnUtc or EnteredBacklogOnUtc field exists.
    private static IReadOnlyList<ActionTaskQueryService.CountSummary> BuildBacklogAgeingBuckets(IReadOnlyList<ActionTaskItem> backlogTasks, DateTime utcToday)
        => new[]
        {
            new ActionTaskQueryService.CountSummary("0-3 days in backlog", backlogTasks.Count(t => DaysSince(t.AssignedOn, utcToday) is >= 0 and <= 3)),
            new ActionTaskQueryService.CountSummary("4-7 days in backlog", backlogTasks.Count(t => DaysSince(t.AssignedOn, utcToday) is >= 4 and <= 7)),
            new ActionTaskQueryService.CountSummary("8-14 days in backlog", backlogTasks.Count(t => DaysSince(t.AssignedOn, utcToday) is >= 8 and <= 14)),
            new ActionTaskQueryService.CountSummary("15+ days in backlog", backlogTasks.Count(t => DaysSince(t.AssignedOn, utcToday) >= 15))
        };

    // SECTION: Assigned task ageing applies only to Outside Sprint and Sprint work, never backlog or closed tasks.
    private static IReadOnlyList<ActionTaskQueryService.CountSummary> BuildAssignedTaskAgeingBuckets(IReadOnlyList<ActionTaskItem> assignedTasks, DateTime utcToday)
        => new[]
        {
            new ActionTaskQueryService.CountSummary("0-3 days assigned", assignedTasks.Count(t => DaysSince(t.AssignedOn, utcToday) is >= 0 and <= 3)),
            new ActionTaskQueryService.CountSummary("4-7 days assigned", assignedTasks.Count(t => DaysSince(t.AssignedOn, utcToday) is >= 4 and <= 7)),
            new ActionTaskQueryService.CountSummary("8-14 days assigned", assignedTasks.Count(t => DaysSince(t.AssignedOn, utcToday) is >= 8 and <= 14)),
            new ActionTaskQueryService.CountSummary("15+ days assigned", assignedTasks.Count(t => DaysSince(t.AssignedOn, utcToday) >= 15))
        };

    // SECTION: Overdue analysis applies only to assigned work and excludes submitted pending-closure tasks.
    private static IReadOnlyList<ActionTaskQueryService.CountSummary> BuildOverdueAgeingBuckets(IReadOnlyList<ActionTaskItem> overdueTasks, DateTime utcToday)
        => new[]
        {
            new ActionTaskQueryService.CountSummary("1-3 days overdue", overdueTasks.Count(t => DaysOverdue(t, utcToday) is >= 1 and <= 3)),
            new ActionTaskQueryService.CountSummary("4-7 days overdue", overdueTasks.Count(t => DaysOverdue(t, utcToday) is >= 4 and <= 7)),
            new ActionTaskQueryService.CountSummary("8+ days overdue", overdueTasks.Count(t => DaysOverdue(t, utcToday) >= 8))
        };

    // SECTION: Pending closure ageing is separated from assignee overdue exposure.
    private static IReadOnlyList<ActionTaskQueryService.CountSummary> BuildSubmittedPendingClosureAgeingBuckets(IReadOnlyList<ActionTaskItem> submittedTasks, DateTime utcToday)
        => new[]
        {
            new ActionTaskQueryService.CountSummary("0-1 day pending closure", submittedTasks.Count(t => DaysSince(t.SubmittedOn ?? t.AssignedOn, utcToday) is >= 0 and <= 1)),
            new ActionTaskQueryService.CountSummary("2-3 days pending closure", submittedTasks.Count(t => DaysSince(t.SubmittedOn ?? t.AssignedOn, utcToday) is >= 2 and <= 3)),
            new ActionTaskQueryService.CountSummary("4+ days pending closure", submittedTasks.Count(t => DaysSince(t.SubmittedOn ?? t.AssignedOn, utcToday) >= 4))
        };

    // SECTION: Sprint performance is compact trend-style summary, not a Sprint Board duplicate.
    private static IReadOnlyList<ActionTaskQueryService.SprintPerformanceSummary> BuildSprintPerformanceRows(IReadOnlyList<ActionTaskItem> tasks, IReadOnlyList<ActionSprint> sprints, DateTime utcToday)
    {
        var sprintTasks = tasks.Where(t => t.SprintId.HasValue).ToLookup(t => t.SprintId!.Value);
        return sprints
            .Where(s => sprintTasks.Contains(s.Id))
            .OrderByDescending(s => s.Status == ActionSprintStatus.Active)
            .ThenByDescending(s => s.StartDate)
            .ThenBy(s => s.Id)
            .Select(s =>
            {
                var scopedTasks = sprintTasks[s.Id].ToList();
                var assignedOpen = scopedTasks.Where(t => IsOpen(t) && IsAssignedReportWork(t)).ToList();
                return new ActionTaskQueryService.SprintPerformanceSummary
                {
                    Sprint = s.Name,
                    Status = s.Status.ToString(),
                    Open = assignedOpen.Count,
                    Closed = scopedTasks.Count(t => string.Equals(t.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase)),
                    CarriedForward = s.Status == ActionSprintStatus.Closed ? 0 : assignedOpen.Count(t => !string.Equals(t.Status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase)),
                    OverdueAtClosure = s.Status == ActionSprintStatus.Closed
                        ? scopedTasks.Count(IsClosedLate)
                        : assignedOpen.Count(t => IsAssignedTaskOverdue(t, utcToday))
                };
            })
            .ToList();
    }

    // SECTION: Invalid state rows give compact data-quality correction guidance only when inconsistencies exist.
    private static IReadOnlyList<ActionTaskQueryService.InvalidTaskStateSummary> BuildInvalidStateRows(IReadOnlyList<ActionTaskItem> tasks)
        => tasks
            .Where(t => ActionTaskCategorization.ResolveBucket(t) == ActionTaskBucket.Invalid)
            .OrderBy(t => t.Id)
            .Select(t => new ActionTaskQueryService.InvalidTaskStateSummary
            {
                Task = $"AT-{t.Id}: {t.Title}",
                Issue = BuildInvalidIssue(t),
                SuggestedCorrection = BuildInvalidCorrection(t)
            })
            .ToList();

    // SECTION: Legacy count helpers retain stable data for callers not yet moved to the table model.
    private static IReadOnlyList<ActionTaskQueryService.CountSummary> BuildAssigneePendingCounts(IReadOnlyList<ActionTaskItem> tasks, IReadOnlyDictionary<string, string> assigneeNames)
        => tasks
            .GroupBy(t => Assignee(t.AssignedToUserId, assigneeNames))
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Select(g => new ActionTaskQueryService.CountSummary(g.Key, g.Count()))
            .ToList();

    private static IReadOnlyList<ActionTaskQueryService.CountSummary> BuildOutsideSprintWorkloadCounts(IReadOnlyList<ActionTaskItem> tasks, IReadOnlyDictionary<string, string> assigneeNames)
        => BuildAssigneePendingCounts(tasks, assigneeNames);

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

    private static string Assignee(string id, IReadOnlyDictionary<string, string> assigneeNames)
        => string.IsNullOrWhiteSpace(id) ? "Unassigned" : assigneeNames.TryGetValue(id, out var name) ? name : "User";

    private static bool IsAssignedReportWork(ActionTaskItem task)
    {
        var bucket = ActionTaskCategorization.ResolveBucket(task);
        return bucket is (ActionTaskBucket.OutsideSprint or ActionTaskBucket.Sprint) && IsOpen(task);
    }

    private static bool IsAssignedTaskOverdue(ActionTaskItem task, DateTime utcToday)
        => IsAssignedReportWork(task)
           && !IsSubmitted(task)
           && task.DueDate.Date < utcToday
           && ActionTaskCategorization.HasAssignedUser(task);

    private static bool IsSubmitted(ActionTaskItem task)
        => string.Equals(task.Status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase);

    private static bool IsClosedLate(ActionTaskItem task)
        => string.Equals(task.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase)
           && task.ClosedOn.HasValue
           && task.ClosedOn.Value.Date > task.DueDate.Date;

    private static bool IsOpen(ActionTaskItem task) => ActionTaskCategorization.IsOpenTask(task);

    private static int DaysSince(DateTime date, DateTime utcToday) => (int)(utcToday - date.Date).TotalDays;

    private static int DaysOverdue(ActionTaskItem task, DateTime utcToday) => (int)(utcToday - task.DueDate.Date).TotalDays;

    private static string BuildInvalidIssue(ActionTaskItem task)
    {
        if (task.SprintId.HasValue && !ActionTaskCategorization.HasAssignedUser(task)) return "SprintId exists but responsible person is missing";
        if (string.Equals(task.Status, ActionTaskStatuses.Backlog, StringComparison.OrdinalIgnoreCase) && ActionTaskCategorization.HasAssignedUser(task)) return "Backlog status has assignee";
        if (!string.Equals(task.Status, ActionTaskStatuses.Backlog, StringComparison.OrdinalIgnoreCase) && !ActionTaskCategorization.HasAssignedUser(task)) return "Assigned status has no responsible person";
        return "Task has inconsistent assignment or sprint data";
    }

    private static string BuildInvalidCorrection(ActionTaskItem task)
    {
        if (task.SprintId.HasValue && !ActionTaskCategorization.HasAssignedUser(task)) return "Assign responsible person or return to backlog.";
        if (string.Equals(task.Status, ActionTaskStatuses.Backlog, StringComparison.OrdinalIgnoreCase) && ActionTaskCategorization.HasAssignedUser(task)) return "Remove assignee or convert to assigned task.";
        if (!string.Equals(task.Status, ActionTaskStatuses.Backlog, StringComparison.OrdinalIgnoreCase) && !ActionTaskCategorization.HasAssignedUser(task)) return "Assign responsible person or return to backlog.";
        return "Review assignment, status and sprint fields.";
    }
}
