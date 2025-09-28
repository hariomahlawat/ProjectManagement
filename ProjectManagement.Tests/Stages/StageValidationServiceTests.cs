using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Stages;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests.Stages;

public class StageValidationServiceTests
{
    [Fact]
    public async Task ValidateAsync_CompletingWithFutureDate_ReturnsError()
    {
        var today = new DateOnly(2025, 5, 10);
        var clock = FakeClock.ForIstDate(today);
        await using var db = CreateContext();
        await SeedAsync(
            db,
            new StageSeed(StageCodes.FS, StageStatus.InProgress, null, null));

        var service = new StageValidationService(db, clock);

        var result = await service.ValidateAsync(
            1,
            StageCodes.FS,
            StageStatus.Completed.ToString(),
            today.AddDays(1),
            isHoD: false);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("future", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAsync_CompletingWithUnmetPredecessors_ReturnsMissingList()
    {
        var today = new DateOnly(2025, 6, 1);
        var clock = FakeClock.ForIstDate(today);
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
            today,
            isHoD: false);

        Assert.False(result.IsValid);
        Assert.Contains(StageCodes.FS, result.MissingPredecessors);
    }

    [Fact]
    public async Task ValidateAsync_CompletingEarlierThanLatestPredecessor_ReturnsErrorAndSuggestedAutoStart()
    {
        var clock = FakeClock.ForIstDate(new DateOnly(2025, 9, 15));
        await using var db = CreateContext();
        await SeedAsync(
            db,
            new StageSeed(StageCodes.FS, StageStatus.Completed, new DateOnly(2025, 9, 1), new DateOnly(2025, 9, 10)),
            new StageSeed(StageCodes.IPA, StageStatus.InProgress, new DateOnly(2025, 9, 11), null));

        var service = new StageValidationService(db, clock);

        var result = await service.ValidateAsync(
            1,
            StageCodes.IPA,
            StageStatus.Completed.ToString(),
            new DateOnly(2025, 9, 8),
            isHoD: false);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            e => e.Contains("2025-09-10", StringComparison.Ordinal));
        Assert.Equal(new DateOnly(2025, 9, 10), result.SuggestedAutoStart);
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
}
