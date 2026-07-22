using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectIdeas;
using ProjectManagement.Services;
using ProjectManagement.Services.ProjectIdeas;
using ProjectManagement.Services.Workspace;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Tests;

public sealed class ConferenceIdeaCommandServiceTests
{
    [Fact]
    public async Task CreateAsync_HodCreatesActiveIdeaForSelectedOfficerUnderOwnOversight()
    {
        await using var scope = await TestScope.CreateAsync();
        var actor = await scope.AddUserWithRolesAsync("hod", RoleNames.HoD);
        var officer = await scope.AddUserWithRolesAsync("officer", RoleNames.ProjectOfficer);
        var ideas = new CapturingIdeaCommandService();
        var service = scope.CreateService(actor.Id, officer, ideas);

        var result = await service.CreateAsync(
            actor.Id,
            new CreateConferenceIdeaRequest(
                officer.Id,
                "  AI maintenance assistant  ",
                "  Explore fault diagnosis support.  ",
                AssignedHodUserId: null));

        Assert.NotNull(ideas.CreatedIdea);
        Assert.Equal("AI maintenance assistant", ideas.CreatedIdea!.Title);
        Assert.Equal("Explore fault diagnosis support.", ideas.CreatedIdea.Description);
        Assert.Equal(ProjectIdeaStatuses.Active, ideas.CreatedIdea.Status);
        Assert.Equal(officer.Id, ideas.CreatedIdea.AssignedProjectOfficerUserId);
        Assert.Equal(actor.Id, ideas.CreatedIdea.AssignedHodUserId);
        Assert.Equal(actor.Id, ideas.CreatedIdea.CreatedByUserId);
        Assert.Equal(77, result.Idea.ItemId);
        Assert.Equal("Active", result.Idea.CurrentStateName);
        Assert.Equal("/ProjectIdeas/Details/77", result.Idea.OpenUrl);
    }

    [Fact]
    public async Task CreateAsync_ComdtUsesExplicitActiveHodForOversight()
    {
        await using var scope = await TestScope.CreateAsync();
        var actor = await scope.AddUserWithRolesAsync("comdt", RoleNames.Comdt);
        var officer = await scope.AddUserWithRolesAsync("officer", RoleNames.ProjectOfficer);
        var hod = await scope.AddUserWithRolesAsync("hod", RoleNames.HoD);
        var ideas = new CapturingIdeaCommandService();
        var service = scope.CreateService(actor.Id, officer, ideas);

        await service.CreateAsync(
            actor.Id,
            new CreateConferenceIdeaRequest(
                officer.Id,
                "Idea",
                "Description",
                hod.Id));

        Assert.Equal(hod.Id, ideas.CreatedIdea!.AssignedHodUserId);
        Assert.Equal(actor.Id, ideas.CreatedIdea.CreatedByUserId);
    }

    [Fact]
    public async Task CreateAsync_ComdtRejectsMissingHodOversight()
    {
        await using var scope = await TestScope.CreateAsync();
        var actor = await scope.AddUserWithRolesAsync("comdt", RoleNames.Comdt);
        var officer = await scope.AddUserWithRolesAsync("officer", RoleNames.ProjectOfficer);
        var service = scope.CreateService(actor.Id, officer, new CapturingIdeaCommandService());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            actor.Id,
            new CreateConferenceIdeaRequest(
                officer.Id,
                "Idea",
                "Description",
                AssignedHodUserId: null)));

        Assert.Equal("Select the HoD responsible for oversight of this idea.", exception.Message);
    }

    [Fact]
    public async Task CreateAsync_DualRoleCommandUserUsesOwnHodOversight()
    {
        await using var scope = await TestScope.CreateAsync();
        var actor = await scope.AddUserWithRolesAsync("command", RoleNames.Comdt, RoleNames.HoD);
        var officer = await scope.AddUserWithRolesAsync("officer", RoleNames.ProjectOfficer);
        var otherHod = await scope.AddUserWithRolesAsync("other-hod", RoleNames.HoD);
        var ideas = new CapturingIdeaCommandService();
        var service = scope.CreateService(actor.Id, officer, ideas);

        await service.CreateAsync(
            actor.Id,
            new CreateConferenceIdeaRequest(
                officer.Id,
                "Idea",
                "Description",
                otherHod.Id));

        Assert.Equal(actor.Id, ideas.CreatedIdea!.AssignedHodUserId);
    }

    [Fact]
    public async Task CreateAsync_RejectsOfficerOutsideConferenceWorkload()
    {
        await using var scope = await TestScope.CreateAsync();
        var actor = await scope.AddUserWithRolesAsync("hod", RoleNames.HoD);
        var officer = await scope.AddUserWithRolesAsync("officer", RoleNames.ProjectOfficer);
        var other = await scope.AddUserWithRolesAsync("other", RoleNames.ProjectOfficer);
        var service = scope.CreateService(actor.Id, officer, new CapturingIdeaCommandService());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            actor.Id,
            new CreateConferenceIdeaRequest(
                other.Id,
                "Idea",
                "Description",
                actor.Id)));

        Assert.Contains("current conference workload", exception.Message);
    }

    [Fact]
    public async Task CreateAsync_RejectsSelectedOversightUserWithoutHodRole()
    {
        await using var scope = await TestScope.CreateAsync();
        var actor = await scope.AddUserWithRolesAsync("comdt", RoleNames.Comdt);
        var officer = await scope.AddUserWithRolesAsync("officer", RoleNames.ProjectOfficer);
        var invalidHod = await scope.AddUserWithRolesAsync("not-hod", RoleNames.ProjectOfficer);
        var service = scope.CreateService(actor.Id, officer, new CapturingIdeaCommandService());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            actor.Id,
            new CreateConferenceIdeaRequest(
                officer.Id,
                "Idea",
                "Description",
                invalidHod.Id)));

        Assert.Equal("Select a valid HoD for oversight of this idea.", exception.Message);
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
        public FixedClock Clock { get; } = new();

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

                Db.UserRoles.Add(new IdentityUserRole<string>
                {
                    UserId = user.Id,
                    RoleId = role.Id
                });
            }

            await Db.SaveChangesAsync();
            return user;
        }

        public ConferenceIdeaCommandService CreateService(
            string requestingUserId,
            ApplicationUser officer,
            IProjectIdeaCommandService ideaCommands)
            => new(
                Users,
                new FixedWorkloadService(requestingUserId, officer),
                ideaCommands,
                Clock,
                NullLogger<ConferenceIdeaCommandService>.Instance);

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

        public Task<IReadOnlyList<CommandOfficerWorkloadVm>> GetAllAsync(
            string userId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(string.Equals(userId, requestingUserId, StringComparison.Ordinal)
                ? _officers
                : (IReadOnlyList<CommandOfficerWorkloadVm>)Array.Empty<CommandOfficerWorkloadVm>());

        public Task<CommandOfficerWorkloadVm?> GetOfficerAsync(
            string officerUserId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_officers.FirstOrDefault(item => item.UserId == officerUserId));

        public Task<int> CountActiveOfficersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_officers.Count);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 7, 12, 4, 0, 0, TimeSpan.Zero);
    }

    private sealed class CapturingIdeaCommandService : IProjectIdeaCommandService
    {
        public ProjectIdea? CreatedIdea { get; private set; }

        public Task<ProjectIdea> CreateAsync(
            ProjectIdea idea,
            CancellationToken cancellationToken = default)
        {
            idea.Id = 77;
            idea.CreatedAt = idea.UpdatedAt = new DateTime(2026, 7, 12, 4, 0, 0, DateTimeKind.Utc);
            CreatedIdea = idea;
            return Task.FromResult(idea);
        }

        public Task<ProjectIdeaComment> AddConferenceCommentAsync(
            ProjectIdea idea,
            string text,
            string userId,
            string actorRole,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
