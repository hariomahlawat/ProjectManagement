using ProjectManagement.Models;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.ProjectBriefings;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

public sealed class ProjectBriefingStageOrderTests
{
    [Fact]
    public void Stages_ExposeCompleteCurrentWorkflowInMaturityFirstOrder()
    {
        var codes = ProjectBriefingStageOrder.Stages
            .Select(stage => stage.Code)
            .ToArray();

        Assert.Equal(new[]
        {
            ProjectBriefingStageOrder.CompletedCode,
            StageCodes.TOT,
            StageCodes.PAYMENT,
            StageCodes.ATP,
            StageCodes.DEVP,
            StageCodes.SO,
            StageCodes.EAS,
            StageCodes.PNC,
            StageCodes.COB,
            StageCodes.BM,
            StageCodes.TEC,
            StageCodes.BID,
            StageCodes.AON,
            StageCodes.IPA,
            StageCodes.SOW,
            StageCodes.FS
        }, codes);

        Assert.Equal(
            ProjectBriefingStageOrder.Stages.Select(stage => stage.Order).OrderBy(order => order),
            ProjectBriefingStageOrder.Stages.Select(stage => stage.Order));
    }

    [Fact]
    public void Resolve_UsesCurrentWorkflowRelationshipBetweenAonIpaSowAndFs()
    {
        var ranks = new[]
        {
            ProjectBriefingStageOrder.Resolve(ProjectLifecycleStatus.Active, StageCodes.AON),
            ProjectBriefingStageOrder.Resolve(ProjectLifecycleStatus.Active, StageCodes.IPA),
            ProjectBriefingStageOrder.Resolve(ProjectLifecycleStatus.Active, StageCodes.SOW),
            ProjectBriefingStageOrder.Resolve(ProjectLifecycleStatus.Active, StageCodes.FS)
        };

        Assert.Equal(ranks.OrderBy(value => value), ranks);
    }

    [Fact]
    public void BuildCompleteSummary_KeepsZeroCountStagesAndAddsOnlyOneUnresolvedRow()
    {
        var summary = ProjectBriefingStageOrder.BuildCompleteSummary(new[]
        {
            ProjectBriefingStageOrder.Completed,
            ProjectBriefingStageOrder.Completed,
            ProjectBriefingStageOrder.Development,
            ProjectBriefingStageOrder.AcceptanceOfNecessity,
            8_888
        });

        Assert.Equal(ProjectBriefingStageOrder.Stages.Count + 1, summary.Count);
        Assert.Equal(2, Assert.Single(summary, point => point.Label == "Completed").Count);
        Assert.Equal(1, Assert.Single(summary, point => point.Label == "Development").Count);
        Assert.Equal(0, Assert.Single(summary, point => point.Label == "In-Principle Approval").Count);
        Assert.Equal(0, Assert.Single(summary, point => point.Label == "Scope of Work Vetting").Count);
        Assert.Equal(1, Assert.Single(summary, point => point.Label == "Stage unresolved").Count);
    }

    [Fact]
    public void Resolve_PlacesClosureStagesBeforeDevelopmentAndUnknownStagesLast()
    {
        Assert.True(ProjectBriefingStageOrder.Payment < ProjectBriefingStageOrder.Development);
        Assert.True(ProjectBriefingStageOrder.AcceptanceTesting < ProjectBriefingStageOrder.Development);
        Assert.True(ProjectBriefingStageOrder.Development < ProjectBriefingStageOrder.SupplyOrder);
        Assert.True(ProjectBriefingStageOrder.InPrincipleApproval < ProjectBriefingStageOrder.SowVetting);
        Assert.True(ProjectBriefingStageOrder.SowVetting < ProjectBriefingStageOrder.Unknown);
        Assert.Equal(
            ProjectBriefingStageOrder.Unknown,
            ProjectBriefingStageOrder.Resolve(ProjectLifecycleStatus.Active, "UNMAPPED"));
    }
}
