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

        Assert.Equal(StageCodes.IPA, snapshot.CurrentStageCode);
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

    [Fact]
    public void ComputePresentStageAndAge_CompletedLifecycleUsesLatestCompletedStageInsteadOfVirtualNextStage()
    {
        var stages = new[]
        {
            new ProjectStageStatusSnapshot(
                StageCodes.FS,
                StageStatus.Completed,
                0,
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 1, 10))
        };

        var snapshot = PresentStageHelper.ComputePresentStageAndAge(
            stages,
            new WorkflowStageMetadataProvider(),
            ProcurementWorkflow.VersionV1,
            ProjectLifecycleStatus.Completed,
            new DateOnly(2026, 1, 15));

        Assert.Equal(StageCodes.FS, snapshot.CurrentStageCode);
        Assert.Equal(new DateOnly(2026, 1, 10), snapshot.LastCompletedDate);
    }


    [Fact]
    public void ComputePresentStageAndAge_BlockedStageWithRecordedStart_IsAgedFromThatStart()
    {
        var stages = new[]
        {
            new ProjectStageStatusSnapshot(StageCodes.FS, StageStatus.Completed, 0, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10)),
            new ProjectStageStatusSnapshot(StageCodes.SOW, StageStatus.Blocked, 1, new DateOnly(2026, 9, 12), null)
        };

        var snapshot = PresentStageHelper.ComputePresentStageAndAge(
            stages,
            new WorkflowStageMetadataProvider(),
            ProcurementWorkflow.VersionV2,
            new DateOnly(2026, 10, 22));

        Assert.Equal(StageCodes.SOW, snapshot.CurrentStageCode);
        Assert.False(snapshot.IsCurrentStageInProgress);
        // 40 days since the stage started, not 285 since FS was completed.
        Assert.Equal(40, snapshot.DaysSinceStartOrLastCompletion);
    }

    [Theory]
    [InlineData(StageStatus.InProgress, "2026-09-12", 40)]
    [InlineData(StageStatus.Blocked, "2026-09-12", 40)]
    [InlineData(StageStatus.Blocked, null, 285)]
    [InlineData(StageStatus.InProgress, null, 285)]
    [InlineData(StageStatus.NotStarted, "2026-09-12", 285)]
    public void CurrentStageAgeDays_UsesARecordedStartOnlyOnceTheStageHasBegun(StageStatus status, string? start, int expected)
    {
        var age = PresentStageHelper.CurrentStageAgeDays(
            status,
            start is null ? null : DateOnly.Parse(start),
            latestCompletion: new DateOnly(2026, 1, 10),
            today: new DateOnly(2026, 10, 22));

        Assert.Equal(expected, age);
    }

    [Fact]
    public void CurrentStageAgeDays_WithoutStartOrCompletion_IsUnknown()
    {
        Assert.Null(PresentStageHelper.CurrentStageAgeDays(StageStatus.InProgress, null, null, new DateOnly(2026, 10, 22)));
    }

    [Fact]
    public void WorkspaceStageAge_BlockedStageWithRecordedStart_MatchesTheSharedRule()
    {
        var project = new Project { Id = 12, Name = "Project", CreatedByUserId = "seed" };
        project.ProjectStages.Add(new ProjectStage
        {
            StageCode = StageCodes.FS,
            SortOrder = 0,
            Status = StageStatus.Completed,
            CompletedOn = new DateOnly(2026, 1, 10)
        });
        project.ProjectStages.Add(new ProjectStage
        {
            StageCode = StageCodes.SOW,
            SortOrder = 1,
            Status = StageStatus.Blocked,
            ActualStart = new DateOnly(2026, 9, 12)
        });

        Assert.Equal(40, ProjectManagement.Services.Workspace.WorkspaceNudgeService.GetCurrentStageAgeDays(project, new DateOnly(2026, 10, 22)));
    }

    [Fact]
    public void ComputePresentStageAndAge_ClosedLifecycleWithoutStageRowsRemainsUnassigned()
    {
        var snapshot = PresentStageHelper.ComputePresentStageAndAge(
            Array.Empty<ProjectStageStatusSnapshot>(),
            new WorkflowStageMetadataProvider(),
            ProcurementWorkflow.VersionV1,
            ProjectLifecycleStatus.Completed,
            new DateOnly(2026, 1, 15));

        Assert.Null(snapshot.CurrentStageCode);
    }

}
