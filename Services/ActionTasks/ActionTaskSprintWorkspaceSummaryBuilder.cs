using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models;

namespace ProjectManagement.Services.ActionTasks;

public sealed class ActionTaskSprintWorkspaceSummaryBuilder
{
    private readonly IActionTrackerClock _clock;

    public ActionTaskSprintWorkspaceSummaryBuilder(IActionTrackerClock clock)
    {
        _clock = clock;
    }

    // SECTION: Sprint workspace summary centralizes backlog, selected-sprint and active-sprint attention composition.
    public ActionTaskSprintWorkspaceSummary Build(ActionTaskSprintWorkspaceSummaryRequest request)
    {
        var activeSprintTasks = GetActiveSprintTasks(request.Tasks, request.ActiveSprint);

        return new ActionTaskSprintWorkspaceSummary(
            request.Sprints.Where(s => s.Status == ActionSprintStatus.Planned).OrderBy(s => s.StartDate).ThenBy(s => s.Id).ToList(),
            request.Sprints.Where(s => s.Status == ActionSprintStatus.Closed).OrderByDescending(s => s.EndDate).ThenByDescending(s => s.Id).ToList(),
            request.SelectedSprintTasks.Count(IsTaskOverdue),
            request.SelectedSprintTasks.Where(IsTaskOverdue).OrderBy(t => t.DueDate).ThenBy(t => t.Id).ToList(),
            request.SelectedSprintTasks.Where(IsBlocked).OrderBy(t => t.DueDate).ThenBy(t => t.Id).ToList(),
            request.SelectedSprintTasks.Where(IsSubmitted).OrderBy(t => t.SubmittedOn ?? t.DueDate).ThenBy(t => t.Id).ToList(),
            request.UnfinishedClosureTasks.OrderBy(t => t.DueDate).ThenBy(t => t.Id).ToList(),
            request.BacklogTasks.Count,
            request.BacklogTasks.Count(IsHighPriorityBacklogTask),
            request.BacklogTasks.Count(IsBacklogPastTarget),
            activeSprintTasks.OrderBy(t => StatusOrder(t.Status)).ThenBy(t => t.DueDate).ThenBy(t => t.Id).ToList(),
            activeSprintTasks.Where(IsTaskOverdue).OrderBy(t => t.DueDate).ThenBy(t => t.Id).ToList(),
            activeSprintTasks.Where(IsBlocked).OrderBy(t => t.DueDate).ThenBy(t => t.Id).ToList(),
            activeSprintTasks.Count(IsSubmitted),
            activeSprintTasks.Where(IsSubmitted).OrderBy(t => t.SubmittedOn ?? t.DueDate).ThenBy(t => t.Id).ToList());
    }

    // SECTION: Sprint summary predicates separate assigned overdue work from backlog target dates.
    private bool IsTaskOverdue(ActionTaskItem task)
        => IsOpenTask(task)
           && !IsSubmitted(task)
           && ActionTaskCategorization.HasAssignedUser(task)
           && ActionTaskBucketClassifier.ResolveBucket(task) != ActionTaskBucket.Invalid
           && task.DueDate.Date < _clock.UtcToday;

    private bool IsBacklogPastTarget(ActionTaskItem task)
        => ActionTaskCategorization.IsBacklogTask(task)
           && task.DueDate.Date < _clock.UtcToday;

    private static bool IsHighPriorityBacklogTask(ActionTaskItem task) => string.Equals(task.Priority, "High", StringComparison.OrdinalIgnoreCase) || string.Equals(task.Priority, "Critical", StringComparison.OrdinalIgnoreCase);
    private static bool IsBlocked(ActionTaskItem task) => string.Equals(task.Status, ActionTaskStatuses.Blocked, StringComparison.OrdinalIgnoreCase);
    private static bool IsSubmitted(ActionTaskItem task) => string.Equals(task.Status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase);
    private static bool IsOpenTask(ActionTaskItem task) => !string.Equals(task.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<ActionTaskItem> GetActiveSprintTasks(IReadOnlyList<ActionTaskItem> tasks, ActionSprint? activeSprint)
        => activeSprint is null ? Array.Empty<ActionTaskItem>() : tasks.Where(t => t.SprintId == activeSprint.Id).ToList();

    private static int StatusOrder(string status) => status switch
    {
        ActionTaskStatuses.Assigned => 1,
        ActionTaskStatuses.InProgress => 2,
        ActionTaskStatuses.Blocked => 3,
        ActionTaskStatuses.Submitted => 4,
        ActionTaskStatuses.Closed => 5,
        _ => 99
    };
}

public sealed record ActionTaskSprintWorkspaceSummaryRequest(
    IReadOnlyList<ActionTaskItem> Tasks,
    IReadOnlyList<ActionTaskItem> BacklogTasks,
    IReadOnlyList<ActionTaskItem> SelectedSprintTasks,
    IReadOnlyList<ActionTaskItem> UnfinishedClosureTasks,
    IReadOnlyList<ActionSprint> Sprints,
    ActionSprint? ActiveSprint);

public sealed record ActionTaskSprintWorkspaceSummary(
    IReadOnlyList<ActionSprint> PlannedSprints,
    IReadOnlyList<ActionSprint> ClosedSprints,
    int SelectedSprintOverdueCount,
    IReadOnlyList<ActionTaskItem> SprintAttentionOverdueTasks,
    IReadOnlyList<ActionTaskItem> SprintAttentionBlockedTasks,
    IReadOnlyList<ActionTaskItem> SprintAttentionSubmittedTasks,
    IReadOnlyList<ActionTaskItem> SprintCarryForwardCandidateTasks,
    int BacklogTotalCount,
    int BacklogHighPriorityCount,
    int BacklogOverdueCount,
    IReadOnlyList<ActionTaskItem> ActiveSprintTasks,
    IReadOnlyList<ActionTaskItem> ActiveSprintOverdueTasks,
    IReadOnlyList<ActionTaskItem> ActiveSprintBlockedTasks,
    int ActiveSprintSubmittedTaskCount,
    IReadOnlyList<ActionTaskItem> ActiveSprintSubmittedTasks)
{
    public static ActionTaskSprintWorkspaceSummary Empty { get; } = new(
        Array.Empty<ActionSprint>(),
        Array.Empty<ActionSprint>(),
        0,
        Array.Empty<ActionTaskItem>(),
        Array.Empty<ActionTaskItem>(),
        Array.Empty<ActionTaskItem>(),
        Array.Empty<ActionTaskItem>(),
        0,
        0,
        0,
        Array.Empty<ActionTaskItem>(),
        Array.Empty<ActionTaskItem>(),
        Array.Empty<ActionTaskItem>(),
        0,
        Array.Empty<ActionTaskItem>());
}
