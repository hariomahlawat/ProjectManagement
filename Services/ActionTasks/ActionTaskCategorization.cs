using System;
using ProjectManagement.Models;

namespace ProjectManagement.Services.ActionTasks;

public enum ActionTaskBucket
{
    Backlog,
    OutsideSprint,
    Sprint,
    Closed
}

public enum ActionTaskWorkCategory
{
    Sprint,
    Backlog,
    OutsideSprint,
    NonSprint,
    Closed
}

public static class ActionTaskBucketClassifier
{
    // SECTION: Central Agile Backlog bucket source of truth for backlog, outside-sprint, sprint, and closed work.
    public static ActionTaskBucket ResolveBucket(ActionTaskItem task)
    {
        if (IsClosedTask(task))
        {
            return ActionTaskBucket.Closed;
        }

        if (IsSprintTask(task))
        {
            return ActionTaskBucket.Sprint;
        }

        return HasAssignedUser(task)
            ? ActionTaskBucket.OutsideSprint
            : ActionTaskBucket.Backlog;
    }

    public static bool IsSprintTask(ActionTaskItem task)
        => task.SprintId.HasValue && IsOpenTask(task);

    public static bool IsBacklogTask(ActionTaskItem task)
        => task.SprintId is null && !HasAssignedUser(task) && IsOpenTask(task);

    public static bool IsOutsideSprintTask(ActionTaskItem task)
        => task.SprintId is null && HasAssignedUser(task) && IsOpenTask(task);

    public static bool IsAssignedNonSprintTask(ActionTaskItem task)
        => IsOutsideSprintTask(task);

    public static bool IsClosedTask(ActionTaskItem task)
        => string.Equals(task.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase);

    public static bool IsOpenTask(ActionTaskItem task)
        => !IsClosedTask(task);

    public static bool HasAssignedUser(ActionTaskItem task)
        => !string.IsNullOrWhiteSpace(task.AssignedToUserId);
}

public static class ActionTaskCategorization
{
    // SECTION: Backward-compatible category facade delegates to the central Agile Backlog bucket classifier.
    public static ActionTaskWorkCategory ResolveCategory(ActionTaskItem task)
        => ActionTaskBucketClassifier.ResolveBucket(task) switch
        {
            ActionTaskBucket.Sprint => ActionTaskWorkCategory.Sprint,
            ActionTaskBucket.OutsideSprint => ActionTaskWorkCategory.OutsideSprint,
            ActionTaskBucket.Closed => ActionTaskWorkCategory.Closed,
            _ => ActionTaskWorkCategory.Backlog
        };

    public static ActionTaskBucket ResolveBucket(ActionTaskItem task)
        => ActionTaskBucketClassifier.ResolveBucket(task);

    public static bool IsSprintTask(ActionTaskItem task)
        => ActionTaskBucketClassifier.IsSprintTask(task);

    public static bool IsBacklogTask(ActionTaskItem task)
        => ActionTaskBucketClassifier.IsBacklogTask(task);

    public static bool IsOutsideSprintTask(ActionTaskItem task)
        => ActionTaskBucketClassifier.IsOutsideSprintTask(task);

    public static bool IsAssignedNonSprintTask(ActionTaskItem task)
        => ActionTaskBucketClassifier.IsAssignedNonSprintTask(task);

    public static bool IsClosedTask(ActionTaskItem task)
        => ActionTaskBucketClassifier.IsClosedTask(task);

    public static bool IsOpenTask(ActionTaskItem task)
        => ActionTaskBucketClassifier.IsOpenTask(task);

    public static bool HasAssignedUser(ActionTaskItem task)
        => ActionTaskBucketClassifier.HasAssignedUser(task);
}
