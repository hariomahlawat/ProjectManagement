using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using ProjectManagement.Features.MediaLibrary.Domain;
using SkiaSharp;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Reads image dimensions with SkiaSharp and camera/creation metadata with
/// MetadataExtractor. Both dependencies use permissive open-source licences.
/// </summary>
public sealed class MediaMetadataReader : IMediaMetadataReader
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mov", ".m4v", ".ogg"
    };

    public Task<MediaFileMetadata> ReadAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();

        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new FileNotFoundException("The media file is no longer available.", path);
        }

        var extension = file.Extension;
        var kind = VideoExtensions.Contains(extension) ? MediaAssetKind.Video : MediaAssetKind.Photo;
        var contentType = ResolveContentType(extension);
        var modified = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);

        if (kind == MediaAssetKind.Video)
        {
            return Task.FromResult(new MediaFileMetadata(
                kind,
                contentType,
                file.Length,
                modified,
                modified,
                null,
                null,
                null,
                false,
                null,
                null));
        }

        int width;
        int height;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                   FileShare.ReadWrite | FileShare.Delete))
        using (var codec = SKCodec.Create(stream))
        {
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
            {
                throw new InvalidDataException("The image format is not supported by the current server decoder.");
            }

            var rotates = codec.EncodedOrigin is SKEncodedOrigin.LeftTop
                or SKEncodedOrigin.RightTop
                or SKEncodedOrigin.RightBottom
                or SKEncodedOrigin.LeftBottom;
            width = rotates ? codec.Info.Height : codec.Info.Width;
            height = rotates ? codec.Info.Width : codec.Info.Height;
        }

        cancellationToken.ThrowIfCancellationRequested();
        string? make = null;
        string? model = null;
        DateTimeOffset? taken = null;

        try
        {
            var directories = ImageMetadataReader.ReadMetadata(path);
            var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();

            make = ifd0?.GetString(ExifDirectoryBase.TagMake)?.Trim();
            model = ifd0?.GetString(ExifDirectoryBase.TagModel)?.Trim();

            if (TryGetDate(subIfd, ExifDirectoryBase.TagDateTimeOriginal, out var original)
                || TryGetDate(subIfd, ExifDirectoryBase.TagDateTimeDigitized, out original)
                || TryGetDate(ifd0, ExifDirectoryBase.TagDateTime, out original))
            {
                taken = new DateTimeOffset(DateTime.SpecifyKind(original, DateTimeKind.Local));
            }
        }
        catch (ImageProcessingException)
        {
            // Metadata is optional. A valid image remains indexable without EXIF.
        }

        return Task.FromResult(new MediaFileMetadata(
            kind,
            contentType,
            file.Length,
            modified,
            taken ?? modified,
            width,
            height,
            null,
            !string.IsNullOrWhiteSpace(make) || !string.IsNullOrWhiteSpace(model),
            make,
            model));
    }


    public async Task<MediaFileMetadata> ReadAsync(MediaContentDescriptor content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();

        var extension = Path.GetExtension(content.FileName);
        var kind = VideoExtensions.Contains(extension) ? MediaAssetKind.Video : MediaAssetKind.Photo;
        var modified = content.LastModifiedUtc ?? DateTimeOffset.UtcNow;
        if (kind == MediaAssetKind.Video)
        {
            return new MediaFileMetadata(kind, content.ContentType, content.Length ?? 0, modified, modified, null, null, null, false, null, null);
        }

        await using var source = await content.OpenReadAsync(cancellationToken);
        using var memory = new MemoryStream();
        await source.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;

        int width;
        int height;
        using (var codec = SKCodec.Create(memory))
        {
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
                throw new InvalidDataException("The image format is not supported by the current server decoder.");
            var rotates = codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
            width = rotates ? codec.Info.Height : codec.Info.Width;
            height = rotates ? codec.Info.Width : codec.Info.Height;
        }

        memory.Position = 0;
        string? make = null;
        string? model = null;
        DateTimeOffset? taken = null;
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(memory);
            var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            make = ifd0?.GetString(ExifDirectoryBase.TagMake)?.Trim();
            model = ifd0?.GetString(ExifDirectoryBase.TagModel)?.Trim();
            if (TryGetDate(subIfd, ExifDirectoryBase.TagDateTimeOriginal, out var original)
                || TryGetDate(subIfd, ExifDirectoryBase.TagDateTimeDigitized, out original)
                || TryGetDate(ifd0, ExifDirectoryBase.TagDateTime, out original))
                taken = new DateTimeOffset(DateTime.SpecifyKind(original, DateTimeKind.Local));
        }
        catch (ImageProcessingException) { }

        return new MediaFileMetadata(kind, content.ContentType, content.Length ?? memory.Length, modified, taken ?? modified, width, height, null,
            !string.IsNullOrWhiteSpace(make) || !string.IsNullOrWhiteSpace(model), make, model);
    }

    private static bool TryGetDate(ExifDirectoryBase? directory, int tag, out DateTime value)
    {
        value = default;
        return directory is not null && directory.TryGetDateTime(tag, out value);
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
}
