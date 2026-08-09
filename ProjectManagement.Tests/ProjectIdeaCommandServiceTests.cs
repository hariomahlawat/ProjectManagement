using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models.ProjectIdeas;
using ProjectManagement.Services.ProjectIdeas;

namespace ProjectManagement.Tests;

public sealed class ProjectIdeaCommandServiceTests
{
    [Fact]
    public async Task CreateAsync_PersistsIdeaThroughCommandBoundary()
    {
        await using var db = CreateDb();
        var service = new ProjectIdeaCommandService(db);
        var idea = new ProjectIdea
        {
            Title = "Conference idea",
            Description = "Explore the concept.",
            Status = ProjectIdeaStatuses.Active,
            AssignedProjectOfficerUserId = "po-1",
            AssignedHodUserId = "hod-1",
            CreatedByUserId = "hod-1"
        };

        var created = await service.CreateAsync(idea);

        Assert.True(created.Id > 0);
        var stored = await db.ProjectIdeas.SingleAsync();
        Assert.Equal("Conference idea", stored.Title);
        Assert.Equal("po-1", stored.AssignedProjectOfficerUserId);
        Assert.Equal("hod-1", stored.AssignedHodUserId);
        Assert.Equal(ProjectIdeaStatuses.Active, stored.Status);
        Assert.Equal(stored.CreatedAt, stored.UpdatedAt);
    }

    [Fact]
    public async Task AddCommentAsync_CapturesGeneralTypeAndStatusSnapshot()
    {
        await using var db = CreateDb();
        var idea = await SeedIdeaAsync(db, ProjectIdeaStatuses.OnHold);
        var service = new ProjectIdeaCommandService(db);

        await service.AddCommentAsync(idea, "  Progress recorded.  ", "po-1");
        var comment = await db.ProjectIdeaComments.SingleAsync();

        Assert.Equal(ProjectIdeaCommentTypes.General, comment.CommentType);
        Assert.Equal("Progress recorded.", comment.CommentText);
        Assert.Equal(ProjectIdeaStatuses.OnHold, comment.StatusSnapshot);
        Assert.Null(comment.CreatedByRole);
    }

    [Fact]
    public async Task AddConferenceCommentAsync_RejectsNonCommandRole()
    {
        await using var db = CreateDb();
        var idea = await SeedIdeaAsync(db, ProjectIdeaStatuses.Active);
        var service = new ProjectIdeaCommandService(db);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddConferenceCommentAsync(
                idea,
                "Command direction",
                "admin-1",
                RoleNames.Admin));

        Assert.Equal("Only Comdt or HoD may add conference remarks.", exception.Message);
        Assert.Empty(await db.ProjectIdeaComments.ToListAsync());
    }

    [Fact]
    public async Task AddConferenceCommentAsync_CapturesCommandContext()
    {
        await using var db = CreateDb();
        var idea = await SeedIdeaAsync(db, ProjectIdeaStatuses.Active);
        var service = new ProjectIdeaCommandService(db);

        var comment = await service.AddConferenceCommentAsync(
            idea,
            "Complete the feasibility review.",
            "hod-1",
            RoleNames.HoD);

        Assert.Equal(ProjectIdeaCommentTypes.Conference, comment.CommentType);
        Assert.Equal(RoleNames.HoD, comment.CreatedByRole);
        Assert.Equal(ProjectIdeaStatuses.Active, comment.StatusSnapshot);
    }



    [Theory]
    [InlineData(RoleNames.Comdt)]
    [InlineData(RoleNames.HoD)]
    [InlineData(RoleNames.Admin)]
    public async Task SoftDeleteIdeaAsync_AllowsCommandGovernanceRolesAndRetainsChildren(string role)
    {
        await using var db = CreateDb();
        var idea = await SeedIdeaAsync(db, ProjectIdeaStatuses.Active);
        db.ProjectIdeaComments.Add(new ProjectIdeaComment
        {
            ProjectIdeaId = idea.Id,
            CommentText = "Retained comment",
            CommentType = ProjectIdeaCommentTypes.General,
            CreatedByUserId = "po-1",
            StatusSnapshot = idea.Status,
            CreatedAt = DateTime.UtcNow
        });
        db.ProjectIdeaNotes.Add(new ProjectIdeaNote
        {
            ProjectIdeaId = idea.Id,
            Title = "Retained note",
            Body = "Body",
            CreatedByUserId = "po-1",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new ProjectIdeaCommandService(db);
        var deleted = await service.SoftDeleteIdeaAsync(
            idea.Id,
            "Duplicate entry",
            idea.RowVersion.ToArray(),
            Actor($"user-{role}", role));

        Assert.True(deleted);
        var stored = await db.ProjectIdeas.SingleAsync();
        Assert.True(stored.IsDeleted);
        Assert.Equal("Duplicate entry", stored.DeleteReason);
        Assert.Equal($"user-{role}", stored.DeletedByUserId);
        Assert.NotNull(stored.DeletedAt);
        Assert.Single(await db.ProjectIdeaComments.ToListAsync());
        Assert.Single(await db.ProjectIdeaNotes.ToListAsync());
    }

    [Fact]
    public async Task SoftDeleteIdeaAsync_RejectsProjectOfficer()
    {
        await using var db = CreateDb();
        var idea = await SeedIdeaAsync(db, ProjectIdeaStatuses.Active);
        var service = new ProjectIdeaCommandService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SoftDeleteIdeaAsync(
            idea.Id,
            "Entered in error",
            idea.RowVersion.ToArray(),
            Actor("po-1", RoleNames.ProjectOfficer)));

        Assert.False(idea.IsDeleted);
    }

    [Fact]
    public async Task UpdateAsync_AllowsAssignedProjectOfficerToUpdateFullOperationalRecord()
    {
        await using var db = CreateDb();
        var idea = await SeedIdeaAsync(db, ProjectIdeaStatuses.Active);
        var service = new ProjectIdeaCommandService(db);

        idea.Title = "Updated title";
        idea.Description = "Updated description";
        idea.Status = ProjectIdeaStatuses.OnHold;
        idea.AssignedProjectOfficerUserId = "po-2";
        idea.AssignedHodUserId = "hod-2";

        await service.UpdateAsync(
            idea,
            idea.RowVersion.ToArray(),
            Actor("po-1", RoleNames.ProjectOfficer));

        var stored = await db.ProjectIdeas.SingleAsync();
        Assert.Equal("Updated title", stored.Title);
        Assert.Equal("Updated description", stored.Description);
        Assert.Equal(ProjectIdeaStatuses.OnHold, stored.Status);
        Assert.Equal("po-2", stored.AssignedProjectOfficerUserId);
        Assert.Equal("hod-2", stored.AssignedHodUserId);
    }

    [Fact]
    public async Task UpdateAsync_RejectsUnassignedProjectOfficerAndAdministrator()
    {
        await using var db = CreateDb();
        var idea = await SeedIdeaAsync(db, ProjectIdeaStatuses.Active);
        var service = new ProjectIdeaCommandService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(
            idea,
            idea.RowVersion.ToArray(),
            Actor("po-2", RoleNames.ProjectOfficer)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(
            idea,
            idea.RowVersion.ToArray(),
            Actor("admin-1", RoleNames.Admin)));
    }

    [Fact]
    public async Task ArchiveAsync_RejectsAssignedProjectOfficer()
    {
        await using var db = CreateDb();
        var idea = await SeedIdeaAsync(db, ProjectIdeaStatuses.Active);
        var service = new ProjectIdeaCommandService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ArchiveAsync(
            idea,
            "Completed",
            idea.RowVersion.ToArray(),
            Actor("po-1", RoleNames.ProjectOfficer)));

        Assert.Equal(ProjectIdeaStatuses.Active, idea.Status);
    }

    [Theory]
    [InlineData(RoleNames.Comdt)]
    [InlineData(RoleNames.HoD)]
    [InlineData(RoleNames.Admin)]
    public async Task ArchiveAndRestoreAsync_AllowLifecycleAuthorities(string role)
    {
        await using var db = CreateDb();
        var idea = await SeedIdeaAsync(db, ProjectIdeaStatuses.Active);
        var service = new ProjectIdeaCommandService(db);
        var actor = Actor($"user-{role}", role);

        await service.ArchiveAsync(idea, "Closing note", idea.RowVersion.ToArray(), actor);
        Assert.Equal(ProjectIdeaStatuses.Archived, idea.Status);

        await service.RestoreAsync(idea, idea.RowVersion.ToArray(), actor);
        Assert.Equal(ProjectIdeaStatuses.Active, idea.Status);
    }

    [Fact]
    public async Task RestoreAsync_RejectsAnIdeaThatIsNotArchived()
    {
        await using var db = CreateDb();
        var idea = await SeedIdeaAsync(db, ProjectIdeaStatuses.Active);
        var service = new ProjectIdeaCommandService(db);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RestoreAsync(idea, idea.RowVersion.ToArray(), Actor("hod-1", RoleNames.HoD)));

        Assert.Equal("Only an archived idea can be restored.", exception.Message);
        Assert.Equal(ProjectIdeaStatuses.Active, idea.Status);
    }

    [Fact]
    public async Task RestoreDeletedIdeaAsync_PreservesStatusAndRelatedHistory()
    {
        await using var db = CreateDb();
        var idea = await SeedIdeaAsync(db, ProjectIdeaStatuses.Archived);
        var service = new ProjectIdeaCommandService(db);
        await service.SoftDeleteIdeaAsync(
            idea.Id,
            "Cleanup",
            idea.RowVersion.ToArray(),
            Actor("hod-1", RoleNames.HoD));
        var deletedVersion = idea.RowVersion.ToArray();

        var restored = await service.RestoreDeletedIdeaAsync(
            idea.Id,
            deletedVersion,
            Actor("admin-1", RoleNames.Admin));

        Assert.True(restored);
        Assert.False(idea.IsDeleted);
        Assert.Equal(ProjectIdeaStatuses.Archived, idea.Status);
        Assert.Null(idea.DeletedAt);
        Assert.Null(idea.DeletedByUserId);
        Assert.Null(idea.DeleteReason);
    }

    [Fact]
    public async Task EditCommentAsync_GeneralAuthorWithinWindowUpdatesTextOnly()
    {
        var now = new DateTimeOffset(2026, 8, 9, 10, 0, 0, TimeSpan.Zero);
        await using var db = CreateDb();
        var idea = await SeedIdeaAsync(db, ProjectIdeaStatuses.Active);
        var comment = await SeedCommentAsync(db, idea, ProjectIdeaCommentTypes.General, "po-1", now.UtcDateTime.AddHours(-2));
        var originalCreatedAt = comment.CreatedAt;
        var service = new ProjectIdeaCommandService(db, clock: new FixedClock(now));

        var edited = await service.EditCommentAsync(
            idea.Id,
            comment.Id,
            "  Revised progress.  ",
            comment.RowVersion.ToArray(),
            Actor("po-1", RoleNames.ProjectOfficer));

        Assert.NotNull(edited);
        Assert.Equal("Revised progress.", edited!.CommentText);
        Assert.Equal(originalCreatedAt, edited.CreatedAt);
        Assert.Equal("po-1", edited.CreatedByUserId);
        Assert.Equal(ProjectIdeaCommentTypes.General, edited.CommentType);
        Assert.NotNull(edited.EditedAt);
        Assert.Equal("po-1", edited.EditedByUserId);
    }

    [Fact]
    public async Task EditCommentAsync_ConferencePreservesIssueSnapshotAndOriginalAttribution()
    {
        var now = new DateTimeOffset(2026, 8, 9, 10, 0, 0, TimeSpan.Zero);
        await using var db = CreateDb();
        var idea = await SeedIdeaAsync(db, ProjectIdeaStatuses.OnHold);
        var comment = await SeedCommentAsync(db, idea, ProjectIdeaCommentTypes.Conference, "hod-1", now.UtcDateTime.AddDays(-7), RoleNames.HoD);
        var originalCreatedAt = comment.CreatedAt;
        var service = new ProjectIdeaCommandService(db, clock: new FixedClock(now));

        var edited = await service.EditCommentAsync(
            idea.Id,
            comment.Id,
            "Submit the revised concept paper.",
            comment.RowVersion.ToArray(),
            Actor("comdt-1", RoleNames.Comdt));

        Assert.NotNull(edited);
        Assert.Equal(ProjectIdeaStatuses.OnHold, edited!.StatusSnapshot);
        Assert.Equal(originalCreatedAt, edited.CreatedAt);
        Assert.Equal("hod-1", edited.CreatedByUserId);
        Assert.Equal(RoleNames.HoD, edited.CreatedByRole);
        Assert.Equal(ProjectIdeaCommentTypes.Conference, edited.CommentType);
    }

    [Fact]
    public async Task ConferenceCommentMutation_RejectsAdministratorToMatchProjectRemarks()
    {
        var now = new DateTimeOffset(2026, 8, 9, 10, 0, 0, TimeSpan.Zero);
        await using var db = CreateDb();
        var idea = await SeedIdeaAsync(db, ProjectIdeaStatuses.Active);
        var comment = await SeedCommentAsync(db, idea, ProjectIdeaCommentTypes.Conference, "hod-1", now.UtcDateTime.AddMinutes(-30), RoleNames.HoD);
        var service = new ProjectIdeaCommandService(db, clock: new FixedClock(now));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EditCommentAsync(
            idea.Id,
            comment.Id,
            "Attempted edit",
            comment.RowVersion.ToArray(),
            Actor("admin-1", RoleNames.Admin)));
    }

    [Fact]
    public async Task UpdateAsync_RejectsAStaleIdeaVersionWithFriendlyConcurrencyMessage()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var firstDb = new ApplicationDbContext(options);
        await firstDb.Database.EnsureCreatedAsync();
        var seeded = new ProjectIdea
        {
            Title = "Concurrent idea",
            Description = "Original",
            Status = ProjectIdeaStatuses.Active,
            CreatedByUserId = "creator"
        };
        firstDb.ProjectIdeas.Add(seeded);
        await firstDb.SaveChangesAsync();

        await using var secondDb = new ApplicationDbContext(options);
        var firstCopy = await firstDb.ProjectIdeas.SingleAsync();
        var staleCopy = await secondDb.ProjectIdeas.SingleAsync();

        firstCopy.Description = "First save";
        await new ProjectIdeaCommandService(firstDb).UpdateAsync(firstCopy, firstCopy.RowVersion.ToArray(), Actor("hod-1", RoleNames.HoD));

        staleCopy.Description = "Stale save";
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ProjectIdeaCommandService(secondDb).UpdateAsync(staleCopy, staleCopy.RowVersion.ToArray(), Actor("hod-1", RoleNames.HoD)));

        Assert.Equal(ProjectIdeaCommandService.ConcurrencyConflictMessage, exception.Message);
    }

    [Fact]
    public async Task SoftDeleteCommentAsync_LeavesAuditableRowAndHidesItOperationally()
    {
        var now = new DateTimeOffset(2026, 8, 9, 10, 0, 0, TimeSpan.Zero);
        await using var db = CreateDb();
        var idea = await SeedIdeaAsync(db, ProjectIdeaStatuses.Active);
        var comment = await SeedCommentAsync(db, idea, ProjectIdeaCommentTypes.Conference, "hod-1", now.UtcDateTime.AddDays(-2), RoleNames.HoD);
        var service = new ProjectIdeaCommandService(db, clock: new FixedClock(now));

        var deleted = await service.SoftDeleteCommentAsync(
            idea.Id,
            comment.Id,
            comment.RowVersion.ToArray(),
            Actor("hod-2", RoleNames.HoD));

        Assert.True(deleted);
        var stored = await db.ProjectIdeaComments.SingleAsync();
        Assert.True(stored.IsDeleted);
        Assert.NotNull(stored.DeletedAt);
        Assert.Equal("hod-2", stored.DeletedByUserId);
        Assert.Equal("Direction", stored.CommentText);
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task<ProjectIdea> SeedIdeaAsync(ApplicationDbContext db, string status)
    {
        var idea = new ProjectIdea
        {
            Title = "Idea",
            Description = "Description",
            Status = status,
            AssignedProjectOfficerUserId = "po-1",
            CreatedByUserId = "creator"
        };

        db.ProjectIdeas.Add(idea);
        await db.SaveChangesAsync();
        return idea;
    }

    private static async Task<ProjectIdeaComment> SeedCommentAsync(
        ApplicationDbContext db,
        ProjectIdea idea,
        string type,
        string authorUserId,
        DateTime createdAt,
        string? authorRole = null)
    {
        var comment = new ProjectIdeaComment
        {
            ProjectIdeaId = idea.Id,
            CommentText = type == ProjectIdeaCommentTypes.Conference ? "Direction" : "Progress",
            CommentType = type,
            CreatedByUserId = authorUserId,
            CreatedByRole = authorRole,
            StatusSnapshot = idea.Status,
            CreatedAt = createdAt
        };
        db.ProjectIdeaComments.Add(comment);
        await db.SaveChangesAsync();
        return comment;
    }

    private static ProjectIdeaActorContext Actor(string userId, params string[] roles) => new(userId, roles);

    private sealed class FixedClock(DateTimeOffset utcNow) : ProjectManagement.Services.IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
