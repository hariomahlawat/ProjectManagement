using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using ProjectManagement.Data;
using ProjectManagement.Features.Collage;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Storage;
using ProjectManagement.Tests.Fakes;
using SkiaSharp;
using ProjectManagement.Services.Imaging;
using Xunit;

namespace ProjectManagement.Tests.Tools;

/// <summary>
/// A sticker upload whose COMMIT reports a failure. The failure may come after the server saved
/// the row (a dropped connection), so the new picture is deleted only when a fresh look at the
/// database shows no row uses it. Runs on SQLite, which has real transactions.
/// </summary>
public sealed class CollageStickerCommitTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "collage-sticker-commit-" + Guid.NewGuid().ToString("N"));
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly ServiceProvider _services;

    public CollageStickerCommitTests()
    {
        Directory.CreateDirectory(_root);
        _connection.Open();
        _services = new ServiceCollection()
            .AddDbContext<ApplicationDbContext>(options => options.UseSqlite(_connection))
            .BuildServiceProvider();
        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task When_commit_fails_after_the_row_was_saved_the_picture_is_kept()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => AddAsync(FailingCommit.AfterCommit));

        Assert.Contains("connection dropped", error.Message);
        var sticker = Assert.Single(await ListAsync());
        Assert.True(File.Exists(Path.Combine(_root, sticker.StorageKey)), "The saved sticker lost its picture.");
    }

    [Fact]
    public async Task When_commit_fails_and_the_row_was_not_saved_the_picture_is_removed()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => AddAsync(FailingCommit.BeforeCommit));

        Assert.Empty(await ListAsync());
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, CollageStickerService.Folder)));
    }

    [Fact]
    public async Task When_the_check_itself_cannot_be_made_the_picture_is_kept()
    {
        // No way to look again (no scope factory): the file is not deleted blindly.
        await Assert.ThrowsAsync<InvalidOperationException>(() => AddAsync(FailingCommit.BeforeCommit, withScopes: false));

        Assert.Empty(await ListAsync());
        Assert.Single(Directory.EnumerateFiles(Path.Combine(_root, CollageStickerService.Folder)));
    }

    private async Task AddAsync(FailingCommit failure, bool withScopes = true)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(failure)
            .Options);
        var roots = new TestRoots(_root);
        var service = new CollageStickerService(
            db, roots, new UploadPathResolver(roots), new NoAudit(),
            FakeClock.AtUtc(new DateTimeOffset(2026, 11, 16, 6, 0, 0, TimeSpan.Zero)),
            scopes: withScopes ? _services.GetRequiredService<IServiceScopeFactory>() : null);
        await service.AddAsync(new MemoryStream(Png()), "crest.png", "Crest", null, "admin");
    }

    private async Task<List<ProjectManagement.Models.CollageSticker>> ListAsync()
    {
        using var scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().CollageStickers.AsNoTracking().ToListAsync();
    }

    private static byte[] Png()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(20, 20, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.Erase(SKColors.DarkRed);
        return PrismImageCodec.EncodePng(bitmap);
    }

    /// <summary>Makes the COMMIT report a failure, either before it reaches the database or after it is done.</summary>
    private sealed class FailingCommit : DbTransactionInterceptor
    {
        private readonly bool _afterCommit;

        private FailingCommit(bool afterCommit) => _afterCommit = afterCommit;

        public static FailingCommit BeforeCommit => new(false);

        public static FailingCommit AfterCommit => new(true);

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            _afterCommit ? ValueTask.FromResult(result) : throw new InvalidOperationException("The connection dropped before the commit.");

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) =>
            _afterCommit ? throw new InvalidOperationException("The connection dropped after the commit.") : Task.CompletedTask;
    }

    private sealed class NoAudit : IAdminAuditService
    {
        public Task RecordAsync(AdminAuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TestRoots(string root) : IUploadRootProvider
    {
        public string RootPath { get; } = root;
        public string GetProjectRoot(int projectId) => RootPath;
        public string GetProjectPhotosRoot(int projectId) => RootPath;
        public string GetProjectDocumentsRoot(int projectId) => RootPath;
        public string GetProjectCommentsRoot(int projectId) => RootPath;
        public string GetProjectVideosRoot(int projectId) => RootPath;
        public string GetSocialMediaRoot(string storagePrefix, Guid eventId) => RootPath;
    }
}
