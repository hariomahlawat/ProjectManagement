using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Tests.MediaLibrary;

/// <summary>
/// Retiring face intelligence removes face rows on the caller's next save. Their thumbnail
/// files used to be deleted immediately, leaving faces without thumbnails whenever that
/// save failed.
/// </summary>
public sealed class FaceThumbnailRetirementTests : IDisposable
{
    private readonly string _cacheRoot = Path.Combine(Path.GetTempPath(), $"face-thumbs-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_cacheRoot))
        {
            Directory.Delete(_cacheRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Thumbnail_is_deleted_only_after_the_save_succeeds()
    {
        await using var db = CreateContext();
        var thumbnail = await SeedFaceWithThumbnailAsync(db);
        var service = new MediaContentChangeInvalidationService(db, new Cache(_cacheRoot), NullLogger<MediaContentChangeInvalidationService>.Instance);

        await service.RetireFaceIntelligenceAsync(new[] { 1L }, "Test", "admin", "Retired for a test.", DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.True(File.Exists(thumbnail));

        await db.SaveChangesAsync();
        Assert.False(File.Exists(thumbnail));
    }

    [Fact]
    public async Task Thumbnail_survives_a_failed_save()
    {
        await using var db = CreateContext(new FailingSave());
        var thumbnail = await SeedFaceWithThumbnailAsync(db);
        var service = new MediaContentChangeInvalidationService(db, new Cache(_cacheRoot), NullLogger<MediaContentChangeInvalidationService>.Instance);

        await service.RetireFaceIntelligenceAsync(new[] { 1L }, "Test", "admin", "Retired for a test.", DateTimeOffset.UtcNow, CancellationToken.None);
        FailingSave.Armed = true;
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        Assert.True(File.Exists(thumbnail));
    }

    private async Task<string> SeedFaceWithThumbnailAsync(MediaLibraryDbContext db)
    {
        var source = ReclassifySeed.Source();
        db.Sources.Add(source);
        db.Assets.Add(ReclassifySeed.Asset(source, 1, null));
        var relative = Path.Combine("faces", "ab", "face.webp");
        db.Faces.Add(new MediaFace
        {
            Id = Guid.NewGuid(),
            MediaAssetId = 1,
            SequenceNumber = 1,
            DetectorModelKey = "yunet",
            DetectorModelVersion = "1",
            ReviewThumbnailPath = relative,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var full = Path.Combine(_cacheRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllBytesAsync(full, new byte[] { 1, 2, 3 });
        return full;
    }

    private static MediaLibraryDbContext CreateContext(IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<MediaLibraryDbContext>()
            .UseInMemoryDatabase($"face-thumbs-{Guid.NewGuid():N}");
        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

        return new MediaLibraryDbContext(builder.Options);
    }

    private sealed class Cache(string root) : IMediaCachePathResolver
    {
        public string CacheRoot => root;
        public string GetThumbnailPath(long assetId, int cacheVersion) => Path.Combine(root, "t.webp");
        public string GetPreviewPath(long assetId, int cacheVersion) => Path.Combine(root, "p.webp");
    }

    private sealed class FailingSave : SaveChangesInterceptor
    {
        public static bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                Armed = false;
                throw new DbUpdateException("Simulated conflict.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
