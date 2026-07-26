using System;
using System.Linq;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Tests;

public sealed class ProjectPortfolioPresentationVmTests
{
    [Fact]
    public void Create_UsesOneMissingFactsCollectionForCountAndPercentage()
    {
        var project = new Project
        {
            Name = "Project",
            CategoryId = 1,
            TechnicalCategoryId = 2,
            HodUserId = "hod",
            LeadPoUserId = "po",
            SponsoringLineDirectorateId = 3
        };

        var presentation = ProjectPortfolioPresentationVm.Create(
            project,
            new TimelineVm(),
            hasBackfill: false);

        Assert.Equal(
            new[] { "project type", "sponsoring unit", "project description" },
            presentation.MissingProfileFacts);
        Assert.Equal(67, presentation.CompletenessPercent);
        Assert.Equal("3 recommended details missing", presentation.ProfileCompletenessDetail);
    }

    [Fact]
    public void Create_UsesProcurementLifecyclePositionAndKeepsTotSeparate()
    {
        var definitions = new WorkflowStageMetadataProvider()
            .GetStages(ProcurementWorkflow.VersionV2);
        var timeline = new TimelineVm
        {
            TotalStages = definitions.Count,
            Items = definitions
                .Select((definition, index) => new TimelineItemVm
                {
                    Code = definition.Code,
                    Name = definition.Name,
                    SortOrder = index,
                    Status = StageCodes.IsTot(definition.Code)
                        ? StageStatus.NotStarted
                        : StageStatus.Completed
                })
                .ToArray()
        };

        var presentation = ProjectPortfolioPresentationVm.Create(
            new Project { Name = "Project", CreatedByUserId = "seed" },
            timeline,
            hasBackfill: false);

        Assert.True(presentation.IsWorkflowConcluded);
        Assert.Null(presentation.CurrentStage);
        Assert.Equal("No further lifecycle action", presentation.NextAction);
    }

    [Fact]
    public void Create_KeepsNotStartedStageCurrentAndUsesStartLanguage()
    {
        var timeline = new TimelineVm
        {
            TotalStages = 2,
            Items = new[]
            {
                new TimelineItemVm
                {
                    Code = StageCodes.FS,
                    Name = "Feasibility Study",
                    SortOrder = 0,
                    Status = StageStatus.Completed,
                    CompletedOn = new DateOnly(2026, 1, 1)
                },
                new TimelineItemVm
                {
                    Code = StageCodes.SOW,
                    Name = "SOW Vetting",
                    SortOrder = 1,
                    Status = StageStatus.NotStarted
                }
            }
        };

        var presentation = ProjectPortfolioPresentationVm.Create(
            new Project { Name = "Project", CreatedByUserId = "seed" },
            timeline,
            hasBackfill: false);

        Assert.Equal(StageCodes.SOW, presentation.CurrentStage?.Code);
        Assert.Equal("SOW Vetting", presentation.CurrentStageDisplay);
        Assert.Equal("Start SOW Vetting", presentation.NextAction);
        Assert.Equal("Current stage not started", presentation.ScheduleStatus);
    }

    [Fact]
    public void Create_CancelledLifecycleUsesHistoricalLanguage()
    {
        var timeline = new TimelineVm
        {
            TotalStages = 2,
            Items = new[]
            {
                new TimelineItemVm
                {
                    Code = StageCodes.FS,
                    Name = "Feasibility Study",
                    SortOrder = 0,
                    Status = StageStatus.Completed,
                    CompletedOn = new DateOnly(2025, 11, 1)
                },
                new TimelineItemVm
                {
                    Code = StageCodes.SOW,
                    Name = "SOW Vetting",
                    SortOrder = 1,
                    Status = StageStatus.InProgress
                }
            }
        };

        var presentation = ProjectPortfolioPresentationVm.Create(
            new Project
            {
                Name = "Cancelled project",
                CreatedByUserId = "seed",
                LifecycleStatus = ProjectLifecycleStatus.Cancelled
            },
            timeline,
            hasBackfill: false);

        Assert.True(presentation.IsTerminalLifecycle);
        Assert.Equal("No further lifecycle action", presentation.NextAction);
        Assert.Equal("Stage progression ceased at cancellation", presentation.NextActionDetail);
        Assert.Equal("Lifecycle cancelled", presentation.ScheduleStatus);
        Assert.Equal("Historical plan", presentation.TimelinePlanLabel);
        Assert.Null(presentation.CurrentStageOverdueDays);
    }
}
