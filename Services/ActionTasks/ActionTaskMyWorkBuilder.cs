using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models;

namespace ProjectManagement.Services.ActionTasks;

public sealed class ActionTaskMyWorkBuilder
{
    private readonly IActionTrackerClock _clock;

    public ActionTaskMyWorkBuilder(IActionTrackerClock clock)
    {
        _clock = clock;
    }

    // SECTION: Compose My Work queues in one place so page rendering cannot accidentally duplicate cards.
    public ActionTaskMyWorkQueues Build(IReadOnlyList<ActionTaskItem> tasks, ActionSprint? activeSprint)
    {
        var activeSprintTasks = activeSprint is null
            ? Array.Empty<ActionTaskItem>()
            : tasks.Where(t => t.SprintId == activeSprint.Id).ToArray();

        return new ActionTaskMyWorkQueues(
            Overdue: tasks.Where(IsTaskOverdue).OrderBy(t => t.DueDate).ThenBy(t => t.Id).ToList(),
            DueToday: tasks.Where(t => IsOpenTask(t) && t.DueDate.Date == _clock.UtcToday).OrderBy(t => StatusOrder(t.Status)).ThenBy(t => t.DueDate).ThenBy(t => t.Id).ToList(),
            InProgress: tasks.Where(IsTaskInProgress).OrderBy(t => t.DueDate).ThenBy(t => t.Id).ToList(),
            Submitted: tasks.Where(IsTaskSubmitted).OrderBy(t => t.SubmittedOn ?? t.DueDate).ThenBy(t => t.Id).ToList(),
            ActiveSprint: activeSprintTasks.OrderBy(t => StatusOrder(t.Status)).ThenBy(t => t.DueDate).ThenBy(t => t.Id).ToList(),
            ActionRequired: BuildPrimarySection(tasks, ActionTaskMyWorkQueueSection.ActionRequired),
            CurrentWork: BuildPrimarySection(tasks, ActionTaskMyWorkQueueSection.CurrentWork),
            SubmittedAwaitingClosure: BuildPrimarySection(tasks, ActionTaskMyWorkQueueSection.SubmittedAwaitingClosure),
            AllMyTasks: BuildPrimarySection(tasks, ActionTaskMyWorkQueueSection.AllMyTasks));
    }

    // SECTION: Priority-based My Work queue assigns each task to exactly one primary section.
    private IReadOnlyList<ActionTaskItem> BuildPrimarySection(IReadOnlyList<ActionTaskItem> tasks, ActionTaskMyWorkQueueSection section)
        => tasks
            .Select(task => new { Task = task, Section = ResolvePrimarySection(task) })
            .Where(item => item.Section == section)
            .Select(item => item.Task)
            .OrderBy(ResolvePriorityRank)
            .ThenBy(task => StatusOrder(task.Status))
            .ThenBy(task => task.DueDate)
            .ThenBy(task => task.Id)
            .ToList();

    // SECTION: My Work precedence preserves historical de-duplication across urgent, current, submitted and remaining queues.
    private ActionTaskMyWorkQueueSection ResolvePrimarySection(ActionTaskItem task)
    {
        if (IsTaskOverdue(task) || IsTaskDueToday(task) || IsTaskBlocked(task))
        {
            return ActionTaskMyWorkQueueSection.ActionRequired;
        }

        if (IsTaskInProgress(task))
        {
            return ActionTaskMyWorkQueueSection.CurrentWork;
        }

        if (IsTaskSubmitted(task))
        {
            return ActionTaskMyWorkQueueSection.SubmittedAwaitingClosure;
        }

        return ActionTaskMyWorkQueueSection.AllMyTasks;
    }

    // SECTION: My Work ordering keeps urgent work first inside compact workspace sections.
    private int ResolvePriorityRank(ActionTaskItem task)
    {
        if (IsTaskOverdue(task)) return 1;
        if (IsTaskDueToday(task)) return 2;
        if (IsTaskBlocked(task)) return 3;
        if (IsTaskInProgress(task)) return 4;
        if (IsTaskSubmitted(task)) return 5;
        return 6;
    }

    // SECTION: Shared My Work predicates centralize date and lifecycle checks behind the Action Tracker clock.
    private bool IsTaskOverdue(ActionTaskItem task) => IsOpenTask(task) && task.DueDate.Date < _clock.UtcToday;
    private bool IsTaskDueToday(ActionTaskItem task) => IsOpenTask(task) && task.DueDate.Date == _clock.UtcToday;
    private static bool IsTaskBlocked(ActionTaskItem task) => string.Equals(task.Status, ActionTaskStatuses.Blocked, StringComparison.OrdinalIgnoreCase);
    private static bool IsTaskInProgress(ActionTaskItem task) => string.Equals(task.Status, ActionTaskStatuses.InProgress, StringComparison.OrdinalIgnoreCase);
    private static bool IsTaskSubmitted(ActionTaskItem task) => string.Equals(task.Status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase);
    private static bool IsOpenTask(ActionTaskItem task) => !string.Equals(task.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase);

    // SECTION: Status ordering mirrors the existing page-level operational list ordering.
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

public sealed record ActionTaskMyWorkQueues(
    IReadOnlyList<ActionTaskItem> Overdue,
    IReadOnlyList<ActionTaskItem> DueToday,
    IReadOnlyList<ActionTaskItem> InProgress,
    IReadOnlyList<ActionTaskItem> Submitted,
    IReadOnlyList<ActionTaskItem> ActiveSprint,
    IReadOnlyList<ActionTaskItem> ActionRequired,
    IReadOnlyList<ActionTaskItem> CurrentWork,
    IReadOnlyList<ActionTaskItem> SubmittedAwaitingClosure,
    IReadOnlyList<ActionTaskItem> AllMyTasks)
{
    public static ActionTaskMyWorkQueues Empty { get; } = new(
        Array.Empty<ActionTaskItem>(),
        Array.Empty<ActionTaskItem>(),
        Array.Empty<ActionTaskItem>(),
        Array.Empty<ActionTaskItem>(),
        Array.Empty<ActionTaskItem>(),
        Array.Empty<ActionTaskItem>(),
        Array.Empty<ActionTaskItem>(),
        Array.Empty<ActionTaskItem>(),
        Array.Empty<ActionTaskItem>());
}

public enum ActionTaskMyWorkQueueSection
{
    ActionRequired,
    CurrentWork,
    SubmittedAwaitingClosure,
    AllMyTasks
}
