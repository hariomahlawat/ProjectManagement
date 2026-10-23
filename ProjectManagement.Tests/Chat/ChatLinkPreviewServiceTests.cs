using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectIdeas;
using ProjectManagement.Services.ActionTasks;
using ProjectManagement.Services.Chat;
using ProjectManagement.Services.ProjectIdeas;
using Xunit;

namespace ProjectManagement.Tests.Chat;

public sealed class ChatLinkPreviewServiceTests
{
    private static readonly Guid RepoDocId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");
    private static readonly Guid DeletedRepoDocId = Guid.Parse("7f9619ff-8b86-d011-b42d-00c04fc964ff");

    [Theory]
    [InlineData("/Projects/Overview/12", "project", "12")]
    [InlineData("/projects/overview/12/", "project", "12")]
    [InlineData("/Projects/Documents/Preview?documentId=7", "document", "7")]
    [InlineData("/ActionTasks/Details/3", "task", "3")]
    [InlineData("/ProjectIdeas/Details/4", "idea", "4")]
    [InlineData("/DocumentRepository/Documents/Reader/6F9619FF-8B86-D011-B42D-00C04FC964FF", "repository-document", "6f9619ff-8b86-d011-b42d-00c04fc964ff")]
    [InlineData("/DocumentRepository/Documents/View?id=6f9619ff-8b86-d011-b42d-00c04fc964ff", "repository-document", "6f9619ff-8b86-d011-b42d-00c04fc964ff")]
    public void Parse_RecognisesEachRecordPage(string link, string kind, string key)
    {
        var target = ChatLinkPreviewService.Parse(link, PathString.Empty);

        Assert.Equal((kind, key), target);
    }

    [Theory]
    [InlineData("/Projects/Overview/12/Timeline")]
    [InlineData("/Projects/Overview/abc")]
    [InlineData("/Projects/Documents/Preview")]
    [InlineData("/Projects/Documents/Preview?documentId=-1")]
    [InlineData("//evil.example/Projects/Overview/1")]
    [InlineData("https://prism.local/Projects/Overview/1")]
    [InlineData("/Admin/Users")]
    [InlineData("")]
    public void Parse_IgnoresAnythingElse(string link)
    {
        Assert.Null(ChatLinkPreviewService.Parse(link, PathString.Empty));
    }

    [Fact]
    public void Parse_UnderAPathBase_NeedsThePathBase()
    {
        Assert.Equal(("project", "12"), ChatLinkPreviewService.Parse("/pm/Projects/Overview/12", new PathString("/pm")));
        Assert.Null(ChatLinkPreviewService.Parse("/Projects/Overview/12", new PathString("/pm")));
    }

    [Fact]
    public async Task Project_ShowsNameOfficerAndCover_AndADeletedOneShowsNothing()
    {
        await using var scope = await PreviewScope.CreateAsync();

        var cards = await scope.Previews.PreviewAsync(
            User("alice"),
            new[] { "/pm/Projects/Overview/1", "/pm/Projects/Overview/2", "/pm/Projects/Overview/3", "/pm/Projects/Overview/999" },
            new PathString("/pm"));

        var withCover = cards.Single(c => c.Link == "/pm/Projects/Overview/1");
        Assert.Equal("project", withCover.Kind);
        Assert.Equal("Simulator Upgrade", withCover.Title);
        Assert.Equal("Project officer: Bob Bhatia", withCover.Subtitle);
        Assert.Equal("/pm/Projects/1/Photos/10/View/xs?v=3", withCover.ImageUrl);

        var noCover = cards.Single(c => c.Link == "/pm/Projects/Overview/2");
        Assert.Null(noCover.ImageUrl);
        Assert.Equal("No project officer assigned", noCover.Subtitle);

        Assert.DoesNotContain(cards, c => c.Link.EndsWith("/3", StringComparison.Ordinal)); // deleted
        Assert.DoesNotContain(cards, c => c.Link.EndsWith("/999", StringComparison.Ordinal)); // missing
    }

    [Fact]
    public async Task ProjectDocument_OnlyPublishedUnarchivedInALiveProject()
    {
        await using var scope = await PreviewScope.CreateAsync();

        var cards = await scope.Previews.PreviewAsync(
            User("alice"),
            new[] { 1, 2, 3, 4 }.Select(id => $"/Projects/Documents/Preview?documentId={id}"),
            PathString.Empty);

        var card = Assert.Single(cards);
        Assert.Equal("Test report", card.Title);
        Assert.Equal("Document · Simulator Upgrade", card.Subtitle);
    }

    [Fact]
    public async Task ActionTask_OnlyForTheAssigneeOrSomeoneWhoSeesAllTasks()
    {
        await using var scope = await PreviewScope.CreateAsync();
        var links = new[] { "/ActionTasks/Details/1" };

        // The assignee (a project officer) sees it.
        Assert.Single(await scope.Previews.PreviewAsync(User("bob", RoleNames.ProjectOfficer), links, PathString.Empty));
        // Another project officer does not, and gets nothing that says it exists.
        Assert.Empty(await scope.Previews.PreviewAsync(User("carol", RoleNames.ProjectOfficer), links, PathString.Empty));
        // A HoD sees every task.
        var card = Assert.Single(await scope.Previews.PreviewAsync(User("dan", RoleNames.HoD), links, PathString.Empty));
        Assert.Equal("Collect trial data", card.Title);
        Assert.StartsWith("Task · Assigned · due ", card.Subtitle);
        // Someone without the Action Tracker at all does not, even as the assignee.
        Assert.Empty(await scope.Previews.PreviewAsync(User("bob"), links, PathString.Empty));
        // A deleted task shows nothing, even to a HoD.
        Assert.Empty(await scope.Previews.PreviewAsync(User("dan", RoleNames.HoD), new[] { "/ActionTasks/Details/2" }, PathString.Empty));
    }

    [Fact]
    public async Task ProjectIdea_NotWhenDeleted()
    {
        await using var scope = await PreviewScope.CreateAsync();

        var cards = await scope.Previews.PreviewAsync(User("alice"), new[] { "/ProjectIdeas/Details/1", "/ProjectIdeas/Details/2" }, PathString.Empty);

        var card = Assert.Single(cards);
        Assert.Equal("Low-cost trainer", card.Title);
        Assert.Equal("Idea · On Hold", card.Subtitle);
    }

    [Fact]
    public async Task RepositoryDocument_NotWhenDeleted()
    {
        await using var scope = await PreviewScope.CreateAsync();

        var cards = await scope.Previews.PreviewAsync(
            User("alice"),
            new[] { $"/DocumentRepository/Documents/Reader/{RepoDocId}", $"/DocumentRepository/Documents/View?id={DeletedRepoDocId}" },
            PathString.Empty);

        var card = Assert.Single(cards);
        Assert.Equal("Monthly returns", card.Title);
        Assert.Equal("Repository · Training · Returns", card.Subtitle);
    }

    [Fact]
    public async Task NobodySignedIn_GetsNothing_AndRequestsAreCapped()
    {
        await using var scope = await PreviewScope.CreateAsync();

        Assert.Empty(await scope.Previews.PreviewAsync(new ClaimsPrincipal(new ClaimsIdentity()), new[] { "/Projects/Overview/1" }, PathString.Empty));

        var many = Enumerable.Range(0, 30).Select(_ => "/Projects/Overview/1")
            .Concat(Enumerable.Range(1, 30).Select(i => $"/Projects/Overview/{i}?x={i}"));
        var cards = await scope.Previews.PreviewAsync(User("alice"), many, PathString.Empty);
        Assert.True(cards.Count <= ChatLinkPreviewService.MaxLinksPerRequest);
    }

    private static ClaimsPrincipal User(string id, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, id), new(ClaimTypes.Name, id) };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private sealed class PreviewScope : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _services;

        private PreviewScope(SqliteConnection connection, ApplicationDbContext db, ServiceProvider services)
        {
            _connection = connection;
            _services = services;
            Db = db;
            Previews = new ChatLinkPreviewService(
                db,
                services.GetRequiredService<IAuthorizationService>(),
                new ActionTaskPermissionService(),
                new ProjectIdeaPermissionService());
        }

        public ApplicationDbContext Db { get; }

        public ChatLinkPreviewService Previews { get; }

        public static async Task<PreviewScope> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            await SeedAsync(db);

            // The same two policies as Program.cs (the endpoint tests use the app's own).
            var services = new ServiceCollection()
                .AddLogging()
                .AddAuthorizationCore(options =>
                {
                    options.AddPolicy(Policies.ActionTracker.Access, policy => policy.RequireRole(Policies.ActionTracker.AccessAllowedRoles));
                    options.AddPolicy(Policies.Documents.View, policy => policy.RequireAuthenticatedUser());
                })
                .BuildServiceProvider();
            return new PreviewScope(connection, db, services);
        }

        private static async Task SeedAsync(ApplicationDbContext db)
        {
            db.Users.AddRange(
                new ApplicationUser { Id = "alice", UserName = "alice", FullName = "Alice Anand" },
                new ApplicationUser { Id = "bob", UserName = "bob", FullName = "Bob Bhatia" },
                new ApplicationUser { Id = "carol", UserName = "carol", FullName = "Carol Chauhan" },
                new ApplicationUser { Id = "dan", UserName = "dan", FullName = "Dan Dutta" });

            db.Projects.AddRange(
                new Project { Id = 1, Name = "Simulator Upgrade", CreatedByUserId = "alice", LeadPoUserId = "bob", CoverPhotoVersion = 3 },
                new Project { Id = 2, Name = "Range Survey", CreatedByUserId = "alice" },
                new Project { Id = 3, Name = "Withdrawn Study", CreatedByUserId = "alice", IsDeleted = true });
            db.ProjectPhotos.Add(new ProjectPhoto
            {
                Id = 10, ProjectId = 1, StorageKey = "p/1/10", OriginalFileName = "cover.jpg", ContentType = "image/jpeg",
                Width = 800, Height = 600, Version = 3, IsCover = true, CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow,
            });

            ProjectDocument Doc(int id, int projectId, string title, ProjectDocumentStatus status = ProjectDocumentStatus.Published, bool archived = false) => new()
            {
                Id = id, ProjectId = projectId, Title = title, StorageKey = $"d/{id}", OriginalFileName = $"{id}.pdf",
                ContentType = "application/pdf", FileSize = 10, Status = status, IsArchived = archived, UploadedByUserId = "alice",
            };
            db.ProjectDocuments.AddRange(
                Doc(1, 1, "Test report"),
                Doc(2, 1, "Removed memo", ProjectDocumentStatus.SoftDeleted),
                Doc(3, 1, "Old plan", archived: true),
                Doc(4, 3, "Report of a deleted project"));

            db.ActionTasks.AddRange(
                new ActionTaskItem
                {
                    Id = 1, Title = "Collect trial data", Description = "-", CreatedByUserId = "dan", AssignedToUserId = "bob",
                    CreatedByRole = RoleNames.HoD, AssignedToRole = RoleNames.ProjectOfficer, AssignedOn = DateTime.UtcNow, DueDate = new DateTime(2026, 11, 5),
                },
                new ActionTaskItem
                {
                    Id = 2, Title = "Removed task", Description = "-", CreatedByUserId = "dan", AssignedToUserId = "bob",
                    CreatedByRole = RoleNames.HoD, AssignedToRole = RoleNames.ProjectOfficer, AssignedOn = DateTime.UtcNow, DueDate = DateTime.UtcNow, IsDeleted = true,
                });

            db.ProjectIdeas.AddRange(
                new ProjectIdea { Id = 1, Title = "Low-cost trainer", Description = "-", Status = ProjectIdeaStatuses.OnHold, CreatedByUserId = "alice" },
                new ProjectIdea { Id = 2, Title = "Dropped idea", Description = "-", CreatedByUserId = "alice", IsDeleted = true });

            db.Set<OfficeCategory>().Add(new OfficeCategory { Id = 901, Name = "Training" });
            db.Set<DocumentCategory>().Add(new DocumentCategory { Id = 902, Name = "Returns" });
            Document Repo(Guid id, string subject, bool deleted) => new()
            {
                Id = id, Subject = subject, OfficeCategoryId = 901, DocumentCategoryId = 902, OriginalFileName = "r.pdf",
                Sha256 = id.ToString("N") + id.ToString("N"), StoragePath = $"r/{id}", CreatedByUserId = "alice", CreatedAtUtc = DateTime.UtcNow, IsDeleted = deleted,
            };
            db.Documents.AddRange(Repo(RepoDocId, "Monthly returns", false), Repo(DeletedRepoDocId, "Shredded", true));

            await db.SaveChangesAsync();

            // The project and its cover photo point at each other: the cover is set second.
            (await db.Projects.SingleAsync(p => p.Id == 1)).CoverPhotoId = 10;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _services.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
