using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class PresentStageHelperTests
{
    [Fact]
    public void Resolve_Project_PrefersExplicitInProgressStage()
    {
        var project = new Project
        {
            Id = 10,
            Name = "Project",
            CreatedByUserId = "seed"
        };
        project.ProjectStages.Add(new ProjectStage
        {
            StageCode = StageCodes.DEVP,
            SortOrder = 999,
            Status = StageStatus.InProgress
        });

        var current = PresentStageHelper.Resolve(project);

        Assert.NotNull(current);
        Assert.Equal(StageCodes.DEVP, current!.StageCode);
    }

    [Fact]
    public void Resolve_Project_UsesWorkflowOrderWhenPersistedSortOrderIsStale()
    {
        var project = new Project
        {
            Id = 11,
            Name = "Project",
            CreatedByUserId = "seed"
        };
        project.ProjectStages.Add(new ProjectStage
        {
            StageCode = StageCodes.FS,
            SortOrder = 900,
            Status = StageStatus.Completed,
            CompletedOn = new DateOnly(2026, 1, 1)
        });

        var current = PresentStageHelper.Resolve(project);

        Assert.NotNull(current);
        Assert.Equal(StageCodes.SOW, current!.StageCode);
        Assert.Equal(StageStatus.NotStarted, current.Status);
    }

    [Fact]
    public void ComputePresentStageAndAge_InsertsMissingWorkflowStagesInCanonicalOrder()
    {
        var stages = new[]
        {
            new ProjectStageStatusSnapshot(
                StageCodes.FS,
                StageStatus.Completed,
                900,
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 1, 10))
        };

        var snapshot = PresentStageHelper.ComputePresentStageAndAge(
            stages,
            new WorkflowStageMetadataProvider(),
            ProcurementWorkflow.VersionV1,
            new DateOnly(2026, 1, 15));

        Assert.Equal(StageCodes.SOW, snapshot.CurrentStageCode);
        Assert.False(snapshot.IsCurrentStageInProgress);
        Assert.Equal(new DateOnly(2026, 1, 10), snapshot.LastCompletedDate);
    }

    [Fact]
    public void ComputePresentStageAndAge_UsesFirstWorkflowStageWhenNoRowsExist()
    {
        var snapshot = PresentStageHelper.ComputePresentStageAndAge(
            Array.Empty<ProjectStageStatusSnapshot>(),
            new WorkflowStageMetadataProvider(),
            ProcurementWorkflow.VersionV1,
            new DateOnly(2026, 1, 15));

        Assert.Equal(StageCodes.FS, snapshot.CurrentStageCode);
        Assert.False(snapshot.IsCurrentStageInProgress);
    }
}
