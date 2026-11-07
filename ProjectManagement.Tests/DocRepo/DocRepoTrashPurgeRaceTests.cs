using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests.DocRepo;

/// <summary>
/// A purge must never lose a document that is not purged. Runs the real set-based delete and
/// transaction on SQLite (each context on its own connection to one shared database) and the
/// real file store in a temporary folder.
/// </summary>
public sealed class DocRepoTrashPurgeRaceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Content = { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31 };

    private readonly string _connectionString = $"Data Source=purge-race-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"prism-purge-race-{Guid.NewGuid():N}");
    private SqliteConnection _keepAlive = null!;

    public async Task InitializeAsync()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        await _keepAlive.OpenAsync();
        await using var db = CreateDb();
        await db.Database.EnsureCreatedAsync();
        db.OfficeCategories.Add(new OfficeCategory { Id = 1, Name = "Office" });
        db.DocumentCategories.Add(new DocumentCategory { Id = 1, Name = "Letter" });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _keepAlive.DisposeAsync();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Purge_RacingARestore_KeepsTheRestoredDocumentAndItsFile()
    {
        var id = await SeedAsync(deletedDaysAgo: 3);
        var storage = new HookedStorage(CreateStorage())
        {
            // Another admin restores the document while this purge has the file set aside.
            AfterFirstMove = async () =>
            {
                await using var other = CreateDb();
                var document = await other.Documents.SingleAsync(d => d.Id == id);
                document.IsDeleted = false;
                document.DeletedAtUtc = null;
                await other.SaveChangesAsync();
            }
        };

        await using var db = CreateDb();
        var result = await CreateService(db, storage).PurgeAsync(id, "admin");

        Assert.Equal(DocRepoPurgeOutcome.NotFound, result.Outcome);
        await using var check = CreateDb();
        var restored = await check.Documents.AsNoTracking().SingleAsync(d => d.Id == id);
        Assert.False(restored.IsDeleted);
        Assert.Equal(Content, await File.ReadAllBytesAsync(FullPath(restored.StoragePath)));
        Assert.Empty(QuarantinedFiles());
        Assert.Empty(await check.DocRepoAudits.AsNoTracking().Where(a => a.EventType == "Purged").ToListAsync());
    }

    [Fact]
    public async Task Purge_WhenTheSaveFailsAfterTheFileMoved_PutsTheFileBack()
    {
        var id = await SeedAsync(deletedDaysAgo: 3);
        await using var db = CreateDb(new FailingSaveInterceptor());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService(db, CreateStorage()).PurgeAsync(id, "admin"));

        Assert.Equal(FailingSaveInterceptor.Message, error.Message);
        await using var check = CreateDb();
        var document = await check.Documents.AsNoTracking().SingleAsync(d => d.Id == id);
        Assert.True(document.IsDeleted);
        Assert.Equal(Content, await File.ReadAllBytesAsync(FullPath(document.StoragePath)));
        Assert.Empty(QuarantinedFiles());
        Assert.Empty(await check.DocRepoAudits.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task PurgeExpired_TwoOverlappingRuns_PurgeEachDocumentOnceWithoutThrowing()
    {
        var first = await SeedAsync(deletedDaysAgo: 40);
        var second = await SeedAsync(deletedDaysAgo: 45);
        var otherRunPurged = 0;
        var storage = new HookedStorage(CreateStorage())
        {
            // A second run (an overlapping deployment, say) starts while this one has a file aside.
            AfterFirstMove = async () =>
            {
                await using var other = CreateDb();
                otherRunPurged = await CreateService(other, CreateStorage()).PurgeExpiredAsync();
            }
        };

        await using var db = CreateDb();
        var purged = await CreateService(db, storage).PurgeExpiredAsync();

        Assert.Equal(2, purged + otherRunPurged);
        await using var check = CreateDb();
        Assert.Empty(await check.Documents.AsNoTracking().ToListAsync());
        var audits = await check.DocRepoAudits.AsNoTracking().Where(a => a.EventType == "Purged").ToListAsync();
        Assert.Equal(new[] { first, second }.OrderBy(x => x), audits.Select(a => a.DocumentId!.Value).OrderBy(x => x));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.pdf", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Purge_DeletesTheRowItsDependentsAndTheFile()
    {
        var id = await SeedAsync(deletedDaysAgo: 3);
        await using (var seed = CreateDb())
        {
            seed.DocRepoExternalLinks.Add(new DocRepoExternalLink { DocumentId = id, SourceModule = "Ipr", SourceItemId = "7" });
            await seed.SaveChangesAsync();
        }

        await using var db = CreateDb();
        var result = await CreateService(db, CreateStorage()).PurgeAsync(id, "admin");

        Assert.Equal(DocRepoPurgeOutcome.Purged, result.Outcome);
        await using var check = CreateDb();
        Assert.Empty(await check.Documents.AsNoTracking().ToListAsync());
        Assert.Empty(await check.DocRepoExternalLinks.AsNoTracking().ToListAsync());
        Assert.Equal("admin", (await check.DocRepoAudits.AsNoTracking().SingleAsync(a => a.EventType == "Purged")).ActorUserId);
        Assert.Empty(Directory.EnumerateFiles(_root, "*.pdf", SearchOption.AllDirectories));
    }

    private ApplicationDbContext CreateDb(params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connectionString);
        if (interceptors.Length > 0)
        {
            builder.AddInterceptors(interceptors);
        }

        return new ApplicationDbContext(builder.Options);
    }

    private LocalDocStorageService CreateStorage()
        => new(Options.Create(new DocRepoOptions { RootPath = _root }), new TestWebHostEnvironment());

    private static DocRepoTrashPurgeService CreateService(ApplicationDbContext db, IDocStorage storage)
        => new(
            db,
            storage,
            new DocRepoAuditService(db),
            FakeClock.AtUtc(Now),
            new StaticOptionsMonitor(new DocRepoOptions { TrashRetentionDays = 30 }));

    private string FullPath(string storagePath) => Path.Combine(_root, storagePath);

    private string[] QuarantinedFiles()
    {
        var folder = Path.Combine(_root, DocRepoTrashPurgeService.QuarantineFolder);
        return Directory.Exists(folder) ? Directory.GetFiles(folder) : Array.Empty<string>();
    }

    private async Task<Guid> SeedAsync(int deletedDaysAgo)
    {
        var storagePath = await CreateStorage().SaveAsync(new MemoryStream(Content), "document.pdf", Now.UtcDateTime, CancellationToken.None);
        var id = Guid.NewGuid();
        await using var db = CreateDb();
        db.Documents.Add(new Document
        {
            Id = id,
            Subject = "Document",
            OfficeCategoryId = 1,
            DocumentCategoryId = 1,
            OriginalFileName = "document.pdf",
            FileSizeBytes = Content.Length,
            Sha256 = id.ToString("N"),
            StoragePath = storagePath,
            IsDeleted = true,
            DeletedAtUtc = Now.UtcDateTime.AddDays(-deletedDaysAgo),
            CreatedByUserId = "system",
            CreatedAtUtc = Now.UtcDateTime.AddYears(-1)
        });
        await db.SaveChangesAsync();
        return id;
    }

    // Runs a step of another actor right after this purge first sets a file aside.
    private sealed class HookedStorage : IDocStorage
    {
        private readonly IDocStorage _inner;
        private bool _fired;

        public HookedStorage(IDocStorage inner) => _inner = inner;

        public Func<Task>? AfterFirstMove { get; init; }

        public async Task MoveAsync(string sourcePath, string destinationPath, CancellationToken ct)
        {
            await _inner.MoveAsync(sourcePath, destinationPath, ct);
            if (!_fired && AfterFirstMove is not null)
            {
                _fired = true;
                await AfterFirstMove();
            }
        }

        public Task<string> SaveAsync(Stream source, string safeFileName, DateTime utcNow, CancellationToken ct) => _inner.SaveAsync(source, safeFileName, utcNow, ct);

        public Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct) => _inner.OpenReadAsync(storagePath, ct);

        public Task DeleteAsync(string storagePath, CancellationToken ct) => _inner.DeleteAsync(storagePath, ct);

        public Task<bool> ExistsAsync(string storagePath, CancellationToken ct) => _inner.ExistsAsync(storagePath, ct);
    }

    private sealed class FailingSaveInterceptor : SaveChangesInterceptor
    {
        public const string Message = "The database went away.";

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(Message);
    }

    private sealed class StaticOptionsMonitor : IOptionsMonitor<DocRepoOptions>
    {
        public StaticOptionsMonitor(DocRepoOptions value) => CurrentValue = value;

        public DocRepoOptions CurrentValue { get; }

        public DocRepoOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<DocRepoOptions, string?> listener) => null;
    }
}
