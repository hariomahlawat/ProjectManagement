using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Contracts.Stages;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Stages;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests.Stages;

public class StageRequestServiceTests
{
    [Fact]
    public async Task CreateAsync_UnmetPredecessors_Returns422Result()
    {
        var clock = FakeClock.ForIstDate(new DateOnly(2025, 1, 10));
        await using var db = CreateContext();
        await SeedStageAsync(db, StageStatus.NotStarted);

        var validator = new StubStageValidationService
        {
            Result = new StageValidationResult(
                false,
                new[] { "Complete required predecessor stages first." },
                Array.Empty<string>(),
                new[] { StageCodes.FS },
                null)
        };

        var service = new StageRequestService(db, clock, validator);

        var result = await service.CreateAsync(
            new StageChangeRequestInput
            {
                ProjectId = 1,
                StageCode = StageCodes.IPA,
                RequestedStatus = StageStatus.Completed.ToString(),
                RequestedDate = new DateOnly(2025, 1, 9)
            },
            "po-1");

        Assert.Equal(StageRequestOutcome.ValidationFailed, result.Outcome);
        Assert.Equal("Complete required predecessor stages first.", result.Error);
        var missing = Assert.IsAssignableFrom<IReadOnlyList<string>>(result.MissingPredecessors);
        Assert.Single(missing);
        Assert.Equal(StageCodes.FS, missing[0]);
        Assert.Contains("Complete required predecessor stages first.", result.Errors);

        var call = Assert.Single(validator.Calls);
        Assert.Equal(1, call.ProjectId);
        Assert.Equal(StageCodes.IPA, call.StageCode);
        Assert.Equal(StageStatus.Completed.ToString(), call.TargetStatus);
        Assert.False(call.IsHoD);
    }

    [Fact]
    public async Task CreateAsync_ValidRequest_CreatesPendingAndLogs()
    {
        var clock = FakeClock.AtUtc(new DateTimeOffset(2025, 2, 5, 6, 30, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStageAsync(db, StageStatus.NotStarted);

        var validator = new StubStageValidationService
        {
            Result = new StageValidationResult(
                true,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                null)
        };

        var service = new StageRequestService(db, clock, validator);

        var input = new StageChangeRequestInput
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            RequestedStatus = StageStatus.InProgress.ToString(),
            RequestedDate = new DateOnly(2025, 2, 4),
            Note = "  Start stage  "
        };

        var result = await service.CreateAsync(input, "po-1");

        Assert.Equal(StageRequestOutcome.Success, result.Outcome);
        Assert.True(result.RequestId.HasValue);
        Assert.Empty(result.Errors);

        var request = await db.StageChangeRequests.SingleAsync();
        Assert.Equal("po-1", request.RequestedByUserId);
        Assert.Equal(StageStatus.InProgress.ToString(), request.RequestedStatus);
        Assert.Equal(new DateOnly(2025, 2, 4), request.RequestedDate);
        Assert.Equal("Start stage", request.Note);
        Assert.Equal("Pending", request.DecisionStatus);
        Assert.Equal(clock.UtcNow, request.RequestedOn);

        var log = await db.StageChangeLogs.SingleAsync();
        Assert.Equal("Requested", log.Action);
        Assert.Equal(StageStatus.NotStarted.ToString(), log.FromStatus);
        Assert.Equal(StageStatus.InProgress.ToString(), log.ToStatus);
        Assert.Equal(new DateOnly(2025, 2, 4), log.ToActualStart);
        Assert.Null(log.ToCompletedOn);
        Assert.Equal("Start stage", log.Note);
        Assert.Equal(clock.UtcNow, log.At);

        var call = Assert.Single(validator.Calls);
        Assert.Equal(StageStatus.InProgress.ToString(), call.TargetStatus);
        Assert.Equal(new DateOnly(2025, 2, 4), call.TargetDate);
        Assert.False(call.IsHoD);
    }

    [Fact]
    public async Task CreateAsync_ReplacesPendingRequest_SupersedesExisting()
    {
        var clock = FakeClock.AtUtc(new DateTimeOffset(2025, 2, 5, 6, 30, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStageAsync(db, StageStatus.NotStarted);

        db.StageChangeRequests.Add(new StageChangeRequest
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            RequestedStatus = StageStatus.Completed.ToString(),
            RequestedDate = new DateOnly(2025, 2, 3),
            Note = "First request",
            RequestedByUserId = "po-1",
            RequestedOn = clock.UtcNow.AddDays(-1),
            DecisionStatus = "Pending"
        });

        await db.SaveChangesAsync();

        var validator = new StubStageValidationService();

        var service = new StageRequestService(db, clock, validator);

        var result = await service.CreateAsync(
            new StageChangeRequestInput
            {
                ProjectId = 1,
                StageCode = StageCodes.IPA,
                RequestedStatus = StageStatus.InProgress.ToString(),
                RequestedDate = new DateOnly(2025, 2, 4)
            },
            "po-1");

        Assert.Equal(StageRequestOutcome.Success, result.Outcome);

        var requests = await db.StageChangeRequests
            .OrderBy(r => r.RequestedOn)
            .ToListAsync();

        Assert.Equal(2, requests.Count);

        var superseded = requests.First();
        Assert.Equal("Superseded", superseded.DecisionStatus);
        Assert.Equal("po-1", superseded.DecidedByUserId);
        Assert.Equal(clock.UtcNow, superseded.DecidedOn);
        Assert.Equal("Superseded by newer request", superseded.DecisionNote);

        var latest = requests.Last();
        Assert.Equal(StageStatus.InProgress.ToString(), latest.RequestedStatus);
        Assert.Equal("Pending", latest.DecisionStatus);
        Assert.True(latest.RequestedOn >= superseded.RequestedOn);

        var logs = await db.StageChangeLogs
            .OrderBy(l => l.At)
            .ToListAsync();

        Assert.Equal(2, logs.Count);

        var supersededLog = logs[0];
        Assert.Equal("Superseded", supersededLog.Action);
        Assert.Equal(StageStatus.NotStarted.ToString(), supersededLog.FromStatus);
        Assert.Equal(StageStatus.Completed.ToString(), supersededLog.ToStatus);
        Assert.Equal(clock.UtcNow, supersededLog.At);
        Assert.Equal("Superseded by newer request", supersededLog.Note);
    }

    [Fact]
    public async Task CreateBatchAsync_AllValid_CreatesRequestsForEachStage()
    {
        var clock = FakeClock.AtUtc(new DateTimeOffset(2025, 2, 5, 6, 30, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStagesAsync(db,
            (StageCodes.FS, StageStatus.NotStarted),
            (StageCodes.IPA, StageStatus.NotStarted));

        var validator = new StubStageValidationService();
        var service = new StageRequestService(db, clock, validator);

        var batch = new BatchStageChangeRequestInput
        {
            ProjectId = 1,
            Stages = new[]
            {
                new StageChangeRequestItemInput
                {
                    StageCode = StageCodes.FS,
                    RequestedStatus = StageStatus.InProgress.ToString(),
                    RequestedDate = new DateOnly(2025, 2, 4)
                },
                new StageChangeRequestItemInput
                {
                    StageCode = StageCodes.IPA,
                    RequestedStatus = StageStatus.Completed.ToString(),
                    RequestedDate = new DateOnly(2025, 2, 5)
                }
            }
        };

        var result = await service.CreateBatchAsync(batch, "po-1");

        Assert.Equal(BatchStageRequestOutcome.Success, result.Outcome);
        Assert.Equal(2, result.Items.Count);

        var requests = await db.StageChangeRequests
            .OrderBy(r => r.StageCode)
            .ToListAsync();

        Assert.Collection(
            requests,
            r =>
            {
                Assert.Equal(StageCodes.FS, r.StageCode);
                Assert.Equal(StageStatus.InProgress.ToString(), r.RequestedStatus);
                Assert.Equal(new DateOnly(2025, 2, 4), r.RequestedDate);
            },
            r =>
            {
                Assert.Equal(StageCodes.IPA, r.StageCode);
                Assert.Equal(StageStatus.Completed.ToString(), r.RequestedStatus);
                Assert.Equal(new DateOnly(2025, 2, 5), r.RequestedDate);
            });

        var calls = validator.Calls;
        Assert.Equal(2, calls.Count);
        Assert.Contains(calls, c => c.StageCode == StageCodes.FS && c.TargetStatus == StageStatus.InProgress.ToString());
        Assert.Contains(calls, c => c.StageCode == StageCodes.IPA && c.TargetStatus == StageStatus.Completed.ToString());
    }

    [Fact]
    public async Task CreateBatchAsync_InvalidStage_RollsBackPendingChanges()
    {
        var clock = FakeClock.AtUtc(new DateTimeOffset(2025, 2, 5, 6, 30, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStagesAsync(db,
            (StageCodes.FS, StageStatus.NotStarted),
            (StageCodes.IPA, StageStatus.NotStarted));

        var validator = new StubStageValidationService
        {
            Resolver = call => call.StageCode == StageCodes.FS
                ? new StageValidationResult(true, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), null)
                : new StageValidationResult(false, new[] { "Complete required predecessor stages first." }, Array.Empty<string>(), new[] { StageCodes.FS }, null)
        };

        var service = new StageRequestService(db, clock, validator);

        var batch = new BatchStageChangeRequestInput
        {
            ProjectId = 1,
            Stages = new[]
            {
                new StageChangeRequestItemInput
                {
                    StageCode = StageCodes.FS,
                    RequestedStatus = StageStatus.Completed.ToString(),
                    RequestedDate = new DateOnly(2025, 2, 4)
                },
                new StageChangeRequestItemInput
                {
                    StageCode = StageCodes.IPA,
                    RequestedStatus = StageStatus.Completed.ToString(),
                    RequestedDate = new DateOnly(2025, 2, 5)
                }
            }
        };

        var result = await service.CreateBatchAsync(batch, "po-1");

        Assert.Equal(BatchStageRequestOutcome.ValidationFailed, result.Outcome);
        Assert.NotEmpty(result.Items);
        Assert.Equal(0, await db.StageChangeRequests.CountAsync());
        Assert.Equal(0, await db.StageChangeLogs.CountAsync());

        var blocking = Assert.Single(result.Items.Where(i => i.Result.Outcome != StageRequestOutcome.Success));
        Assert.Equal(StageCodes.IPA, blocking.StageCode);
        Assert.Contains("Complete required predecessor stages first.", blocking.Result.Errors);
    }

    private static async Task SeedStageAsync(ApplicationDbContext db, StageStatus status)
    {
        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Project",
            CreatedByUserId = "seed",
            LeadPoUserId = "po-1"
        });

        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            SortOrder = 1,
            Status = status
        });

        await db.SaveChangesAsync();
    }

    private static async Task SeedStagesAsync(
        ApplicationDbContext db,
        params (string Code, StageStatus Status)[] stages)
    {
        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Project",
            CreatedByUserId = "seed",
            LeadPoUserId = "po-1"
        });

        var sort = 1;
        foreach (var (code, status) in stages)
        {
            db.ProjectStages.Add(new ProjectStage
            {
                ProjectId = 1,
                StageCode = code,
                SortOrder = sort++,
                Status = status
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

    private sealed class StubStageValidationService : IStageValidationService
    {
        public StageValidationResult Result { get; set; } = new(
            true,
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            null);

        public Func<(int ProjectId, string StageCode, string TargetStatus, DateOnly? TargetDate, bool IsHoD), StageValidationResult>? Resolver { get; set; }
        
        public List<(int ProjectId, string StageCode, string TargetStatus, DateOnly? TargetDate, bool IsHoD)> Calls { get; } = new();

        public Task<StageValidationResult> ValidateAsync(
            int projectId,
            string stageCode,
            string targetStatus,
            DateOnly? targetDate,
            bool isHoD,
            CancellationToken ct = default)
        {
            Calls.Add((projectId, stageCode, targetStatus, targetDate, isHoD));
            if (Resolver is not null)
            {
                return Task.FromResult(Resolver((projectId, stageCode, targetStatus, targetDate, isHoD)));
            }

            return Task.FromResult(Result);
        }
    }
}
