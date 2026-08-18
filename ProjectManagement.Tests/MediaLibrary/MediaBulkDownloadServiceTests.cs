using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;
using ProjectManagement.Pages.Photos;

namespace ProjectManagement.Tests.MediaLibrary;

public sealed class MediaBulkDownloadServiceTests
{
    [Fact]
    public async Task CreateAsync_BuildsReadableArchive_WithUniqueEntryNames()
    {
        await using var fixture = await BulkDownloadFixture.CreateAsync(
            maxItems: 120,
            maxSourceBytes: 10 * 1024 * 1024);
        fixture.AddAsset(1, "photo.jpg", Encoding.UTF8.GetBytes("first"));
        fixture.AddAsset(2, "photo.jpg", Encoding.UTF8.GetBytes("second"));
        await fixture.SaveAsync();

        var result = await fixture.Service.CreateAsync(
            new long[] { 1, 2 },
            "tester",
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Archive);
        Assert.Equal(2, result.Archive!.IncludedCount);
        Assert.Equal(0, result.Archive.SkippedCount);
        Assert.True(result.Archive.Length > 0);

        using var archive = new ZipArchive(result.Archive.Stream, ZipArchiveMode.Read, leaveOpen: true);
        Assert.Equal(new[] { "photo.jpg", "photo (2).jpg" }, archive.Entries.Select(entry => entry.FullName).ToArray());
        Assert.Equal("first", await ReadEntryAsync(archive.Entries[0]));
        Assert.Equal("second", await ReadEntryAsync(archive.Entries[1]));
        await result.Archive.Stream.DisposeAsync();
    }

    [Fact]
    public async Task CreateAsync_SanitizesArchiveEntryName_IndependentlyOfHostOperatingSystem()
    {
        await using var fixture = await BulkDownloadFixture.CreateAsync(
            maxItems: 120,
            maxSourceBytes: 10 * 1024 * 1024);
        fixture.AddAsset(1, @"..\folder/evil?:.jpg", Encoding.UTF8.GetBytes("safe"));
        await fixture.SaveAsync();

        var result = await fixture.Service.CreateAsync(
            new long[] { 1 },
            "tester",
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Archive);
        using var archive = new ZipArchive(result.Archive!.Stream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = Assert.Single(archive.Entries);
        Assert.Equal("evil__.jpg", entry.FullName);
        Assert.DoesNotContain("..", entry.FullName, StringComparison.Ordinal);
        Assert.DoesNotContain('\\', entry.FullName);
        Assert.DoesNotContain('/', entry.FullName);
        await result.Archive.Stream.DisposeAsync();
    }

    [Fact]
    public async Task CreateAsync_RejectsSelection_WhenKnownSourceBytesExceedLimit()
    {
        await using var fixture = await BulkDownloadFixture.CreateAsync(
            maxItems: 120,
            maxSourceBytes: 8);
        fixture.AddAsset(1, "one.jpg", new byte[5]);
        fixture.AddAsset(2, "two.jpg", new byte[5]);
        await fixture.SaveAsync();

        var result = await fixture.Service.CreateAsync(
            new long[] { 1, 2 },
            "tester",
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(MediaBulkDownloadFailureReason.SourceBytesExceeded, result.FailureReason);
        Assert.Null(result.Archive);
    }

    [Fact]
    public async Task CreateAsync_AbortsArchive_WhenSourceFailsAfterEntryCreation()
    {
        await using var fixture = await BulkDownloadFixture.CreateAsync(
            maxItems: 120,
            maxSourceBytes: 10 * 1024 * 1024);
        fixture.AddAsset(1, "good.jpg", Encoding.UTF8.GetBytes("good"));
        fixture.AddAsset(2, "broken.jpg", Encoding.UTF8.GetBytes("broken"), failDuringRead: true);
        await fixture.SaveAsync();

        var result = await fixture.Service.CreateAsync(
            new long[] { 1, 2 },
            "tester",
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(MediaBulkDownloadFailureReason.SourceReadFailed, result.FailureReason);
        Assert.Null(result.Archive);
    }

    [Fact]
    public async Task CreateAsync_UsesCanonicalVisibilityPolicy()
    {
        await using var fixture = await BulkDownloadFixture.CreateAsync(
            maxItems: 120,
            maxSourceBytes: 10 * 1024 * 1024);
        fixture.AddAsset(1, "visible.jpg", Encoding.UTF8.GetBytes("visible"));
        fixture.AddAsset(2, "archived.jpg", Encoding.UTF8.GetBytes("archived"), archived: true);
        await fixture.SaveAsync();

        var result = await fixture.Service.CreateAsync(
            new long[] { 1, 2 },
            "tester",
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Archive);
        Assert.Equal(1, result.Archive!.EligibleCount);
        Assert.Equal(1, result.Archive.IncludedCount);
        await result.Archive.Stream.DisposeAsync();
    }

    [Fact]
    public async Task DownloadPage_ReturnsFileStreamResult_WithoutWritingResponseBody()
    {
        var archive = new MediaBulkDownloadArchive(
            new MemoryStream(new byte[] { 1, 2, 3 }),
            "PRISM_Photos_test.zip",
            3,
            1,
            1,
            1,
            0,
            3);
        var service = new StubBulkDownloadService(MediaBulkDownloadResult.Success(archive));
        var model = new DownloadModel(service, NullLogger<DownloadModel>.Instance);
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new SynchronousWriteForbiddenStream();
        model.PageContext = new PageContext { HttpContext = httpContext };

        var action = await model.OnPostAsync(new long[] { 1 }, CancellationToken.None);

        var file = Assert.IsType<FileStreamResult>(action);
        Assert.Same(archive.Stream, file.FileStream);
        Assert.Equal("application/zip", file.ContentType);
        Assert.Equal("PRISM_Photos_test.zip", file.FileDownloadName);
        await archive.Stream.DisposeAsync();
    }

    private static async Task<string> ReadEntryAsync(ZipArchiveEntry entry)
    {
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private sealed class BulkDownloadFixture : IAsyncDisposable
    {
        private readonly string _cacheRoot;
        private readonly MediaLibrarySource _source;

        private BulkDownloadFixture(
            string cacheRoot,
            MediaLibraryDbContext db,
            MediaLibrarySource source,
            MediaBulkDownloadService service)
        {
            _cacheRoot = cacheRoot;
            Db = db;
            _source = source;
            Service = service;
        }

        public MediaLibraryDbContext Db { get; }
        public MediaBulkDownloadService Service { get; }

        public static async Task<BulkDownloadFixture> CreateAsync(int maxItems, long maxSourceBytes)
        {
            var dbOptions = new DbContextOptionsBuilder<MediaLibraryDbContext>()
                .UseInMemoryDatabase($"media-bulk-download-{Guid.NewGuid():N}")
                .Options;
            var db = new MediaLibraryDbContext(dbOptions);
            var source = new MediaLibrarySource
            {
                Id = Guid.NewGuid(),
                Key = "prism",
                Name = "PRISM",
                SourceType = MediaLibrarySourceType.Prism,
                IsEnabled = true,
                IsVisibleInLibrary = true,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            db.Sources.Add(source);
            await db.SaveChangesAsync();

            var options = new MediaLibraryOptions
            {
                Enabled = true,
                CacheRoot = "unused",
                Catalogue = new MediaCatalogueOptions { Enabled = true },
                BulkDownload = new MediaBulkDownloadOptions
                {
                    MaxItems = maxItems,
                    MaxSourceBytes = maxSourceBytes
                }
            };
            var cacheRoot = Path.Combine(Path.GetTempPath(), $"prism-bulk-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(cacheRoot);
            var definitions = new Dictionary<long, ContentDefinition>();
            var resolver = new StubContentResolver(definitions);
            var service = new MediaBulkDownloadService(
                db,
                new MediaAssetVisibilityPolicy(Options.Create(options)),
                resolver,
                new StubCachePathResolver(cacheRoot),
                Options.Create(options),
                NullLogger<MediaBulkDownloadService>.Instance);
            var fixture = new BulkDownloadFixture(cacheRoot, db, source, service)
            {
                ResolverDefinitions = definitions
            };
            return fixture;
        }

        private Dictionary<long, ContentDefinition> ResolverDefinitions { get; set; } = null!;

        public void AddAsset(
            long id,
            string fileName,
            byte[] bytes,
            bool archived = false,
            bool failDuringRead = false)
        {
            Db.Assets.Add(new MediaAsset
            {
                Id = id,
                SourceId = _source.Id,
                Source = _source,
                Origin = MediaAssetOrigin.ProjectPhoto,
                Kind = MediaAssetKind.Photo,
                SourceEntityId = $"project:1:photo:{id}",
                ParentEntityId = "1",
                OriginalFileName = fileName,
                ContentType = "image/jpeg",
                FileSizeBytes = bytes.Length,
                ContextKey = "project:1",
                CollectionKey = "project:1",
                ContextTitle = "Project",
                ContextSubtitle = "Project media",
                SourceLabel = "PRISM",
                Title = fileName,
                MediaDateUtc = DateTimeOffset.UtcNow,
                IndexedAtUtc = DateTimeOffset.UtcNow,
                LastSeenAtUtc = DateTimeOffset.UtcNow,
                IsAvailable = true,
                AvailabilityStatus = MediaAvailabilityStatus.Available,
                IsArchived = archived
            });
            ResolverDefinitions[id] = new ContentDefinition(fileName, bytes, failDuringRead);
        }

        public Task SaveAsync() => Db.SaveChangesAsync();

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            try
            {
                if (Directory.Exists(_cacheRoot))
                {
                    Directory.Delete(_cacheRoot, recursive: true);
                }
            }
            catch
            {
                // Test cleanup only.
            }
        }
    }

    private sealed record ContentDefinition(string FileName, byte[] Bytes, bool FailDuringRead);

    private sealed class StubContentResolver(IReadOnlyDictionary<long, ContentDefinition> definitions)
        : IMediaContentProviderResolver
    {
        public Task<MediaContentDescriptor?> ResolveAsync(MediaAsset asset, CancellationToken cancellationToken)
        {
            if (!definitions.TryGetValue(asset.Id, out var definition))
            {
                return Task.FromResult<MediaContentDescriptor?>(null);
            }

            return Task.FromResult<MediaContentDescriptor?>(new MediaContentDescriptor(
                definition.FileName,
                "image/jpeg",
                definition.Bytes.LongLength,
                null,
                _ => Task.FromResult<Stream>(definition.FailDuringRead
                    ? new ThrowAfterFirstReadStream(definition.Bytes)
                    : new MemoryStream(definition.Bytes, writable: false))));
        }
    }

    private sealed class StubCachePathResolver(string cacheRoot) : IMediaCachePathResolver
    {
        public string CacheRoot { get; } = cacheRoot;
        public string GetThumbnailPath(long assetId, int cacheVersion) => throw new NotSupportedException();
        public string GetPreviewPath(long assetId, int cacheVersion) => throw new NotSupportedException();
    }

    private sealed class StubBulkDownloadService(MediaBulkDownloadResult result) : IMediaBulkDownloadService
    {
        public Task<MediaBulkDownloadResult> CreateAsync(
            IReadOnlyCollection<long> assetIds,
            string requestedByUserId,
            CancellationToken cancellationToken)
            => Task.FromResult(result);
    }

    private sealed class ThrowAfterFirstReadStream : MemoryStream
    {
        private bool _readOnce;

        public ThrowAfterFirstReadStream(byte[] bytes) : base(bytes, writable: false)
        {
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_readOnce)
            {
                throw new IOException("Simulated source failure.");
            }

            _readOnce = true;
            var length = Math.Min(1, buffer.Length);
            return base.ReadAsync(buffer[..length], cancellationToken);
        }
    }

    private sealed class SynchronousWriteForbiddenStream : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
            => throw new InvalidOperationException("Synchronous response writes are forbidden.");

        public override void Write(ReadOnlySpan<byte> buffer)
            => throw new InvalidOperationException("Synchronous response writes are forbidden.");
    }
}
