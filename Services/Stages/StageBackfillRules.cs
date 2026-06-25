using System;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Stages;

public static class StageBackfillRules
{
    public static bool RequiresBackfill(ProjectStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);

        if (stage.Status != StageStatus.Completed)
        {
            return false;
        }

        // For completed stages, completion date is authoritative. Actual start is optional.
        return !stage.CompletedOn.HasValue;
    }

    public static bool IsMissingRequiredDates(ProjectStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        return stage.Status == StageStatus.Completed && !stage.CompletedOn.HasValue;
    }
}
