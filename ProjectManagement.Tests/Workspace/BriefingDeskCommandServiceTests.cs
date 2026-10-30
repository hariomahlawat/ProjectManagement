using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectIdeas;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Services.ActionTasks;
using ProjectManagement.Services.ProjectIdeas;
using ProjectManagement.Services.Remarks;
using ProjectManagement.Services.Workspace;
using Xunit;

namespace ProjectManagement.Tests.Workspace;

public sealed class BriefingDeskCommandServiceTests
{
    private const string OfficerId = "po-1";
    private const string OtherOfficerId = "po-2";

    // SECTION: Project updates
    [Fact]
    public async Task ProjectUpdate_ByAssignedOfficer_PostsAnInternalRemarkAsProjectOfficer()
    {
        await using var scope = await TestScope.CreateAsync();

        var result = await scope.Service.PostProjectUpdateAsync(Officer(), OfficerId, 10, "  Vendor trials moved to 4 Nov.  ", CancellationToken.None);

        Assert.True(result.Succeeded);
        var request = Assert.IsType<CreateRemarkRequest>(scope.Remarks.Request);
        Assert.Equal(10, request.ProjectId);
        Assert.Equal(RemarkType.Internal, request.Type);
        Assert.Equal(RemarkScope.General, request.Scope);
        Assert.Equal(RemarkActorRole.ProjectOfficer, request.Actor.ActorRole);
        Assert.Equal(OfficerId, request.Actor.UserId);
        Assert.Equal("Vendor trials moved to 4 Nov.", request.Body);
        Assert.Equal(new DateOnly(2030, 1, 15), request.EventDate);
    }

    [Fact]
    public async Task ProjectUpdate_OnAnotherOfficersProject_IsForbidden()
    {
        await using var scope = await TestScope.CreateAsync();

        var result = await scope.Service.PostProjectUpdateAsync(Officer(OtherOfficerId), OtherOfficerId, 10, "Update", CancellationToken.None);

        Assert.Equal(BriefingDeskCommandOutcome.Forbidden, result.Outcome);
        Assert.Null(scope.Remarks.Request);
    }

    [Fact]
    public async Task ProjectUpdate_WithoutTheProjectOfficerRole_IsForbidden()
    {
        await using var scope = await TestScope.CreateAsync();

        var result = await scope.Service.PostProjectUpdateAsync(Principal(OfficerId, RoleNames.HoD), OfficerId, 10, "Update", CancellationToken.None);

        Assert.Equal(BriefingDeskCommandOutcome.Forbidden, result.Outcome);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task ProjectUpdate_WithEmptyText_IsInvalid(string? body)
    {
        await using var scope = await TestScope.CreateAsync();

        var result = await scope.Service.PostProjectUpdateAsync(Officer(), OfficerId, 10, body, CancellationToken.None);

        Assert.Equal(BriefingDeskCommandOutcome.Invalid, result.Outcome);
        Assert.Null(scope.Remarks.Request);
    }

    [Fact]
    public async Task ProjectUpdate_ForMissingProject_IsNotFound()
    {
        await using var scope = await TestScope.CreateAsync();

        var result = await scope.Service.PostProjectUpdateAsync(Officer(), OfficerId, 999, "Update", CancellationToken.None);

        Assert.Equal(BriefingDeskCommandOutcome.NotFound, result.Outcome);
    }

    // SECTION: Idea notes
    [Fact]
    public async Task IdeaNote_ByAssignedOfficer_IsSavedThroughTheIdeaCommandService()
    {
        await using var scope = await TestScope.CreateAsync();

        var result = await scope.Service.PostIdeaNoteAsync(Officer(), OfficerId, scope.IdeaId, "Spoke to the DRDO lab.", CancellationToken.None);

        Assert.True(result.Succeeded);
        var note = await scope.Db.ProjectIdeaNotes.SingleAsync();
        Assert.Equal("Progress update · 15 Jan 2030", note.Title);
        Assert.Equal("Spoke to the DRDO lab.", note.Body);
        Assert.Equal(OfficerId, note.CreatedByUserId);
    }

    [Fact]
    public async Task IdeaNote_ByAnotherOfficer_IsForbidden()
    {
        await using var scope = await TestScope.CreateAsync();

        var result = await scope.Service.PostIdeaNoteAsync(Officer(OtherOfficerId), OtherOfficerId, scope.IdeaId, "Note", CancellationToken.None);

        Assert.Equal(BriefingDeskCommandOutcome.Forbidden, result.Outcome);
        Assert.Empty(scope.Db.ProjectIdeaNotes);
    }

    // SECTION: Tasks
    [Fact]
    public async Task TaskProgress_ByAssignee_PostsACommentUpdate()
    {
        await using var scope = await TestScope.CreateAsync();

        var result = await scope.Service.PostTaskProgressAsync(Officer(), OfficerId, scope.TaskId, "Draft ready.", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ActionTaskUpdateTypes.Comment, scope.Collaboration.UpdateType);
        Assert.Equal("Draft ready.", scope.Collaboration.Body);
        Assert.Equal(RoleNames.ProjectOfficer, scope.Collaboration.Role);
    }

    [Fact]
    public async Task TaskProgress_ByAnotherUser_IsForbidden()
    {
        await using var scope = await TestScope.CreateAsync();

        var result = await scope.Service.PostTaskProgressAsync(Officer(OtherOfficerId), OtherOfficerId, scope.TaskId, "Draft ready.", CancellationToken.None);

        Assert.Equal(BriefingDeskCommandOutcome.Forbidden, result.Outcome);
        Assert.Null(scope.Collaboration.Body);
    }

    [Fact]
    public async Task SubmitTask_RequiresAClosureNote()
    {
        await using var scope = await TestScope.CreateAsync();

        var result = await scope.Service.SubmitTaskAsync(Officer(), OfficerId, scope.TaskId, "AQI=", "  ", CancellationToken.None);

        Assert.Equal(BriefingDeskCommandOutcome.Invalid, result.Outcome);
        Assert.Empty(scope.TaskCalls.Calls);
    }

    [Fact]
    public async Task SubmitTask_ByAssignee_CallsTheTaskServiceWithTheDecodedVersion()
    {
        await using var scope = await TestScope.CreateAsync();

        var result = await scope.Service.SubmitTaskAsync(Officer(), OfficerId, scope.TaskId, "AQI=", "Report filed in DMS.", CancellationToken.None);

        Assert.True(result.Succeeded);
        var call = Assert.Single(scope.TaskCalls.Calls);
        Assert.Equal(nameof(IActionTaskService.SubmitTaskAsync), call.Name);
        Assert.Equal(new byte[] { 1, 2 }, (byte[])call.Args[1]!);
        Assert.Equal("Report filed in DMS.", call.Args[4]);
    }

    [Fact]
    public async Task FlagBlocked_ByAssignee_SetsTheBlockedStatusWithTheReason()
    {
        await using var scope = await TestScope.CreateAsync();

        var result = await scope.Service.FlagTaskBlockedAsync(Officer(), OfficerId, scope.TaskId, "AQI=", "Awaiting vendor data.", CancellationToken.None);

        Assert.True(result.Succeeded);
        var call = Assert.Single(scope.TaskCalls.Calls);
        Assert.Equal(nameof(IActionTaskService.UpdateStatusAsync), call.Name);
        Assert.Equal(ActionTaskStatuses.Blocked, call.Args[2]);
        Assert.Equal("Awaiting vendor data.", call.Args[5]);
    }

    [Fact]
    public async Task SubmitTask_ServiceRefusal_IsReportedAsInvalid()
    {
        await using var scope = await TestScope.CreateAsync();
        scope.TaskCalls.Throw = new ActionTaskConcurrencyException("This task was updated by another user.");

        var result = await scope.Service.SubmitTaskAsync(Officer(), OfficerId, scope.TaskId, "AQI=", "Done.", CancellationToken.None);

        Assert.Equal(BriefingDeskCommandOutcome.Invalid, result.Outcome);
        Assert.Equal("This task was updated by another user.", result.Message);
    }

    [Fact]
    public async Task SubmitTask_ClosureNoteOverTheAuditLimit_IsInvalidBeforeTheTaskServiceIsCalled()
    {
        await using var scope = await TestScope.CreateAsync();

        var note = new string('x', BriefingDeskCommandService.MaxTaskNoteLength + 1);
        var result = await scope.Service.SubmitTaskAsync(Officer(), OfficerId, scope.TaskId, "AQI=", note, CancellationToken.None);

        Assert.Equal(BriefingDeskCommandOutcome.Invalid, result.Outcome);
        Assert.Contains("2000 characters", result.Message);
        Assert.Empty(scope.TaskCalls.Calls);
    }

    [Fact]
    public async Task FlagBlocked_OnAnAlreadyBlockedTask_IsRefusedInsteadOfDroppingTheReason()
    {
        await using var scope = await TestScope.CreateAsync();
        var task = await scope.Db.ActionTasks.SingleAsync(t => t.Id == scope.TaskId);
        task.Status = ActionTaskStatuses.Blocked;
        await scope.Db.SaveChangesAsync();

        var result = await scope.Service.FlagTaskBlockedAsync(Officer(), OfficerId, scope.TaskId, "AQI=", "New blocker.", CancellationToken.None);

        Assert.Equal(BriefingDeskCommandOutcome.Invalid, result.Outcome);
        Assert.Contains("already flagged blocked", result.Message);
        Assert.Empty(scope.TaskCalls.Calls);
    }

    // SECTION: Test scope
    private static ClaimsPrincipal Officer(string userId = OfficerId) => Principal(userId, RoleNames.ProjectOfficer);

    private static ClaimsPrincipal Principal(string userId, string role)
        => new(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, userId), new Claim(ClaimTypes.Role, role) },
            authenticationType: "Test"));

    private sealed class TestScope : IAsyncDisposable
    {
        private TestScope(ApplicationDbContext db)
        {
            Db = db;
            Tasks = RecordingProxy<IActionTaskService>.Create(out var recorder);
            TaskCalls = recorder;
            Service = new BriefingDeskCommandService(
                db,
                Remarks,
                new ProjectIdeaCommandService(db),
                new ProjectIdeaPermissionService(),
                Tasks,
                Collaboration,
                new FixedClock());
        }

        public ApplicationDbContext Db { get; }
        public CapturingRemarkService Remarks { get; } = new();
        public CapturingCollaboration Collaboration { get; } = new();
        public IActionTaskService Tasks { get; }
        public RecordingProxy<IActionTaskService> TaskCalls { get; }
        public BriefingDeskCommandService Service { get; }
        public int IdeaId { get; private set; }
        public int TaskId { get; private set; }

        public static async Task<TestScope> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var scope = new TestScope(new ApplicationDbContext(options));
            scope.Db.Projects.Add(new Project { Id = 10, Name = "VR Based BFSR", LeadPoUserId = OfficerId, CreatedByUserId = "hod-1" });
            var idea = new ProjectIdea
            {
                Title = "Fall-of-shot correction",
                Description = "Explore it.",
                Status = ProjectIdeaStatuses.Active,
                AssignedProjectOfficerUserId = OfficerId,
                CreatedByUserId = "hod-1"
            };
            scope.Db.ProjectIdeas.Add(idea);
            var task = new ActionTaskItem
            {
                Title = "Prepare TEC report",
                AssignedToUserId = OfficerId,
                AssignedToRole = RoleNames.ProjectOfficer,
                CreatedByUserId = "hod-1",
                Status = ActionTaskStatuses.InProgress,
                DueDate = new DateTime(2030, 1, 20)
            };
            scope.Db.ActionTasks.Add(task);
            await scope.Db.SaveChangesAsync();
            scope.IdeaId = idea.Id;
            scope.TaskId = task.Id;
            return scope;
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FixedClock : IActionTrackerClock
    {
        public DateTime UtcNow => new(2030, 1, 15, 6, 30, 0, DateTimeKind.Utc);
        public DateTime UtcToday => UtcNow.Date;
        public DateTime IstNow => UtcNow.AddHours(5.5);
        public DateTime IstToday => IstNow.Date;
    }

    private sealed class CapturingRemarkService : IRemarkService
    {
        public CreateRemarkRequest? Request { get; private set; }

        public Task<Remark> CreateRemarkAsync(CreateRemarkRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new Remark { Id = 1, ProjectId = request.ProjectId, Body = request.Body });
        }

        public Task<RemarkListResult> ListRemarksAsync(ListRemarksRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<Remark?> EditRemarkAsync(int remarkId, EditRemarkRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<bool> SoftDeleteRemarkAsync(int remarkId, SoftDeleteRemarkRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<RemarkAudit>> GetRemarkAuditAsync(int remarkId, RemarkActorContext actor, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class CapturingCollaboration : IActionTaskCollaborationService
    {
        public string? Body { get; private set; }
        public string? UpdateType { get; private set; }
        public string? Role { get; private set; }

        public Task<ActionTaskUpdate> AddUpdateAsync(int taskId, string body, string updateType, string userId, string role, IReadOnlyList<IFormFile> files, CancellationToken cancellationToken = default)
        {
            Body = body;
            UpdateType = updateType;
            Role = role;
            return Task.FromResult(new ActionTaskUpdate { Id = 1, TaskId = taskId, Body = body, UpdateType = updateType, CreatedByUserId = userId });
        }

        public Task<ActionTaskUpdate?> AddUpdateAndMaybeChangeStatusAsync(int taskId, string body, string? newStatus, string userId, string role, IReadOnlyList<IFormFile> files, byte[] rowVersion, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<List<ActionTaskUpdate>> GetUpdatesAsync(int taskId, string userId, string role, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<int, IReadOnlyList<ActionTaskAttachmentMetadata>>> GetAttachmentMetadataByUpdateAsync(int taskId, string userId, string role, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task EditRemarkAsync(int taskId, int updateId, string body, string userId, string role, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task DeleteRemarkAsync(int taskId, int updateId, string userId, string role, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}

// Records interface calls without hand-writing every member of a wide service interface.
public class RecordingProxy<T> : DispatchProxy where T : class
{
    public List<(string Name, object?[] Args)> Calls { get; } = new();

    public Exception? Throw { get; set; }

    public static T Create(out RecordingProxy<T> recorder)
    {
        var proxy = Create<T, RecordingProxy<T>>();
        recorder = (RecordingProxy<T>)(object)proxy;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        Calls.Add((targetMethod!.Name, args ?? Array.Empty<object?>()));
        if (Throw is not null)
        {
            return Task.FromException(Throw);
        }

        return targetMethod.ReturnType == typeof(Task) ? Task.CompletedTask : null;
    }
}
