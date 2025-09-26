using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using Xunit;

namespace ProjectManagement.Tests;

public class StageProgressServiceTests
{
    [Fact]
    public async Task UpdateStageStatusAsync_SetsActualStartWhenMovingToInProgress()
    {
        var initial = new DateTimeOffset(2024, 1, 5, 0, 0, 0, TimeSpan.Zero);
        var clock = new TestClock(initial);
        await using var db = CreateContext();
        await SeedStageAsync(db, StageStatus.NotStarted);

        var service = new StageProgressService(db, clock, new FakeAudit());

        await service.UpdateStageStatusAsync(1, StageCodes.IPA, StageStatus.InProgress, null, "tester");

        var stage = await db.ProjectStages.SingleAsync();
        Assert.Equal(StageStatus.InProgress, stage.Status);
        Assert.Equal(DateOnly.FromDateTime(clock.UtcNow.UtcDateTime), stage.ActualStart);
        Assert.Null(stage.CompletedOn);
    }

    [Fact]
    public async Task UpdateStageStatusAsync_SetsCompletedOnWhenFinishingStage()
    {
        var initial = new DateTimeOffset(2024, 1, 5, 0, 0, 0, TimeSpan.Zero);
        var clock = new TestClock(initial);
        await using var db = CreateContext();
        await SeedStageAsync(db, StageStatus.NotStarted);

        var service = new StageProgressService(db, clock, new FakeAudit());

        await service.UpdateStageStatusAsync(1, StageCodes.IPA, StageStatus.InProgress, null, "tester");

        clock.UtcNow = new DateTimeOffset(2024, 1, 12, 0, 0, 0, TimeSpan.Zero);
        await service.UpdateStageStatusAsync(1, StageCodes.IPA, StageStatus.Completed, null, "tester");

        var stage = await db.ProjectStages.SingleAsync();
        Assert.Equal(StageStatus.Completed, stage.Status);
        Assert.Equal(DateOnly.FromDateTime(initial.UtcDateTime), stage.ActualStart);
        Assert.Equal(DateOnly.FromDateTime(clock.UtcNow.UtcDateTime), stage.CompletedOn);
    }

    [Fact]
    public async Task UpdateStageStatusAsync_ResetToNotStartedClearsDates()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 1, 5, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStageAsync(db, StageStatus.Completed, actualStart: new DateOnly(2024, 1, 1), completedOn: new DateOnly(2024, 1, 2));

        var service = new StageProgressService(db, clock, new FakeAudit());

        await service.UpdateStageStatusAsync(1, StageCodes.IPA, StageStatus.NotStarted, null, "tester");

        var stage = await db.ProjectStages.SingleAsync();
        Assert.Equal(StageStatus.NotStarted, stage.Status);
        Assert.Null(stage.ActualStart);
        Assert.Null(stage.CompletedOn);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task SeedStageAsync(ApplicationDbContext db, StageStatus status, DateOnly? actualStart = null, DateOnly? completedOn = null)
    {
        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Project",
            CreatedByUserId = "seed"
        });

        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            Status = status,
            ActualStart = actualStart,
            CompletedOn = completedOn
        });

        await db.SaveChangesAsync();
    }

    private sealed class TestClock : IClock
    {
        public TestClock(DateTimeOffset now)
        {
            UtcNow = now;
        }

        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class FakeAudit : IAuditService
    {
        public Task LogAsync(string action, string? message = null, string level = "Info", string? userId = null, string? userName = null, IDictionary<string, string?>? data = null, Microsoft.AspNetCore.Http.HttpContext? http = null)
            => Task.CompletedTask;
    }
}
