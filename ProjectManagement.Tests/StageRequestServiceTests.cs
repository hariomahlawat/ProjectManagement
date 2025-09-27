using System;
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

public class StageRequestServiceTests
{
    [Fact]
    public async Task CreateAsync_CreatesPendingRequestAndLog()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 1, 10, 8, 30, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStageAsync(db, StageStatus.NotStarted);

        var service = new StageRequestService(db, clock);

        var input = new StageChangeRequestInput
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            RequestedStatus = StageStatus.InProgress.ToString(),
            RequestedDate = new DateOnly(2024, 1, 9),
            Note = "  Please begin  "
        };

        var result = await service.CreateAsync(input, "po-1");

        Assert.Equal(StageRequestOutcome.Success, result.Outcome);

        var request = await db.StageChangeRequests.SingleAsync();
        Assert.Equal("po-1", request.RequestedByUserId);
        Assert.Equal(StageStatus.InProgress.ToString(), request.RequestedStatus);
        Assert.Equal(new DateOnly(2024, 1, 9), request.RequestedDate);
        Assert.Equal("Please begin", request.Note);
        Assert.Equal("Pending", request.DecisionStatus);

        var log = await db.StageChangeLogs.SingleAsync();
        Assert.Equal("Requested", log.Action);
        Assert.Equal(StageStatus.NotStarted.ToString(), log.FromStatus);
        Assert.Equal(StageStatus.InProgress.ToString(), log.ToStatus);
        Assert.Equal(new DateOnly(2024, 1, 9), log.ToActualStart);
        Assert.Null(log.ToCompletedOn);
        Assert.Equal(clock.UtcNow, log.At);
        Assert.Equal("Please begin", log.Note);
    }

    [Fact]
    public async Task CreateAsync_DuplicatePendingReturnsConflict()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStageAsync(db, StageStatus.NotStarted);

        var service = new StageRequestService(db, clock);

        var input = new StageChangeRequestInput
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            RequestedStatus = StageStatus.InProgress.ToString()
        };

        var first = await service.CreateAsync(input, "po-1");
        var second = await service.CreateAsync(input, "po-1");

        Assert.Equal(StageRequestOutcome.Success, first.Outcome);
        Assert.Equal(StageRequestOutcome.DuplicatePending, second.Outcome);
    }

    [Fact]
    public async Task CreateAsync_DisallowedTransitionReturnsValidationError()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStageAsync(db, StageStatus.NotStarted);

        var service = new StageRequestService(db, clock);

        var result = await service.CreateAsync(new StageChangeRequestInput
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            RequestedStatus = StageStatus.Completed.ToString(),
            RequestedDate = new DateOnly(2024, 3, 5)
        }, "po-1");

        Assert.Equal(StageRequestOutcome.ValidationFailed, result.Outcome);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task CreateAsync_CompletedBeforeActualStartReturnsValidationError()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 4, 1, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStageAsync(db, StageStatus.InProgress, new DateOnly(2024, 4, 10));

        var service = new StageRequestService(db, clock);

        var result = await service.CreateAsync(new StageChangeRequestInput
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            RequestedStatus = StageStatus.Completed.ToString(),
            RequestedDate = new DateOnly(2024, 4, 5)
        }, "po-1");

        Assert.Equal(StageRequestOutcome.ValidationFailed, result.Outcome);
        Assert.Equal("Completion date cannot be before the actual start date.", result.Error);
    }

    private static async Task SeedStageAsync(ApplicationDbContext db, StageStatus status, DateOnly? actualStart = null)
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
