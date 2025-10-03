using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests;

public class StageProgressServiceTests
{
    [Fact]
    public async Task UpdateStageStatusAsync_SetsActualStartWhenMovingToInProgress()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 1, 5, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStagesAsync(db, (StageCodes.IPA, StageStatus.NotStarted));

        var service = CreateService(db, clock);

        await service.UpdateStageStatusAsync(1, StageCodes.IPA, StageStatus.InProgress, null, "tester");

        var stage = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.IPA);
        Assert.Equal(StageStatus.InProgress, stage.Status);
        Assert.Equal(DateOnly.FromDateTime(clock.UtcNow.UtcDateTime), stage.ActualStart);
        Assert.False(stage.IsAutoCompleted);
        Assert.False(stage.RequiresBackfill);
    }

    [Fact]
    public async Task UpdateStageStatusAsync_CompletingStageSetsDates()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStagesAsync(db, (StageCodes.IPA, StageStatus.NotStarted));

        db.ProjectIpaFacts.Add(new ProjectIpaFact
        {
            ProjectId = 1,
            IpaCost = 123m,
            CreatedByUserId = "seed",
            CreatedOnUtc = clock.UtcNow.UtcDateTime
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, clock);

        var completedOn = new DateOnly(2024, 2, 15);
        await service.UpdateStageStatusAsync(1, StageCodes.IPA, StageStatus.Completed, completedOn, "tester");

        var stage = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.IPA);
        Assert.Equal(StageStatus.Completed, stage.Status);
        Assert.Equal(completedOn, stage.CompletedOn);
        Assert.Equal(completedOn, stage.ActualStart);
        Assert.False(stage.IsAutoCompleted);
        Assert.False(stage.RequiresBackfill);
    }

    [Fact]
    public async Task UpdateStageStatusAsync_CompletingStageCascadesPredecessors()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStagesAsync(
            db,
            (StageCodes.FS, StageStatus.NotStarted),
            (StageCodes.IPA, StageStatus.NotStarted),
            (StageCodes.SOW, StageStatus.NotStarted),
            (StageCodes.AON, StageStatus.NotStarted));

        db.ProjectAonFacts.Add(new ProjectAonFact
        {
            ProjectId = 1,
            AonCost = 500m,
            CreatedByUserId = "seed",
            CreatedOnUtc = clock.UtcNow.UtcDateTime
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, clock);

        var completedOn = new DateOnly(2024, 3, 5);
        await service.UpdateStageStatusAsync(1, StageCodes.AON, StageStatus.Completed, completedOn, "tester");

        var aon = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.AON);
        var sow = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.SOW);
        var ipa = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.IPA);
        var fs = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.FS);

        Assert.False(aon.IsAutoCompleted);
        Assert.Equal(StageStatus.Completed, sow.Status);
        Assert.True(sow.IsAutoCompleted);
        Assert.Equal(StageCodes.AON, sow.AutoCompletedFromCode);
        Assert.True(sow.RequiresBackfill); // no SOW facts captured
        Assert.Equal(completedOn, sow.CompletedOn);

        Assert.Equal(StageStatus.Completed, ipa.Status);
        Assert.True(ipa.IsAutoCompleted);
        Assert.Equal(StageCodes.AON, ipa.AutoCompletedFromCode);
        Assert.True(ipa.RequiresBackfill);

        Assert.Equal(StageStatus.Completed, fs.Status);
        Assert.True(fs.IsAutoCompleted);
        Assert.Equal(StageCodes.AON, fs.AutoCompletedFromCode);
        Assert.False(fs.RequiresBackfill);
    }

    [Fact]
    public async Task UpdateStageStatusAsync_ThrowsWhenFactsMissing()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 4, 1, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStagesAsync(db, (StageCodes.IPA, StageStatus.NotStarted));

        var service = CreateService(db, clock);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UpdateStageStatusAsync(1, StageCodes.IPA, StageStatus.Completed, null, "tester"));
    }

    [Fact]
    public async Task UpdateStageStatusAsync_ResetClearsDatesAndFlags()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStagesAsync(
            db,
            (StageCodes.FS, StageStatus.NotStarted),
            (StageCodes.IPA, StageStatus.NotStarted),
            (StageCodes.SOW, StageStatus.NotStarted),
            (StageCodes.AON, StageStatus.NotStarted));

        db.ProjectAonFacts.Add(new ProjectAonFact
        {
            ProjectId = 1,
            AonCost = 200m,
            CreatedByUserId = "seed",
            CreatedOnUtc = clock.UtcNow.UtcDateTime
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, clock);

        await service.UpdateStageStatusAsync(1, StageCodes.AON, StageStatus.Completed, null, "tester");

        await service.UpdateStageStatusAsync(1, StageCodes.IPA, StageStatus.NotStarted, null, "tester");

        var ipa = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.IPA);
        Assert.Equal(StageStatus.NotStarted, ipa.Status);
        Assert.Null(ipa.ActualStart);
        Assert.Null(ipa.CompletedOn);
        Assert.False(ipa.IsAutoCompleted);
        Assert.False(ipa.RequiresBackfill);
    }

    [Fact]
    public async Task UpdateStageStatusAsync_CompletingEasDoesNotAutoCompletePncWhenOptional()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 8, 1, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStagesAsync(
            db,
            (StageCodes.COB, StageStatus.Completed),
            (StageCodes.PNC, StageStatus.NotStarted),
            (StageCodes.EAS, StageStatus.InProgress));

        var project = await db.Projects.SingleAsync();
        project.ActivePlanVersionNo = 1;

        db.PlanVersions.Add(new PlanVersion
        {
            Id = 1,
            ProjectId = project.Id,
            VersionNo = 1,
            Status = PlanVersionStatus.Approved,
            CreatedByUserId = "seed",
            PncApplicable = false
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, clock);

        await service.UpdateStageStatusAsync(
            1,
            StageCodes.EAS,
            StageStatus.Completed,
            new DateOnly(2024, 8, 2),
            "tester");

        var eas = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.EAS);
        Assert.Equal(StageStatus.Completed, eas.Status);

        var pnc = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.PNC);
        Assert.Equal(StageStatus.NotStarted, pnc.Status);
    }

    private static StageProgressService CreateService(ApplicationDbContext db, TestClock clock)
        => new StageProgressService(db, clock, new FakeAudit(), new ProjectFactsReadService(db));

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task SeedStagesAsync(ApplicationDbContext db, params (string Code, StageStatus Status)[] stages)
    {
        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Project",
            CreatedByUserId = "seed"
        });

        foreach (var (code, status) in stages)
        {
            db.ProjectStages.Add(new ProjectStage
            {
                ProjectId = 1,
                StageCode = code,
                SortOrder = Array.IndexOf(StageCodes.All, code),
                Status = status
            });
        }

        await db.SaveChangesAsync();
    }

    private sealed class TestClock : IClock
    {
        public TestClock(DateTimeOffset now) => UtcNow = now;

        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class FakeAudit : IAuditService
    {
        public Task LogAsync(
            string action,
            string? message = null,
            string level = "Info",
            string? userId = null,
            string? userName = null,
            IDictionary<string, string?>? data = null,
            Microsoft.AspNetCore.Http.HttpContext? http = null)
            => Task.CompletedTask;
    }
}
