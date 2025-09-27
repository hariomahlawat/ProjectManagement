using System.Collections.Generic;
using ProjectManagement.Models.Execution;

namespace ProjectManagement.Services.Stages;

internal static class StageTransitionRules
{
    private static readonly IReadOnlyDictionary<StageStatus, IReadOnlyCollection<StageStatus>> AllowedTransitions =
        new Dictionary<StageStatus, IReadOnlyCollection<StageStatus>>
        {
            [StageStatus.NotStarted] = new[] { StageStatus.InProgress, StageStatus.Blocked },
            [StageStatus.InProgress] = new[] { StageStatus.Completed, StageStatus.Blocked },
            [StageStatus.Blocked] = new[] { StageStatus.InProgress }
        };

    public static bool IsTransitionAllowed(StageStatus fromStatus, StageStatus toStatus)
    {
        return AllowedTransitions.TryGetValue(fromStatus, out var allowed) && allowed.Contains(toStatus);
    }
}
