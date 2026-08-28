using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Stages;
using Xunit;

namespace ProjectManagement.Tests.Projects;

public sealed class ProjectTimelineReadServiceTests
{
    [Fact]
    public async Task GetAsync_BenchmarkingSuggestionUsesBid_NotTechnicalEvaluation()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Parallel Timeline Project",
            CreatedByUserId = "seed",
            WorkflowVersion = ProcurementWorkflow.VersionV2
        });
        db.ProjectStages.AddRange(
            new ProjectStage
            {
                ProjectId = 1,
                StageCode = StageCodes.BID,
                SortOrder = 5,
                Status = StageStatus.Completed,
                ActualStart = new DateOnly(2026, 8, 1),
                CompletedOn = new DateOnly(2026, 8, 6)
            },
            new ProjectStage
            {
                ProjectId = 1,
                StageCode = StageCodes.TEC,
                SortOrder = 6,
                Status = StageStatus.Completed,
                ActualStart = new DateOnly(2026, 8, 7),
                CompletedOn = new DateOnly(2026, 8, 21)
            },
            new ProjectStage
            {
                ProjectId = 1,
                StageCode = StageCodes.BM,
                SortOrder = 7,
                Status = StageStatus.NotStarted
            });
        db.StageDependencyTemplates.Add(new StageDependencyTemplate
        {
            Version = ProcurementWorkflow.VersionV2,
            FromStageCode = StageCodes.BM,
            DependsOnStageCode = StageCodes.BID
        });
        await db.SaveChangesAsync();

        var metadata = new WorkflowStageMetadataProvider();
        var policy = new ProjectStageWorkflowPolicy(db, metadata);
        var service = new ProjectTimelineReadService(
            db,
            new FixedClock(new DateTimeOffset(2026, 8, 28, 6, 30, 0, TimeSpan.Zero)),
            metadata,
            policy);

        var timeline = await service.GetAsync(1);
        var benchmarking = timeline.Items.Single(item => item.Code == StageCodes.BM);

        Assert.Equal(new DateOnly(2026, 8, 6), benchmarking.EarliestAllowedStartDate);
        Assert.Equal(new DateOnly(2026, 8, 7), benchmarking.SuggestedStartDate);
        Assert.Equal(StageCodes.BID, benchmarking.SuggestedStartSourceCode);
        Assert.Contains("Bidding", benchmarking.SuggestedStartSourceName, StringComparison.OrdinalIgnoreCase);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;

        public DateTimeOffset UtcNow { get; }
    }
}
