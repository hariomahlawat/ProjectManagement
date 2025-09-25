using System;
using System.Collections.Generic;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Scheduling;

public sealed record ScheduleOptions(
    bool SkipWeekends,
    PlanTransitionRule TransitionRule,
    bool PncApplicable,
    DateOnly Today);

public interface IScheduleEngine
{
    IDictionary<string, (DateOnly start, DateOnly due)> ComputeForecast(
        IReadOnlyList<StageTemplate> templates,
        IReadOnlyList<StageDependencyTemplate> deps,
        IReadOnlyDictionary<string, int> durationsDays,
        IReadOnlyDictionary<string, ProjectStage> execution,
        ScheduleOptions opts);
}
