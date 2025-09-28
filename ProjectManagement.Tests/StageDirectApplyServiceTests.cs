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
using Xunit;

namespace ProjectManagement.Tests;

public class StageDirectApplyServiceTests
{
    [Fact]
    public async Task DirectApply_Completed_SetsDatesAndLogs()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 5, 10, 9, 30, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStageAsync(db, StageStatus.InProgress, new DateOnly(2024, 5, 1));

        var service = new StageDirectApplyService(db, clock);

        var result = await service.ApplyAsync(
            projectId: 1,
            stageCode: StageCodes.IPA,
            newStatus: StageStatus.Completed.ToString(),
            date: new DateOnly(2024, 5, 12),
            note: "  Completed ahead of schedule  ",
            hodUserId: "hod-1",
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
        var clock = new TestClock(new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero));
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

        var service = new StageDirectApplyService(db, clock);
        var result = await service.ApplyAsync(
            projectId: 1,
            stageCode: StageCodes.IPA,
            newStatus: StageStatus.InProgress.ToString(),
            date: new DateOnly(2024, 6, 2),
            note: null,
            hodUserId: "hod-1",
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

    private sealed class TestClock : IClock
    {
        public TestClock(DateTimeOffset now) => UtcNow = now;

        public DateTimeOffset UtcNow { get; set; }
    }
}
