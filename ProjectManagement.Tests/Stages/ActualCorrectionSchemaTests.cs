using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using Xunit;

namespace ProjectManagement.Tests.Stages;

/// <summary>
/// Phase D-2: the Actual-correction proposal tables (plan section D.2). The
/// constraints are checked against SQLite here; the PostgreSQL physical schema is
/// checked by <see cref="ActualCorrectionSchemaPostgresFixture"/>.
/// </summary>
public sealed class ActualCorrectionSchemaTests
{
    public const string MigrationId = "20261217053003_AddActualCorrectionProposals";

    [Fact]
    public void Model_MapsTheApprovedTablesIndexesAndDeleteBehaviour()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata-only").Options);
        // Check constraints live on the design-time model only.
        var model = db.GetService<IDesignTimeModel>().Model;
        var revision = model.FindEntityType(typeof(ActualCorrectionRevision))!;
        var item = model.FindEntityType(typeof(ActualCorrectionItem))!;

        Assert.Equal("ActualCorrectionRevisions", revision.GetTableName());
        Assert.Equal("ActualCorrectionItems", item.GetTableName());

        var pending = revision.GetIndexes().Single(index => index.GetDatabaseName() == "ux_actualcorrectionrevisions_pending");
        Assert.True(pending.IsUnique);
        Assert.Equal("\"Status\" = 'Pending'", pending.GetFilter());
        Assert.Equal(new[] { "ProjectId" }, pending.Properties.Select(property => property.Name));
        Assert.True(revision.GetIndexes().Single(index => index.GetDatabaseName() == "ux_actualcorrectionrevisions_project_revision").IsUnique);
        Assert.True(item.GetIndexes().Single(index => index.GetDatabaseName() == "ux_actualcorrectionitems_revision_stage").IsUnique);

        Assert.Equal(DeleteBehavior.Cascade, revision.GetForeignKeys().Single(fk => fk.PrincipalEntityType.ClrType == typeof(Project)).DeleteBehavior);
        Assert.All(revision.GetForeignKeys().Where(fk => fk.PrincipalEntityType == revision),
            fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
        Assert.Equal(DeleteBehavior.Restrict, item.GetForeignKeys().Single().DeleteBehavior);

        Assert.Equal(
            new[]
            {
                "CK_ActualCorrectionRevisions_Closure", "CK_ActualCorrectionRevisions_EntryPoint",
                "CK_ActualCorrectionRevisions_RevisionNo", "CK_ActualCorrectionRevisions_Status"
            },
            revision.GetCheckConstraints().Select(check => check.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(
            new[] { "CK_ActualCorrectionItems_ClearsActualStart", "CK_ActualCorrectionItems_Kind" },
            item.GetCheckConstraints().Select(check => check.Name).OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void Migration_IsTheLineageTail()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata-only").Options);
        var migrations = db.Database.GetMigrations().ToArray();

        Assert.Equal(MigrationId, migrations[^1]);
        Assert.Equal("20261217000002_VersionPlanningInputAuthority", migrations[^2]);
    }

    [Fact]
    public async Task ValidLineage_WithItems_IsAccepted()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.NewContext();

        var (first, second) = await SeedSupersededPairAsync(db, fixture.ProjectId);

        await using var verify = fixture.NewContext();
        var stored = await verify.ActualCorrectionRevisions.Include(revision => revision.Items).OrderBy(revision => revision.RevisionNo).ToListAsync();
        Assert.Equal(new[] { "Superseded", "Pending" }, stored.Select(revision => revision.Status));
        Assert.Equal(second, stored[0].SupersededByRevisionId);
        Assert.Equal(first, stored[1].SupersedesRevisionId);
        Assert.Equal(2, stored.Sum(revision => revision.Items.Count));
    }

    [Fact]
    public async Task SecondPendingRevisionForAProject_IsRejected()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.NewContext();
        db.ActualCorrectionRevisions.Add(Revision(fixture.ProjectId, 1));
        await db.SaveChangesAsync();

        db.ActualCorrectionRevisions.Add(Revision(fixture.ProjectId, 2));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task DuplicateRevisionNumberForAProject_IsRejected()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.NewContext();
        db.ActualCorrectionRevisions.Add(Closed(Revision(fixture.ProjectId, 1), ActualCorrectionRevisionStatus.Withdrawn));
        db.ActualCorrectionRevisions.Add(Revision(fixture.ProjectId, 1));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData("Draft", false)]                                   // not an approved status
    [InlineData(ActualCorrectionRevisionStatus.Approved, false)]   // terminal without closure
    [InlineData(ActualCorrectionRevisionStatus.Pending, true)]     // pending with closure
    public async Task RevisionStatusAndClosure_AreConstrained(string status, bool closed)
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.NewContext();
        var revision = Revision(fixture.ProjectId, 1);
        revision.Status = status;
        if (closed)
        {
            revision.ClosedByUserId = "hod";
            revision.ClosedOn = DateTimeOffset.UtcNow;
        }

        db.ActualCorrectionRevisions.Add(revision);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData("Import", 1)]
    [InlineData(ActualCorrectionEntryPoint.Editor, 0)]
    public async Task RevisionEntryPointAndNumber_AreConstrained(string entryPoint, int revisionNo)
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.NewContext();
        var revision = Revision(fixture.ProjectId, revisionNo);
        revision.EntryPoint = entryPoint;
        db.ActualCorrectionRevisions.Add(revision);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ItemKindStageAndClearIntent_AreConstrained()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var db = fixture.NewContext())
        {
            db.ActualCorrectionRevisions.Add(Revision(fixture.ProjectId, 1));
            await db.SaveChangesAsync();
        }

        var revisionId = await RevisionIdAsync(fixture);

        await AssertItemRejectedAsync(fixture, Item(revisionId, StageCodes.FS, kind: "Lifecycle"));
        await AssertItemRejectedAsync(fixture, Item(revisionId, StageCodes.FS, clears: true, start: new DateOnly(2026, 1, 5)));

        await using (var db = fixture.NewContext())
        {
            db.ActualCorrectionItems.Add(Item(revisionId, StageCodes.FS));
            await db.SaveChangesAsync();
        }

        await AssertItemRejectedAsync(fixture, Item(revisionId, StageCodes.FS)); // one item per stage
    }

    [Fact]
    public async Task RevisionWithItems_CannotBeDeletedOnItsOwn()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var db = fixture.NewContext())
        {
            var revision = Revision(fixture.ProjectId, 1);
            revision.Items.Add(Item(0, StageCodes.FS));
            db.ActualCorrectionRevisions.Add(revision);
            await db.SaveChangesAsync();
        }

        await using var other = fixture.NewContext();
        var refusal = await Assert.ThrowsAsync<SqliteException>(() =>
            other.Database.ExecuteSqlRawAsync("DELETE FROM \"ActualCorrectionRevisions\""));
        Assert.Contains("FOREIGN KEY", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(1, await other.ActualCorrectionItems.CountAsync());
    }

    // SECTION: Helpers
    /// <summary>Seeds revision 1 superseded by revision 2 in the C.4 write order.</summary>
    internal static async Task<(int First, int Second)> SeedSupersededPairAsync(ApplicationDbContext db, int projectId)
    {
        var first = Closed(Revision(projectId, 1), ActualCorrectionRevisionStatus.Superseded);
        first.Items.Add(Item(0, StageCodes.FS));
        db.ActualCorrectionRevisions.Add(first);
        await db.SaveChangesAsync();

        var second = Revision(projectId, 2);
        second.SupersedesRevisionId = first.Id;
        second.Items.Add(Item(0, StageCodes.FS, start: new DateOnly(2026, 1, 6)));
        db.ActualCorrectionRevisions.Add(second);
        await db.SaveChangesAsync();

        first.SupersededByRevisionId = second.Id;
        await db.SaveChangesAsync();
        return (first.Id, second.Id);
    }

    internal static ActualCorrectionRevision Revision(int projectId, int revisionNo) => new()
    {
        ProjectId = projectId,
        RevisionNo = revisionNo,
        Status = ActualCorrectionRevisionStatus.Pending,
        EntryPoint = ActualCorrectionEntryPoint.Editor,
        SubmittedByUserId = "po",
        SubmittedOn = DateTimeOffset.UtcNow,
        ContentHash = new string('a', 64)
    };

    private static ActualCorrectionRevision Closed(ActualCorrectionRevision revision, string status)
    {
        revision.Status = status;
        revision.ClosedByUserId = "po";
        revision.ClosedOn = DateTimeOffset.UtcNow;
        return revision;
    }

    internal static ActualCorrectionItem Item(
        int revisionId,
        string stageCode,
        string kind = ActualCorrectionItemKind.Correction,
        bool clears = false,
        DateOnly? start = null) => new()
    {
        RevisionId = revisionId,
        StageCode = stageCode,
        Kind = kind,
        BeforeStatus = StageStatus.InProgress,
        BeforeActualStart = new DateOnly(2026, 1, 5),
        ProposedActualStart = start,
        ClearsActualStart = clears
    };

    private static async Task<int> RevisionIdAsync(Fixture fixture)
    {
        await using var db = fixture.NewContext();
        return await db.ActualCorrectionRevisions.Select(revision => revision.Id).SingleAsync();
    }

    private static async Task AssertItemRejectedAsync(Fixture fixture, ActualCorrectionItem item)
    {
        await using var db = fixture.NewContext();
        db.ActualCorrectionItems.Add(item);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(SqliteConnection connection) => _connection = connection;

        public int ProjectId { get; private set; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var fixture = new Fixture(connection);

            await using var db = fixture.NewContext();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new ApplicationUser { Id = "seed", UserName = "seed" });
            var project = new Project { Name = "D-2 schema", CreatedByUserId = "seed", WorkflowVersion = ProcurementWorkflow.VersionV1 };
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            fixture.ProjectId = project.Id;
            return fixture;
        }

        public ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
    }
}
