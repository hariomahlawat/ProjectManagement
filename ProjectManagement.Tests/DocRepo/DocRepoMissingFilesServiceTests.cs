using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using ProjectManagement.Areas.DocumentRepository.Pages.Admin;
using ProjectManagement.Data;
using ProjectManagement.Services;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.DocRepo;
using Xunit;
using static ProjectManagement.Tests.DocRepo.DocRepoTestSupport;

namespace ProjectManagement.Tests.DocRepo;

/// <summary>
/// Second admin review, PR 6 (M6): Missing files and System health's "Document Repository
/// files" row share one capped, briefly cached check, so they report the same number.
/// </summary>
public sealed class DocRepoMissingFilesServiceTests
{
    [Fact]
    public async Task MissingFilesPage_AndSystemHealth_ReportTheSameNumber_FromOneCheck()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        var storage = new CountingStorage();
        for (var i = 0; i < 12; i++)
        {
            var document = SeedDocument(db, $"Document {i}");
            if (i % 4 != 0) storage.Inner.Put(document.StoragePath, PdfBytes());
        }

        SeedDocument(db, "Trashed, file gone", isDeleted: true);
        await db.SaveChangesAsync();
        var missingFiles = Service(db, storage);

        var page = new MissingFilesModel(db, missingFiles, new ProjectManagement.Services.Admin.AdminTimeService(new ProjectManagement.Services.SystemClock()));
        ConfigurePage(page, Manager);
        await page.OnGetAsync(page: 1, pageSize: 25);
        var health = await AdminSystemHealthService.RepositoryFilesCheckAsync(missingFiles, forceRefresh: false, CancellationToken.None);

        Assert.Equal(3, page.MissingCount);
        Assert.Equal(12, page.TotalCount);
        Assert.False(page.Capped);
        Assert.Equal(AdminHealthStatus.Warning, health.Status);
        Assert.StartsWith("3 documents have lost their file", health.Summary);
        Assert.Equal("Checked 12 documents.", health.Detail);
        // One scan served both.
        Assert.Equal(12, storage.Checks);
    }

    [Fact]
    public async Task Check_StopsAtTheCap_NewestFirst_AndSaysSo()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        var newest = SeedDocument(db, "Newest");
        newest.CreatedAtUtc = DateTime.UtcNow;
        for (var i = 0; i < 4; i++)
        {
            SeedDocument(db, $"Older {i}").CreatedAtUtc = DateTime.UtcNow.AddDays(-10 - i);
        }

        await db.SaveChangesAsync();
        var storage = new CountingStorage();

        var scan = await Service(db, storage, limit: 3).ScanAsync(includeDeleted: false);
        var health = AdminSystemHealthService.RepositoryFilesResult(scan.CheckedCount, scan.MissingCount, scan.Capped);

        Assert.True(scan.Capped);
        Assert.Equal(3, scan.CheckedCount);
        Assert.Equal(3, storage.Checks);
        Assert.Equal(newest.Id, scan.MissingIds[0]);
        Assert.Equal("Checked the newest 3 documents.", health.Detail);
    }

    [Fact]
    public async Task Check_IsReusedBriefly_UnlessRefreshed_AndKeepsTrashSeparate()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        SeedDocument(db, "Library");
        SeedDocument(db, "Trashed", isDeleted: true);
        await db.SaveChangesAsync();
        var storage = new CountingStorage();
        var clock = new StepClock();
        var service = Service(db, storage, clock: clock);

        var first = await service.ScanAsync(includeDeleted: false);
        clock.Now = clock.Now.AddSeconds(30);
        var cached = await service.ScanAsync(includeDeleted: false);
        var withTrash = await service.ScanAsync(includeDeleted: true);
        var refreshed = await service.ScanAsync(includeDeleted: false, forceRefresh: true);

        Assert.Same(first, cached);
        Assert.Equal(1, first.MissingCount);
        Assert.Equal(2, withTrash.MissingCount);
        Assert.NotSame(first, refreshed);
        Assert.Equal(clock.Now, refreshed.CheckedUtc);
        Assert.Equal(1 + 2 + 1, storage.Checks);
    }

    [Fact]
    public async Task Check_StopsBetweenFiles_WhenCancelled_AndKeepsNoResult()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        for (var i = 0; i < 5; i++) SeedDocument(db, $"Document {i}");
        await db.SaveChangesAsync();
        using var cancel = new CancellationTokenSource();
        var storage = new CountingStorage { CancelAfter = 2, Cancel = cancel };
        var service = Service(db, storage);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ScanAsync(false, cancellationToken: cancel.Token));

        Assert.Equal(2, storage.Checks);
        var again = await service.ScanAsync(false);
        Assert.Equal(5, again.CheckedCount);
    }

    // SECTION: Helpers
    private static DocRepoMissingFilesService Service(ApplicationDbContext db, IDocStorage storage, int limit = DocRepoMissingFilesService.CheckLimit, IClock? clock = null) =>
        new(db, storage, new MemoryCache(new MemoryCacheOptions()), clock ?? new StepClock(), limit);

    private sealed class StepClock : IClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 7, 6, 0, 0, TimeSpan.Zero);

        public DateTimeOffset UtcNow => Now;
    }

    private sealed class CountingStorage : IDocStorage
    {
        public RecordingDocStorage Inner { get; } = new();

        public int Checks { get; private set; }

        public int CancelAfter { get; init; } = int.MaxValue;

        public CancellationTokenSource? Cancel { get; init; }

        public Task<string> SaveAsync(Stream source, string safeFileName, DateTime utcNow, CancellationToken ct) => Inner.SaveAsync(source, safeFileName, utcNow, ct);

        public Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct) => Inner.OpenReadAsync(storagePath, ct);

        public Task DeleteAsync(string storagePath, CancellationToken ct) => Inner.DeleteAsync(storagePath, ct);

        public Task<bool> ExistsAsync(string storagePath, CancellationToken ct)
        {
            Checks++;
            if (Checks >= CancelAfter) Cancel?.Cancel();
            return Inner.ExistsAsync(storagePath, ct);
        }
    }
}
