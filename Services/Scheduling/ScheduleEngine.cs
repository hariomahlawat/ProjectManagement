using System;
using System.Collections.Generic;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Scheduling;

public class ScheduleEngine : IScheduleEngine
{
    public IDictionary<string, (DateOnly start, DateOnly due)> ComputeForecast(
        IReadOnlyList<StageTemplate> templates,
        IReadOnlyList<StageDependencyTemplate> deps,
        IReadOnlyDictionary<string, int> durationsDays,
        IReadOnlyDictionary<string, ProjectStage> execution,
        ScheduleOptions opts)
    {
        throw new NotImplementedException();
    }
}
