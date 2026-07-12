using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.ActionTasks;
using ProjectManagement.Services.Workspace;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Tests;

public sealed class ConferenceTaskCommandServiceTests
{
    [Fact]
    public async Task CreateAsync_CreatesAssignedActionTaskForSelectedOfficer()
    {
        await using var scope = await TestScope.CreateAsync();
        var actor = await scope.AddUserWithRolesAsync("hod", RoleNames.HoD);
        var officer = await scope.AddUserWithRolesAsync("officer", RoleNames.ProjectOfficer);
        var taskService = new CapturingTaskService();
        var service = scope.CreateService(actor.Id, officer, taskService);

        var result = await service.CreateAsync(
            actor.Id,
            new CreateConferenceTaskRequest(
                officer.Id,
                " Prepare status brief ",
                " Consolidate the current position. ",
                scope.Clock.IstToday.AddDays(3),
                "high"));

        Assert.NotNull(taskService.CreatedTask);
        Assert.Equal("Prepare status brief", taskService.CreatedTask!.Title);
        Assert.Equal("Consolidate the current position.", taskService.CreatedTask.Description);
        Assert.Equal(actor.Id, taskService.CreatedTask.CreatedByUserId);
        Assert.Equal(officer.Id, taskService.CreatedTask.AssignedToUserId);
        Assert.Equal(RoleNames.HoD, taskService.CreatedTask.CreatedByRole);
        Assert.Equal(RoleNames.ProjectOfficer, taskService.CreatedTask.AssignedToRole);
        Assert.Equal("High", taskService.CreatedTask.Priority);
        Assert.Equal("Created from Officer Conference Review.", taskService.AuditRemarks);
        Assert.Equal(42, result.Task.ItemId);
        Assert.Equal("Assigned", result.Task.CurrentStateName);
    }

    [Fact]
    public async Task CreateAsync_DualRoleOfficerUsesRoleAssignableByHoD()
    {
        await using var scope = await TestScope.CreateAsync();
        var actor = await scope.AddUserWithRolesAsync("hod", RoleNames.HoD);
        var officer = await scope.AddUserWithRolesAsync("dual", RoleNames.HoD, RoleNames.ProjectOfficer);
        var taskService = new CapturingTaskService();
        var service = scope.CreateService(actor.Id, officer, taskService);

        await service.CreateAsync(
            actor.Id,
            new CreateConferenceTaskRequest(
                officer.Id,
                "Task",
                "Brief",
                scope.Clock.IstToday,
                "Normal"));

        Assert.Equal(RoleNames.ProjectOfficer, taskService.CreatedTask!.AssignedToRole);
    }

    [Fact]
    public async Task CreateAsync_RejectsOfficerOutsideConferenceWorkload()
    {
        await using var scope = await TestScope.CreateAsync();
        var actor = await scope.AddUserWithRolesAsync("hod", RoleNames.HoD);
        var officer = await scope.AddUserWithRolesAsync("officer", RoleNames.ProjectOfficer);
        var other = await scope.AddUserWithRolesAsync("other", RoleNames.ProjectOfficer);
        var service = scope.CreateService(actor.Id, officer, new CapturingTaskService());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            actor.Id,
            new CreateConferenceTaskRequest(
                other.Id,
                "Task",
                "Brief",
                scope.Clock.IstToday,
                "Normal")));

        Assert.Contains("current conference workload", exception.Message);
    }

    [Fact]
    public async Task CreateAsync_RejectsPastDueDate()
    {
        await using var scope = await TestScope.CreateAsync();
        var actor = await scope.AddUserWithRolesAsync("command", RoleNames.Comdt);
        var officer = await scope.AddUserWithRolesAsync("officer", RoleNames.ProjectOfficer);
        var service = scope.CreateService(actor.Id, officer, new CapturingTaskService());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            actor.Id,
            new CreateConferenceTaskRequest(
                officer.Id,
                "Task",
                "Brief",
                scope.Clock.IstToday.AddDays(-1),
                "Normal")));

        Assert.Equal("Due date cannot be in the past.", exception.Message);
    }

    private sealed class TestScope : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private TestScope(ApplicationDbContext db, SqliteConnection connection, UserManager<ApplicationUser> users)
        {
            Db = db;
            _connection = connection;
            Users = users;
        }

        public ApplicationDbContext Db { get; }
        public UserManager<ApplicationUser> Users { get; }
        public FixedActionTrackerClock Clock { get; } = new();

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

        public async Task<ApplicationUser> AddUserWithRolesAsync(string id, params string[] roles)
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

            foreach (var roleName in roles)
            {
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
                }
                Db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = role.Id });
            }

            await Db.SaveChangesAsync();
            return user;
        }

        public ConferenceTaskCommandService CreateService(
            string requestingUserId,
            ApplicationUser officer,
            IActionTaskService taskService)
            => new(
                Users,
                new FixedWorkloadService(requestingUserId, officer),
                new ActionTaskPermissionService(),
                taskService,
                Clock);

        public async ValueTask DisposeAsync()
        {
            Users.Dispose();
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

    private sealed class FixedWorkloadService(string requestingUserId, ApplicationUser officer) : IOfficerWorkloadReadService
    {
        private readonly IReadOnlyList<CommandOfficerWorkloadVm> _officers =
        [
            new CommandOfficerWorkloadVm
            {
                UserId = officer.Id,
                OfficerName = officer.FullName,
                Rank = officer.Rank,
                ProjectCount = 1,
                Projects = [new CommandOfficerProjectVm(1, "Project", "DEV", "Development", "/Projects/Overview/1")]
            }
        ];

        public Task<IReadOnlyList<CommandOfficerWorkloadVm>> GetAllAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Equals(userId, requestingUserId, StringComparison.Ordinal)
                ? _officers
                : (IReadOnlyList<CommandOfficerWorkloadVm>)Array.Empty<CommandOfficerWorkloadVm>());

        public Task<CommandOfficerWorkloadVm?> GetOfficerAsync(string officerUserId, CancellationToken cancellationToken = default)
            => Task.FromResult(_officers.FirstOrDefault(item => item.UserId == officerUserId));

        public Task<int> CountActiveOfficersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_officers.Count);
    }

    private sealed class FixedActionTrackerClock : IActionTrackerClock
    {
        public DateTime UtcNow { get; } = new(2026, 7, 12, 4, 0, 0, DateTimeKind.Utc);
        public DateTime UtcToday => UtcNow.Date;
        public DateTime IstNow => UtcNow.AddHours(5.5);
        public DateTime IstToday => IstNow.Date;
    }

    private sealed class CapturingTaskService : IActionTaskService
    {
        public ActionTaskItem? CreatedTask { get; private set; }
        public string? AuditRemarks { get; private set; }

        public Task<ActionTaskItem> CreateTaskAsync(ActionTaskItem task, CancellationToken cancellationToken = default)
            => CreateTaskAsync(task, null, cancellationToken);

        public Task<ActionTaskItem> CreateTaskAsync(ActionTaskItem task, string? auditRemarks, CancellationToken cancellationToken = default)
        {
            task.Id = 42;
            task.Status = ActionTaskStatuses.Assigned;
            CreatedTask = task;
            AuditRemarks = auditRemarks;
            return Task.FromResult(task);
        }

        public Task<List<ActionTaskItem>> GetTasksAsync(string userId, string role, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ActionTaskItem> CreateBacklogItemAsync(ActionTaskItem task, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateStatusAsync(int taskId, byte[] rowVersion, string status, string userId, string role, string? remarks = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SubmitTaskAsync(int taskId, byte[] rowVersion, string userId, string role, string? remarks = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CloseTaskAsync(int taskId, byte[] rowVersion, string userId, string role, string? remarks = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CloseTaskDirectlyAsync(int taskId, byte[] rowVersion, string closureRemarks, string closedByUserId, string role, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateTaskDateAsync(int taskId, byte[] rowVersion, DateTime newDate, string userId, string role, string? remarks = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ActionTaskItem?> GetTaskAsync(int taskId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<ActionTaskAuditLog>> GetTaskLogsAsync(int taskId, string userId, string role, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Dictionary<int, DateTime?>> GetLastActivityUtcByTaskIdsAsync(IReadOnlyCollection<int> taskIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
