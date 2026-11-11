using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SkiaSharp;

namespace ProjectManagement.Tests.MediaLibrary;

/// <summary>
/// A small, highly compressible file can declare dimensions whose full decode exhausts
/// server memory. Derivative generation must check the header before allocating, and
/// JPEGs must decode at reduced scale when only a thumbnail or preview is needed.
/// </summary>
public sealed class MediaImageDecodeLimitTests : IDisposable
{
    private readonly string _cacheRoot = Path.Combine(Path.GetTempPath(), $"media-decode-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_cacheRoot))
        {
            Directory.Delete(_cacheRoot, recursive: true);
        }
    }

    [Fact]
    public void Pixel_limit_rejects_oversized_dimensions_only()
    {
        MediaImageBounds.EnsureWithinPixelLimit(1000, 1000, 1_000_000);
        var error = Assert.Throws<MediaProcessingPermanentException>(() =>
            MediaImageBounds.EnsureWithinPixelLimit(1001, 1000, 1_000_000));
        Assert.Contains("exceeds the configured processing limit", error.Message);
        Assert.Throws<MediaProcessingPermanentException>(() =>
            MediaImageBounds.EnsureWithinPixelLimit(0, 10, 1_000_000));
    }

    [Fact]
    public async Task Oversized_png_is_rejected_before_full_decode()
    {
        var png = Encode(3000, 3000, jpeg: false);
        await using var db = await SeedAsync();
        var service = CreateService(db, png, maxImagePixels: 1_000_000);

        var error = await Assert.ThrowsAsync<MediaProcessingPermanentException>(() =>
            service.EnsureAsync(1, "thumb", CancellationToken.None));
        Assert.Contains("exceeds the configured processing limit", error.Message);
    }

    [Fact]
    public async Task Large_jpeg_thumbnail_decodes_at_reduced_scale_within_the_limit()
    {
        // 9 MP would exceed the 1 MP limit at full size; the 1/8 JPEG decode is 375x375.
        var jpeg = Encode(3000, 3000, jpeg: true);
        await using var db = await SeedAsync();
        var service = CreateService(db, jpeg, maxImagePixels: 1_000_000);

        var path = await service.EnsureAsync(1, "thumb", CancellationToken.None);

        using var codec = SKCodec.Create(path);
        Assert.NotNull(codec);
        Assert.True(Math.Max(codec!.Info.Width, codec.Info.Height) <= 480);
        Assert.True(codec.Info.Width > 0);
    }

    [Fact]
    public void Pixel_limit_option_is_validated()
    {
        var options = new MediaLibraryOptions();
        options.Processing.MaxImagePixels = 10;

        var result = new MediaLibraryOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("MaxImagePixels", StringComparison.Ordinal));
    }

    private static byte[] Encode(int width, int height, bool jpeg)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(40, 90, 160));
        using var stream = new MemoryStream();
        if (jpeg)
        {
            image.SaveAsJpeg(stream);
        }
        else
        {
            image.SaveAsPng(stream);
        }

        return stream.ToArray();
    }

    private MediaDerivativeService CreateService(MediaLibraryDbContext db, byte[] content, long maxImagePixels)
    {
        var options = new MediaLibraryOptions();
        options.Processing.MaxImagePixels = maxImagePixels;
        return new MediaDerivativeService(
            db,
            new TempCache(_cacheRoot),
            new BytesResolver(content),
            Options.Create(options));
    }

    private static async Task<MediaLibraryDbContext> SeedAsync()
    {
        var db = new MediaLibraryDbContext(new DbContextOptionsBuilder<MediaLibraryDbContext>()
            .UseInMemoryDatabase($"media-decode-{Guid.NewGuid():N}")
            .Options);
        var source = ReclassifySeed.Source();
        db.Sources.Add(source);
        var asset = ReclassifySeed.Asset(source, 1, null);
        asset.CacheVersion = 1;
        db.Assets.Add(asset);
        await db.SaveChangesAsync();
        return db;
    }

    private sealed class TempCache(string root) : IMediaCachePathResolver
    {
        public string CacheRoot => root;

        public string GetThumbnailPath(long assetId, int cacheVersion)
            => Path.Combine(root, $"{assetId}-{cacheVersion}-thumb.webp");

        public string GetPreviewPath(long assetId, int cacheVersion)
            => Path.Combine(root, $"{assetId}-{cacheVersion}-preview.webp");
    }

    private sealed class BytesResolver(byte[] bytes) : IMediaContentProviderResolver
    {
        public Task<MediaContentDescriptor?> ResolveAsync(MediaAsset asset, CancellationToken cancellationToken)
            => Task.FromResult<MediaContentDescriptor?>(new(
                "photo", "image/jpeg", bytes.Length, null,
                _ => Task.FromResult<Stream>(new MemoryStream(bytes))));
    }
}
