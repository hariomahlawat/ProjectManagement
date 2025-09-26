using System;
using System.Collections.Generic;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Plans;
using Xunit;

namespace ProjectManagement.Tests;

public class PlanCalculatorTests
{
    private static IReadOnlyList<StageTemplate> CreateTemplates()
    {
        return new List<StageTemplate>
        {
            new() { Code = "FS", Name = "Feasibility", Sequence = 10 },
            new() { Code = "IPA", Name = "In-Principle Approval", Sequence = 20 },
            new() { Code = "SOW", Name = "Scope of Work", Sequence = 30 },
            new() { Code = "AON", Name = "Acceptance of Necessity", Sequence = 40 },
            new() { Code = "BID", Name = "Bid Upload", Sequence = 50 },
            new() { Code = "TEC", Name = "Technical Evaluation", Sequence = 60 },
            new() { Code = "BM", Name = "Benchmarking", Sequence = 65, ParallelGroup = "PRE_COB" },
            new() { Code = "COB", Name = "Commercial Opening Board", Sequence = 70 },
            new() { Code = "PNC", Name = "Price Negotiation Committee", Sequence = 80, Optional = true },
            new() { Code = "EAS", Name = "Expenditure Angle Sanction", Sequence = 90 },
            new() { Code = "SO", Name = "Supply Order", Sequence = 100 },
            new() { Code = "DEVP", Name = "Development", Sequence = 110 },
            new() { Code = "ATC", Name = "Acceptance Testing", Sequence = 120 },
            new() { Code = "PAYMENT", Name = "Payment", Sequence = 130 }
        };
    }

    private static IReadOnlyList<StageDependencyTemplate> CreateDependencies()
    {
        return new List<StageDependencyTemplate>
        {
            new() { FromStageCode = "IPA", DependsOnStageCode = "FS" },
            new() { FromStageCode = "SOW", DependsOnStageCode = "IPA" },
            new() { FromStageCode = "AON", DependsOnStageCode = "SOW" },
            new() { FromStageCode = "BID", DependsOnStageCode = "AON" },
            new() { FromStageCode = "TEC", DependsOnStageCode = "BID" },
            new() { FromStageCode = "BM", DependsOnStageCode = "BID" },
            new() { FromStageCode = "COB", DependsOnStageCode = "TEC" },
            new() { FromStageCode = "COB", DependsOnStageCode = "BM" },
            new() { FromStageCode = "PNC", DependsOnStageCode = "COB" },
            new() { FromStageCode = "EAS", DependsOnStageCode = "COB" },
            new() { FromStageCode = "EAS", DependsOnStageCode = "PNC" },
            new() { FromStageCode = "SO", DependsOnStageCode = "EAS" },
            new() { FromStageCode = "DEVP", DependsOnStageCode = "SO" },
            new() { FromStageCode = "ATC", DependsOnStageCode = "DEVP" },
            new() { FromStageCode = "PAYMENT", DependsOnStageCode = "ATC" }
        };
    }

    private static PlanCalculator CreateCalculator()
    {
        return new PlanCalculator(CreateTemplates(), CreateDependencies());
    }

    [Fact]
    public void ComputesScheduleWithPncDisabled()
    {
        var calculator = CreateCalculator();
        var durations = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["IPA"] = 1,
            ["SOW"] = 5,
            ["AON"] = 1,
            ["BID"] = 10,
            ["TEC"] = 3,
            ["BM"] = 4,
            ["COB"] = 2,
            ["EAS"] = 5
        };

        var options = new PlanCalculatorOptions(
            "IPA",
            new DateOnly(2024, 1, 2),
            SkipWeekends: true,
            TransitionRule: PlanTransitionRule.NextWorkingDay,
            PncApplicable: false,
            durations,
            new Dictionary<string, PlanCalculatorManualOverride>(StringComparer.OrdinalIgnoreCase));

        var schedule = calculator.Compute(options);

        Assert.False(schedule.ContainsKey("PNC"));
        Assert.True(schedule.TryGetValue("COB", out var cob));
        Assert.True(schedule.TryGetValue("EAS", out var eas));
        Assert.Equal(cob.due.AddDays(1), eas.start);
    }

    [Fact]
    public void ComputesScheduleWithPncEnabled()
    {
        var calculator = CreateCalculator();
        var durations = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["IPA"] = 2,
            ["SOW"] = 4,
            ["AON"] = 2,
            ["BID"] = 6,
            ["TEC"] = 3,
            ["BM"] = 2,
            ["COB"] = 2,
            ["PNC"] = 7,
            ["EAS"] = 5
        };

        var options = new PlanCalculatorOptions(
            "IPA",
            new DateOnly(2024, 4, 8),
            SkipWeekends: true,
            TransitionRule: PlanTransitionRule.NextWorkingDay,
            PncApplicable: true,
            durations,
            new Dictionary<string, PlanCalculatorManualOverride>(StringComparer.OrdinalIgnoreCase));

        var schedule = calculator.Compute(options);

        Assert.True(schedule.ContainsKey("PNC"));
        Assert.True(schedule.TryGetValue("PNC", out var pnc));
        Assert.True(schedule.TryGetValue("EAS", out var eas));
        Assert.Equal(pnc.due.AddDays(1), eas.start);
    }

    [Fact]
    public void NextWorkingDaySkipsWeekend()
    {
        var calculator = CreateCalculator();
        var durations = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["IPA"] = 1,
            ["SOW"] = 1
        };

        var options = new PlanCalculatorOptions(
            "IPA",
            new DateOnly(2024, 1, 5),
            SkipWeekends: true,
            TransitionRule: PlanTransitionRule.NextWorkingDay,
            PncApplicable: true,
            durations,
            new Dictionary<string, PlanCalculatorManualOverride>(StringComparer.OrdinalIgnoreCase));

        var schedule = calculator.Compute(options);

        Assert.True(schedule.TryGetValue("SOW", out var sow));
        Assert.Equal(new DateOnly(2024, 1, 8), sow.start);
    }

    [Fact]
    public void SameDayRuleAllowsImmediateStart()
    {
        var calculator = CreateCalculator();
        var durations = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["IPA"] = 3,
            ["SOW"] = 2
        };

        var options = new PlanCalculatorOptions(
            "IPA",
            new DateOnly(2024, 2, 1),
            SkipWeekends: false,
            TransitionRule: PlanTransitionRule.SameDay,
            PncApplicable: true,
            durations,
            new Dictionary<string, PlanCalculatorManualOverride>(StringComparer.OrdinalIgnoreCase));

        var schedule = calculator.Compute(options);

        Assert.True(schedule.TryGetValue("IPA", out var ipa));
        Assert.True(schedule.TryGetValue("SOW", out var sow));
        Assert.Equal(ipa.due, sow.start);
    }

    [Fact]
    public void ManualOverrideRespectsEarliestStart()
    {
        var calculator = CreateCalculator();
        var durations = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["IPA"] = 1,
            ["SOW"] = 1,
            ["AON"] = 1,
            ["BID"] = 1,
            ["TEC"] = 2
        };

        var overrides = new Dictionary<string, PlanCalculatorManualOverride>(StringComparer.OrdinalIgnoreCase)
        {
            ["TEC"] = new(new DateOnly(2024, 3, 15), null)
        };

        var options = new PlanCalculatorOptions(
            "IPA",
            new DateOnly(2024, 3, 1),
            SkipWeekends: false,
            TransitionRule: PlanTransitionRule.NextWorkingDay,
            PncApplicable: true,
            durations,
            overrides);

        var schedule = calculator.Compute(options);

        Assert.True(schedule.TryGetValue("TEC", out var tec));
        Assert.Equal(new DateOnly(2024, 3, 15), tec.start);
    }
}
