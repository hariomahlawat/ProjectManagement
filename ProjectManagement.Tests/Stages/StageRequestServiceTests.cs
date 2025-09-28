using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
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
            return Task.FromResult(Result);
        }
    }
}
