using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Stages;

namespace ProjectManagement.Tests;

public class StageValidationServiceTests
{
    [Fact]
    public async Task ValidateAsync_CompletingStageWithFutureDate_ReturnsError()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 5, 10, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedAsync(db, new StageSeed(StageCodes.FS, StageStatus.InProgress, new DateOnly(2024, 5, 1), null));

        var service = new StageValidationService(db, clock);

        var result = await service.ValidateAsync(
            1,
            StageCodes.FS,
            StageStatus.Completed.ToString(),
            new DateOnly(2024, 5, 12),
            isHoD: false);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("future", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAsync_CompletingWithUnmetPredecessor_ReturnsMissingList()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 5, 10, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedAsync(
            db,
            new StageSeed(StageCodes.FS, StageStatus.NotStarted, null, null),
            new StageSeed(StageCodes.IPA, StageStatus.NotStarted, null, null));

        var service = new StageValidationService(db, clock);

        var result = await service.ValidateAsync(
            1,
            StageCodes.IPA,
            StageStatus.Completed.ToString(),
            new DateOnly(2024, 5, 9),
            isHoD: false);

        Assert.False(result.IsValid);
        Assert.Contains(StageCodes.FS, result.MissingPredecessors);
    }

    [Fact]
    public async Task ValidateAsync_CompletingBeforeAutoStart_ReturnsError()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 5, 15, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedAsync(
            db,
            new StageSeed(StageCodes.FS, StageStatus.Completed, new DateOnly(2024, 5, 1), new DateOnly(2024, 5, 3)),
            new StageSeed(StageCodes.IPA, StageStatus.Completed, new DateOnly(2024, 5, 4), new DateOnly(2024, 5, 10)),
            new StageSeed(StageCodes.SOW, StageStatus.InProgress, new DateOnly(2024, 5, 11), null));

        var service = new StageValidationService(db, clock);

        var result = await service.ValidateAsync(
            1,
            StageCodes.SOW,
            StageStatus.Completed.ToString(),
            new DateOnly(2024, 5, 8),
            isHoD: false);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("latest predecessor", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAsync_StartingFromBlocked_AllowsTransition()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 5, 20, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedAsync(
            db,
            new StageSeed(StageCodes.FS, StageStatus.Completed, new DateOnly(2024, 5, 1), new DateOnly(2024, 5, 3)),
            new StageSeed(StageCodes.IPA, StageStatus.Blocked, null, null));

        var service = new StageValidationService(db, clock);

        var result = await service.ValidateAsync(
            1,
            StageCodes.IPA,
            StageStatus.InProgress.ToString(),
            targetDate: null,
            isHoD: false);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Empty(result.MissingPredecessors);
        Assert.Equal(new DateOnly(2024, 5, 3), result.SuggestedAutoStart);
    }

    private static async Task SeedAsync(ApplicationDbContext db, params StageSeed[] stages)
    {
        var project = new Project
        {
            Id = 1,
            Name = "Project",
            CreatedByUserId = "seed",
            ActivePlanVersionNo = 1
        };

        db.Projects.Add(project);

        db.PlanVersions.Add(new PlanVersion
        {
            Id = 1,
            ProjectId = 1,
            VersionNo = 1,
            Status = PlanVersionStatus.Approved,
            CreatedByUserId = "seed",
            PncApplicable = true
        });

        var sortOrder = 1;

        foreach (var stage in stages)
        {
            db.ProjectStages.Add(new ProjectStage
            {
                ProjectId = 1,
                StageCode = stage.Code,
                SortOrder = sortOrder++,
                Status = stage.Status,
                ActualStart = stage.ActualStart,
                CompletedOn = stage.CompletedOn
            });
        }

        await db.SaveChangesAsync();
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private sealed record StageSeed(
        string Code,
        StageStatus Status,
        DateOnly? ActualStart,
        DateOnly? CompletedOn);

    private sealed class TestClock : IClock
    {
        public TestClock(DateTimeOffset now) => UtcNow = now;

        public DateTimeOffset UtcNow { get; set; }
    }
}
