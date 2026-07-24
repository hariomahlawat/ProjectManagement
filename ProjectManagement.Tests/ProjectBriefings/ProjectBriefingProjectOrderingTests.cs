using ProjectManagement.Models;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.ProjectBriefings;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

public sealed class ProjectBriefingProjectOrderingTests
{
    [Fact]
    public void OrderProjects_KeepsEveryStageContiguousAndUsesManualOrderWithinStage()
    {
        var projects = new[]
        {
            Project(1, "Development first in saved deck", StageCodes.DEVP, ProjectBriefingStageOrder.Development, 10),
            Project(2, "Completed later in saved deck", ProjectBriefingStageOrder.CompletedCode, ProjectBriefingStageOrder.Completed, 900, ProjectLifecycleStatus.Completed),
            Project(3, "AoN project", StageCodes.AON, ProjectBriefingStageOrder.AcceptanceOfNecessity, 20),
            Project(4, "Completed earlier in saved deck", ProjectBriefingStageOrder.CompletedCode, ProjectBriefingStageOrder.Completed, 30, ProjectLifecycleStatus.Completed),
            Project(5, "Development second", StageCodes.DEVP, ProjectBriefingStageOrder.Development, 40)
        };

        var ordered = ProjectBriefingProjectOrdering.OrderProjects(projects);

        Assert.Equal(new[] { 4, 2, 1, 5, 3 }, ordered.Select(project => project.ProjectId));
        Assert.Equal(
            new[]
            {
                ProjectBriefingStageOrder.Completed,
                ProjectBriefingStageOrder.Completed,
                ProjectBriefingStageOrder.Development,
                ProjectBriefingStageOrder.Development,
                ProjectBriefingStageOrder.AcceptanceOfNecessity
            },
            ordered.Select(project => project.PresentStageOrder));
    }

    private static ProjectBriefingPresentationProject Project(
        int id,
        string name,
        string stageCode,
        int stageOrder,
        int sortOrder,
        ProjectLifecycleStatus lifecycleStatus = ProjectLifecycleStatus.Active)
        => new()
        {
            ProjectId = id,
            ProjectName = name,
            PresentStageCode = stageCode,
            PresentStage = stageCode,
            PresentStageOrder = stageOrder,
            SortOrder = sortOrder,
            LifecycleStatus = lifecycleStatus
        };
}
