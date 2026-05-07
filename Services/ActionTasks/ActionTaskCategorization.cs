using System;
using ProjectManagement.Models;

namespace ProjectManagement.Services.ActionTasks;

public enum ActionTaskWorkCategory
{
    Sprint,
    Backlog,
    NonSprint,
    Closed
}

public static class ActionTaskCategorization
{
    // SECTION: Official task category source of truth for sprint, backlog, non-sprint, and closed work.
    public static ActionTaskWorkCategory ResolveCategory(ActionTaskItem task)
    {
        if (IsClosedTask(task))
        {
            return ActionTaskWorkCategory.Closed;
        }

        if (IsSprintTask(task))
        {
            return ActionTaskWorkCategory.Sprint;
        }

        return HasAssignedUser(task)
            ? ActionTaskWorkCategory.NonSprint
            : ActionTaskWorkCategory.Backlog;
    }

    public static bool IsSprintTask(ActionTaskItem task)
        => task.SprintId.HasValue && IsOpenTask(task);

    public static bool IsBacklogTask(ActionTaskItem task)
        => task.SprintId is null && !HasAssignedUser(task) && IsOpenTask(task);

    public static bool IsAssignedNonSprintTask(ActionTaskItem task)
        => task.SprintId is null && HasAssignedUser(task) && IsOpenTask(task);

    public static bool IsClosedTask(ActionTaskItem task)
        => string.Equals(task.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase);

    public static bool IsOpenTask(ActionTaskItem task)
        => !IsClosedTask(task);

    public static bool HasAssignedUser(ActionTaskItem task)
        => !string.IsNullOrWhiteSpace(task.AssignedToUserId);
}
