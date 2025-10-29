using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models.Activities;
using ProjectManagement.Services;
using ProjectManagement.Services.Activities;
using Xunit;

namespace ProjectManagement.Tests.Activities;

public sealed class ActivityDeleteRequestServiceTests : IDisposable
{
    private readonly ThrowingDbContext _dbContext;
    private readonly TestClock _clock = new();
    private readonly TestUserContext _userContext = new("manager", "Project Office");
    private readonly TestNotificationService _notificationService = new();
    private readonly ActivityDeleteRequestService _service;

    public ActivityDeleteRequestServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ThrowingDbContext(options);

        var type = new ActivityType
        {
            Name = "Engagements",
            CreatedByUserId = "creator"
        };

        _dbContext.ActivityTypes.Add(type);
        _dbContext.Activities.Add(new Activity
        {
            Title = "Quarterly Brief",
            ActivityTypeId = type.Id,
            ActivityType = type,
            CreatedByUserId = "creator",
            CreatedAtUtc = _clock.UtcNow
        });

        _dbContext.SaveChanges();

        _service = new ActivityDeleteRequestService(
            _dbContext,
            new StubActivityService(),
            _notificationService,
            _userContext,
            _clock,
            NullLogger<ActivityDeleteRequestService>.Instance);
    }

    [Fact]
    public async Task RequestAsync_ThrowsValidationWhenReasonTooLong()
    {
        var activityId = _dbContext.Activities.Select(a => a.Id).First();
        var reason = new string('a', 1001);

        await Assert.ThrowsAsync<ActivityValidationException>(() => _service.RequestAsync(activityId, reason));
    }

    [Fact]
    public async Task RequestAsync_TransformsUniqueConstraintViolations()
    {
        var activityId = _dbContext.Activities.Select(a => a.Id).First();
        _dbContext.ThrowOnSave = true;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RequestAsync(activityId, null));

        Assert.Equal("A delete request is already pending for this activity.", ex.Message);
        Assert.False(_notificationService.RequestNotified);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private sealed class ThrowingDbContext : ApplicationDbContext
    {
        public ThrowingDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public bool ThrowOnSave { get; set; }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            if (ThrowOnSave && ChangeTracker.Entries<ActivityDeleteRequest>().Any(e => e.State == EntityState.Added))
            {
                throw new DbUpdateException(
                    "duplicate key value violates unique constraint \"UX_ActivityDeleteRequests_ActivityId_Pending\"",
                    new Exception("duplicate key value violates unique constraint \"UX_ActivityDeleteRequests_ActivityId_Pending\""));
            }

            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
    }

    private sealed class StubActivityService : IActivityService
    {
        public Task<Activity> CreateAsync(ActivityInput input, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<Activity> UpdateAsync(int activityId, ActivityInput input, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task DeleteAsync(int activityId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<Activity?> GetAsync(int activityId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<Activity>> ListByTypeAsync(int activityTypeId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ActivityListResult> ListAsync(ActivityListRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<ActivityAttachmentMetadata>> GetAttachmentMetadataAsync(int activityId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ActivityAttachment> AddAttachmentAsync(int activityId, ActivityAttachmentUpload upload, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task RemoveAttachmentAsync(int attachmentId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private sealed class TestNotificationService : IActivityNotificationService
    {
        public bool RequestNotified { get; private set; }

        public Task NotifyDeleteRequestedAsync(ActivityDeleteNotificationContext context, CancellationToken cancellationToken)
        {
            RequestNotified = true;
            return Task.CompletedTask;
        }

        public Task NotifyDeleteApprovedAsync(ActivityDeleteNotificationContext context, string approverUserId, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task NotifyDeleteRejectedAsync(ActivityDeleteNotificationContext context, string approverUserId, string decisionNotes, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class TestUserContext : IUserContext
    {
        public TestUserContext(string userId, string role)
        {
            var identity = new ClaimsIdentity();
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId));
            identity.AddClaim(new Claim(ClaimTypes.Role, role));

            User = new ClaimsPrincipal(identity);
            UserId = userId;
        }

        public ClaimsPrincipal User { get; }

        public string? UserId { get; }
    }
}
