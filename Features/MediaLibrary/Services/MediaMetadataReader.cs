using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using SkiaSharp;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Reads media metadata without sharing stream ownership between native decoders.
/// Each parser receives an independent view over an immutable byte buffer, preventing
/// SkiaSharp from closing a stream that MetadataExtractor still needs to read.
/// </summary>
public sealed class MediaMetadataReader : IMediaMetadataReader
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mov", ".m4v", ".ogg"
    };

    private readonly long _maximumImageBytes;

    public MediaMetadataReader(IOptions<MediaLibraryOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _maximumImageBytes = Math.Max(1, options.Value.Processing.MaxImageFileSizeBytes);
    }

    public async Task<MediaFileMetadata> ReadAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();

        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new MediaContentUnavailableException("The media file is no longer available.");
        }

        var descriptor = new MediaContentDescriptor(
            file.Name,
            ResolveContentType(file.Extension),
            file.Length,
            new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero),
            _ => Task.FromResult<Stream>(new FileStream(
                file.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan)));

        return await ReadAsync(descriptor, cancellationToken);
    }

    public async Task<MediaFileMetadata> ReadAsync(
        MediaContentDescriptor content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();

        var extension = Path.GetExtension(content.FileName);
        var kind = VideoExtensions.Contains(extension)
            ? MediaAssetKind.Video
            : MediaAssetKind.Photo;
        var modified = content.LastModifiedUtc ?? DateTimeOffset.UtcNow;

        if (kind == MediaAssetKind.Video)
        {
            return new MediaFileMetadata(
                kind,
                ResolveContentType(content.ContentType, extension),
                content.Length ?? 0,
                modified,
                modified,
                null,
                null,
                null,
                false,
                null,
                null);
        }

        ValidateDeclaredLength(content.Length);

        byte[] bytes;
        try
        {
            await using var source = await content.OpenReadAsync(cancellationToken)
                ?? throw new MediaContentUnavailableException("The media provider returned no readable stream.");
            bytes = await ReadBoundedAsync(source, _maximumImageBytes, cancellationToken);
        }
        catch (MediaProcessingPermanentException)
        {
            throw;
        }
        catch (FileNotFoundException ex)
        {
            throw new MediaContentUnavailableException("The image is no longer available.", ex);
        }
        catch (DirectoryNotFoundException ex)
        {
            throw new MediaContentUnavailableException("The image storage location is no longer available.", ex);
        }

        if (bytes.Length == 0)
        {
            throw new MediaProcessingPermanentException("The image file is empty.");
        }

        var (width, height) = ReadDimensions(bytes);
        cancellationToken.ThrowIfCancellationRequested();
        var exif = ReadExif(bytes);

        return new MediaFileMetadata(
            kind,
            ResolveContentType(content.ContentType, extension),
            content.Length is > 0 ? content.Length.Value : bytes.LongLength,
            modified,
            exif.TakenAt ?? modified,
            width,
            height,
            null,
            !string.IsNullOrWhiteSpace(exif.Make) || !string.IsNullOrWhiteSpace(exif.Model),
            exif.Make,
            exif.Model);
    }

    private void ValidateDeclaredLength(long? length)
    {
        if (length is <= 0)
        {
            return;
        }

        if (length > _maximumImageBytes)
        {
            throw new MediaProcessingPermanentException(
                $"The image exceeds the configured processing size limit of {_maximumImageBytes:N0} bytes.");
        }
    }

    private static (int Width, int Height) ReadDimensions(byte[] bytes)
    {
        try
        {
            // SKData owns its own native copy. Disposing SKCodec cannot close or mutate
            // any managed stream used later by MetadataExtractor.
            using var data = SKData.CreateCopy(bytes)
                ?? throw new MediaProcessingPermanentException("The image buffer could not be prepared for decoding.");
            using var codec = SKCodec.Create(data)
                ?? throw new MediaProcessingPermanentException("The image format is not supported by the server decoder.");

            if (codec.Info.Width <= 0 || codec.Info.Height <= 0)
            {
                throw new MediaProcessingPermanentException("The image has invalid dimensions.");
            }

            var rotates = codec.EncodedOrigin is SKEncodedOrigin.LeftTop
                or SKEncodedOrigin.RightTop
                or SKEncodedOrigin.RightBottom
                or SKEncodedOrigin.LeftBottom;

            return rotates
                ? (codec.Info.Height, codec.Info.Width)
                : (codec.Info.Width, codec.Info.Height);
        }
        catch (MediaProcessingPermanentException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new MediaProcessingPermanentException("The image header could not be decoded.", ex);
        }
    }

    private static ExifMetadata ReadExif(byte[] bytes)
    {
        try
        {
            // MetadataExtractor receives an independent stream. It is intentionally
            // never shared with SKCodec or any other native component.
            using var metadataStream = new MemoryStream(bytes, writable: false);
            var directories = ImageMetadataReader.ReadMetadata(metadataStream);
            var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();

            var make = ifd0?.GetString(ExifDirectoryBase.TagMake)?.Trim();
            var model = ifd0?.GetString(ExifDirectoryBase.TagModel)?.Trim();
            DateTimeOffset? takenAt = null;

            if (TryGetDate(subIfd, ExifDirectoryBase.TagDateTimeOriginal, out var date)
                || TryGetDate(subIfd, ExifDirectoryBase.TagDateTimeDigitized, out date)
                || TryGetDate(ifd0, ExifDirectoryBase.TagDateTime, out date))
            {
                // EXIF usually has no offset. Preserve local-clock semantics instead of
                // incorrectly labelling the value as UTC.
                takenAt = new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Local));
            }

            return new ExifMetadata(make, model, takenAt);
        }
        catch (Exception ex) when (ex is ImageProcessingException
                                   or IOException
                                   or ArgumentException
                                   or NotSupportedException)
        {
            // EXIF is optional. Corrupt or unsupported metadata must not reject a
            // photograph whose encoded pixels are otherwise valid.
            return ExifMetadata.Empty;
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream source,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
        {
            throw new MediaProcessingPermanentException("The media provider returned a non-readable stream.");
        }

        var initialCapacity = source.CanSeek
            ? (int)Math.Min(Math.Max(source.Length - source.Position, 0), Math.Min(maximumBytes, int.MaxValue))
            : 0;
        using var destination = initialCapacity > 0
            ? new MemoryStream(initialCapacity)
            : new MemoryStream();

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
                throw new MediaProcessingPermanentException(
                    $"The image exceeds the configured processing size limit of {maximumBytes:N0} bytes.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return destination.ToArray();
    }

    private static bool TryGetDate(ExifDirectoryBase? directory, int tag, out DateTime value)
    {
        value = default;
        return directory is not null && directory.TryGetDateTime(tag, out value);
    }

    private static string ResolveContentType(string? declaredContentType, string extension)
    {
        if (!string.IsNullOrWhiteSpace(declaredContentType)
            && !declaredContentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            return declaredContentType;
        }

        return ResolveContentType(extension);
    }

    private static string ResolveContentType(string extension)
        => extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".mp4" or ".m4v" => "video/mp4",
            ".webm" => "video/webm",
            ".mov" => "video/quicktime",
            ".ogg" => "video/ogg",
            _ => "application/octet-stream"
        };

    private sealed record ExifMetadata(string? Make, string? Model, DateTimeOffset? TakenAt)
    {
        public static readonly ExifMetadata Empty = new(null, null, null);
    }
}
