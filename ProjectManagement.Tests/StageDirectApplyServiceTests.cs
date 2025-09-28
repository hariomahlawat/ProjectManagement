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
    public async Task DirectApply_Completed_SetsDatesAndLogs()
    {
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 10, 9, 30, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStageAsync(db, StageStatus.InProgress, new DateOnly(2024, 5, 1));

        var validation = new StageValidationService(db, clock);
        var service = new StageDirectApplyService(db, clock, validation);

        var result = await service.ApplyAsync(
            projectId: 1,
            stageCode: StageCodes.IPA,
            newStatus: StageStatus.Completed.ToString(),
            date: new DateOnly(2024, 5, 12),
            note: "  Completed ahead of schedule  ",
            hodUserId: "hod-1",
            forceBackfillPredecessors: false,
            CancellationToken.None);

        Assert.Equal(DirectApplyOutcome.Success, result.Outcome);
        Assert.Equal(StageStatus.Completed, result.UpdatedStatus);
        Assert.Equal(new DateOnly(2024, 5, 1), result.ActualStart);
        Assert.Equal(new DateOnly(2024, 5, 12), result.CompletedOn);
        Assert.False(result.SupersededRequest);

        var stage = await db.ProjectStages.SingleAsync();
        Assert.Equal(StageStatus.Completed, stage.Status);
        Assert.Equal(new DateOnly(2024, 5, 1), stage.ActualStart);
        Assert.Equal(new DateOnly(2024, 5, 12), stage.CompletedOn);

        var logs = await db.StageChangeLogs.OrderBy(l => l.At).ToListAsync();
        Assert.Equal(2, logs.Count);
        Assert.Contains(logs, l => l.Action == "DirectApply" && l.Note == "Completed ahead of schedule");
        Assert.Contains(logs, l => l.Action == "Applied" && l.ToStatus == StageStatus.Completed.ToString());
    }

    [Fact]
    public async Task DirectApply_SupersedesPending()
    {
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStageAsync(db, StageStatus.NotStarted);

        db.StageChangeRequests.Add(new StageChangeRequest
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            RequestedStatus = StageStatus.Completed.ToString(),
            DecisionStatus = "Pending",
            RequestedDate = new DateOnly(2024, 5, 20),
            RequestedByUserId = "po-1",
            RequestedOn = new DateTimeOffset(2024, 5, 20, 0, 0, 0, TimeSpan.Zero)
        });
        await db.SaveChangesAsync();

        var validation = new StageValidationService(db, clock);
        var service = new StageDirectApplyService(db, clock, validation);
        var result = await service.ApplyAsync(
            projectId: 1,
            stageCode: StageCodes.IPA,
            newStatus: StageStatus.InProgress.ToString(),
            date: new DateOnly(2024, 6, 2),
            note: null,
            hodUserId: "hod-1",
            forceBackfillPredecessors: false,
            CancellationToken.None);

        Assert.Equal(DirectApplyOutcome.Success, result.Outcome);
        Assert.True(result.SupersededRequest);
        Assert.Equal(StageStatus.InProgress, result.UpdatedStatus);

        var request = await db.StageChangeRequests.SingleAsync();
        Assert.Equal("Superseded", request.DecisionStatus);
        Assert.Equal("hod-1", request.DecidedByUserId);
        Assert.Equal(clock.UtcNow, request.DecidedOn);
        Assert.Equal("Superseded by HoD direct apply", request.DecisionNote);

        var logs = await db.StageChangeLogs.OrderBy(l => l.At).ToListAsync();
        Assert.Equal(3, logs.Count);
        Assert.Contains(logs, l => l.Action == "Superseded" && l.ToStatus == StageStatus.Completed.ToString());
        Assert.Contains(logs, l => l.Action == "DirectApply" && l.ToStatus == StageStatus.InProgress.ToString());
        Assert.Contains(logs, l => l.Action == "Applied" && l.ToStatus == StageStatus.InProgress.ToString());
    }

    [Fact]
    public async Task DirectApply_UnmetPredecessorsWithoutForce_ReturnsValidationFailure()
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

        var result = await service.ApplyAsync(
            projectId: 1,
            stageCode: StageCodes.IPA,
            newStatus: StageStatus.Completed.ToString(),
            date: new DateOnly(2024, 7, 10),
            note: null,
            hodUserId: "hod-1",
            forceBackfillPredecessors: false,
            CancellationToken.None);

        Assert.Equal(DirectApplyOutcome.ValidationFailed, result.Outcome);
        Assert.Contains(StageCodes.FS, result.MissingPredecessors);
        Assert.Contains("Complete required predecessor stages first.", result.Details);
    }

    [Fact]
    public async Task DirectApply_ForceBackfillsPredecessorsAndCompletesStage()
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
            newStatus: StageStatus.Completed.ToString(),
            date: new DateOnly(2024, 8, 20),
            note: null,
            hodUserId: "hod-1",
            forceBackfillPredecessors: true,
            CancellationToken.None);

        Assert.Equal(DirectApplyOutcome.Success, result.Outcome);
        Assert.Equal(StageStatus.Completed, result.UpdatedStatus);

        var predecessor = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.IPA);
        Assert.Equal(StageStatus.Completed, predecessor.Status);
        Assert.Null(predecessor.ActualStart);
        Assert.Null(predecessor.CompletedOn);

        var stage = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.SOW);
        Assert.Equal(StageStatus.Completed, stage.Status);
        Assert.Equal(new DateOnly(2024, 8, 1), stage.ActualStart);
        Assert.Equal(new DateOnly(2024, 8, 20), stage.CompletedOn);

        var logs = await db.StageChangeLogs
            .Where(l => l.StageCode == StageCodes.IPA || l.StageCode == StageCodes.SOW)
            .ToListAsync();

        Assert.Contains(logs, l => l.StageCode == StageCodes.IPA && l.Action == "AutoBackfill");
        Assert.Contains(logs, l => l.StageCode == StageCodes.SOW && l.Action == "Applied");
        Assert.All(
            logs.Where(l => l.StageCode == StageCodes.IPA && l.Action == "AutoBackfill"),
            log =>
            {
                Assert.Null(log.ToActualStart);
                Assert.Null(log.ToCompletedOn);
            });
    }

    [Fact]
    public async Task DirectApply_CompletionBeforeSuggestedStart_ReturnsValidationFailure()
    {
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 9, 10, 0, 0, 0, TimeSpan.Zero));
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
                Status = StageStatus.Completed,
                ActualStart = new DateOnly(2024, 9, 1),
                CompletedOn = new DateOnly(2024, 9, 5)
            },
            new ProjectStage
            {
                ProjectId = 1,
                StageCode = StageCodes.SOW,
                SortOrder = 2,
                Status = StageStatus.InProgress,
                ActualStart = new DateOnly(2024, 9, 6)
            });
        await db.SaveChangesAsync();

        var validation = new StageValidationService(db, clock);
        var service = new StageDirectApplyService(db, clock, validation);

        var result = await service.ApplyAsync(
            projectId: 1,
            stageCode: StageCodes.SOW,
            newStatus: StageStatus.Completed.ToString(),
            date: new DateOnly(2024, 9, 4),
            note: null,
            hodUserId: "hod-1",
            forceBackfillPredecessors: false,
            CancellationToken.None);

        Assert.Equal(DirectApplyOutcome.ValidationFailed, result.Outcome);
        Assert.Contains(result.Details, d => d.Contains("Completion date cannot be earlier", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DirectApply_AdminCompletionWithoutDate_AllowsNullDatesWithWarning()
    {
        var clock = FakeClock.ForIstDate(2024, 10, 1);
        await using var db = CreateContext();
        await SeedStageAsync(db, StageStatus.InProgress, new DateOnly(2024, 9, 15));

        var validation = new StageValidationService(db, clock);
        var service = new StageDirectApplyService(db, clock, validation);

        var result = await service.ApplyAsync(
            projectId: 1,
            stageCode: StageCodes.IPA,
            newStatus: StageStatus.Completed.ToString(),
            date: null,
            note: null,
            hodUserId: "hod-1",
            forceBackfillPredecessors: false,
            CancellationToken.None);

        Assert.Equal(DirectApplyOutcome.Success, result.Outcome);
        Assert.Equal(StageStatus.Completed, result.UpdatedStatus);
        Assert.Null(result.ActualStart);
        Assert.Null(result.CompletedOn);
        Assert.Contains(result.Warnings, w => w.Contains("Incomplete data", StringComparison.OrdinalIgnoreCase));

        var stage = await db.ProjectStages.SingleAsync();
        Assert.Equal(StageStatus.Completed, stage.Status);
        Assert.Null(stage.ActualStart);
        Assert.Null(stage.CompletedOn);
    }

    [Fact]
    public async Task DirectApply_ClampsCompletionDateAndAddsWarning()
    {
        var clock = FakeClock.ForIstDate(2024, 11, 1);
        await using var db = CreateContext();
        await SeedStageAsync(db, StageStatus.InProgress, new DateOnly(2024, 10, 10));

        var validation = new StageValidationService(db, clock);
        var service = new StageDirectApplyService(db, clock, validation);

        var result = await service.ApplyAsync(
            projectId: 1,
            stageCode: StageCodes.IPA,
            newStatus: StageStatus.Completed.ToString(),
            date: new DateOnly(2024, 10, 5),
            note: null,
            hodUserId: "hod-1",
            forceBackfillPredecessors: false,
            CancellationToken.None);

        Assert.Equal(DirectApplyOutcome.Success, result.Outcome);
        Assert.Equal(StageStatus.Completed, result.UpdatedStatus);
        Assert.Equal(new DateOnly(2024, 10, 10), result.ActualStart);
        Assert.Equal(new DateOnly(2024, 10, 10), result.CompletedOn);
        Assert.Contains(result.Warnings, w => w.Contains("adjusted", StringComparison.OrdinalIgnoreCase));

        var stage = await db.ProjectStages.SingleAsync();
        Assert.Equal(new DateOnly(2024, 10, 10), stage.CompletedOn);
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
