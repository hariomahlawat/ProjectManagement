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
                ?? throw new MediaContentUnavailableException("The media asset is not indexed.");

            if (!asset.IsAvailable || asset.IsDeleted || asset.Kind != MediaAssetKind.Photo)
            {
                throw new MediaContentUnavailableException("The media asset is unavailable.");
            }

            var output = variant == "thumb"
                ? _cache.GetThumbnailPath(asset.Id, asset.CacheVersion)
                : _cache.GetPreviewPath(asset.Id, asset.CacheVersion);
            if (IsUsableDerivative(output))
            {
                return output;
            }

            // A zero-length file can be left behind by an abnormal process termination.
            // It is not a valid cache hit and can be regenerated safely.
            if (File.Exists(output))
            {
                TryDelete(output);
            }

            var content = await _contentResolver.ResolveAsync(asset, cancellationToken)
                ?? throw new MediaContentUnavailableException("The original media content could not be resolved.");
            if (content.Length is > 0 && content.Length > _options.Processing.MaxImageFileSizeBytes)
            {
                throw new MediaProcessingPermanentException("The image exceeds the configured processing size limit.");
            }

            var maxPixels = variant == "thumb"
                ? _options.Processing.ThumbnailMaxPixels
                : _options.Processing.PreviewMaxPixels;
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var temporary = output + $".{Guid.NewGuid():N}.tmp";

            try
            {
                byte[] bytes;
                await using (var source = await content.OpenReadAsync(cancellationToken)
                    ?? throw new MediaContentUnavailableException("The media provider returned no readable stream."))
                {
                    bytes = await ReadBoundedAsync(
                        source,
                        _options.Processing.MaxImageFileSizeBytes,
                        cancellationToken);
                }

                using var oriented = DecodeOriented(bytes);
                using var resized = ResizeToFit(oriented, maxPixels);
                using var image = SKImage.FromBitmap(resized);
                using var encoded = image.Encode(SKEncodedImageFormat.Webp, _options.Processing.WebpQuality)
                    ?? throw new MediaProcessingPermanentException("The server could not encode the media preview.");

                var encodedBytes = encoded.ToArray();
                if (encodedBytes.Length == 0)
                {
                    throw new MediaProcessingPermanentException("The server produced an empty media preview.");
                }

                await WriteAndCommitAsync(temporary, output, encodedBytes, cancellationToken);
            }
            catch (FileNotFoundException ex)
            {
                throw new MediaContentUnavailableException("The source image is no longer available.", ex);
            }
            catch (DirectoryNotFoundException ex)
            {
                throw new MediaContentUnavailableException("The source image storage location is no longer available.", ex);
            }
            finally
            {
                TryDelete(temporary);
            }

            return output;
        }
        finally
        {
            gate.Release();
        }
    }

    private static SKBitmap DecodeOriented(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            throw new MediaProcessingPermanentException("The image file is empty.");
        }

        try
        {
            using var data = SKData.CreateCopy(bytes)
                ?? throw new MediaProcessingPermanentException("The image buffer could not be prepared for decoding.");
            using var codec = SKCodec.Create(data)
                ?? throw new MediaProcessingPermanentException("The image format is not supported by SkiaSharp.");

            if (codec.Info.Width <= 0 || codec.Info.Height <= 0)
            {
                throw new MediaProcessingPermanentException("The image has invalid dimensions.");
            }

            var source = new SKBitmap(
                codec.Info.Width,
                codec.Info.Height,
                codec.Info.ColorType,
                codec.Info.AlphaType);
            var result = codec.GetPixels(source.Info, source.GetPixels());
            if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            {
                source.Dispose();
                throw new MediaProcessingPermanentException($"The image could not be decoded ({result}).");
            }

            if (codec.EncodedOrigin == SKEncodedOrigin.TopLeft)
            {
                return source;
            }

            var swapsAxes = codec.EncodedOrigin is SKEncodedOrigin.LeftTop
                or SKEncodedOrigin.RightTop
                or SKEncodedOrigin.RightBottom
                or SKEncodedOrigin.LeftBottom;
            var destination = new SKBitmap(
                swapsAxes ? source.Height : source.Width,
                swapsAxes ? source.Width : source.Height,
                source.ColorType,
                source.AlphaType);

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
        catch (MediaProcessingPermanentException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new MediaProcessingPermanentException("The image could not be decoded safely.", ex);
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream source,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        if (!source.CanRead)
        {
            throw new MediaProcessingPermanentException("The media provider returned a non-readable stream.");
        }

        using var destination = new MemoryStream();
        var buffer = new byte[128 * 1024];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > maximumBytes)
            {
                throw new MediaProcessingPermanentException("The image exceeds the configured processing size limit.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return destination.ToArray();
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

    private static SKMatrix Matrix(
        float scaleX,
        float skewX,
        float transX,
        float skewY,
        float scaleY,
        float transY) => new()
    {
        ScaleX = scaleX,
        SkewX = skewX,
        TransX = transX,
        SkewY = skewY,
        ScaleY = scaleY,
        TransY = transY,
        Persp0 = 0,
        Persp1 = 0,
        Persp2 = 1
    };

    private static SKBitmap ResizeToFit(SKBitmap source, int maxPixels)
    {
        var longest = Math.Max(source.Width, source.Height);
        if (longest <= maxPixels)
        {
            return source.Copy();
        }

        var scale = maxPixels / (double)longest;
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        return source.Resize(new SKImageInfo(width, height), SKFilterQuality.High)
               ?? throw new MediaProcessingPermanentException("The image preview could not be resized.");
    }



    private static bool IsUsableDerivative(string path)
    {
        try
        {
            return File.Exists(path) && new FileInfo(path).Length > 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task WriteAndCommitAsync(
        string temporary,
        string output,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken)
    {
        const int maximumAttempts = 4;

        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TryDelete(temporary);

            try
            {
                // SkiaSharp never receives this stream. The handle is released before
                // the atomic rename, which is required on Windows when FileShare.None is used.
                await using (var destination = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough))
                {
                    await destination.WriteAsync(bytes, cancellationToken);
                    await destination.FlushAsync(cancellationToken);
                }

                // Cache-versioned derivative names are immutable. If another process won
                // the race, its completed file is authoritative and this temporary copy
                // can be discarded safely.
                if (IsUsableDerivative(output))
                {
                    TryDelete(temporary);
                    return;
                }

                File.Move(temporary, output, overwrite: false);
                return;
            }
            catch (IOException) when (IsUsableDerivative(output))
            {
                TryDelete(temporary);
                return;
            }
            catch (IOException) when (attempt < maximumAttempts)
            {
                TryDelete(temporary);
                var delay = TimeSpan.FromMilliseconds((attempt * 125) + Random.Shared.Next(40, 180));
                await Task.Delay(delay, cancellationToken);
            }
        }

        throw new IOException("The media derivative could not be committed after repeated file-system sharing conflicts.");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // A stale temporary file is non-authoritative and can be cleaned later.
        }
    }
}
