using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Stages;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class StageDecisionServiceTests
{
    [Fact]
    public async Task ApproveAsync_UpdatesStageAndLogs()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 1, 20, 8, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedProjectAsync(
            db,
            "hod-1",
            (StageCodes.FS, StageStatus.Completed),
            (StageCodes.IPA, StageStatus.NotStarted));

        var request = await SeedRequestAsync(
            db,
            projectId: 1,
            StageCodes.IPA,
            StageStatus.InProgress.ToString(),
            new DateOnly(2024, 1, 15),
            note: null);

        var service = CreateService(db, clock);
        var result = await service.DecideAsync(
            new StageDecisionInput(request.Id, StageDecisionAction.Approve, "  Please approve  "),
            "hod-1");

        Assert.Equal(StageDecisionOutcome.Success, result.Outcome);
        Assert.Empty(result.Warnings);

        var stage = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.IPA);
        Assert.Equal(StageStatus.InProgress, stage.Status);
        Assert.Equal(new DateOnly(2024, 1, 15), stage.ActualStart);

        var persistedRequest = await db.StageChangeRequests.SingleAsync();
        Assert.Equal("Approved", persistedRequest.DecisionStatus);
        Assert.Equal("Please approve", persistedRequest.DecisionNote);
        Assert.Equal("hod-1", persistedRequest.DecidedByUserId);
        Assert.NotNull(persistedRequest.DecidedOn);

        var logs = await db.StageChangeLogs.OrderBy(l => l.Id).ToListAsync();
        Assert.Collection(
            logs,
            log =>
            {
                Assert.Equal("Approved", log.Action);
                Assert.Equal(StageStatus.NotStarted.ToString(), log.FromStatus);
                Assert.Equal(StageStatus.InProgress.ToString(), log.ToStatus);
                Assert.Equal(new DateOnly(2024, 1, 15), log.ToActualStart);
                Assert.Equal("Please approve", log.Note);
            },
            log =>
            {
                Assert.Equal("Applied", log.Action);
                Assert.Equal(StageStatus.NotStarted.ToString(), log.FromStatus);
                Assert.Equal(StageStatus.InProgress.ToString(), log.ToStatus);
                Assert.Equal(new DateOnly(2024, 1, 15), log.ToActualStart);
                Assert.Equal("Please approve", log.Note);
            });
    }

    [Fact]
    public async Task RejectAsync_DoesNotModifyStage()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 2, 10, 9, 30, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedProjectAsync(
            db,
            "hod-9",
            (StageCodes.FS, StageStatus.Completed),
            (StageCodes.IPA, StageStatus.NotStarted));

        var request = await SeedRequestAsync(
            db,
            projectId: 1,
            StageCodes.IPA,
            StageStatus.InProgress.ToString(),
            new DateOnly(2024, 2, 5),
            note: null);

        var service = CreateService(db, clock);
        var result = await service.DecideAsync(
            new StageDecisionInput(request.Id, StageDecisionAction.Reject, " Need more info "),
            "hod-9");

        Assert.Equal(StageDecisionOutcome.Success, result.Outcome);

        var stage = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.IPA);
        Assert.Equal(StageStatus.NotStarted, stage.Status);
        Assert.Null(stage.ActualStart);

        var persistedRequest = await db.StageChangeRequests.SingleAsync();
        Assert.Equal("Rejected", persistedRequest.DecisionStatus);
        Assert.Equal("Need more info", persistedRequest.DecisionNote);
        Assert.Equal("hod-9", persistedRequest.DecidedByUserId);

        var log = await db.StageChangeLogs.SingleAsync();
        Assert.Equal("Rejected", log.Action);
        Assert.Equal(StageStatus.NotStarted.ToString(), log.FromStatus);
        Assert.Equal(StageStatus.NotStarted.ToString(), log.ToStatus);
        Assert.Equal("Need more info", log.Note);
    }

    [Fact]
    public async Task ApproveAsync_ClampsCompletionDateAndAddsWarning()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 3, 20, 12, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedProjectAsync(
            db,
            "hod-5",
            (StageCodes.FS, StageStatus.Completed, null, null),
            (StageCodes.IPA, StageStatus.InProgress, new DateOnly(2024, 3, 10), null));

        db.ProjectIpaFacts.Add(new ProjectIpaFact
        {
            ProjectId = 1,
            IpaCost = 200m,
            CreatedByUserId = "seed",
            CreatedOnUtc = clock.UtcNow.UtcDateTime
        });
        await db.SaveChangesAsync();

        var request = await SeedRequestAsync(
            db,
            projectId: 1,
            StageCodes.IPA,
            StageStatus.Completed.ToString(),
            new DateOnly(2024, 3, 5),
            note: " Close stage ");

        var service = CreateService(db, clock);
        var result = await service.DecideAsync(
            new StageDecisionInput(request.Id, StageDecisionAction.Approve, " Close stage "),
            "hod-5");

        Assert.Equal(StageDecisionOutcome.Success, result.Outcome);
        Assert.Contains(result.Warnings, w => w.Contains("Completion date was earlier"));

        var stage = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.IPA);
        Assert.Equal(StageStatus.Completed, stage.Status);
        Assert.Equal(new DateOnly(2024, 3, 10), stage.CompletedOn);
        Assert.Equal(new DateOnly(2024, 3, 10), stage.ActualStart);

        var persistedRequest = await db.StageChangeRequests.SingleAsync();
        Assert.Equal("Approved", persistedRequest.DecisionStatus);
        Assert.Contains("Warning:", persistedRequest.DecisionNote);
        Assert.Contains("adjusted", persistedRequest.DecisionNote, StringComparison.OrdinalIgnoreCase);

        var appliedLog = await db.StageChangeLogs.SingleAsync(l => l.Action == "Applied");
        Assert.Equal(new DateOnly(2024, 3, 10), appliedLog.ToCompletedOn);
    }

    [Fact]
    public async Task ApproveAsync_AddsPredecessorWarning()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 4, 15, 7, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedProjectAsync(
            db,
            "hod-4",
            (StageCodes.FS, StageStatus.Completed),
            (StageCodes.IPA, StageStatus.NotStarted),
            (StageCodes.SOW, StageStatus.NotStarted));

        var request = await SeedRequestAsync(
            db,
            projectId: 1,
            StageCodes.SOW,
            StageStatus.InProgress.ToString(),
            new DateOnly(2024, 4, 14),
            note: null);

        var service = CreateService(db, clock);
        var result = await service.DecideAsync(
            new StageDecisionInput(request.Id, StageDecisionAction.Approve, null),
            "hod-4");

        Assert.Equal(StageDecisionOutcome.Success, result.Outcome);
        Assert.Contains(result.Warnings, w => w.Contains(StageCodes.IPA));

        var persistedRequest = await db.StageChangeRequests.SingleAsync(r => r.Id == request.Id);
        Assert.Contains(StageCodes.IPA, persistedRequest.DecisionNote);

        var stage = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.SOW);
        Assert.Equal(StageStatus.InProgress, stage.Status);
        Assert.Equal(new DateOnly(2024, 4, 14), stage.ActualStart);
    }

    private static async Task SeedProjectAsync(
        ApplicationDbContext db,
        string hodUserId,
        params (string Code, StageStatus Status, DateOnly? ActualStart, DateOnly? CompletedOn)[] stages)
    {
        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Project",
            CreatedByUserId = "seed",
            HodUserId = hodUserId
        });

        foreach (var (code, status, actualStart, completedOn) in stages)
        {
            db.ProjectStages.Add(new ProjectStage
            {
                ProjectId = 1,
                StageCode = code,
                SortOrder = Array.IndexOf(StageCodes.All, code),
                Status = status,
                ActualStart = actualStart,
                CompletedOn = completedOn
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedProjectAsync(
        ApplicationDbContext db,
        string hodUserId,
        params (string Code, StageStatus Status)[] stages)
    {
        var expanded = stages
            .Select(s => (s.Code, s.Status, (DateOnly?)null, (DateOnly?)null))
            .ToArray();

        await SeedProjectAsync(db, hodUserId, expanded);
    }

    private static async Task<StageChangeRequest> SeedRequestAsync(
        ApplicationDbContext db,
        int projectId,
        string stageCode,
        string requestedStatus,
        DateOnly? requestedDate,
        string? note)
    {
        var request = new StageChangeRequest
        {
            ProjectId = projectId,
            StageCode = stageCode,
            RequestedStatus = requestedStatus,
            RequestedDate = requestedDate,
            Note = note,
            RequestedByUserId = "po-1",
            RequestedOn = DateTimeOffset.UtcNow,
            DecisionStatus = "Pending"
        };

        db.StageChangeRequests.Add(request);
        await db.SaveChangesAsync();
        return request;
    }

    private static StageDecisionService CreateService(ApplicationDbContext db, TestClock clock)
    {
        var progress = new StageProgressService(db, clock, new FakeAudit(), new ProjectFactsReadService(db));
        return new StageDecisionService(db, clock, progress);
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
