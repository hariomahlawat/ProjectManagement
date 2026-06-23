using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Workspace;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class WorkspaceProjectRecordHealthServiceTests
{
    [Fact]
    public async Task HistoricalTimeline_UsesOnlyActualDates_AndAwardsPartialCredit()
    {
        await using var db = CreateContext();
        var project = NewProject();
        project.ProjectStages.Add(new ProjectStage
        {
            StageCode = StageCodes.FS,
            SortOrder = 1,
            Status = StageStatus.Completed,
            ActualStart = new DateOnly(2026, 1, 1),
            CompletedOn = null,
            PlannedStart = null,
            PlannedDue = null
        });
        project.ProjectStages.Add(new ProjectStage
        {
            StageCode = StageCodes.IPA,
            SortOrder = 2,
            Status = StageStatus.NotStarted,
            PlannedStart = new DateOnly(2026, 2, 1),
            PlannedDue = new DateOnly(2026, 2, 15)
        });

        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.CalculateForProjectsAsync(new[] { project }, "po-1", default);
        var health = result[project.Id];

        // 15 profile + 25 no procurement fields applicable + 10/20 historical
        // + 15 current-stage planning + 0 documents + 0 media = 65.
        Assert.Equal(65, health.HealthPercent);
        Assert.Contains(health.GapDetails, gap => gap.Code == "FS_ACTUAL_COMPLETION" && gap.FieldLabel == "FS — Actual Completion");
        Assert.DoesNotContain(health.GapDetails, gap => gap.FieldLabel.Contains("planned", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Procurement_UsesLatestOverviewValues_AndOnlyCompletedStageFields()
    {
        await using var db = CreateContext();
        var project = NewProject();
        project.ProjectStages.Add(new ProjectStage
        {
            StageCode = StageCodes.IPA,
            SortOrder = 1,
            Status = StageStatus.Completed,
            ActualStart = new DateOnly(2026, 1, 1),
            CompletedOn = new DateOnly(2026, 1, 3)
        });
        project.ProjectStages.Add(new ProjectStage
        {
            StageCode = StageCodes.AON,
            SortOrder = 2,
            Status = StageStatus.Completed,
            ActualStart = new DateOnly(2026, 1, 4),
            CompletedOn = new DateOnly(2026, 1, 5)
        });
        project.ProjectStages.Add(new ProjectStage
        {
            StageCode = StageCodes.BM,
            SortOrder = 3,
            Status = StageStatus.NotStarted,
            PlannedStart = new DateOnly(2026, 1, 6),
            PlannedDue = new DateOnly(2026, 1, 20)
        });

        db.Projects.Add(project);
        await db.SaveChangesAsync();

        db.ProjectIpaFacts.Add(new ProjectIpaFact
        {
            ProjectId = project.Id,
            IpaCost = 65m,
            CreatedByUserId = "seed",
            CreatedOnUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        db.ProjectAonFacts.AddRange(
            new ProjectAonFact
            {
                ProjectId = project.Id,
                AonCost = 64.9m,
                CreatedByUserId = "seed",
                CreatedOnUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new ProjectAonFact
            {
                ProjectId = project.Id,
                AonCost = 0m,
                CreatedByUserId = "seed",
                CreatedOnUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
            });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.CalculateForProjectsAsync(new[] { project }, "po-1", default);
        var health = result[project.Id];

        Assert.Contains(health.GapDetails, gap => gap.Code == "AON_COST" && gap.FieldLabel == "AoN Cost");
        Assert.DoesNotContain(health.GapDetails, gap => gap.Code == "BENCHMARK_COST"); // BM is not completed yet.
        Assert.Equal(63, health.HealthPercent);
    }

    [Fact]
    public async Task CurrentStageTimeline_PdcAndStartReceiveIndependentCredit()
    {
        await using var db = CreateContext();
        var project = NewProject();
        project.ProjectStages.Add(new ProjectStage
        {
            StageCode = StageCodes.FS,
            SortOrder = 1,
            Status = StageStatus.InProgress,
            ActualStart = new DateOnly(2026, 1, 1),
            PlannedDue = null
        });

        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.CalculateForProjectsAsync(new[] { project }, "po-1", default);
        var health = result[project.Id];

        // 15 profile + 25 procurement N/A + 20 no historical stages + 6/15 current stage.
        Assert.Equal(66, health.HealthPercent);
        Assert.Contains(health.GapDetails, gap => gap.Code == "CURRENT_STAGE_PDC" && gap.FieldLabel == "FS — PDC");
        Assert.DoesNotContain(health.GapDetails, gap => gap.Code == "CURRENT_STAGE_ACTUAL_START");
    }

    private static ProjectRecordHealthService CreateService(ApplicationDbContext db)
        => new(db, new ProjectProcurementReadService(db));

    private static Project NewProject()
        => new()
        {
            Name = "Test Project",
            Description = "A sufficiently detailed project description for completeness scoring.",
            CreatedByUserId = "seed"
        };

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
