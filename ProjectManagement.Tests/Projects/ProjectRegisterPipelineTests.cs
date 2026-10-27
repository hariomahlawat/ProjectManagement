using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests.Projects;

public sealed class ProjectRegisterPipelineTests
{
    private static readonly IReadOnlyList<WorkflowStageDefinition> Order =
        new WorkflowStageMetadataProvider().GetStages(null);

    // Two in BID (one late, one blocked and late), one in DEVP on plan and stale, one finished
    // in the last stage, one in DEVP with no PDC.
    private static readonly ProjectRegisterActiveFacts[] Sample =
    {
        Facts(1, StageCodes.BID, daysLate: 10),
        Facts(2, StageCodes.BID, ProjectRegisterStageState.Blocked, daysLate: 5),
        Facts(3, StageCodes.DEVP, daysLeft: 20, daysSinceUpdate: 45),
        Facts(4, Order[^1].Code, ProjectRegisterStageState.Finished, daysSinceUpdate: 60),
        Facts(5, StageCodes.DEVP)
    };

    [Fact]
    public void Stages_FollowTheWorkflowOrderWithOnPlanLateAndBlockedCounts()
    {
        var pipeline = ProjectRegisterPipeline.Build(Sample, Order, currentStage: null, attention: null);

        Assert.Equal(Order.Select(stage => stage.Code), pipeline.Stages.Select(stage => stage.Code));

        var bid = pipeline.Stages.Single(stage => stage.Code == StageCodes.BID);
        Assert.Equal((2, 0, 1, 1), (bid.Total, bid.OnPlan, bid.Late, bid.Blocked));

        var devp = pipeline.Stages.Single(stage => stage.Code == StageCodes.DEVP);
        Assert.Equal((2, 2, 0, 0), (devp.Total, devp.OnPlan, devp.Late, devp.Blocked));

        // A finished project is in no stage.
        Assert.Equal(4, pipeline.Stages.Sum(stage => stage.Total));
    }

    [Fact]
    public void AttentionCounts_IncludeEveryProjectThatMatches()
    {
        var pipeline = ProjectRegisterPipeline.Build(Sample, Order, currentStage: null, attention: null);

        Assert.Equal(
            new[] { (ProjectRegisterAttention.Late, 2), (ProjectRegisterAttention.Blocked, 1), (ProjectRegisterAttention.Stale, 2) },
            pipeline.Attention.Select(item => (item.Attention, item.Count)));
        Assert.Equal("Not updated in 30+ days", pipeline.Attention.Single(item => item.Attention == ProjectRegisterAttention.Stale).Label);
    }

    [Fact]
    public void EachControlFollowsTheOthersSelectionButNotItsOwn()
    {
        var pipeline = ProjectRegisterPipeline.Build(Sample, Order, currentStage: StageCodes.BID, attention: ProjectRegisterAttention.Stale);

        // Stage counts follow the Stale filter: only project 3 is stale and in a stage.
        Assert.Equal(1, pipeline.Stages.Sum(stage => stage.Total));
        Assert.Equal(1, pipeline.Stages.Single(stage => stage.Code == StageCodes.DEVP).Total);

        // Attention counts follow the BID stage, not the Stale selection.
        Assert.Equal(
            new[] { 2, 1, 0 },
            pipeline.Attention.Select(item => item.Count));
    }

    [Fact]
    public void Filters_MatchTheCurrentStageAndAttention()
    {
        Assert.True(Sample[0].IsCurrentlyIn("bid"));
        Assert.False(Sample[3].IsCurrentlyIn(Order[^1].Code));
        Assert.True(Sample[1].Matches(ProjectRegisterAttention.Late));
        Assert.True(Sample[1].Matches(ProjectRegisterAttention.Blocked));
        Assert.False(Sample[4].Matches(ProjectRegisterAttention.Stale));
    }

    private static ProjectRegisterActiveFacts Facts(
        int id,
        string stageCode,
        ProjectRegisterStageState state = ProjectRegisterStageState.InProgress,
        int? daysLate = null,
        int? daysLeft = null,
        int daysSinceUpdate = 3)
    {
        var today = new DateOnly(2026, 10, 22);
        DateOnly? pdc = daysLate is int late ? today.AddDays(-late) : daysLeft is int left ? today.AddDays(left) : null;
        return new ProjectRegisterActiveFacts(
            id,
            Array.Empty<ProjectRegisterStageMark>(),
            0,
            stageCode,
            stageCode,
            state,
            today.AddDays(-30),
            30,
            pdc,
            null,
            daysLate,
            daysLeft,
            IsProlonged: false,
            IsApproachingProlonged: false,
            today.AddDays(-daysSinceUpdate),
            daysSinceUpdate);
    }
}
