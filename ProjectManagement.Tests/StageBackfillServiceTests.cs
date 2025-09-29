using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Stages;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

public class StageBackfillServiceTests
{
    [Fact]
    public async Task ApplyAsync_WhenValid_UpdatesStageAndLogs()
    {
        var clock = FakeClock.ForIstDate(new DateOnly(2024, 12, 5));
        await using var db = CreateContext();

        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            SortOrder = 1,
            Status = StageStatus.Completed,
            RequiresBackfill = true,
            IsAutoCompleted = true,
            AutoCompletedFromCode = StageCodes.SOW
        });
        await db.SaveChangesAsync();

        var service = new StageBackfillService(db, clock);

        var result = await service.ApplyAsync(
            projectId: 1,
            updates: new[]
            {
                new StageBackfillUpdate(StageCodes.IPA, new DateOnly(2024, 11, 20), new DateOnly(2024, 11, 25))
            },
            userId: "user-1",
            ct: CancellationToken.None);

        Assert.Equal(1, result.UpdatedCount);
        Assert.Contains(StageCodes.IPA, result.StageCodes);

        var stage = await db.ProjectStages.SingleAsync();
        Assert.Equal(new DateOnly(2024, 11, 20), stage.ActualStart);
        Assert.Equal(new DateOnly(2024, 11, 25), stage.CompletedOn);
        Assert.False(stage.RequiresBackfill);
        Assert.False(stage.IsAutoCompleted);
        Assert.Null(stage.AutoCompletedFromCode);

        var log = await db.StageChangeLogs.SingleAsync();
        Assert.Equal("Backfill", log.Action);
        Assert.Equal(new DateOnly(2024, 11, 20), log.ToActualStart);
        Assert.Equal(new DateOnly(2024, 11, 25), log.ToCompletedOn);
    }

    [Fact]
    public async Task ApplyAsync_WhenBackfillingAutoCompletedStage_ClearsInferredFlags()
    {
        var clock = FakeClock.ForIstDate(new DateOnly(2024, 12, 5));
        await using var db = CreateContext();

        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            SortOrder = 1,
            Status = StageStatus.Completed,
            ActualStart = new DateOnly(2024, 11, 1),
            CompletedOn = new DateOnly(2024, 11, 5),
            RequiresBackfill = true,
            IsAutoCompleted = true,
            AutoCompletedFromCode = StageCodes.SOW
        });
        await db.SaveChangesAsync();

        var service = new StageBackfillService(db, clock);

        await service.ApplyAsync(
            projectId: 1,
            updates: new[]
            {
                new StageBackfillUpdate(StageCodes.IPA, new DateOnly(2024, 11, 2), new DateOnly(2024, 11, 6))
            },
            userId: "user-1",
            ct: CancellationToken.None);

        var stage = await db.ProjectStages.SingleAsync();
        Assert.False(stage.IsAutoCompleted);
        Assert.Null(stage.AutoCompletedFromCode);
    }

    [Fact]
    public async Task ApplyAsync_WhenStageDoesNotRequireBackfill_ThrowsConflict()
    {
        var clock = FakeClock.ForIstDate(new DateOnly(2025, 1, 10));
        await using var db = CreateContext();

        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            SortOrder = 1,
            Status = StageStatus.Completed,
            ActualStart = new DateOnly(2024, 11, 1),
            CompletedOn = new DateOnly(2024, 11, 5),
            RequiresBackfill = false
        });
        await db.SaveChangesAsync();

        var service = new StageBackfillService(db, clock);

        var ex = await Assert.ThrowsAsync<StageBackfillConflictException>(() => service.ApplyAsync(
            projectId: 1,
            updates: new[]
            {
                new StageBackfillUpdate(StageCodes.IPA, new DateOnly(2024, 10, 1), new DateOnly(2024, 10, 5))
            },
            userId: "user-1",
            ct: CancellationToken.None));

        Assert.Contains(StageCodes.IPA, ex.ConflictingStages);
    }

    [Fact]
    public async Task ApplyAsync_WhenDatesMissing_ThrowsValidation()
    {
        var clock = FakeClock.ForIstDate(new DateOnly(2025, 2, 1));
        await using var db = CreateContext();

        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1,
            StageCode = StageCodes.SOW,
            SortOrder = 2,
            Status = StageStatus.Completed,
            RequiresBackfill = true
        });
        await db.SaveChangesAsync();

        var service = new StageBackfillService(db, clock);

        var ex = await Assert.ThrowsAsync<StageBackfillValidationException>(() => service.ApplyAsync(
            projectId: 1,
            updates: new[]
            {
                new StageBackfillUpdate(StageCodes.SOW, null, null)
            },
            userId: "user-1",
            ct: CancellationToken.None));

        Assert.Contains(ex.Details, detail => detail.Contains(StageCodes.SOW, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ApplyAsync_WhenStageMissing_ThrowsNotFound()
    {
        var clock = FakeClock.ForIstDate(new DateOnly(2025, 3, 15));
        await using var db = CreateContext();
        var service = new StageBackfillService(db, clock);

        await Assert.ThrowsAsync<StageBackfillNotFoundException>(() => service.ApplyAsync(
            projectId: 1,
            updates: new[]
            {
                new StageBackfillUpdate(StageCodes.PNC, new DateOnly(2025, 1, 2), new DateOnly(2025, 1, 5))
            },
            userId: "user-1",
            ct: CancellationToken.None));
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
