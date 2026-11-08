using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests.DocRepo;

public sealed class DocRepoTrashPurgeServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PurgeExpiredAsync_DeletesOnlyDocumentsTrashedLongerThanTheRetention()
    {
        await using var db = CreateDb();
        var old = Seed(db, deletedDaysAgo: 31);
        var exactlyDue = Seed(db, deletedDaysAgo: 30);
        var recent = Seed(db, deletedDaysAgo: 29);
        var live = Seed(db, deletedDaysAgo: null);
        var undated = Seed(db, deletedDaysAgo: null, isDeleted: true);
        await db.SaveChangesAsync();
        var storage = new RecordingStorage();

        var purged = await CreateService(db, storage).PurgeExpiredAsync();

        Assert.Equal(2, purged);
        var remaining = await db.Documents.AsNoTracking().Select(d => d.Id).ToListAsync();
        Assert.Equal(new[] { recent, live, undated }.OrderBy(id => id), remaining.OrderBy(id => id));
        Assert.False(storage.Exists($"docs/{old}.pdf"));
        Assert.False(storage.Exists($"docs/{exactlyDue}.pdf"));
        Assert.True(storage.Exists($"docs/{recent}.pdf"));
        Assert.Equal(2, storage.Deleted.Count);
        Assert.All(storage.Deleted, path => Assert.StartsWith(DocRepoTrashPurgeService.QuarantineFolder, path));
        Assert.Empty(storage.Quarantined);

        var audits = await db.DocRepoAudits.AsNoTracking().ToListAsync();
        Assert.Equal(2, audits.Count);
        Assert.All(audits, audit =>
        {
            Assert.Equal("Purged", audit.EventType);
            Assert.Equal(DocRepoTrashPurgeService.SystemActor, audit.ActorUserId);
            Assert.Contains("TrashRetention", audit.DetailsJson);
        });
    }

    [Fact]
    public async Task PurgeExpiredAsync_DoesNothingWhenRetentionIsOff()
    {
        await using var db = CreateDb();
        Seed(db, deletedDaysAgo: 400);
        await db.SaveChangesAsync();

        var purged = await CreateService(db, new RecordingStorage(), retentionDays: 0).PurgeExpiredAsync();

        Assert.Equal(0, purged);
        Assert.Single(await db.Documents.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task PurgeExpiredAsync_KeepsADocumentWhoseFileCannotBeDeleted()
    {
        await using var db = CreateDb();
        var locked = Seed(db, deletedDaysAgo: 40);
        var free = Seed(db, deletedDaysAgo: 40);
        await db.SaveChangesAsync();
        var storage = new RecordingStorage { Failing = { $"docs/{locked}.pdf" } };

        var purged = await CreateService(db, storage).PurgeExpiredAsync();

        Assert.Equal(1, purged);
        Assert.Equal(locked, (await db.Documents.AsNoTracking().SingleAsync()).Id);
        Assert.DoesNotContain(await db.Documents.AsNoTracking().ToListAsync(), d => d.Id == free);
    }

    [Fact]
    public async Task PurgeExpiredAsync_AFullBatchOfUndeletableFilesDoesNotBlockNewerDocuments()
    {
        await using var db = CreateDb();
        var storage = new RecordingStorage();
        for (var i = 0; i < 100; i++)
        {
            storage.Failing.Add($"docs/{Seed(db, deletedDaysAgo: 200 + i)}.pdf");
        }

        var newer = Seed(db, deletedDaysAgo: 40);
        await db.SaveChangesAsync();

        var purged = await CreateService(db, storage).PurgeExpiredAsync();

        Assert.Equal(1, purged);
        Assert.DoesNotContain(await db.Documents.AsNoTracking().ToListAsync(), d => d.Id == newer);
        Assert.Equal(100, await db.Documents.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task PurgeAsync_ReportsAPermissionFailureForThatDocumentOnly()
    {
        await using var db = CreateDb();
        var readOnly = Seed(db, deletedDaysAgo: 40);
        var free = Seed(db, deletedDaysAgo: 40);
        await db.SaveChangesAsync();
        var storage = new RecordingStorage { Denied = { $"docs/{readOnly}.pdf" } };

        var purged = await CreateService(db, storage).PurgeExpiredAsync();

        Assert.Equal(1, purged);
        Assert.Equal(readOnly, (await db.Documents.AsNoTracking().SingleAsync()).Id);
        var result = await CreateService(db, storage).PurgeAsync(readOnly, "admin");
        Assert.Equal(DocRepoPurgeOutcome.FileError, result.Outcome);
        Assert.DoesNotContain(await db.Documents.AsNoTracking().ToListAsync(), d => d.Id == free);
    }

    [Fact]
    public async Task PurgeAsync_WithACutoff_SkipsADocumentTrashedAgainSinceItWasSelected()
    {
        await using var db = CreateDb();
        // Selected as due, then restored and deleted again today: its trash date is new.
        var retrashed = Seed(db, deletedDaysAgo: 0);
        await db.SaveChangesAsync();
        var storage = new RecordingStorage();

        var result = await CreateService(db, storage).PurgeAsync(
            retrashed, DocRepoTrashPurgeService.SystemActor, trashedOnOrBefore: Now.UtcDateTime.AddDays(-30));

        Assert.Equal(DocRepoPurgeOutcome.NotFound, result.Outcome);
        Assert.Single(await db.Documents.AsNoTracking().ToListAsync());
        Assert.Empty(storage.Deleted);
    }

    [Fact]
    public async Task PurgeExpiredAsync_UsesTheRetentionAsChangedWithoutARestart()
    {
        await using var db = CreateDb();
        Seed(db, deletedDaysAgo: 40);
        await db.SaveChangesAsync();
        var options = new MutableOptionsMonitor(new DocRepoOptions { TrashRetentionDays = 30 });
        var service = new DocRepoTrashPurgeService(db, new RecordingStorage(), new DocRepoAuditService(db), FakeClock.AtUtc(Now), options);

        options.CurrentValue = new DocRepoOptions { TrashRetentionDays = 90 };

        Assert.Equal(90, service.RetentionDays);
        Assert.Equal(0, await service.PurgeExpiredAsync());
        Assert.Single(await db.Documents.AsNoTracking().ToListAsync());
    }

    // Fourth review, C4: a database error deleting one document used to abort the whole run, so
    // every document after it waited for ever. Now it is skipped, its file put back, and counted.
    [Fact]
    public async Task RunExpiredPurgeAsync_SkipsADocumentWhoseRowCannotBeDeleted_AndPurgesTheRest()
    {
        var name = Guid.NewGuid().ToString();
        Guid broken;
        Guid before;
        Guid after;
        await using (var seed = CreateDb(name))
        {
            before = Seed(seed, deletedDaysAgo: 60);
            broken = Seed(seed, deletedDaysAgo: 50);
            after = Seed(seed, deletedDaysAgo: 40);
            await seed.SaveChangesAsync();
        }

        await using var db = CreateDb(name, new FailingDeleteInterceptor(broken));
        var storage = new RecordingStorage();

        var run = await CreateService(db, storage).RunExpiredPurgeAsync();

        Assert.Equal(new DocRepoTrashPurgeRun(Purged: 2, Failed: 1), run);
        await using var check = CreateDb(name);
        Assert.Equal(broken, (await check.Documents.AsNoTracking().SingleAsync()).Id);
        // Its file is back where it was; nothing of it is left set aside.
        Assert.True(storage.Exists($"docs/{broken}.pdf"));
        Assert.Empty(storage.Quarantined);
        Assert.False(storage.Exists($"docs/{before}.pdf"));
        Assert.False(storage.Exists($"docs/{after}.pdf"));
        Assert.Equal(2, await check.DocRepoAudits.AsNoTracking().CountAsync(a => a.EventType == "Purged"));
    }

    [Fact]
    public async Task PurgeExpiredAsync_StillReturnsTheCountWhenADocumentFails()
    {
        var name = Guid.NewGuid().ToString();
        Guid broken;
        await using (var seed = CreateDb(name))
        {
            broken = Seed(seed, deletedDaysAgo: 50);
            Seed(seed, deletedDaysAgo: 40);
            await seed.SaveChangesAsync();
        }

        await using var db = CreateDb(name, new FailingDeleteInterceptor(broken));

        Assert.Equal(1, await CreateService(db, new RecordingStorage()).PurgeExpiredAsync());
    }

    [Fact]
    public void RunSummary_NamesTheDocumentsLeftInTheBin()
    {
        Assert.Equal(
            "Removed 2 documents that were in the bin over 30 days; 1 document could not be removed and stays in the bin; the server log says why",
            ProjectManagement.Hosted.DocRepoTrashPurgeWorker.RunSummary(new DocRepoTrashPurgeRun(2, 1), 30));
        Assert.Equal(
            "3 documents could not be removed and stay in the bin; the server log says why",
            ProjectManagement.Hosted.DocRepoTrashPurgeWorker.RunSummary(new DocRepoTrashPurgeRun(0, 3), 30));
        Assert.Equal(
            "Removed 1 document that was in the bin over 30 days",
            ProjectManagement.Hosted.DocRepoTrashPurgeWorker.RunSummary(new DocRepoTrashPurgeRun(1, 0), 30));
        Assert.Equal(
            "Nothing to remove",
            ProjectManagement.Hosted.DocRepoTrashPurgeWorker.RunSummary(new DocRepoTrashPurgeRun(0, 0), 30));
    }

    [Fact]
    public async Task PurgeDueUtc_IsTheTrashDatePlusTheRetention()
    {
        await using var db = CreateDb();
        var service = CreateService(db, new RecordingStorage());
        var deletedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(deletedAt.AddDays(30), service.PurgeDueUtc(deletedAt));
        Assert.Null(service.PurgeDueUtc(null));
        Assert.Null(CreateService(db, new RecordingStorage(), retentionDays: 0).PurgeDueUtc(deletedAt));
    }

    private static DocRepoTrashPurgeService CreateService(ApplicationDbContext db, IDocStorage storage, int retentionDays = 30)
        => new(
            db,
            storage,
            new DocRepoAuditService(db),
            FakeClock.AtUtc(Now),
            new MutableOptionsMonitor(new DocRepoOptions { TrashRetentionDays = retentionDays }));

    private static ApplicationDbContext CreateDb()
        => CreateDb(Guid.NewGuid().ToString());

    private static ApplicationDbContext CreateDb(string name, params IInterceptor[] interceptors)
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(name)
            .AddInterceptors(interceptors)
            .Options);

    // Fails the save that deletes one document, as a database error other than a concurrency one.
    private sealed class FailingDeleteInterceptor : SaveChangesInterceptor
    {
        private readonly Guid _documentId;

        public FailingDeleteInterceptor(Guid documentId) => _documentId = documentId;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var deletesIt = eventData.Context!.ChangeTracker.Entries<Document>()
                .Any(e => e.State == EntityState.Deleted && e.Entity.Id == _documentId);
            if (deletesIt)
            {
                throw new DbUpdateException("A row the document depends on is locked.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private static Guid Seed(ApplicationDbContext db, int? deletedDaysAgo, bool isDeleted = false)
    {
        var id = Guid.NewGuid();
        db.Documents.Add(new Document
        {
            Id = id,
            Subject = "Document",
            OfficeCategoryId = 1,
            DocumentCategoryId = 1,
            OriginalFileName = "document.pdf",
            FileSizeBytes = 10,
            Sha256 = id.ToString("N"),
            StoragePath = $"docs/{id}.pdf",
            IsDeleted = isDeleted || deletedDaysAgo.HasValue,
            DeletedAtUtc = deletedDaysAgo.HasValue ? Now.UtcDateTime.AddDays(-deletedDaysAgo.Value) : null,
            CreatedByUserId = "system",
            CreatedAtUtc = Now.UtcDateTime.AddYears(-1)
        });
        return id;
    }

    private sealed class MutableOptionsMonitor : IOptionsMonitor<DocRepoOptions>
    {
        public MutableOptionsMonitor(DocRepoOptions value) => CurrentValue = value;

        public DocRepoOptions CurrentValue { get; set; }

        public DocRepoOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<DocRepoOptions, string?> listener) => null;
    }

    // Files under docs/ exist until moved or deleted; files moved elsewhere exist at their new path.
    private sealed class RecordingStorage : IDocStorage
    {
        private readonly HashSet<string> _gone = new();
        private readonly HashSet<string> _added = new();

        public List<string> Deleted { get; } = new();

        public HashSet<string> Failing { get; } = new();

        public HashSet<string> Denied { get; } = new();

        public bool Exists(string storagePath)
            => !_gone.Contains(storagePath) && (storagePath.StartsWith("docs/", StringComparison.Ordinal) || _added.Contains(storagePath));

        public IEnumerable<string> Quarantined
            => _added.Where(p => p.StartsWith(DocRepoTrashPurgeService.QuarantineFolder, StringComparison.Ordinal) && Exists(p));

        public Task<string> SaveAsync(Stream source, string safeFileName, DateTime utcNow, CancellationToken ct)
            => Task.FromResult(safeFileName);

        public Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct)
            => Task.FromResult<Stream>(new MemoryStream());

        public Task MoveAsync(string sourcePath, string destinationPath, CancellationToken ct)
        {
            if (Failing.Contains(sourcePath))
            {
                throw new IOException("The file is in use.");
            }

            if (Denied.Contains(sourcePath))
            {
                throw new UnauthorizedAccessException("Access to the path is denied.");
            }

            if (!Exists(sourcePath))
            {
                throw new FileNotFoundException(sourcePath);
            }

            _gone.Add(sourcePath);
            _added.Remove(sourcePath);
            _gone.Remove(destinationPath);
            _added.Add(destinationPath);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string storagePath, CancellationToken ct)
        {
            Deleted.Add(storagePath);
            _gone.Add(storagePath);
            _added.Remove(storagePath);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string storagePath, CancellationToken ct) => Task.FromResult(Exists(storagePath));
    }
}
