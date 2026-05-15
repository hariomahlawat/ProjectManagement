using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models;

namespace ProjectManagement.Services.ActionTasks;

public sealed class ActionTaskMyWorkQueueBuilder
{
    private readonly IActionTrackerClock _clock;

    public ActionTaskMyWorkQueueBuilder(IActionTrackerClock clock)
    {
        _clock = clock;
    }

    // SECTION: My Work queue composition centralizes section precedence and de-duplication.
    public ActionTaskMyWorkQueueReadModel Build(IReadOnlyList<ActionTaskItem> tasks, ActionSprint? activeSprint)
    {
        var openTasks = tasks.Where(IsOpenTask).ToList();
        var sectionedTasks = openTasks
            .Select(task => new SectionedTask(task, ResolveMyWorkQueueSection(task)))
            .ToList();

        return new ActionTaskMyWorkQueueReadModel(
            BuildOverdueTasks(openTasks),
            BuildDueTodayTasks(openTasks),
            BuildInProgressTasks(openTasks),
            BuildSubmittedTasks(openTasks),
            BuildActiveSprintTasks(openTasks, activeSprint),
            BuildQueueSection(sectionedTasks, ActionTaskMyWorkQueueSection.ActionRequired),
            BuildQueueSection(sectionedTasks, ActionTaskMyWorkQueueSection.CurrentWork),
            BuildQueueSection(sectionedTasks, ActionTaskMyWorkQueueSection.SubmittedAwaitingClosure),
            BuildQueueSection(sectionedTasks, ActionTaskMyWorkQueueSection.AllMyTasks));
    }

    // SECTION: My Work queue sections assign each task to exactly one primary section.
    public IReadOnlyList<ActionTaskItem> BuildMyWorkQueueSection(IReadOnlyList<ActionTaskItem> tasks, ActionTaskMyWorkQueueSection section)
        => BuildQueueSection(
            tasks.Where(IsOpenTask).Select(task => new SectionedTask(task, ResolveMyWorkQueueSection(task))).ToList(),
            section);

    // SECTION: Legacy personal list projections remain available for existing partials.
    private IReadOnlyList<ActionTaskItem> BuildOverdueTasks(IReadOnlyList<ActionTaskItem> tasks)
        => tasks
            .Where(IsTaskOverdue)
            .OrderBy(t => t.DueDate)
            .ThenBy(t => t.Id)
            .ToList();

    private IReadOnlyList<ActionTaskItem> BuildDueTodayTasks(IReadOnlyList<ActionTaskItem> tasks)
        => tasks
            .Where(t => IsOpenTask(t) && t.DueDate.Date == _clock.UtcToday)
            .OrderBy(t => StatusOrder(t.Status))
            .ThenBy(t => t.DueDate)
            .ThenBy(t => t.Id)
            .ToList();

    private static IReadOnlyList<ActionTaskItem> BuildInProgressTasks(IReadOnlyList<ActionTaskItem> tasks)
        => tasks
            .Where(IsTaskInProgress)
            .OrderBy(t => t.DueDate)
            .ThenBy(t => t.Id)
            .ToList();

    private static IReadOnlyList<ActionTaskItem> BuildSubmittedTasks(IReadOnlyList<ActionTaskItem> tasks)
        => tasks
            .Where(IsTaskSubmitted)
            .OrderBy(t => t.SubmittedOn ?? t.DueDate)
            .ThenBy(t => t.Id)
            .ToList();

    private static IReadOnlyList<ActionTaskItem> BuildActiveSprintTasks(IReadOnlyList<ActionTaskItem> tasks, ActionSprint? activeSprint)
        => activeSprint is null
            ? Array.Empty<ActionTaskItem>()
            : tasks
                .Where(t => t.SprintId == activeSprint.Id)
                .OrderBy(t => StatusOrder(t.Status))
                .ThenBy(t => t.DueDate)
                .ThenBy(t => t.Id)
                .ToList();

    private IReadOnlyList<ActionTaskItem> BuildQueueSection(IReadOnlyList<SectionedTask> sectionedTasks, ActionTaskMyWorkQueueSection section)
        => sectionedTasks
            .Where(item => item.Section == section)
            .Select(item => item.Task)
            .OrderBy(ResolveMyWorkPriorityRank)
            .ThenBy(task => StatusOrder(task.Status))
            .ThenBy(task => task.DueDate)
            .ThenBy(task => task.Id)
            .ToList();

    // SECTION: My Work precedence prevents repeated cards across action, execution, submitted, and remaining sections.
    private ActionTaskMyWorkQueueSection ResolveMyWorkQueueSection(ActionTaskItem task)
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

    // SECTION: My Work row ordering follows required urgency precedence inside compact sections.
    private int ResolveMyWorkPriorityRank(ActionTaskItem task)
    {
        if (IsTaskOverdue(task)) return 1;
        if (IsTaskDueToday(task)) return 2;
        if (IsTaskBlocked(task)) return 3;
        if (IsTaskInProgress(task)) return 4;
        if (IsTaskSubmitted(task)) return 5;
        return 6;
    }

    // SECTION: Shared My Work predicates keep queue grouping aligned with open-task lifecycle rules.
    private bool IsTaskOverdue(ActionTaskItem task) => IsOpenTask(task) && task.DueDate.Date < _clock.UtcToday;
    private bool IsTaskDueToday(ActionTaskItem task) => IsOpenTask(task) && task.DueDate.Date == _clock.UtcToday;
    private static bool IsTaskBlocked(ActionTaskItem task) => string.Equals(task.Status, ActionTaskStatuses.Blocked, StringComparison.OrdinalIgnoreCase);
    private static bool IsTaskInProgress(ActionTaskItem task) => string.Equals(task.Status, ActionTaskStatuses.InProgress, StringComparison.OrdinalIgnoreCase);
    private static bool IsTaskSubmitted(ActionTaskItem task) => string.Equals(task.Status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase);
    private static bool IsOpenTask(ActionTaskItem task) => !string.Equals(task.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase);

    // SECTION: Reusable status ordering mirrors operational list ordering.
    private static int StatusOrder(string status) => status switch
    {
        ActionTaskStatuses.Assigned => 1,
        ActionTaskStatuses.InProgress => 2,
        ActionTaskStatuses.Blocked => 3,
        ActionTaskStatuses.Submitted => 4,
        ActionTaskStatuses.Closed => 5,
        _ => 99
    };

    private sealed record SectionedTask(ActionTaskItem Task, ActionTaskMyWorkQueueSection Section);
}

public sealed record ActionTaskMyWorkQueueReadModel(
    IReadOnlyList<ActionTaskItem> OverdueTasks,
    IReadOnlyList<ActionTaskItem> DueTodayTasks,
    IReadOnlyList<ActionTaskItem> InProgressTasks,
    IReadOnlyList<ActionTaskItem> SubmittedTasks,
    IReadOnlyList<ActionTaskItem> ActiveSprintTasks,
    IReadOnlyList<ActionTaskItem> ActionRequiredTasks,
    IReadOnlyList<ActionTaskItem> CurrentWorkTasks,
    IReadOnlyList<ActionTaskItem> SubmittedAwaitingClosureTasks,
    IReadOnlyList<ActionTaskItem> AllMyTasks);

public enum ActionTaskMyWorkQueueSection
{
    ActionRequired,
    CurrentWork,
    SubmittedAwaitingClosure,
    AllMyTasks
}
