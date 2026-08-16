using ProjectManagement.Models;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests.Reports;

public sealed class ProjectStageMaturityOrderTests
{
    [Fact]
    public void Completed_lifecycle_overrides_any_stage_code()
    {
        Assert.Equal(
            ProjectStageMaturityOrder.Completed,
            ProjectStageMaturityOrder.Resolve(ProjectLifecycleStatus.Completed, StageCodes.FS));
        Assert.Equal(
            ProjectStageMaturityOrder.Completed,
            ProjectStageMaturityOrder.Resolve(ProjectLifecycleStatus.Completed, StageCodes.DEVP));
    }

    [Fact]
    public void Ongoing_stages_are_ranked_maturity_first()
    {
        Assert.True(ProjectStageMaturityOrder.Development < ProjectStageMaturityOrder.SupplyOrder);
        Assert.True(ProjectStageMaturityOrder.SupplyOrder < ProjectStageMaturityOrder.Pnc);
        Assert.True(ProjectStageMaturityOrder.Pnc < ProjectStageMaturityOrder.AcceptanceOfNecessity);
        Assert.True(ProjectStageMaturityOrder.AcceptanceOfNecessity < ProjectStageMaturityOrder.InPrincipleApproval);
        Assert.Equal(
            ProjectStageMaturityOrder.Unknown,
            ProjectStageMaturityOrder.Resolve(ProjectLifecycleStatus.Active, "UNMAPPED"));
    }
}
