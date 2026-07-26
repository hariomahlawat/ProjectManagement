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
}
