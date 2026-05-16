using System;
using ProjectManagement.Models;

namespace ProjectManagement.Services.ActionTasks;

public enum ActionTaskBucket
{
    Backlog,
    OutsideSprint,
    Sprint,
    Closed,
    Invalid
}

public enum ActionTaskWorkCategory
{
    Sprint,
    Backlog,
    OutsideSprint,
    NonSprint,
    Closed,
    Invalid
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

        if (task.SprintId.HasValue)
        {
            return HasAssignedUser(task) && HasAssignedRole(task)
                ? ActionTaskBucket.Sprint
                : ActionTaskBucket.Invalid;
        }

        if (IsBacklogTask(task))
        {
            return ActionTaskBucket.Backlog;
        }

        return HasAssignedUser(task) && HasAssignedRole(task)
            ? ActionTaskBucket.OutsideSprint
            : ActionTaskBucket.Invalid;
    }

    public static bool IsSprintTask(ActionTaskItem task)
        => task.SprintId.HasValue && HasAssignedUser(task) && HasAssignedRole(task) && IsSprintStatus(task.Status);

    public static bool IsBacklogTask(ActionTaskItem task)
        => task.SprintId is null
           && !HasAssignedUser(task)
           && !HasAssignedRole(task)
           && string.Equals(task.Status, ActionTaskStatuses.Backlog, StringComparison.OrdinalIgnoreCase);

    public static bool IsOutsideSprintTask(ActionTaskItem task)
        => task.SprintId is null && HasAssignedUser(task) && HasAssignedRole(task) && IsAssignedWorkStatus(task.Status);

    public static bool IsAssignedNonSprintTask(ActionTaskItem task)
        => IsOutsideSprintTask(task);

    public static bool IsClosedTask(ActionTaskItem task)
        => string.Equals(task.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase);

    public static bool IsOpenTask(ActionTaskItem task)
        => !IsClosedTask(task);

    public static bool HasAssignedUser(ActionTaskItem task)
        => !string.IsNullOrWhiteSpace(task.AssignedToUserId);

    public static bool HasAssignedRole(ActionTaskItem task)
        => !string.IsNullOrWhiteSpace(task.AssignedToRole);

    // SECTION: Official Agile Backlog execution statuses for assigned work buckets.
    private static bool IsSprintStatus(string status)
        => IsAssignedWorkStatus(status)
           || string.Equals(status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase);

    private static bool IsAssignedWorkStatus(string status)
        => string.Equals(status, ActionTaskStatuses.Assigned, StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, ActionTaskStatuses.InProgress, StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, ActionTaskStatuses.Blocked, StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase);
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
            ActionTaskBucket.Backlog => ActionTaskWorkCategory.Backlog,
            _ => ActionTaskWorkCategory.Invalid
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

    public static bool HasAssignedRole(ActionTaskItem task)
        => ActionTaskBucketClassifier.HasAssignedRole(task);
}
