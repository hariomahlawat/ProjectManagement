using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using SkiaSharp;

namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed class MediaDerivativeService : IMediaDerivativeService
{
    private const int LockStripeCount = 257;
    private static readonly SemaphoreSlim[] AssetLocks = Enumerable.Range(0, LockStripeCount)
        .Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private readonly MediaLibraryDbContext _db;
    private readonly IMediaCachePathResolver _cache;
    private readonly IMediaContentProviderResolver _contentResolver;
    private readonly MediaLibraryOptions _options;

    public MediaDerivativeService(
        MediaLibraryDbContext db,
        IMediaCachePathResolver cache,
        IMediaContentProviderResolver contentResolver,
        IOptions<MediaLibraryOptions> options)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _contentResolver = contentResolver ?? throw new ArgumentNullException(nameof(contentResolver));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<string> EnsureAsync(long assetId, string variant, CancellationToken cancellationToken)
    {
        variant = variant.Equals("thumb", StringComparison.OrdinalIgnoreCase) ? "thumb" : "preview";
        var gate = AssetLocks[(int)((ulong)assetId % LockStripeCount)];
        await gate.WaitAsync(cancellationToken);

        try
        {
            var asset = await _db.Assets.Include(x => x.Source)
                .SingleOrDefaultAsync(x => x.Id == assetId, cancellationToken)
                ?? throw new FileNotFoundException("The media asset is not indexed.");

            if (!asset.IsAvailable || asset.IsDeleted || asset.Kind != MediaAssetKind.Photo)
                throw new FileNotFoundException("The media asset is unavailable.");

            var output = variant == "thumb"
                ? _cache.GetThumbnailPath(asset.Id, asset.CacheVersion)
                : _cache.GetPreviewPath(asset.Id, asset.CacheVersion);
            if (File.Exists(output)) return output;

            var content = await _contentResolver.ResolveAsync(asset, cancellationToken)
                ?? throw new FileNotFoundException("The original media content could not be resolved.");
            if (content.Length is > 0 && content.Length > _options.Processing.MaxImageFileSizeBytes)
                throw new InvalidDataException("The image exceeds the configured processing size limit.");

            var maxPixels = variant == "thumb"
                ? _options.Processing.ThumbnailMaxPixels
                : _options.Processing.PreviewMaxPixels;
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var temporary = output + $".{Guid.NewGuid():N}.tmp";

            try
            {
                await using var source = await content.OpenReadAsync(cancellationToken);
                using var oriented = DecodeOriented(source);
                using var resized = ResizeToFit(oriented, maxPixels);
                using var image = SKImage.FromBitmap(resized);
                using var encoded = image.Encode(SKEncodedImageFormat.Webp, _options.Processing.WebpQuality)
                    ?? throw new InvalidDataException("The server could not encode the media preview.");
                await using var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                encoded.SaveTo(destination);
                await destination.FlushAsync(cancellationToken);
                File.Move(temporary, output, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }

            return output;
        }
        finally { gate.Release(); }
    }

    private static SKBitmap DecodeOriented(Stream stream)
    {
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        memory.Position = 0;
        using var codec = SKCodec.Create(memory)
            ?? throw new InvalidDataException("The image format is not supported by SkiaSharp.");
        var source = new SKBitmap(codec.Info.Width, codec.Info.Height, codec.Info.ColorType, codec.Info.AlphaType);
        var result = codec.GetPixels(source.Info, source.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            source.Dispose();
            throw new InvalidDataException($"The image could not be decoded ({result}).");
        }
        if (codec.EncodedOrigin == SKEncodedOrigin.TopLeft) return source;

        var swapsAxes = codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var destination = new SKBitmap(swapsAxes ? source.Height : source.Width,
            swapsAxes ? source.Width : source.Height, source.ColorType, source.AlphaType);
        using (source)
        using (var canvas = new SKCanvas(destination))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.SetMatrix(CreateOrientationMatrix(codec.EncodedOrigin, source.Width, source.Height));
            canvas.DrawBitmap(source, 0, 0);
            canvas.Flush();
        }
        return destination;
    }

    private static SKMatrix CreateOrientationMatrix(SKEncodedOrigin origin, int width, int height) => origin switch
    {
        SKEncodedOrigin.TopRight => Matrix(-1, 0, width, 0, 1, 0),
        SKEncodedOrigin.BottomRight => Matrix(-1, 0, width, 0, -1, height),
        SKEncodedOrigin.BottomLeft => Matrix(1, 0, 0, 0, -1, height),
        SKEncodedOrigin.LeftTop => Matrix(0, 1, 0, 1, 0, 0),
        SKEncodedOrigin.RightTop => Matrix(0, -1, height, 1, 0, 0),
        SKEncodedOrigin.RightBottom => Matrix(0, -1, height, -1, 0, width),
        SKEncodedOrigin.LeftBottom => Matrix(0, 1, 0, -1, 0, width),
        _ => SKMatrix.CreateIdentity()
    };

    private static SKMatrix Matrix(float scaleX, float skewX, float transX, float skewY, float scaleY, float transY) => new()
    { ScaleX = scaleX, SkewX = skewX, TransX = transX, SkewY = skewY, ScaleY = scaleY, TransY = transY, Persp0 = 0, Persp1 = 0, Persp2 = 1 };

    private static SKBitmap ResizeToFit(SKBitmap source, int maxPixels)
    {
        var longest = Math.Max(source.Width, source.Height);
        if (longest <= maxPixels) return source.Copy();
        var scale = maxPixels / (double)longest;
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        return source.Resize(new SKImageInfo(width, height), SKFilterQuality.High)
               ?? throw new InvalidDataException("The image preview could not be resized.");
    }
}
