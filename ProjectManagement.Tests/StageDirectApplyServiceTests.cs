using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Stages;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

public class StageDirectApplyServiceTests
{
    [Fact]
    public async Task ApplyAsync_AdminCompletion_AllowsNullDatesAndLogsNote()
    {
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 10, 9, 30, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStageAsync(db, StageStatus.InProgress, new DateOnly(2024, 5, 1));

        var validation = new StageValidationService(db, clock);
        var service = new StageDirectApplyService(db, clock, validation);

        var result = await service.ApplyAsync(
            projectId: 1,
            stageCode: StageCodes.IPA,
            status: StageStatus.Completed.ToString(),
            date: null,
            note: "  Completed administratively  ",
            hodUserId: "hod-1",
            forceBackfillPredecessors: false,
            CancellationToken.None);

        Assert.Equal(StageStatus.Completed.ToString(), result.UpdatedStatus);
        Assert.Null(result.ActualStart);
        Assert.Null(result.CompletedOn);
        Assert.Equal(0, result.BackfilledCount);
        Assert.Empty(result.BackfilledStages);
        Assert.Empty(result.Warnings);

        var stage = await db.ProjectStages.SingleAsync();
        Assert.Equal(StageStatus.Completed, stage.Status);
        Assert.Null(stage.ActualStart);
        Assert.Null(stage.CompletedOn);
        Assert.True(stage.RequiresBackfill);

        var logs = await db.StageChangeLogs.OrderBy(l => l.Id).ToListAsync();
        Assert.Contains(logs, l => l.Note != null &&
            l.Note.Contains("Administrative completion (no dates) by HoD", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplyAsync_WithMissingPredecessorsWithoutForce_ThrowsValidation()
    {
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 7, 1, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStageAsync(db, StageStatus.InProgress, new DateOnly(2024, 6, 15));

        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1,
            StageCode = StageCodes.FS,
            SortOrder = 0,
            Status = StageStatus.InProgress
        });
        await db.SaveChangesAsync();

        var validation = new StageValidationService(db, clock);
        var service = new StageDirectApplyService(db, clock, validation);

        var ex = await Assert.ThrowsAsync<StageDirectApplyValidationException>(() => service.ApplyAsync(
            projectId: 1,
            stageCode: StageCodes.IPA,
            status: StageStatus.Completed.ToString(),
            date: new DateOnly(2024, 7, 10),
            note: null,
            hodUserId: "hod-1",
            forceBackfillPredecessors: false,
            CancellationToken.None));

        Assert.Contains(StageCodes.FS, ex.MissingPredecessors);
        Assert.Contains(ex.Details, d => d.Contains("Complete required predecessor stages first.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplyAsync_WithForceBackfill_CompletesPredecessorsAndTarget()
    {
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 8, 15, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();

        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Project",
            CreatedByUserId = "creator",
            HodUserId = "hod-1",
            CreatedAt = DateTime.UtcNow
        });

        db.ProjectStages.AddRange(
            new ProjectStage
            {
                ProjectId = 1,
                StageCode = StageCodes.IPA,
                SortOrder = 1,
                Status = StageStatus.NotStarted
            },
            new ProjectStage
            {
                ProjectId = 1,
                StageCode = StageCodes.SOW,
                SortOrder = 2,
                Status = StageStatus.InProgress,
                ActualStart = new DateOnly(2024, 8, 1)
            });
        await db.SaveChangesAsync();

        var validation = new StageValidationService(db, clock);
        var service = new StageDirectApplyService(db, clock, validation);

        var result = await service.ApplyAsync(
            projectId: 1,
            stageCode: StageCodes.SOW,
            status: StageStatus.Completed.ToString(),
            date: new DateOnly(2024, 8, 20),
            note: null,
            hodUserId: "hod-1",
            forceBackfillPredecessors: true,
            CancellationToken.None);

        Assert.Equal(StageStatus.Completed.ToString(), result.UpdatedStatus);
        Assert.Equal(1, result.BackfilledCount);
        Assert.Contains(StageCodes.IPA, result.BackfilledStages);

        var predecessor = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.IPA);
        Assert.Equal(StageStatus.Completed, predecessor.Status);
        Assert.Null(predecessor.ActualStart);
        Assert.Null(predecessor.CompletedOn);
        Assert.True(predecessor.RequiresBackfill);
        Assert.True(predecessor.IsAutoCompleted);
        Assert.Equal(StageCodes.SOW, predecessor.AutoCompletedFromCode);

        var stage = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.SOW);
        Assert.Equal(StageStatus.Completed, stage.Status);
        Assert.Equal(new DateOnly(2024, 8, 1), stage.ActualStart);
        Assert.Equal(new DateOnly(2024, 8, 20), stage.CompletedOn);
        Assert.False(stage.RequiresBackfill);

        var logs = await db.StageChangeLogs
            .Where(l => l.StageCode == StageCodes.IPA || l.StageCode == StageCodes.SOW)
            .ToListAsync();

        Assert.Contains(logs, l => l.StageCode == StageCodes.IPA &&
            string.Equals(l.Action, "AutoBackfill", StringComparison.OrdinalIgnoreCase) &&
            l.Note != null &&
            l.Note.Contains("Auto-backfilled (no dates) due to completion of", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplyAsync_WhenCompletedOnBeforeStart_ClampsAndWarns()
    {
        var clock = FakeClock.ForIstDate(2024, 11, 1);
        await using var db = CreateContext();
        await SeedStageAsync(db, StageStatus.InProgress, new DateOnly(2024, 10, 10));

        var validation = new StageValidationService(db, clock);
        var service = new StageDirectApplyService(db, clock, validation);

        var result = await service.ApplyAsync(
            projectId: 1,
            stageCode: StageCodes.IPA,
            status: StageStatus.Completed.ToString(),
            date: new DateOnly(2024, 10, 5),
            note: null,
            hodUserId: "hod-1",
            forceBackfillPredecessors: false,
            CancellationToken.None);

        Assert.Equal(new DateOnly(2024, 10, 10), result.CompletedOn);
        Assert.Contains(result.Warnings, w => w.Contains("clamped", StringComparison.OrdinalIgnoreCase));

        var stage = await db.ProjectStages.SingleAsync();
        Assert.Equal(new DateOnly(2024, 10, 10), stage.CompletedOn);
        Assert.False(stage.RequiresBackfill);
    }

    [Fact]
    public async Task ApplyAsync_AllowsCaseInsensitiveHodMatch()
    {
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 9, 1, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStageAsync(db, StageStatus.NotStarted);

        var project = await db.Projects.SingleAsync();
        project.HodUserId = "HOD-1";
        await db.SaveChangesAsync();

        var validation = new StageValidationService(db, clock);
        var service = new StageDirectApplyService(db, clock, validation);

        var result = await service.ApplyAsync(
            projectId: 1,
            stageCode: StageCodes.IPA,
            status: StageStatus.InProgress.ToString(),
            date: new DateOnly(2024, 9, 5),
            note: null,
            hodUserId: "hod-1",
            forceBackfillPredecessors: false,
            CancellationToken.None);

        Assert.Equal(StageStatus.InProgress.ToString(), result.UpdatedStatus);
    }

    private static async Task SeedStageAsync(ApplicationDbContext db, StageStatus status, DateOnly? actualStart = null)
    {
        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Project",
            CreatedByUserId = "creator",
            HodUserId = "hod-1",
            CreatedAt = DateTime.UtcNow
        });

        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            SortOrder = 1,
            Status = status,
            ActualStart = actualStart
        });

        await db.SaveChangesAsync();
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
