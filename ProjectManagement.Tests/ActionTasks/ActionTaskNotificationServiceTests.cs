using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Services;
using ProjectManagement.Services.ActionTasks;
using Xunit;

namespace ProjectManagement.Tests.ActionTasks;

public sealed class ActionTaskNotificationServiceTests
{
    [Fact]
    public async Task TaskAssigned_NotifiesAssigneeAndExcludesActor()
    {
        // SECTION: Arrange
        var publisher = new RecordingNotificationPublisher();
        var service = CreateService(publisher);
        var task = NewTask(assignedTo: "assignee");

        // SECTION: Act
        await service.NotifyTaskAssignedAsync(task, "creator");

        // SECTION: Assert
        var evt = Assert.Single(publisher.Events);
        Assert.Equal(NotificationKind.ActionTaskAssigned, evt.Kind);
        Assert.Equal(new[] { "assignee" }, evt.Recipients);
        Assert.Equal("ActionTracker", evt.Module);
        Assert.Equal("ActionTask", evt.ScopeType);
        Assert.Equal("ActionTaskAssigned", evt.EventType);
    }

    [Fact]
    public async Task AssignmentFingerprint_ChangesWhenPersistedTaskVersionChanges()
    {
        var publisher = new RecordingNotificationPublisher();
        var service = CreateService(publisher);
        var task = NewTask(assignedTo: "assignee");
        task.RowVersion = new byte[] { 0x01, 0x02 };

        await service.NotifyTaskAssignedAsync(task, "creator");
        task.RowVersion = new byte[] { 0x01, 0x03 };
        await service.NotifyTaskAssignedAsync(task, "creator");

        Assert.Equal(2, publisher.Events.Count);
        Assert.NotEqual(publisher.Events[0].Fingerprint, publisher.Events[1].Fingerprint);
        Assert.Contains("0102", publisher.Events[0].Fingerprint);
        Assert.Contains("0103", publisher.Events[1].Fingerprint);
    }

    [Fact]
    public async Task DueDateFingerprint_ChangesWhenTaskReturnsToAnEarlierDueDate()
    {
        var publisher = new RecordingNotificationPublisher();
        var service = CreateService(publisher);
        var task = NewTask();
        task.RowVersion = new byte[] { 0x10 };

        await service.NotifyDueDateChangedAsync(task, new DateTime(2026, 5, 5), new DateTime(2026, 5, 10), "actor");
        task.RowVersion = new byte[] { 0x11 };
        await service.NotifyDueDateChangedAsync(task, new DateTime(2026, 5, 15), new DateTime(2026, 5, 10), "actor");

        Assert.Equal(2, publisher.Events.Count);
        Assert.NotEqual(publisher.Events[0].Fingerprint, publisher.Events[1].Fingerprint);
    }

    [Fact]
    public async Task ProgressUpdated_NotifiesCreatorAndAssignee_ExcludesActor_AndUsesUpdateIdFingerprint()
    {
        // SECTION: Arrange
        var publisher = new RecordingNotificationPublisher();
        var service = CreateService(publisher);
        var task = NewTask(createdBy: "creator", assignedTo: "assignee");

        // SECTION: Act
        await service.NotifyProgressUpdatedAsync(task, new ActionTaskUpdate { Id = 71, TaskId = task.Id }, "assignee");
        await service.NotifyProgressUpdatedAsync(task, new ActionTaskUpdate { Id = 72, TaskId = task.Id }, "assignee");

        // SECTION: Assert
        Assert.Equal(2, publisher.Events.Count);
        Assert.Equal(new[] { "creator" }, publisher.Events[0].Recipients);
        Assert.Equal("action-task:12:update:71", publisher.Events[0].Fingerprint);
        Assert.Equal("action-task:12:update:72", publisher.Events[1].Fingerprint);
        Assert.NotEqual(publisher.Events[0].Fingerprint, publisher.Events[1].Fingerprint);
    }

    [Fact]
    public async Task ConferenceRemark_UsesDirectionSpecificNotificationMetadata()
    {
        var publisher = new RecordingNotificationPublisher();
        var service = CreateService(publisher);
        var task = NewTask(createdBy: "creator", assignedTo: "assignee");
        var update = new ActionTaskUpdate
        {
            Id = 73,
            TaskId = task.Id,
            UpdateType = ActionTaskUpdateTypes.Conference
        };

        await service.NotifyProgressUpdatedAsync(task, update, "creator");

        var evt = Assert.Single(publisher.Events);
        Assert.Equal(NotificationKind.ActionTaskProgressUpdated, evt.Kind);
        Assert.Equal("ActionTaskConferenceRemarkAdded", evt.EventType);
        Assert.Equal("Conference direction added", evt.Title);
        Assert.Contains("new conference direction", evt.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "assignee" }, evt.Recipients);
        Assert.Equal("action-task:12:update:73", evt.Fingerprint);
    }

    [Fact]
    public async Task StatusChanged_NotifiesCreatorAndAssignee()
    {
        // SECTION: Arrange
        var publisher = new RecordingNotificationPublisher();
        var service = CreateService(publisher);
        var task = NewTask(createdBy: "creator", assignedTo: "assignee", status: ActionTaskStatuses.InProgress);

        // SECTION: Act
        await service.NotifyStatusChangedAsync(task, ActionTaskStatuses.Assigned, ActionTaskStatuses.InProgress, "observer");

        // SECTION: Assert
        var evt = Assert.Single(publisher.Events);
        Assert.Equal(NotificationKind.ActionTaskStatusChanged, evt.Kind);
        Assert.Equal(new[] { "assignee", "creator" }, evt.Recipients.OrderBy(x => x));
        Assert.Contains("AT-12 moved from Assigned to In Progress", evt.Summary);
    }

    [Fact]
    public async Task Blocked_NotifiesCreatorAssigneeHodAndComdt()
    {
        // SECTION: Arrange
        var publisher = new RecordingNotificationPublisher();
        var userManager = new FakeUserManager(new Dictionary<string, IReadOnlyList<ApplicationUser>>
        {
            [RoleNames.HoD] = [new ApplicationUser { Id = "hod-1" }],
            [RoleNames.Comdt] = [new ApplicationUser { Id = "comdt-1" }]
        });
        var service = CreateService(publisher, userManager);
        var task = NewTask(createdBy: "creator", assignedTo: "assignee", status: ActionTaskStatuses.Blocked);

        // SECTION: Act
        await service.NotifyTaskBlockedAsync(task, "actor");

        // SECTION: Assert
        var evt = Assert.Single(publisher.Events);
        Assert.Equal(NotificationKind.ActionTaskBlocked, evt.Kind);
        Assert.Equal(new[] { "assignee", "comdt-1", "creator", "hod-1" }, evt.Recipients.OrderBy(x => x));
    }

    [Fact]
    public async Task SubmittedForClosure_NotifiesHodComdtAndCreator()
    {
        // SECTION: Arrange
        var publisher = new RecordingNotificationPublisher();
        var userManager = new FakeUserManager(new Dictionary<string, IReadOnlyList<ApplicationUser>>
        {
            [RoleNames.HoD] = [new ApplicationUser { Id = "hod-1" }],
            [RoleNames.Comdt] = [new ApplicationUser { Id = "comdt-1" }]
        });
        var service = CreateService(publisher, userManager);
        var task = NewTask(createdBy: "creator", assignedTo: "assignee", status: ActionTaskStatuses.Submitted);

        // SECTION: Act
        await service.NotifySubmittedForClosureAsync(task, "assignee");

        // SECTION: Assert
        var evt = Assert.Single(publisher.Events);
        Assert.Equal(NotificationKind.ActionTaskSubmittedForClosure, evt.Kind);
        Assert.Equal(new[] { "comdt-1", "creator", "hod-1" }, evt.Recipients.OrderBy(x => x));
    }

    [Theory]
    [InlineData(nameof(IActionTaskNotificationService.NotifyTaskClosedAsync), NotificationKind.ActionTaskClosed)]
    [InlineData(nameof(IActionTaskNotificationService.NotifyRemovedFromSprintAsync), NotificationKind.ActionTaskRemovedFromSprint)]
    [InlineData(nameof(IActionTaskNotificationService.NotifyAddedToSprintAsync), NotificationKind.ActionTaskAddedToSprint)]
    public async Task BasicTaskNotifications_NotifyAssigneeAndCreator(string methodName, NotificationKind expectedKind)
    {
        // SECTION: Arrange
        var publisher = new RecordingNotificationPublisher();
        var service = CreateService(publisher);
        var task = NewTask(createdBy: "creator", assignedTo: "assignee");

        // SECTION: Act
        if (methodName == nameof(IActionTaskNotificationService.NotifyTaskClosedAsync))
        {
            await service.NotifyTaskClosedAsync(task, "actor", closedByCommandAuthority: true);
        }
        else if (methodName == nameof(IActionTaskNotificationService.NotifyRemovedFromSprintAsync))
        {
            await service.NotifyRemovedFromSprintAsync(task, "actor");
        }
        else
        {
            await service.NotifyAddedToSprintAsync(task, "actor");
        }

        // SECTION: Assert
        var evt = Assert.Single(publisher.Events);
        Assert.Equal(expectedKind, evt.Kind);
        Assert.Equal(new[] { "assignee", "creator" }, evt.Recipients.OrderBy(x => x));
    }

    [Fact]
    public async Task DueDateChanged_NotifiesAssigneeAndCreator_AndFormatsDateOnly()
    {
        // SECTION: Arrange
        var publisher = new RecordingNotificationPublisher();
        var service = CreateService(publisher);
        var task = NewTask(createdBy: "creator", assignedTo: "assignee");

        // SECTION: Act
        await service.NotifyDueDateChangedAsync(task, new DateTime(2026, 5, 5), new DateTime(2026, 5, 10), "actor");

        // SECTION: Assert
        var evt = Assert.Single(publisher.Events);
        Assert.Equal(NotificationKind.ActionTaskDueDateChanged, evt.Kind);
        Assert.Equal(new[] { "assignee", "creator" }, evt.Recipients.OrderBy(x => x));
        Assert.Contains("05 May 2026", evt.Summary);
        Assert.Contains("10 May 2026", evt.Summary);
    }

    [Fact]
    public async Task MovedToBacklog_NotifiesPreviousAssigneeAndCreator()
    {
        // SECTION: Arrange
        var publisher = new RecordingNotificationPublisher();
        var service = CreateService(publisher);
        var task = NewTask(createdBy: "creator", assignedTo: string.Empty, status: ActionTaskStatuses.Backlog);

        // SECTION: Act
        await service.NotifyMovedToBacklogAsync(task, "previous-assignee", "actor");

        // SECTION: Assert
        var evt = Assert.Single(publisher.Events);
        Assert.Equal(NotificationKind.ActionTaskMovedToBacklog, evt.Kind);
        Assert.Equal(new[] { "creator", "previous-assignee" }, evt.Recipients.OrderBy(x => x));
    }

    [Fact]
    public async Task Route_IncludesTaskIdAndUsesAllScope()
    {
        // SECTION: Arrange
        var publisher = new RecordingNotificationPublisher();
        var service = CreateService(publisher);
        var task = NewTask();

        // SECTION: Act
        await service.NotifyTaskAssignedAsync(task, "creator");

        // SECTION: Assert
        var evt = Assert.Single(publisher.Events);
        Assert.Equal("/ActionTasks?ViewMode=Register&TaskScope=All&TaskId=12", evt.Route);
    }

    private static ActionTaskNotificationService CreateService(
        RecordingNotificationPublisher publisher,
        UserManager<ApplicationUser>? userManager = null)
        => new(
            publisher,
            new TestPreferenceService(),
            userManager ?? new FakeUserManager(),
            new SystemClock(),
            NullLogger<ActionTaskNotificationService>.Instance);

    private static ActionTaskItem NewTask(string createdBy = "creator", string assignedTo = "assignee", string status = ActionTaskStatuses.Assigned)
        => new()
        {
            Id = 12,
            Title = "Prepare note",
            Description = "Task description",
            CreatedByUserId = createdBy,
            AssignedToUserId = assignedTo,
            CreatedByRole = RoleNames.HoD,
            AssignedToRole = RoleNames.Ta,
            DueDate = new DateTime(2026, 5, 5),
            Status = status
        };

    private sealed class FakeUserManager : UserManager<ApplicationUser>
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<ApplicationUser>> _usersByRole;

        public FakeUserManager(IReadOnlyDictionary<string, IReadOnlyList<ApplicationUser>>? usersByRole = null)
            : base(
                new FakeUserStore(),
                // SECTION: Identity options
                Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
                new PasswordHasher<ApplicationUser>(),
                Array.Empty<IUserValidator<ApplicationUser>>(),
                Array.Empty<IPasswordValidator<ApplicationUser>>(),
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                null!,
                NullLogger<UserManager<ApplicationUser>>.Instance)
        {
            _usersByRole = usersByRole ?? new Dictionary<string, IReadOnlyList<ApplicationUser>>();
        }

        public override Task<IList<ApplicationUser>> GetUsersInRoleAsync(string roleName)
        {
            IList<ApplicationUser> users = _usersByRole.TryGetValue(roleName, out var roleUsers)
                ? roleUsers.ToList()
                : new List<ApplicationUser>();
            return Task.FromResult(users);
        }
    }

    private sealed class FakeUserStore : IUserStore<ApplicationUser>
    {
        public void Dispose()
        {
        }

        public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(user.Id);

        public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(user.UserName);

        public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken)
        {
            user.UserName = userName;
            return Task.CompletedTask;
        }

        public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(user.NormalizedUserName);

        public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken)
        {
            user.NormalizedUserName = normalizedName;
            return Task.CompletedTask;
        }

        public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(IdentityResult.Success);

        public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
            => Task.FromResult<ApplicationUser?>(null);

        public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
            => Task.FromResult<ApplicationUser?>(null);
    }
}
