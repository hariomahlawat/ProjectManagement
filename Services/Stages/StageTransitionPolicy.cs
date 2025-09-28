using ProjectManagement.Models.Execution;

namespace ProjectManagement.Services.Stages;

internal static class StageTransitionPolicy
{
    public static bool TryValidateTransition(StageStatus current, StageStatus target, DateOnly? targetDate, out string? error)
    {
        if (current == target)
        {
            error = "The stage is already in the requested status.";
            return false;
        }

        return target switch
        {
            StageStatus.InProgress => ValidateStartTransition(current, targetDate, out error),
            StageStatus.Completed => ValidateCompleteTransition(current, out error),
            StageStatus.Blocked => ValidateBlockTransition(current, out error),
            StageStatus.Skipped => ValidateSkipTransition(current, out error),
            StageStatus.NotStarted => ValidateReopenTransition(current, out error),
            _ => DenyTransition(current, target, out error)
        };
    }

    private static bool ValidateStartTransition(StageStatus current, DateOnly? targetDate, out string? error)
    {
        error = null;

        return current switch
        {
            StageStatus.NotStarted => true,
            StageStatus.Blocked => true,
            StageStatus.Skipped => true,
            StageStatus.Completed when targetDate.HasValue => true,
            StageStatus.Completed => Deny(
                "Reopening a completed stage to InProgress requires an actual start date.",
                out error),
            _ => DenyTransition(current, StageStatus.InProgress, out error)
        };
    }

    private static bool ValidateCompleteTransition(StageStatus current, out string? error)
    {
        error = null;
        return current switch
        {
            StageStatus.NotStarted => true,
            StageStatus.InProgress => true,
            _ => DenyTransition(current, StageStatus.Completed, out error)
        };
    }

    private static bool ValidateBlockTransition(StageStatus current, out string? error)
    {
        if (current == StageStatus.Completed)
        {
            error = "Completed stages cannot be blocked.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool ValidateSkipTransition(StageStatus current, out string? error)
    {
        if (current != StageStatus.NotStarted)
        {
            error = "Only stages that have not started can be skipped.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool ValidateReopenTransition(StageStatus current, out string? error)
    {
        if (current is StageStatus.Completed or StageStatus.Skipped or StageStatus.Blocked)
        {
            error = null;
            return true;
        }

        error = "Only completed, skipped, or blocked stages can be reopened to NotStarted.";
        return false;
    }

    private static bool DenyTransition(StageStatus current, StageStatus target, out string? error)
    {
        error = $"Changing from {current} to {target} is not allowed.";
        return false;
    }

    private static bool Deny(string message, out string? error)
    {
        error = message;
        return false;
    }
}
