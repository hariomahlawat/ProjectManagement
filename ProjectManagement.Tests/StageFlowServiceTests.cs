using System;
using System.Collections.Generic;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Plans;
using Xunit;

namespace ProjectManagement.Tests;

public class StageFlowServiceTests
{
    private static StageFlowService CreateService(bool skipWeekends, PlanTransitionRule rule)
    {
        var dependencies = new[]
        {
            new StageDependencyTemplate { FromStageCode = "B", DependsOnStageCode = "A" },
            new StageDependencyTemplate { FromStageCode = "C", DependsOnStageCode = "A" },
            new StageDependencyTemplate { FromStageCode = "C", DependsOnStageCode = "B" }
        };

        return new StageFlowService(dependencies, skipWeekends, rule);
    }

    [Fact]
    public void ComputeAutoStart_RespectsNextWorkingDayRule()
    {
        var service = CreateService(skipWeekends: true, rule: PlanTransitionRule.NextWorkingDay);
        var completed = new Dictionary<string, DateOnly?>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = new DateOnly(2024, 1, 5)
        };

        var start = service.ComputeAutoStart("B", completed, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.Equal(new DateOnly(2024, 1, 8), start);
    }

    [Fact]
    public void ComputeAutoStart_IgnoresSkippedPredecessors()
    {
        var service = CreateService(skipWeekends: false, rule: PlanTransitionRule.SameDay);
        var completed = new Dictionary<string, DateOnly?>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = new DateOnly(2024, 2, 1),
            ["B"] = null
        };

        var skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "B" };
        var start = service.ComputeAutoStart("C", completed, skipped);

        Assert.Equal(new DateOnly(2024, 2, 1), start);
    }

    [Fact]
    public void ComputeAutoStart_ReturnsNullWhenPredecessorIncomplete()
    {
        var service = CreateService(skipWeekends: false, rule: PlanTransitionRule.SameDay);
        var completed = new Dictionary<string, DateOnly?>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = null
        };

        var start = service.ComputeAutoStart("B", completed, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.Null(start);
    }
}
