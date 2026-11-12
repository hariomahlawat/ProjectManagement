using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.ItProjects;
using ProjectManagement.Models.ProjectIdeas;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Services;
using ProjectManagement.Services.ActionTasks;
using ProjectManagement.Services.ConferenceRemarks;
using ProjectManagement.Services.ItProjects;
using ProjectManagement.Services.ProjectIdeas;
using ProjectManagement.Services.Remarks;
using ProjectManagement.Services.Workspace;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Tests;

public sealed class ConferenceRemarkCommandServiceTests
{
    [Fact]
    public async Task AddAsync_Project_UsesValidStructuredAuditMetadata()
    {
        await using var scope = await TestScope.CreateAsync();
        var actor = await scope.AddCommandUserAsync("command", RoleNames.HoD);
        var officer = await scope.AddUserAsync("officer");
        var project = new Project
        {
            Name = "Project",
            CreatedByUserId = actor.Id,
            LeadPoUserId = officer.Id,
            LifecycleStatus = ProjectLifecycleStatus.Active
        };
        scope.Db.Projects.Add(project);
        await scope.Db.SaveChangesAsync();

        var remarks = new CapturingRemarkService();
        var service = scope.CreateService(remarks: remarks);

        var result = await service.AddAsync(
            actor.Id,
            new AddConferenceRemarkRequest(officer.Id, ConferenceItemKind.Project, project.Id, "Direction"));

        Assert.NotNull(remarks.Request);
        Assert.Equal(RemarkType.Conference, remarks.Request!.Type);
        Assert.Equal("Direction", remarks.Request.Body);
        using var metadata = JsonDocument.Parse(remarks.Request.Meta!);
        Assert.Equal("officer-conference-review", metadata.RootElement.GetProperty("origin").GetString());
        Assert.Equal(officer.Id, metadata.RootElement.GetProperty("officerUserId").GetString());
        Assert.Equal("Direction", result.Direction.Body);
        var projectOfficerProgress = Assert.Single(result.ProgressEntries);
        Assert.Equal("Project Officer", projectOfficerProgress.Label);
        Assert.Equal("No remark by the Project Officer after the direction.", projectOfficerProgress.EmptyText);
    }

    [Fact]
    public async Task AddAsync_Idea_DispatchesToNativeIdeaCommentStream()
    {
        await using var scope = await TestScope.CreateAsync();
        var actor = await scope.AddCommandUserAsync("command", RoleNames.Comdt);
        var officer = await scope.AddUserAsync("officer");
        var idea = new ProjectIdea
        {
            Title = "Idea",
            Description = "Description",
            Status = ProjectIdeaStatuses.Active,
            AssignedProjectOfficerUserId = officer.Id,
            CreatedByUserId = actor.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        scope.Db.ProjectIdeas.Add(idea);
        await scope.Db.SaveChangesAsync();

        var ideas = new CapturingIdeaCommandService();
        var service = scope.CreateService(ideas: ideas);

        var result = await service.AddAsync(
            actor.Id,
            new AddConferenceRemarkRequest(officer.Id, ConferenceItemKind.ProjectIdea, idea.Id, "Idea direction"));

        Assert.Equal(idea.Id, ideas.Idea?.Id);
        Assert.Equal("Idea direction", ideas.Text);
        Assert.Equal(RoleNames.Comdt, ideas.ActorRole);
        Assert.Empty(result.ProgressEntries);
        Assert.Equal("No comment or note after the direction.", result.EmptyProgressText);
    }

    [Fact]
    public async Task AddAsync_Task_DispatchesConferenceUpdateWithoutAttachments()
    {
        await using var scope = await TestScope.CreateAsync();
        var actor = await scope.AddCommandUserAsync("command", RoleNames.HoD);
        var officer = await scope.AddUserAsync("officer");
        var task = new ActionTaskItem
        {
            Title = "Task",
            Description = "Description",
            CreatedByUserId = actor.Id,
            AssignedToUserId = officer.Id,
            CreatedByRole = RoleNames.HoD,
            AssignedToRole = RoleNames.ProjectOfficer,
            AssignedOn = DateTime.UtcNow,
            DueDate = DateTime.UtcNow.AddDays(10),
            Priority = "High",
            Status = ActionTaskStatuses.Assigned
        };
        scope.Db.ActionTasks.Add(task);
        await scope.Db.SaveChangesAsync();

        var tasks = new CapturingTaskCollaborationService();
        var service = scope.CreateService(tasks: tasks);

        var result = await service.AddAsync(
            actor.Id,
            new AddConferenceRemarkRequest(officer.Id, ConferenceItemKind.ActionTask, task.Id, "Task direction"));

        Assert.Equal(task.Id, tasks.TaskId);
        Assert.Equal(ActionTaskUpdateTypes.Conference, tasks.UpdateType);
        Assert.Equal("Task direction", tasks.Body);
        Assert.Empty(tasks.Files!);
        Assert.Empty(result.ProgressSummary);
        Assert.Null(result.LatestProgressText);
        var assigneeProgress = Assert.Single(result.ProgressEntries);
        Assert.Equal("Task Assignee", assigneeProgress.Label);
        Assert.Equal("No update by the task assignee after the direction.", assigneeProgress.EmptyText);
    }

    [Fact]
    public async Task AddAsync_Project_AllowsRecentlyCompletedConferenceCarryover()
    {
        await using var scope = await TestScope.CreateAsync();
        var actor = await scope.AddCommandUserAsync("command", RoleNames.HoD);
        var officer = await scope.AddUserAsync("officer");
        var project = new Project
        {
            Name = "Completed project",
            CreatedByUserId = actor.Id,
            LeadPoUserId = officer.Id,
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            CompletedOn = new DateOnly(2026, 7, 10),
            CompletedYear = 2026,
            CompletedMonth = null
        };
        scope.Db.Projects.Add(project);
        await scope.Db.SaveChangesAsync();

        var remarks = new CapturingRemarkService();
        var projectScope = new StubConferenceProjectScopeService(officer.Id, project.Id, isAllowed: true);
        var service = scope.CreateService(remarks: remarks, projectScope: projectScope);

        var result = await service.AddAsync(
            actor.Id,
            new AddConferenceRemarkRequest(
                officer.Id,
                ConferenceItemKind.Project,
                project.Id,
                "Confirm post-completion closure."));

        Assert.NotNull(remarks.Request);
        Assert.Equal(project.Id, remarks.Request!.ProjectId);
        Assert.Equal("Confirm post-completion closure.", result.Direction.Body);
        Assert.Equal(officer.Id, projectScope.LastRequest?.OfficerUserId);
        Assert.Equal(project.Id, projectScope.LastRequest?.ProjectId);
    }

    [Fact]
    public async Task AddAsync_RejectsNonCommandRole()
    {
        await using var scope = await TestScope.CreateAsync();
        var actor = await scope.AddCommandUserAsync("project-officer", RoleNames.ProjectOfficer);

        var service = scope.CreateService();

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.AddAsync(
            actor.Id,
            new AddConferenceRemarkRequest("officer", ConferenceItemKind.Project, 1, "Direction")));

        Assert.Contains("Only Comdt or HoD", exception.Message);
    }

    [Fact]
    public async Task AddAsync_RejectsItemOutsideSelectedOfficerWorkload()
    {
        await using var scope = await TestScope.CreateAsync();
        var actor = await scope.AddCommandUserAsync("command", RoleNames.HoD);
        var assignedOfficer = await scope.AddUserAsync("assigned");
        var selectedOfficer = await scope.AddUserAsync("selected");
        var project = new Project
        {
            Name = "Project",
            CreatedByUserId = actor.Id,
            LeadPoUserId = assignedOfficer.Id,
            LifecycleStatus = ProjectLifecycleStatus.Active
        };
        scope.Db.Projects.Add(project);
        await scope.Db.SaveChangesAsync();

        var service = scope.CreateService();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddAsync(
            actor.Id,
            new AddConferenceRemarkRequest(selectedOfficer.Id, ConferenceItemKind.Project, project.Id, "Direction")));

        Assert.Contains("not part of the selected officer", exception.Message);
    }

    [Fact]
    public async Task AddAsync_ItProject_WritesAConferenceRemarkWithTheStageWhenIssued()
    {
        await using var scope = await TestScope.CreateAsync();
        var actor = await scope.AddCommandUserAsync("command", RoleNames.Comdt);
        var ito = await scope.AddCommandUserAsync("ito", RoleNames.Ito);
        var project = ItProject(actor.Id);
        project.Stages.Single(stage => stage.StageCode == ItProjectStages.Ppp).IsCompleted = true;
        scope.Db.ItProjects.Add(project);
        await scope.Db.SaveChangesAsync();

        var result = await ItService(scope).AddAsync(
            actor.Id,
            new AddConferenceRemarkRequest(ito.Id, ConferenceItemKind.ItProject, project.Id, "Get the AoN signed."));

        var remark = await scope.Db.ItProjectRemarks.SingleAsync();
        Assert.Equal(ItProjectRemarkType.Conference, remark.Type);
        Assert.Equal(RoleNames.Comdt, remark.AuthorRole);
        Assert.Equal(ItProjectStages.Aon, remark.StageCode); // the stage the case was at
        Assert.Equal(new DateOnly(2026, 7, 12), remark.EventDate); // today in India
        Assert.Equal("Get the AoN signed.", result.Direction.Body);
        Assert.Equal("Comdt", result.Direction.AuthorRole);
        Assert.Equal("AoN", result.Direction.SnapshotValue);
    }

    [Fact]
    public async Task AddAsync_ItProject_NeedsAnItoOfficerAndALiveCase()
    {
        await using var scope = await TestScope.CreateAsync();
        var actor = await scope.AddCommandUserAsync("command", RoleNames.HoD);
        var ito = await scope.AddCommandUserAsync("ito", RoleNames.Ito);
        var notIto = await scope.AddUserAsync("officer");
        var project = ItProject(actor.Id);
        var dropped = ItProject(actor.Id);
        dropped.DroppedOn = new DateOnly(2026, 7, 1);
        scope.Db.ItProjects.AddRange(project, dropped);
        await scope.Db.SaveChangesAsync();
        var service = ItService(scope);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddAsync(
            actor.Id, new AddConferenceRemarkRequest(notIto.Id, ConferenceItemKind.ItProject, project.Id, "Direction")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddAsync(
            actor.Id, new AddConferenceRemarkRequest(ito.Id, ConferenceItemKind.ItProject, dropped.Id, "Direction")));
        Assert.False(await scope.Db.ItProjectRemarks.AnyAsync());
    }

    [Fact]
    public async Task AddAsync_MoltiProject_GoesUnderItsAssignedOfficer_InTheModulesWords()
    {
        await using var scope = await TestScope.CreateAsync();
        var actor = await scope.AddCommandUserAsync("command", RoleNames.HoD);
        var officer = await scope.AddCommandUserAsync("officer", RoleNames.ProjectOfficer);
        var project = ItProject(actor.Id);
        project.Module = ItProjectModules.Molti;
        project.ProjectOfficerUserId = officer.Id;
        scope.Db.ItProjects.Add(project);
        await scope.Db.SaveChangesAsync();

        var result = await ItService(scope).AddAsync(
            actor.Id,
            new AddConferenceRemarkRequest(officer.Id, ConferenceItemKind.ItProject, project.Id, "Put up the IPA file."));

        var remark = await scope.Db.ItProjectRemarks.SingleAsync();
        Assert.Equal(ItProjectRemarkType.Conference, remark.Type);
        Assert.Equal(RoleNames.HoD, remark.AuthorRole);
        Assert.Equal(ItProjectStages.Ppp, remark.StageCode);
        Assert.Equal("MOLTI ARPPP / IPA", result.Direction.SnapshotValue);
        Assert.Equal("Progress update awaited. The officer has not written a remark since the direction was issued.", result.EmptyProgressText);
    }

    [Fact]
    public async Task AddAsync_MoltiProject_NeedsTheOfficerItIsAssignedTo()
    {
        await using var scope = await TestScope.CreateAsync();
        var actor = await scope.AddCommandUserAsync("command", RoleNames.HoD);
        var officer = await scope.AddCommandUserAsync("officer", RoleNames.ProjectOfficer);
        var another = await scope.AddCommandUserAsync("another", RoleNames.ProjectOfficer);
        var ito = await scope.AddCommandUserAsync("ito", RoleNames.Ito);
        var assigned = ItProject(actor.Id);
        assigned.Module = ItProjectModules.Molti;
        assigned.ProjectOfficerUserId = officer.Id;
        var unassigned = ItProject(actor.Id);
        unassigned.Module = ItProjectModules.Sre;
        var it = ItProject(actor.Id);
        scope.Db.ItProjects.AddRange(assigned, unassigned, it);
        await scope.Db.SaveChangesAsync();
        var service = ItService(scope);

        // Another officer, the ITO, or nobody's project: not that officer's workload.
        foreach (var (officerId, projectId) in new[]
                 {
                     (another.Id, assigned.Id),
                     (ito.Id, assigned.Id),
                     (officer.Id, unassigned.Id),
                     (officer.Id, it.Id)
                 })
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddAsync(
                actor.Id, new AddConferenceRemarkRequest(officerId, ConferenceItemKind.ItProject, projectId, "Direction")));
        }

        Assert.False(await scope.Db.ItProjectRemarks.AnyAsync());

        // An officer who lost the role no longer has the project under them.
        await scope.UserManager.RemoveFromRoleAsync(officer, RoleNames.ProjectOfficer);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddAsync(
            actor.Id, new AddConferenceRemarkRequest(officer.Id, ConferenceItemKind.ItProject, assigned.Id, "Direction")));
        Assert.False(await scope.Db.ItProjectRemarks.AnyAsync());
    }

    private static ConferenceRemarkCommandService ItService(TestScope scope)
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 7, 12, 3, 0, 0, TimeSpan.Zero));
        return new ConferenceRemarkCommandService(
            scope.Db,
            scope.UserManager,
            new CapturingRemarkService(),
            new CapturingIdeaCommandService(),
            new CapturingTaskCollaborationService(),
            clock,
            new StubConferenceProjectScopeService("none", 0, isAllowed: false),
            new ItProjectRemarkService(scope.Db, new ProjectManagement.Tests.Fakes.RecordingAudit(), clock));
    }

    private static ItProject ItProject(string userId) => new()
    {
        Title = "Core switches",
        PppReferenceNumber = "IT PPP 09/2026",
        FinancialYearStart = 2026,
        Description = "Two core switches.",
        CreatedByUserId = userId,
        CreatedAtUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        UpdatedAtUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        RowVersion = Guid.NewGuid().ToByteArray(),
        Stages = ItProjectStages.NewStageRows()
    };

    private sealed class TestScope : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private TestScope(ApplicationDbContext db, SqliteConnection connection, UserManager<ApplicationUser> userManager)
        {
            Db = db;
            _connection = connection;
            UserManager = userManager;
        }

        public ApplicationDbContext Db { get; }
        public UserManager<ApplicationUser> UserManager { get; }

        public static async Task<TestScope> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new ApplicationDbContext(options);
            await db.Database.EnsureCreatedAsync();
            return new TestScope(db, connection, CreateUserManager(db));
        }

        public async Task<ApplicationUser> AddUserAsync(string id)
        {
            var user = new ApplicationUser
            {
                Id = id,
                UserName = id,
                NormalizedUserName = id.ToUpperInvariant(),
                FullName = id,
                SecurityStamp = Guid.NewGuid().ToString()
            };
            Db.Users.Add(user);
            await Db.SaveChangesAsync();
            return user;
        }

        public async Task<ApplicationUser> AddCommandUserAsync(string id, string roleName)
        {
            var user = await AddUserAsync(id);
            var role = await Db.Roles.SingleOrDefaultAsync(candidate => candidate.Name == roleName);
            if (role is null)
            {
                role = new IdentityRole
                {
                    Id = $"role-{roleName}",
                    Name = roleName,
                    NormalizedName = roleName.ToUpperInvariant(),
                    ConcurrencyStamp = Guid.NewGuid().ToString()
                };
                Db.Roles.Add(role);
                await Db.SaveChangesAsync();
            }

            Db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = role.Id });
            await Db.SaveChangesAsync();
            return user;
        }

        public ConferenceRemarkCommandService CreateService(
            IRemarkService? remarks = null,
            IProjectIdeaCommandService? ideas = null,
            IActionTaskCollaborationService? tasks = null,
            IConferenceProjectScopeService? projectScope = null)
        {
            var clock = new FixedClock(new DateTimeOffset(2026, 7, 12, 3, 0, 0, TimeSpan.Zero));
            return projectScope is null
                ? new ConferenceRemarkCommandService(
                    Db,
                    UserManager,
                    remarks ?? new CapturingRemarkService(),
                    ideas ?? new CapturingIdeaCommandService(),
                    tasks ?? new CapturingTaskCollaborationService(),
                    clock)
                : new ConferenceRemarkCommandService(
                    Db,
                    UserManager,
                    remarks ?? new CapturingRemarkService(),
                    ideas ?? new CapturingIdeaCommandService(),
                    tasks ?? new CapturingTaskCollaborationService(),
                    clock,
                    projectScope);
        }

        public async ValueTask DisposeAsync()
        {
            UserManager.Dispose();
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private static UserManager<ApplicationUser> CreateUserManager(ApplicationDbContext db)
        {
            var store = new UserStore<ApplicationUser, IdentityRole, ApplicationDbContext>(db);
            return new UserManager<ApplicationUser>(
                store,
                new OptionsWrapper<IdentityOptions>(new IdentityOptions()),
                new PasswordHasher<ApplicationUser>(),
                Array.Empty<IUserValidator<ApplicationUser>>(),
                Array.Empty<IPasswordValidator<ApplicationUser>>(),
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                null!,
                NullLogger<UserManager<ApplicationUser>>.Instance);
        }
    }

    private sealed class StubConferenceProjectScopeService : IConferenceProjectScopeService
    {
        private readonly string _officerUserId;
        private readonly int _projectId;
        private readonly bool _isAllowed;

        public StubConferenceProjectScopeService(
            string officerUserId,
            int projectId,
            bool isAllowed)
        {
            _officerUserId = officerUserId;
            _projectId = projectId;
            _isAllowed = isAllowed;
        }

        public int CompletedProjectRetentionDays => 90;
        public (string OfficerUserId, int ProjectId)? LastRequest { get; private set; }

        public Task<IReadOnlyList<ConferenceProjectCarryover>> GetRecentlyCompletedProjectsAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ConferenceProjectCarryover>>(
                Array.Empty<ConferenceProjectCarryover>());

        public Task<bool> IsProjectInScopeAsync(
            string officerUserId,
            int projectId,
            CancellationToken cancellationToken = default)
        {
            LastRequest = (officerUserId, projectId);
            return Task.FromResult(
                _isAllowed
                && string.Equals(officerUserId, _officerUserId, StringComparison.Ordinal)
                && projectId == _projectId);
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class CapturingRemarkService : IRemarkService
    {
        public CreateRemarkRequest? Request { get; private set; }

        public Task<Remark> CreateRemarkAsync(CreateRemarkRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new Remark
            {
                Id = 10,
                ProjectId = request.ProjectId,
                Body = request.Body,
                Type = request.Type,
                Scope = request.Scope,
                AuthorUserId = request.Actor.UserId,
                AuthorRole = request.Actor.ActorRole,
                EventDate = request.EventDate,
                StageRef = "TEC",
                StageNameSnapshot = "Technical Evaluation",
                CreatedAtUtc = DateTime.UtcNow
            });
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

    private sealed class CapturingIdeaCommandService : IProjectIdeaCommandService
    {
        public ProjectIdea? Idea { get; private set; }
        public string? Text { get; private set; }
        public string? ActorRole { get; private set; }

        public Task<ProjectIdea> CreateAsync(
            ProjectIdea idea,
            CancellationToken cancellationToken = default)
        {
            Idea = idea;
            return Task.FromResult(idea);
        }

        public Task<ProjectIdeaComment> AddConferenceCommentAsync(
            ProjectIdea idea,
            string text,
            string userId,
            string actorRole,
            CancellationToken cancellationToken = default)
        {
            Idea = idea;
            Text = text;
            ActorRole = actorRole;
            return Task.FromResult(new ProjectIdeaComment
            {
                Id = 20,
                ProjectIdeaId = idea.Id,
                CommentText = text,
                CommentType = ProjectIdeaCommentTypes.Conference,
                CreatedByUserId = userId,
                CreatedByRole = actorRole,
                StatusSnapshot = idea.Status,
                CreatedAt = DateTime.UtcNow
            });
        }
    }

    private sealed class CapturingTaskCollaborationService : IActionTaskCollaborationService
    {
        public int TaskId { get; private set; }
        public string? Body { get; private set; }
        public string? UpdateType { get; private set; }
        public IReadOnlyList<IFormFile>? Files { get; private set; }

        public Task<ActionTaskUpdate> AddUpdateAsync(
            int taskId,
            string body,
            string updateType,
            string userId,
            string role,
            IReadOnlyList<IFormFile> files,
            CancellationToken cancellationToken = default)
        {
            TaskId = taskId;
            Body = body;
            UpdateType = updateType;
            Files = files;
            return Task.FromResult(new ActionTaskUpdate
            {
                Id = 30,
                TaskId = taskId,
                Body = body,
                UpdateType = updateType,
                CreatedByUserId = userId,
                CreatedByRole = role,
                StatusSnapshot = ActionTaskStatuses.Assigned,
                DueDateSnapshot = new DateOnly(2026, 7, 20),
                CreatedAtUtc = DateTime.UtcNow
            });
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
