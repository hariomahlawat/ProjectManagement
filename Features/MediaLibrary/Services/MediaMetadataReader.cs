using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using ProjectManagement.Features.MediaLibrary.Domain;

namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed class MediaMetadataReader : IMediaMetadataReader
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mov", ".m4v", ".ogg"
    };

    public async Task<MediaFileMetadata> ReadAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

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
            return new MediaFileMetadata(
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
                null);
        }

        var info = await Image.IdentifyAsync(path, cancellationToken);
        if (info is null)
        {
            throw new InvalidDataException("The image format is not supported by the current server decoder.");
        }

        var exif = info.Metadata.ExifProfile;
        var make = ReadExifString(exif, ExifTag.Make);
        var model = ReadExifString(exif, ExifTag.Model);
        var taken = TryReadExifDate(exif) ?? modified;

        return new MediaFileMetadata(
            kind,
            contentType,
            file.Length,
            modified,
            taken,
            info.Width,
            info.Height,
            null,
            !string.IsNullOrWhiteSpace(make) || !string.IsNullOrWhiteSpace(model),
            make,
            model);
    }

    private static string? ReadExifString(ExifProfile? profile, ExifTag<string> tag)
    {
        if (profile is null)
        {
            return null;
        }

        return profile.TryGetValue(tag, out var value)
            ? value.Value?.Trim()
            : null;
    }

    private static DateTimeOffset? TryReadExifDate(ExifProfile? profile)
    {
        if (profile is null)
        {
            return null;
        }

        var raw = ReadExifString(profile, ExifTag.DateTimeOriginal)
            ?? ReadExifString(profile, ExifTag.DateTimeDigitized)
            ?? ReadExifString(profile, ExifTag.DateTime);

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (DateTime.TryParseExact(
            raw,
            "yyyy:MM:dd HH:mm:ss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeLocal,
            out var parsed))
        {
            return new DateTimeOffset(parsed);
        }

        return null;
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
