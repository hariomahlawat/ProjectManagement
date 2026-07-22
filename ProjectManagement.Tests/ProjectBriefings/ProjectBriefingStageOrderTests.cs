using ProjectManagement.Models;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.ProjectBriefings;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

public sealed class ProjectBriefingStageOrderTests
{
    [Fact]
    public void Resolve_OrdersProjectsFromCompletionBackToEarlyApproval()
    {
        var ranks = new[]
        {
            ProjectBriefingStageOrder.Resolve(ProjectLifecycleStatus.Completed, StageCodes.PAYMENT),
            ProjectBriefingStageOrder.Resolve(ProjectLifecycleStatus.Active, StageCodes.DEVP),
            ProjectBriefingStageOrder.Resolve(ProjectLifecycleStatus.Active, StageCodes.SO),
            ProjectBriefingStageOrder.Resolve(ProjectLifecycleStatus.Active, StageCodes.EAS),
            ProjectBriefingStageOrder.Resolve(ProjectLifecycleStatus.Active, StageCodes.PNC),
            ProjectBriefingStageOrder.Resolve(ProjectLifecycleStatus.Active, StageCodes.COB),
            ProjectBriefingStageOrder.Resolve(ProjectLifecycleStatus.Active, StageCodes.BM),
            ProjectBriefingStageOrder.Resolve(ProjectLifecycleStatus.Active, StageCodes.TEC),
            ProjectBriefingStageOrder.Resolve(ProjectLifecycleStatus.Active, StageCodes.BID),
            ProjectBriefingStageOrder.Resolve(ProjectLifecycleStatus.Active, StageCodes.AON),
            ProjectBriefingStageOrder.Resolve(ProjectLifecycleStatus.Active, StageCodes.SOW),
            ProjectBriefingStageOrder.Resolve(ProjectLifecycleStatus.Active, StageCodes.IPA)
        };

        Assert.Equal(ranks.OrderBy(value => value), ranks);
    }

    [Fact]
    public void Resolve_PlacesClosureStagesBeforeDevelopmentAndUnknownStagesLast()
    {
        Assert.True(ProjectBriefingStageOrder.Payment < ProjectBriefingStageOrder.Development);
        Assert.True(ProjectBriefingStageOrder.AcceptanceTesting < ProjectBriefingStageOrder.Development);
        Assert.True(ProjectBriefingStageOrder.Development < ProjectBriefingStageOrder.SupplyOrder);
        Assert.True(ProjectBriefingStageOrder.InPrincipleApproval < ProjectBriefingStageOrder.Unknown);
        Assert.Equal(
            ProjectBriefingStageOrder.Unknown,
            ProjectBriefingStageOrder.Resolve(ProjectLifecycleStatus.Active, "UNMAPPED"));
    }
}
