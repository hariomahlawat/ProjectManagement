using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Contracts.Activities;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Activities;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Services;
using ProjectManagement.Services.Activities;
using ProjectManagement.Services.ProjectOfficeReports.Training;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

/// <summary>
/// Activity and training delete-request notifications are written in the same transaction as the
/// request or decision. These run on SQLite, which has real transactions and savepoints (the
/// in-memory provider has neither): a notification commits with its change, and a failing
/// notification never undoes it. PostgreSQL's abort-on-error behaviour is covered by
/// <see cref="TransactionalNotificationQueuePostgresIntegrationTests"/>.
/// </summary>
public sealed class DeleteRequestNotificationAtomicityTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private DbContextOptions<ApplicationDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options;
        await using var db = new ApplicationDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        // SQLite enforces the user foreign keys the in-memory provider ignores.
        foreach (var userId in new[] { "creator", "manager", "requester", "hod", "seed", "approver" })
        {
            db.Users.Add(new ApplicationUser { Id = userId, UserName = userId, NormalizedUserName = userId.ToUpperInvariant() });
        }

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActivityDeleteRequest_CommitsWithItsNotification_AndSurvivesANotificationFailure(bool failNotification)
    {
        var activityId = await SeedActivityAsync();
        await using var db = new ApplicationDbContext(_options);
        var notifier = new QueueingActivityNotifier(db, failNotification);
        var service = new ActivityDeleteRequestService(
            db,
            new UnusedActivityService(),
            notifier,
            new TestUserContext("manager", "Project Office"),
            FakeClock.AtUtc(DateTimeOffset.UtcNow),
            NullLogger<ActivityDeleteRequestService>.Instance);

        var requestId = await service.RequestAsync(activityId, "duplicate entry");

        Assert.True(requestId > 0);
        Assert.Equal(1, notifier.Attempts);
        await using var verify = new ApplicationDbContext(_options);
        Assert.Equal(1, await verify.ActivityDeleteRequests.CountAsync());
        Assert.Equal(failNotification ? 0 : 1, await verify.NotificationDispatches.CountAsync());
    }

    [Fact]
    public async Task ActivityDeleteRequest_InterruptedBeforeCommit_LeavesNeitherTheRequestNorItsNotification()
    {
        // The request is cancelled while its notification is being written, i.e. after the
        // request row was saved but before the commit. Publishing after commit (the old design)
        // left a committed request with no notification; now the two roll back together.
        var activityId = await SeedActivityAsync();
        await using var db = new ApplicationDbContext(_options);
        using var cancellation = new CancellationTokenSource();
        var service = new ActivityDeleteRequestService(
            db,
            new UnusedActivityService(),
            new CancellingActivityNotifier(cancellation),
            new TestUserContext("manager", "Project Office"),
            FakeClock.AtUtc(DateTimeOffset.UtcNow),
            NullLogger<ActivityDeleteRequestService>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.RequestAsync(activityId, "duplicate entry", cancellation.Token));

        await using var verify = new ApplicationDbContext(_options);
        Assert.Equal(0, await verify.ActivityDeleteRequests.CountAsync());
        Assert.Equal(0, await verify.NotificationDispatches.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActivityDeleteRejection_CommitsWithItsNotification_AndSurvivesANotificationFailure(bool failNotification)
    {
        var activityId = await SeedActivityAsync();
        int requestId;
        await using (var setup = new ApplicationDbContext(_options))
        {
            var request = new ActivityDeleteRequest
            {
                ActivityId = activityId,
                RequestedByUserId = "requester",
                RequestedAtUtc = DateTimeOffset.UtcNow,
                Reason = "duplicate",
            };
            setup.ActivityDeleteRequests.Add(request);
            await setup.SaveChangesAsync();
            requestId = request.Id;
        }

        await using var db = new ApplicationDbContext(_options);
        var service = new ActivityDeleteRequestService(
            db,
            new UnusedActivityService(),
            new QueueingActivityNotifier(db, failNotification),
            new TestUserContext("hod", "HoD"),
            FakeClock.AtUtc(DateTimeOffset.UtcNow),
            NullLogger<ActivityDeleteRequestService>.Instance);

        await service.RejectAsync(requestId, "keep it");

        await using var verify = new ApplicationDbContext(_options);
        Assert.NotNull((await verify.ActivityDeleteRequests.SingleAsync()).RejectedAtUtc);
        Assert.Equal(failNotification ? 0 : 1, await verify.NotificationDispatches.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TrainingDeleteRequest_CommitsWithItsNotification_AndSurvivesANotificationFailure(bool failNotification)
    {
        var trainingId = await SeedTrainingAsync();
        await using var db = new ApplicationDbContext(_options);
        var service = new TrainingWriteService(
            db,
            FakeClock.AtUtc(DateTimeOffset.UtcNow),
            new QueueingTrainingNotifier(db, failNotification),
            NullLogger<TrainingWriteService>.Instance);

        var result = await service.RequestDeleteAsync(trainingId, "entered twice", null, "requester", CancellationToken.None);

        Assert.True(result.IsSuccess);
        await using var verify = new ApplicationDbContext(_options);
        Assert.Equal(1, await verify.TrainingDeleteRequests.CountAsync());
        Assert.Equal(failNotification ? 0 : 1, await verify.NotificationDispatches.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TrainingDeleteApproval_DeletesDecidesAndNotifiesTogether_AndSurvivesANotificationFailure(bool failNotification)
    {
        var trainingId = await SeedTrainingAsync();
        Guid requestId;
        await using (var setup = new ApplicationDbContext(_options))
        {
            var requestService = new TrainingWriteService(
                setup,
                FakeClock.AtUtc(DateTimeOffset.UtcNow),
                new QueueingTrainingNotifier(setup, fail: false),
                NullLogger<TrainingWriteService>.Instance);
            requestId = (await requestService.RequestDeleteAsync(trainingId, "entered twice", null, "requester", CancellationToken.None)).RequestId!.Value;
        }

        await using var db = new ApplicationDbContext(_options);
        var service = new TrainingWriteService(
            db,
            FakeClock.AtUtc(DateTimeOffset.UtcNow),
            new QueueingTrainingNotifier(db, failNotification),
            NullLogger<TrainingWriteService>.Instance);

        var result = await service.ApproveDeleteAsync(requestId, "approver", CancellationToken.None);

        Assert.True(result.IsSuccess);
        await using var verify = new ApplicationDbContext(_options);
        // Deleting the training cascades to its delete requests, as it always has.
        Assert.Equal(0, await verify.Trainings.CountAsync());
        Assert.Equal(0, await verify.TrainingDeleteRequests.CountAsync());
        // One row from the request (set up above); the approval adds one more unless it failed.
        Assert.Equal(failNotification ? 1 : 2, await verify.NotificationDispatches.CountAsync());
    }

    private async Task<int> SeedActivityAsync()
    {
        await using var db = new ApplicationDbContext(_options);
        var type = new ActivityType { Name = "Engagements", CreatedByUserId = "creator" };
        db.ActivityTypes.Add(type);
        var activity = new Activity
        {
            Title = "Quarterly Brief",
            ActivityType = type,
            CreatedByUserId = "creator",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        db.Activities.Add(activity);
        await db.SaveChangesAsync();
        return activity.Id;
    }

    private async Task<Guid> SeedTrainingAsync()
    {
        await using var db = new ApplicationDbContext(_options);
        var type = new TrainingType
        {
            Id = Guid.NewGuid(),
            Name = "Induction Course",
            IsActive = true,
            DisplayOrder = 1,
            CreatedByUserId = "seed",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            RowVersion = Guid.NewGuid().ToByteArray(),
        };
        db.TrainingTypes.Add(type);
        await db.SaveChangesAsync();

        var service = new TrainingWriteService(
            db,
            FakeClock.AtUtc(DateTimeOffset.UtcNow),
            new QueueingTrainingNotifier(db, fail: false),
            NullLogger<TrainingWriteService>.Instance);
        var created = await service.CreateAsync(
            new TrainingMutationCommand(type.Id, new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9), null, null, 2, 3, 4, null, Array.Empty<int>()),
            "creator",
            CancellationToken.None);
        Assert.True(created.IsSuccess, created.ErrorMessage);
        return created.TrainingId!.Value;
    }

    private static NotificationDispatch Dispatch(NotificationKind kind) => new()
    {
        RecipientUserId = "recipient",
        Kind = kind,
        PayloadJson = "{}",
        CreatedUtc = DateTime.UtcNow,
    };

    /// <summary>Queues an outbox row into the caller's context like the real producer, or throws.</summary>
    private sealed class QueueingActivityNotifier(ApplicationDbContext db, bool fail) : IActivityNotificationService
    {
        public int Attempts { get; private set; }

        public Task NotifyDeleteRequestedAsync(ActivityDeleteNotificationContext context, CancellationToken cancellationToken)
            => Queue(NotificationKind.ActivityDeleteRequested);

        public Task NotifyDeleteApprovedAsync(ActivityDeleteNotificationContext context, string approverUserId, CancellationToken cancellationToken)
            => Queue(NotificationKind.ActivityDeleteApproved);

        public Task NotifyDeleteRejectedAsync(ActivityDeleteNotificationContext context, string approverUserId, string decisionNotes, CancellationToken cancellationToken)
            => Queue(NotificationKind.ActivityDeleteRejected);

        private Task Queue(NotificationKind kind)
        {
            Attempts++;
            if (fail)
            {
                throw new InvalidOperationException("Injected notification failure.");
            }

            db.NotificationDispatches.Add(Dispatch(kind));
            return Task.CompletedTask;
        }
    }

    /// <summary>Cancels the request mid-notification, as an aborted HTTP request would.</summary>
    private sealed class CancellingActivityNotifier(CancellationTokenSource cancellation) : IActivityNotificationService
    {
        public Task NotifyDeleteRequestedAsync(ActivityDeleteNotificationContext context, CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task NotifyDeleteApprovedAsync(ActivityDeleteNotificationContext context, string approverUserId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task NotifyDeleteRejectedAsync(ActivityDeleteNotificationContext context, string approverUserId, string decisionNotes, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class QueueingTrainingNotifier(ApplicationDbContext db, bool fail) : ITrainingNotificationService
    {
        public Task NotifyDeleteRequestedAsync(TrainingDeleteNotificationContext context, CancellationToken cancellationToken)
            => Queue(NotificationKind.TrainingDeleteRequested);

        public Task NotifyDeleteApprovedAsync(TrainingDeleteNotificationContext context, string approverUserId, CancellationToken cancellationToken)
            => Queue(NotificationKind.TrainingDeleteApproved);

        public Task NotifyDeleteRejectedAsync(TrainingDeleteNotificationContext context, string approverUserId, string decisionNotes, CancellationToken cancellationToken)
            => Queue(NotificationKind.TrainingDeleteRejected);

        private Task Queue(NotificationKind kind)
        {
            if (fail)
            {
                throw new InvalidOperationException("Injected notification failure.");
            }

            db.NotificationDispatches.Add(Dispatch(kind));
            return Task.CompletedTask;
        }
    }

    private sealed class TestUserContext : IUserContext
    {
        public TestUserContext(string userId, string role)
        {
            var identity = new ClaimsIdentity("Test");
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId));
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
            User = new ClaimsPrincipal(identity);
            UserId = userId;
        }

        public ClaimsPrincipal User { get; }

        public string? UserId { get; }
    }

    private sealed class UnusedActivityService : IActivityService
    {
        public Task<Activity> CreateAsync(ActivityInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Activity> UpdateAsync(int activityId, ActivityInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(int activityId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Activity?> GetAsync(int activityId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Activity>> ListByTypeAsync(int activityTypeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ActivityListResult> ListAsync(ActivityListRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ActivityReviewSummaryResult> GetReviewSummaryAsync(ActivityListRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ActivityAttachmentMetadata>> GetAttachmentMetadataAsync(int activityId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ActivityAttachment> AddAttachmentAsync(int activityId, ActivityAttachmentUpload upload, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RemoveAttachmentAsync(int attachmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
