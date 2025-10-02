using System;
using ProjectManagement.Models.Execution;

namespace ProjectManagement.Services.Stages;

public static class StageBackfillRules
{
    public static bool RequiresBackfill(ProjectStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        return stage.RequiresBackfill || IsMissingRequiredDates(stage);
    }

    public static bool IsMissingRequiredDates(ProjectStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        return !stage.ActualStart.HasValue || !stage.CompletedOn.HasValue;
    }
}
