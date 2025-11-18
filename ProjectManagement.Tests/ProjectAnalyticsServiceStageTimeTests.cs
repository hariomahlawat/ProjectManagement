using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Analytics;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Analytics;
using ProjectManagement.Services.Projects;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

public class ProjectAnalyticsServiceStageTimeTests
{
    [Fact]
    public async Task GetStageTimeInsightsAsync_UsesLatestAvailableCostFacts()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        var highCostProject = new Project
        {
            Id = 1,
            Name = "High",
            CreatedByUserId = "system",
            LifecycleStatus = ProjectLifecycleStatus.Completed
        };
        var lowCostProject = new Project
        {
            Id = 2,
            Name = "Low",
            CreatedByUserId = "system",
            LifecycleStatus = ProjectLifecycleStatus.Completed
        };

        db.Projects.AddRange(highCostProject, lowCostProject);

        db.ProjectStages.AddRange(
            new ProjectStage
            {
                Id = 10,
                ProjectId = highCostProject.Id,
                Project = highCostProject,
                StageCode = StageCodes.FS,
                SortOrder = 1,
                ActualStart = new DateOnly(2024, 1, 1),
                CompletedOn = new DateOnly(2024, 1, 11)
            },
            new ProjectStage
            {
                Id = 11,
                ProjectId = lowCostProject.Id,
                Project = lowCostProject,
                StageCode = StageCodes.FS,
                SortOrder = 1,
                ActualStart = new DateOnly(2024, 2, 1),
                CompletedOn = new DateOnly(2024, 2, 6)
            });

        db.ProjectAonFacts.Add(new ProjectAonFact
        {
            ProjectId = highCostProject.Id,
            AonCost = 20_000_000m,
            CreatedByUserId = "seed",
            CreatedOnUtc = DateTime.UtcNow
        });

        db.ProjectPncFacts.Add(new ProjectPncFact
        {
            ProjectId = lowCostProject.Id,
            PncCost = 5_000_000m,
            CreatedByUserId = "seed",
            CreatedOnUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var clock = FakeClock.AtUtc(DateTimeOffset.UtcNow);
        var service = new ProjectAnalyticsService(db, clock, new ProjectCategoryHierarchyService(db));

        var result = await service.GetStageTimeInsightsAsync();

        var fsRows = result.Rows.Where(r => r.StageKey == StageCodes.FS).ToList();
        Assert.True(fsRows.Count >= 2);

        var aboveRow = fsRows.Single(r => r.Bucket == StageTimeBucketKeys.AboveOrEqualOneCrore);
        var belowRow = fsRows.Single(r => r.Bucket == StageTimeBucketKeys.BelowOneCrore);

        Assert.Equal(10, aboveRow.MedianDays);
        Assert.Equal(1, aboveRow.ProjectCount);
        Assert.Equal(5, belowRow.MedianDays);
        Assert.Equal(1, belowRow.ProjectCount);
    }
}
