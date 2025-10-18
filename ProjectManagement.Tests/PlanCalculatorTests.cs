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
            new() { Code = StageCodes.FS, Name = "Feasibility", Sequence = 10 },
            new() { Code = StageCodes.IPA, Name = "In-Principle Approval", Sequence = 20 },
            new() { Code = StageCodes.SOW, Name = "Scope of Work", Sequence = 30 },
            new() { Code = StageCodes.AON, Name = "Acceptance of Necessity", Sequence = 40 },
            new() { Code = StageCodes.BID, Name = "Bid Upload", Sequence = 50 },
            new() { Code = StageCodes.TEC, Name = "Technical Evaluation", Sequence = 60 },
            new() { Code = StageCodes.BM, Name = "Benchmarking", Sequence = 65, ParallelGroup = "PRE_COB" },
            new() { Code = StageCodes.COB, Name = "Commercial Opening Board", Sequence = 70 },
            new() { Code = StageCodes.PNC, Name = "Price Negotiation Committee", Sequence = 80, Optional = true },
            new() { Code = StageCodes.EAS, Name = "Expenditure Angle Sanction", Sequence = 90 },
            new() { Code = StageCodes.SO, Name = "Supply Order", Sequence = 100 },
            new() { Code = StageCodes.DEVP, Name = "Development", Sequence = 110 },
            new() { Code = StageCodes.ATP, Name = "Acceptance Testing", Sequence = 120 },
            new() { Code = StageCodes.PAYMENT, Name = "Payment", Sequence = 130 },
            new() { Code = StageCodes.TOT, Name = "Transfer of Technology", Sequence = 140, Optional = true }
        };
    }

    private static IReadOnlyList<StageDependencyTemplate> CreateDependencies()
    {
        return new List<StageDependencyTemplate>
        {
            new() { FromStageCode = StageCodes.IPA, DependsOnStageCode = StageCodes.FS },
            new() { FromStageCode = StageCodes.SOW, DependsOnStageCode = StageCodes.IPA },
            new() { FromStageCode = StageCodes.AON, DependsOnStageCode = StageCodes.SOW },
            new() { FromStageCode = StageCodes.BID, DependsOnStageCode = StageCodes.AON },
            new() { FromStageCode = StageCodes.TEC, DependsOnStageCode = StageCodes.BID },
            new() { FromStageCode = StageCodes.BM, DependsOnStageCode = StageCodes.BID },
            new() { FromStageCode = StageCodes.COB, DependsOnStageCode = StageCodes.TEC },
            new() { FromStageCode = StageCodes.COB, DependsOnStageCode = StageCodes.BM },
            new() { FromStageCode = StageCodes.PNC, DependsOnStageCode = StageCodes.COB },
            new() { FromStageCode = StageCodes.EAS, DependsOnStageCode = StageCodes.COB },
            new() { FromStageCode = StageCodes.EAS, DependsOnStageCode = StageCodes.PNC },
            new() { FromStageCode = StageCodes.SO, DependsOnStageCode = StageCodes.EAS },
            new() { FromStageCode = StageCodes.DEVP, DependsOnStageCode = StageCodes.SO },
            new() { FromStageCode = StageCodes.ATP, DependsOnStageCode = StageCodes.DEVP },
            new() { FromStageCode = StageCodes.PAYMENT, DependsOnStageCode = StageCodes.ATP },
            new() { FromStageCode = StageCodes.TOT, DependsOnStageCode = StageCodes.PAYMENT }
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
            [StageCodes.IPA] = 1,
            [StageCodes.SOW] = 5,
            [StageCodes.AON] = 1,
            [StageCodes.BID] = 10,
            [StageCodes.TEC] = 3,
            [StageCodes.BM] = 4,
            [StageCodes.COB] = 2,
            [StageCodes.EAS] = 5
        };

        var options = new PlanCalculatorOptions(
            StageCodes.IPA,
            new DateOnly(2024, 1, 2),
            SkipWeekends: true,
            TransitionRule: PlanTransitionRule.NextWorkingDay,
            PncApplicable: false,
            durations,
            new Dictionary<string, PlanCalculatorManualOverride>(StringComparer.OrdinalIgnoreCase));

        var schedule = calculator.Compute(options);

        Assert.False(schedule.ContainsKey(StageCodes.PNC));
        Assert.True(schedule.TryGetValue(StageCodes.COB, out var cob));
        Assert.True(schedule.TryGetValue(StageCodes.EAS, out var eas));
        Assert.Equal(cob.due.AddDays(1), eas.start);
    }

    [Fact]
    public void ComputesScheduleWithPncEnabled()
    {
        var calculator = CreateCalculator();
        var durations = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [StageCodes.IPA] = 2,
            [StageCodes.SOW] = 4,
            [StageCodes.AON] = 2,
            [StageCodes.BID] = 6,
            [StageCodes.TEC] = 3,
            [StageCodes.BM] = 2,
            [StageCodes.COB] = 2,
            [StageCodes.PNC] = 7,
            [StageCodes.EAS] = 5
        };

        var options = new PlanCalculatorOptions(
            StageCodes.IPA,
            new DateOnly(2024, 4, 8),
            SkipWeekends: true,
            TransitionRule: PlanTransitionRule.NextWorkingDay,
            PncApplicable: true,
            durations,
            new Dictionary<string, PlanCalculatorManualOverride>(StringComparer.OrdinalIgnoreCase));

        var schedule = calculator.Compute(options);

        Assert.True(schedule.ContainsKey(StageCodes.PNC));
        Assert.True(schedule.TryGetValue(StageCodes.PNC, out var pnc));
        Assert.True(schedule.TryGetValue(StageCodes.EAS, out var eas));
        Assert.Equal(pnc.due.AddDays(1), eas.start);
    }

    [Fact]
    public void NextWorkingDaySkipsWeekend()
    {
        var calculator = CreateCalculator();
        var durations = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [StageCodes.IPA] = 1,
            [StageCodes.SOW] = 1
        };

        var options = new PlanCalculatorOptions(
            StageCodes.IPA,
            new DateOnly(2024, 1, 5),
            SkipWeekends: true,
            TransitionRule: PlanTransitionRule.NextWorkingDay,
            PncApplicable: true,
            durations,
            new Dictionary<string, PlanCalculatorManualOverride>(StringComparer.OrdinalIgnoreCase));

        var schedule = calculator.Compute(options);

        Assert.True(schedule.TryGetValue(StageCodes.SOW, out var sow));
        Assert.Equal(new DateOnly(2024, 1, 8), sow.start);
    }

    [Fact]
    public void SameDayRuleAllowsImmediateStart()
    {
        var calculator = CreateCalculator();
        var durations = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [StageCodes.IPA] = 3,
            [StageCodes.SOW] = 2
        };

        var options = new PlanCalculatorOptions(
            StageCodes.IPA,
            new DateOnly(2024, 2, 1),
            SkipWeekends: false,
            TransitionRule: PlanTransitionRule.SameDay,
            PncApplicable: true,
            durations,
            new Dictionary<string, PlanCalculatorManualOverride>(StringComparer.OrdinalIgnoreCase));

        var schedule = calculator.Compute(options);

        Assert.True(schedule.TryGetValue(StageCodes.IPA, out var ipa));
        Assert.True(schedule.TryGetValue(StageCodes.SOW, out var sow));
        Assert.Equal(ipa.due, sow.start);
    }

    [Fact]
    public void ManualOverrideRespectsEarliestStart()
    {
        var calculator = CreateCalculator();
        var durations = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [StageCodes.IPA] = 1,
            [StageCodes.SOW] = 1,
            [StageCodes.AON] = 1,
            [StageCodes.BID] = 1,
            [StageCodes.TEC] = 2
        };

        var overrides = new Dictionary<string, PlanCalculatorManualOverride>(StringComparer.OrdinalIgnoreCase)
        {
            [StageCodes.TEC] = new(new DateOnly(2024, 3, 15), null)
        };

        var options = new PlanCalculatorOptions(
            StageCodes.IPA,
            new DateOnly(2024, 3, 1),
            SkipWeekends: false,
            TransitionRule: PlanTransitionRule.NextWorkingDay,
            PncApplicable: true,
            durations,
            overrides);

        var schedule = calculator.Compute(options);

        Assert.True(schedule.TryGetValue(StageCodes.TEC, out var tec));
        Assert.Equal(new DateOnly(2024, 3, 15), tec.start);
    }
}
