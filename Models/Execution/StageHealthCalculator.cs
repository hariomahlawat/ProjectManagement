using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectManagement.Models.Execution;

public enum ProjectRagStatus
{
    Green,
    Amber,
    Red
}

public record ProjectStageHealth(IReadOnlyDictionary<string, int> SlipByStage, ProjectRagStatus Rag)
{
    public static ProjectStageHealth Empty { get; } = new(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), ProjectRagStatus.Green);
}

public static class StageHealthCalculator
{
    public static ProjectStageHealth Compute(IEnumerable<ProjectStage> stages, DateOnly today)
    {
        if (stages == null)
        {
            throw new ArgumentNullException(nameof(stages));
        }

        var slipByStage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var rag = ProjectRagStatus.Green;

        foreach (var stage in stages)
        {
            var slip = CalculateSlip(stage, today);
            slipByStage[stage.StageCode] = slip;

            if (slip >= 7)
            {
                rag = ProjectRagStatus.Red;
            }
            else if (slip >= 1 && rag != ProjectRagStatus.Red)
            {
                rag = ProjectRagStatus.Amber;
            }

            if (rag == ProjectRagStatus.Red)
            {
                continue;
            }

            if (stage.Status is StageStatus.Completed or StageStatus.Skipped)
            {
                continue;
            }

            if (stage.PlannedDue.HasValue)
            {
                var daysToDue = stage.PlannedDue.Value.DayNumber - today.DayNumber;
                if (daysToDue >= 0 && daysToDue <= 2 && rag == ProjectRagStatus.Green)
                {
                    rag = ProjectRagStatus.Amber;
                }
            }
        }

        return new ProjectStageHealth(slipByStage, rag);
    }

    private static int CalculateSlip(ProjectStage stage, DateOnly today)
    {
        if (stage.Status == StageStatus.Completed)
        {
            if (stage.CompletedOn.HasValue && stage.PlannedDue.HasValue)
            {
                var diff = stage.CompletedOn.Value.DayNumber - stage.PlannedDue.Value.DayNumber;
                return Math.Max(0, diff);
            }

            return 0;
        }

        if (stage.Status == StageStatus.Skipped)
        {
            return 0;
        }

        if (!stage.PlannedDue.HasValue)
        {
            return 0;
        }

        var overdue = today.DayNumber - stage.PlannedDue.Value.DayNumber;
        return Math.Max(0, overdue);
    }
}
