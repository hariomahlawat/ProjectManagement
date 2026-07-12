using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class OngoingStagePresentationPolicyTests
{
    [Theory]
    [InlineData(null, "reverse")]
    [InlineData("", "reverse")]
    [InlineData("unknown", "reverse")]
    [InlineData("reverse", "reverse")]
    [InlineData("forward", "forward")]
    [InlineData("FORWARD", "forward")]
    public void NormalizeStageFlow_DefaultsToLaterFirst(string? value, string expected)
    {
        Assert.Equal(expected, OngoingStagePresentationPolicy.NormalizeStageFlow(value));
    }

    [Fact]
    public void ResolveCurrentStageIndex_TreatsSkippedStagesAsTerminal()
    {
        var statuses = new[]
        {
            StageStatus.Completed,
            StageStatus.Skipped,
            StageStatus.NotStarted,
            StageStatus.NotStarted
        };

        var index = OngoingStagePresentationPolicy.ResolveCurrentStageIndex(statuses);

        Assert.Equal(2, index);
    }

    [Fact]
    public void ResolveCurrentStageIndex_PrefersExplicitInProgressStage()
    {
        var statuses = new[]
        {
            StageStatus.Completed,
            StageStatus.NotStarted,
            StageStatus.InProgress,
            StageStatus.NotStarted
        };

        var index = OngoingStagePresentationPolicy.ResolveCurrentStageIndex(statuses);

        Assert.Equal(2, index);
    }

    [Fact]
    public void IsProlonged_DevelopmentUsesSixCalendarMonths()
    {
        var snapshot = BuildSnapshot(
            StageCodes.DEVP,
            new DateOnly(2026, 1, 31));

        Assert.False(OngoingStagePresentationPolicy.IsProlonged(
            StageCodes.DEVP,
            snapshot,
            new DateOnly(2026, 7, 30)));

        Assert.True(OngoingStagePresentationPolicy.IsProlonged(
            StageCodes.DEVP,
            snapshot,
            new DateOnly(2026, 7, 31)));
    }

    [Fact]
    public void IsProlonged_NonDevelopmentStageRetainsNinetyDayRule()
    {
        var anchor = new DateOnly(2026, 1, 1);
        var snapshot = BuildSnapshot(StageCodes.TEC, anchor);

        Assert.False(OngoingStagePresentationPolicy.IsProlonged(
            StageCodes.TEC,
            snapshot,
            anchor.AddDays(89)));

        Assert.True(OngoingStagePresentationPolicy.IsProlonged(
            StageCodes.TEC,
            snapshot,
            anchor.AddDays(90)));
    }

    [Fact]
    public void IsApproachingProlonged_DevelopmentUsesThreeCalendarMonths()
    {
        var snapshot = BuildSnapshot(
            StageCodes.DEVP,
            new DateOnly(2026, 2, 28));

        Assert.False(OngoingStagePresentationPolicy.IsApproachingProlonged(
            StageCodes.DEVP,
            snapshot,
            new DateOnly(2026, 5, 27)));

        Assert.True(OngoingStagePresentationPolicy.IsApproachingProlonged(
            StageCodes.DEVP,
            snapshot,
            new DateOnly(2026, 5, 28)));
    }

    private static PresentStageSnapshot BuildSnapshot(string stageCode, DateOnly actualStart)
        => new(
            stageCode,
            StageCodes.DisplayNameOf(stageCode),
            true,
            null,
            actualStart,
            null);
}
