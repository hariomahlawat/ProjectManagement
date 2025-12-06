using ProjectManagement.Models.Execution;

namespace ProjectManagement.Services.Stages;

internal static class StageTransitionPolicy
{
    public static bool TryValidateTransition(StageStatus current, StageStatus target, DateOnly? targetDate, out string? error)
    {
        // SECTION: Basic validation
        if (current == target)
        {
            error = "The stage is already in the requested status.";
            return false;
        }

        error = null;
        return true;
    }
}
