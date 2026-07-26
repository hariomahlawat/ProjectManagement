using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Projects;
using ProjectManagement.ViewModels;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectLifecyclePositionResolverTests
{
    private readonly WorkflowStageMetadataProvider _workflowMetadata = new();

    [Fact]
    public void RepositoryPosition_UsesWorkflowOrderInsteadOfStalePersistedSortOrder()
    {
        var project = new Project
        {
            Id = 213,
            Name = "SPRINT",
            CreatedByUserId = "seed",
            WorkflowVersion = ProcurementWorkflow.VersionV2
        };

        project.ProjectStages = new List<ProjectStage>
        {
            Stage(StageCodes.FS, 0, StageStatus.Completed, new DateOnly(2026, 1, 1)),
            Stage(StageCodes.SOW, 2, StageStatus.Completed, new DateOnly(2026, 1, 10)),
            Stage(StageCodes.IPA, 1, StageStatus.Completed, new DateOnly(2026, 1, 20)),
            Stage(StageCodes.AON, 3, StageStatus.InProgress)
        };

        var display = ProjectRepositoryStagePositionVm.Create(project, _workflowMetadata);

        Assert.Equal("Acceptance of Necessity · In progress", display.Primary);
        Assert.Equal("Previous: In-Principle Approval · 20 Jan 2026", display.Secondary);
        Assert.False(display.IsMissing);
    }

    [Fact]
    public void RepositoryPosition_InsertsMissingCanonicalNextStage()
    {
        var project = new Project
        {
            Id = 214,
            Name = "Project",
            CreatedByUserId = "seed",
            WorkflowVersion = ProcurementWorkflow.VersionV2
        };

        project.ProjectStages = new List<ProjectStage>
        {
            Stage(StageCodes.FS, 0, StageStatus.Completed, new DateOnly(2026, 1, 1)),
            Stage(StageCodes.SOW, 2, StageStatus.Completed, new DateOnly(2026, 1, 10)),
            Stage(StageCodes.IPA, 1, StageStatus.Completed, new DateOnly(2026, 1, 20))
        };

        var display = ProjectRepositoryStagePositionVm.Create(project, _workflowMetadata);

        Assert.Equal("In-Principle Approval → Acceptance of Necessity", display.Primary);
        Assert.Equal("Completed 20 Jan 2026 · Next stage not started", display.Secondary);
    }

    [Fact]
    public void RepositoryPosition_DoesNotInventHistoryForTerminalLegacyRecord()
    {
        var project = new Project
        {
            Id = 205,
            Name = "Legacy project",
            CreatedByUserId = "seed",
            WorkflowVersion = ProcurementWorkflow.VersionV2,
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            IsLegacy = true
        };

        var display = ProjectRepositoryStagePositionVm.Create(project, _workflowMetadata);

        Assert.Equal(ProjectRepositoryStagePositionVm.Missing, display);
    }

    [Fact]
    public void Resolver_ExcludesTransferOfTechnologyFromProjectLifecyclePosition()
    {
        var project = new Project
        {
            Id = 215,
            Name = "Completed procurement",
            CreatedByUserId = "seed",
            WorkflowVersion = PlanConstants.DefaultStageTemplateVersion
        };

        foreach (var definition in _workflowMetadata.GetStages(project.WorkflowVersion)
                     .Where(definition => !StageCodes.IsTot(definition.Code)))
        {
            project.ProjectStages.Add(Stage(
                definition.Code,
                999,
                StageStatus.Completed,
                new DateOnly(2026, 1, 1)));
        }

        var stages = ProjectLifecyclePositionResolver.BuildProjectStages(project, _workflowMetadata);
        var position = ProjectLifecyclePositionResolver.Resolve(stages);

        Assert.DoesNotContain(stages, stage => StageCodes.IsTot(stage.Code));
        Assert.True(position.IsConcluded);
        Assert.Null(position.CurrentStage);
    }

    private static ProjectStage Stage(
        string code,
        int sortOrder,
        StageStatus status,
        DateOnly? completedOn = null) =>
        new()
        {
            StageCode = code,
            SortOrder = sortOrder,
            Status = status,
            CompletedOn = completedOn
        };
}
