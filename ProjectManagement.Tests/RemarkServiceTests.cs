using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Remarks;
using ProjectManagement.Tests.Fakes;

namespace ProjectManagement.Tests;

public sealed class RemarkServiceTests
{
    [Fact]
    public async Task CreateRemarkAsync_AllowsInternalForRecognisedRole()
    {
        await using var scope = await CreateContextAsync();
        var db = scope.Db;
        await SeedProjectAsync(db, 10);
        await SeedStageAsync(db, 10, StageCodes.FS);
        var clock = FakeClock.ForIstDate(2024, 10, 1, 10, 0, 0);
        var service = new RemarkService(db, clock, NullLogger<RemarkService>.Instance);

        var actor = new RemarkActorContext("user-1", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });
        var request = new CreateRemarkRequest(
            ProjectId: 10,
            Actor: actor,
            Type: RemarkType.Internal,
            Body: "<b>Hello</b><script>alert('x')</script>",
            EventDate: new DateOnly(2024, 9, 30),
            StageRef: StageCodes.FS,
            StageNameSnapshot: null,
            Meta: "{\"origin\":\"test\"}");

        var remark = await service.CreateRemarkAsync(request, CancellationToken.None);

        Assert.Equal("user-1", remark.AuthorUserId);
        Assert.Equal(RemarkActorRole.ProjectOfficer, remark.AuthorRole);
        Assert.Equal(RemarkType.Internal, remark.Type);
        Assert.Equal("<b>Hello</b>", remark.Body);
        Assert.Equal(new DateOnly(2024, 9, 30), remark.EventDate);
        Assert.Equal(StageCodes.FS, remark.StageRef);
        Assert.Equal(StageCodes.DisplayNameOf(StageCodes.FS), remark.StageNameSnapshot);

        var audit = await db.RemarkAudits.SingleAsync(a => a.RemarkId == remark.Id);
        Assert.Equal(RemarkAuditAction.Created, audit.Action);
        Assert.Equal("user-1", audit.ActorUserId);
        Assert.Equal(RemarkActorRole.ProjectOfficer, audit.ActorRole);
        Assert.Equal("{\"origin\":\"test\"}", audit.Meta);
        Assert.Equal("<b>Hello</b>", audit.SnapshotBody);
    }

    [Fact]
    public async Task CreateRemarkAsync_ThrowsWhenExternalWithoutPrivilege()
    {
        await using var scope = await CreateContextAsync();
        var db = scope.Db;
        await SeedProjectAsync(db, 11);
        await SeedStageAsync(db, 11, StageCodes.FS);
        var service = new RemarkService(db, FakeClock.ForIstDate(2024, 9, 15, 12, 0, 0), NullLogger<RemarkService>.Instance);
        var actor = new RemarkActorContext("user-2", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });

        var request = new CreateRemarkRequest(11, actor, RemarkType.External, "Hello", new DateOnly(2024, 9, 14), StageCodes.FS, null, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateRemarkAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateRemarkAsync_ThrowsWhenStageMissing()
    {
        await using var scope = await CreateContextAsync();
        var db = scope.Db;
        await SeedProjectAsync(db, 12);
        var service = new RemarkService(db, FakeClock.ForIstDate(2024, 9, 10, 9, 0, 0), NullLogger<RemarkService>.Instance);
        var actor = new RemarkActorContext("user-3", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });
        var request = new CreateRemarkRequest(12, actor, RemarkType.Internal, "Hello", new DateOnly(2024, 9, 9), StageCodes.IPA, null, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateRemarkAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task EditRemarkAsync_ThrowsWhenWindowExpiredWithoutOverride()
    {
        await using var scope = await CreateContextAsync();
        var db = scope.Db;
        await SeedProjectAsync(db, 20);
        await SeedStageAsync(db, 20, StageCodes.FS);
        var clock = FakeClock.ForIstDate(2024, 9, 1, 9, 0, 0);
        var service = new RemarkService(db, clock, NullLogger<RemarkService>.Instance);
        var actor = new RemarkActorContext("author", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });
        var remark = await service.CreateRemarkAsync(new CreateRemarkRequest(20, actor, RemarkType.Internal, "Body", new DateOnly(2024, 8, 31), StageCodes.FS, null, null), CancellationToken.None);

        clock.Set(clock.UtcNow.AddHours(4));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EditRemarkAsync(remark.Id, new EditRemarkRequest(actor, "Updated", new DateOnly(2024, 8, 30), StageCodes.FS, null, null), CancellationToken.None));
    }

    [Fact]
    public async Task EditRemarkAsync_AllowsOverrideAfterWindow()
    {
        await using var scope = await CreateContextAsync();
        var db = scope.Db;
        await SeedProjectAsync(db, 21);
        await SeedStageAsync(db, 21, StageCodes.FS);
        var clock = FakeClock.ForIstDate(2024, 9, 1, 9, 0, 0);
        var service = new RemarkService(db, clock, NullLogger<RemarkService>.Instance);
        var author = new RemarkActorContext("author", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });
        var remark = await service.CreateRemarkAsync(new CreateRemarkRequest(21, author, RemarkType.Internal, "Body", new DateOnly(2024, 8, 30), StageCodes.FS, null, null), CancellationToken.None);

        clock.Set(clock.UtcNow.AddHours(4));
        var hodActor = new RemarkActorContext("hod", RemarkActorRole.HeadOfDepartment, new[] { RemarkActorRole.HeadOfDepartment, RemarkActorRole.ProjectOfficer });

        var updated = await service.EditRemarkAsync(remark.Id, new EditRemarkRequest(hodActor, "<i>Override</i>", new DateOnly(2024, 8, 29), StageCodes.FS, null, "{\"reason\":\"override\"}"), CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal("<i>Override</i>", updated!.Body);
        Assert.Equal(new DateOnly(2024, 8, 29), updated.EventDate);

        var audits = await db.RemarkAudits.Where(a => a.RemarkId == remark.Id).OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(2, audits.Count);
        Assert.Equal(RemarkAuditAction.Edited, audits.Last().Action);
        Assert.Equal(RemarkActorRole.HeadOfDepartment, audits.Last().ActorRole);
        Assert.Equal("{\"reason\":\"override\"}", audits.Last().Meta);
    }

    [Fact]
    public async Task SoftDeleteRemarkAsync_AllowsAuthorWithinWindow()
    {
        await using var scope = await CreateContextAsync();
        var db = scope.Db;
        await SeedProjectAsync(db, 30);
        await SeedStageAsync(db, 30, StageCodes.FS);
        var clock = FakeClock.ForIstDate(2024, 9, 1, 8, 0, 0);
        var service = new RemarkService(db, clock, NullLogger<RemarkService>.Instance);
        var actor = new RemarkActorContext("author", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });
        var remark = await service.CreateRemarkAsync(new CreateRemarkRequest(30, actor, RemarkType.Internal, "Body", new DateOnly(2024, 8, 31), StageCodes.FS, null, null), CancellationToken.None);

        var deleted = await service.SoftDeleteRemarkAsync(remark.Id, new SoftDeleteRemarkRequest(actor, "{\"reason\":\"cleanup\"}"), CancellationToken.None);

        Assert.True(deleted);
        var reloaded = await db.Remarks.SingleAsync(r => r.Id == remark.Id);
        Assert.True(reloaded.IsDeleted);
        Assert.Equal("author", reloaded.DeletedByUserId);
        Assert.Equal(RemarkActorRole.ProjectOfficer, reloaded.DeletedByRole);

        var audit = await db.RemarkAudits.OrderByDescending(a => a.Id).FirstAsync();
        Assert.Equal(RemarkAuditAction.Deleted, audit.Action);
        Assert.Equal("{\"reason\":\"cleanup\"}", audit.Meta);
    }

    [Fact]
    public async Task SoftDeleteRemarkAsync_ThrowsWhenNotAuthorAndNoOverride()
    {
        await using var scope = await CreateContextAsync();
        var db = scope.Db;
        await SeedProjectAsync(db, 31);
        await SeedStageAsync(db, 31, StageCodes.FS);
        var clock = FakeClock.ForIstDate(2024, 9, 1, 8, 0, 0);
        var service = new RemarkService(db, clock, NullLogger<RemarkService>.Instance);
        var author = new RemarkActorContext("author", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });
        var remark = await service.CreateRemarkAsync(new CreateRemarkRequest(31, author, RemarkType.Internal, "Body", new DateOnly(2024, 8, 31), StageCodes.FS, null, null), CancellationToken.None);

        var otherActor = new RemarkActorContext("other", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SoftDeleteRemarkAsync(remark.Id, new SoftDeleteRemarkRequest(otherActor, null), CancellationToken.None));
    }

    [Fact]
    public async Task ListRemarksAsync_FiltersAndIncludesDeletedForAdmin()
    {
        await using var scope = await CreateContextAsync();
        var db = scope.Db;
        await SeedProjectAsync(db, 40);
        await SeedStageAsync(db, 40, StageCodes.FS);
        await SeedStageAsync(db, 40, StageCodes.IPA);
        var clock = FakeClock.ForIstDate(2024, 9, 1, 8, 0, 0);
        var service = new RemarkService(db, clock, NullLogger<RemarkService>.Instance);
        var author = new RemarkActorContext("author", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });
        var admin = new RemarkActorContext("admin", RemarkActorRole.Administrator, new[] { RemarkActorRole.Administrator, RemarkActorRole.ProjectOfficer });

        await service.CreateRemarkAsync(new CreateRemarkRequest(40, author, RemarkType.Internal, "One", new DateOnly(2024, 8, 30), StageCodes.FS, null, null), CancellationToken.None);
        var remarkToDelete = await service.CreateRemarkAsync(new CreateRemarkRequest(40, author, RemarkType.Internal, "Two", new DateOnly(2024, 8, 29), StageCodes.IPA, null, null), CancellationToken.None);
        await service.SoftDeleteRemarkAsync(remarkToDelete.Id, new SoftDeleteRemarkRequest(admin, null), CancellationToken.None);

        var result = await service.ListRemarksAsync(new ListRemarksRequest(40, admin, Type: RemarkType.Internal, AuthorRole: null, StageRef: null, FromDate: new DateOnly(2024, 8, 29), ToDate: new DateOnly(2024, 8, 30), IncludeDeleted: true, Page: 1, PageSize: 10), CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, r => r.IsDeleted);
    }

    [Fact]
    public async Task GetRemarkAuditAsync_ThrowsForNonAdmin()
    {
        await using var scope = await CreateContextAsync();
        var db = scope.Db;
        await SeedProjectAsync(db, 50);
        await SeedStageAsync(db, 50, StageCodes.FS);
        var clock = FakeClock.ForIstDate(2024, 9, 1, 9, 0, 0);
        var service = new RemarkService(db, clock, NullLogger<RemarkService>.Instance);
        var actor = new RemarkActorContext("author", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });
        var remark = await service.CreateRemarkAsync(new CreateRemarkRequest(50, actor, RemarkType.Internal, "Body", new DateOnly(2024, 8, 31), StageCodes.FS, null, null), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetRemarkAuditAsync(remark.Id, actor, CancellationToken.None));
    }

    [Fact]
    public async Task GetRemarkAuditAsync_ReturnsEntriesForAdmin()
    {
        await using var scope = await CreateContextAsync();
        var db = scope.Db;
        await SeedProjectAsync(db, 51);
        await SeedStageAsync(db, 51, StageCodes.FS);
        var clock = FakeClock.ForIstDate(2024, 9, 1, 9, 0, 0);
        var service = new RemarkService(db, clock, NullLogger<RemarkService>.Instance);
        var author = new RemarkActorContext("author", RemarkActorRole.ProjectOfficer, new[] { RemarkActorRole.ProjectOfficer });
        var remark = await service.CreateRemarkAsync(new CreateRemarkRequest(51, author, RemarkType.Internal, "Body", new DateOnly(2024, 8, 31), StageCodes.FS, null, null), CancellationToken.None);

        var admin = new RemarkActorContext("admin", RemarkActorRole.Administrator, new[] { RemarkActorRole.Administrator });
        var audits = await service.GetRemarkAuditAsync(remark.Id, admin, CancellationToken.None);

        Assert.Single(audits);
        Assert.Equal(RemarkAuditAction.Created, audits[0].Action);
    }

    private static async Task<SqliteContextScope> CreateContextAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();

        return new SqliteContextScope(context, connection);
    }

    private static async Task SeedProjectAsync(ApplicationDbContext db, int projectId)
    {
        db.Projects.Add(new Project
        {
            Id = projectId,
            Name = $"Project {projectId}",
            CreatedByUserId = "seed"
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedStageAsync(ApplicationDbContext db, int projectId, string stageCode)
    {
        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = projectId,
            StageCode = stageCode,
            SortOrder = 1,
            Status = StageStatus.NotStarted
        });
        await db.SaveChangesAsync();
    }
    private sealed class SqliteContextScope : IAsyncDisposable
    {
        public SqliteContextScope(ApplicationDbContext db, SqliteConnection connection)
        {
            Db = db;
            _connection = connection;
        }

        public ApplicationDbContext Db { get; }

        private readonly SqliteConnection _connection;

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
