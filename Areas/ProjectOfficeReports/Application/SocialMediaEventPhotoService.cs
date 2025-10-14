using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Services;
using ProjectManagement.Services.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public interface ISocialMediaEventPhotoService
{
    Task<IReadOnlyList<SocialMediaEventPhoto>> GetPhotosAsync(Guid eventId, CancellationToken cancellationToken);

    Task<SocialMediaEventPhotoUploadResult> UploadAsync(
        Guid eventId,
        Stream content,
        string originalFileName,
        string? contentType,
        string? caption,
        string createdByUserId,
        CancellationToken cancellationToken);

    Task<SocialMediaEventPhotoDeletionResult> RemoveAsync(
        Guid eventId,
        Guid photoId,
        byte[] rowVersion,
        string deletedByUserId,
        CancellationToken cancellationToken);

    Task<SocialMediaEventPhotoSetCoverResult> SetCoverAsync(
        Guid eventId,
        Guid photoId,
        byte[] rowVersion,
        string modifiedByUserId,
        CancellationToken cancellationToken);

    Task RemoveAllAsync(Guid eventId, IReadOnlyCollection<SocialMediaEventPhoto> photos, CancellationToken cancellationToken);

    Task<SocialMediaEventPhotoAsset?> OpenAsync(Guid eventId, Guid photoId, string size, CancellationToken cancellationToken);
}

public sealed class SocialMediaEventPhotoService : ISocialMediaEventPhotoService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly SocialMediaPhotoOptions _options;
    private readonly IUploadRootProvider _uploadRootProvider;
    private readonly ILogger<SocialMediaEventPhotoService> _logger;

    public SocialMediaEventPhotoService(
        ApplicationDbContext db,
        IClock clock,
        IOptions<SocialMediaPhotoOptions> options,
        IUploadRootProvider uploadRootProvider,
        ILogger<SocialMediaEventPhotoService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _uploadRootProvider = uploadRootProvider ?? throw new ArgumentNullException(nameof(uploadRootProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<SocialMediaEventPhoto>> GetPhotosAsync(Guid eventId, CancellationToken cancellationToken)
    {
        return await _db.SocialMediaEventPhotos.AsNoTracking()
            .Where(x => x.SocialMediaEventId == eventId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<SocialMediaEventPhotoUploadResult> UploadAsync(
        Guid eventId,
        Stream content,
        string originalFileName,
        string? contentType,
        string? caption,
        string createdByUserId,
        CancellationToken cancellationToken)
    {
        if (content == null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        if (string.IsNullOrWhiteSpace(createdByUserId))
        {
            throw new ArgumentException("User identifier is required.", nameof(createdByUserId));
        }

        _ = originalFileName;

        var socialEvent = await _db.SocialMediaEvents.Include(x => x.Photos)
            .FirstOrDefaultAsync(x => x.Id == eventId, cancellationToken);

        if (socialEvent == null)
        {
            return SocialMediaEventPhotoUploadResult.NotFound();
        }

        _db.Entry(socialEvent).Property(x => x.RowVersion).OriginalValue = socialEvent.RowVersion;

        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);

        if (buffer.Length == 0)
        {
            return SocialMediaEventPhotoUploadResult.Invalid("Uploaded file is empty.");
        }

        if (buffer.Length > _options.MaxFileSizeBytes)
        {
            return SocialMediaEventPhotoUploadResult.TooLarge(_options.MaxFileSizeBytes);
        }

        buffer.Position = 0;
        IImageFormat? detectedFormat = await Image.DetectFormatAsync(buffer, cancellationToken);
        if (detectedFormat == null)
        {
            return SocialMediaEventPhotoUploadResult.Invalid("Unsupported image format.");
        }

        var normalizedContentType = NormalizeContentType(contentType, detectedFormat);
        if (!_options.AllowedContentTypes.Contains(normalizedContentType))
        {
            return SocialMediaEventPhotoUploadResult.Invalid("Only JPEG, PNG, or WebP images are supported.");
        }

        buffer.Position = 0;
        using var sourceImage = await Image.LoadAsync<Rgba32>(buffer, cancellationToken);
        sourceImage.Mutate(x => x.AutoOrient());

        if (sourceImage.Width < _options.MinWidth || sourceImage.Height < _options.MinHeight)
        {
            return SocialMediaEventPhotoUploadResult.ImageTooSmall(_options.MinWidth, _options.MinHeight);
        }

        var now = _clock.UtcNow;
        var photoId = Guid.NewGuid();
        var storageKey = BuildStorageKey(_options.StoragePrefix, eventId, photoId);
        var photoFolder = GetPhotoFolder(eventId, photoId);
        Directory.CreateDirectory(photoFolder);

        buffer.Position = 0;
        var originalExtension = GetExtension(detectedFormat);
        var originalFileName = $"original{originalExtension}";
        var originalPath = Path.Combine(photoFolder, originalFileName);

        await using (var fileStream = File.Create(originalPath))
        {
            buffer.Position = 0;
            await buffer.CopyToAsync(fileStream, cancellationToken);
        }

        foreach (var derivative in _options.Derivatives)
        {
            var derivativeFileName = $"{NormalizeDerivativeKey(derivative.Key)}.jpg";
            var derivativePath = Path.Combine(photoFolder, derivativeFileName);

            using var clone = sourceImage.Clone(ctx =>
            {
                ctx.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(derivative.Value.Width, derivative.Value.Height)
                });
            });

            await using var derivativeStream = File.Create(derivativePath);
            await clone.SaveAsJpegAsync(
                derivativeStream,
                new JpegEncoder { Quality = derivative.Value.Quality },
                cancellationToken);
        }

        var trimmedCaption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim();
        var photo = new SocialMediaEventPhoto
        {
            Id = photoId,
            SocialMediaEventId = eventId,
            StorageKey = storageKey,
            StoragePath = CombineStoragePath(storageKey, originalFileName),
            ContentType = normalizedContentType,
            Width = sourceImage.Width,
            Height = sourceImage.Height,
            Caption = trimmedCaption,
            VersionStamp = Guid.NewGuid().ToString("N"),
            IsCover = !socialEvent.CoverPhotoId.HasValue,
            CreatedAtUtc = now,
            CreatedByUserId = createdByUserId,
            LastModifiedAtUtc = now,
            LastModifiedByUserId = createdByUserId
        };

        _db.SocialMediaEventPhotos.Add(photo);
        socialEvent.Photos.Add(photo);
        socialEvent.LastModifiedAtUtc = now;
        socialEvent.LastModifiedByUserId = createdByUserId;

        if (!socialEvent.CoverPhotoId.HasValue)
        {
            socialEvent.CoverPhotoId = photoId;
        }

        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogWarning(
                ex,
                "Concurrency conflict while saving social media event photo metadata for event {EventId}",
                eventId);
            _db.Entry(photo).State = EntityState.Detached;
            socialEvent.Photos.Remove(photo);
            await DeletePhysicalAssetsAsync(eventId, photoId, cancellationToken);
            return SocialMediaEventPhotoUploadResult.Concurrency();
        }
        catch (DbUpdateException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Failed to persist social media event photo metadata for event {EventId}", eventId);
            _db.Entry(photo).State = EntityState.Detached;
            socialEvent.Photos.Remove(photo);
            await DeletePhysicalAssetsAsync(eventId, photoId, cancellationToken);
            var userMessage = SocialMediaEventPhotoErrorTranslator.GetUserFacingMessage(ex);
            return SocialMediaEventPhotoUploadResult.Invalid(userMessage);
        }

        return SocialMediaEventPhotoUploadResult.Success(photo);
    }

    public async Task<SocialMediaEventPhotoDeletionResult> RemoveAsync(
        Guid eventId,
        Guid photoId,
        byte[] rowVersion,
        string deletedByUserId,
        CancellationToken cancellationToken)
    {
        if (rowVersion == null)
        {
            throw new ArgumentNullException(nameof(rowVersion));
        }

        if (string.IsNullOrWhiteSpace(deletedByUserId))
        {
            throw new ArgumentException("User identifier is required.", nameof(deletedByUserId));
        }

        var socialEvent = await _db.SocialMediaEvents.Include(x => x.Photos)
            .FirstOrDefaultAsync(x => x.Id == eventId, cancellationToken);

        if (socialEvent == null)
        {
            return SocialMediaEventPhotoDeletionResult.NotFound();
        }

        var photo = socialEvent.Photos.FirstOrDefault(x => x.Id == photoId);
        if (photo == null)
        {
            return SocialMediaEventPhotoDeletionResult.NotFound();
        }

        _db.Entry(socialEvent).Property(x => x.RowVersion).OriginalValue = socialEvent.RowVersion;
        _db.Entry(photo).Property(x => x.RowVersion).OriginalValue = rowVersion;

        socialEvent.Photos.Remove(photo);
        _db.SocialMediaEventPhotos.Remove(photo);

        if (socialEvent.CoverPhotoId == photoId)
        {
            socialEvent.CoverPhotoId = null;
            var nextCover = socialEvent.Photos
                .OrderBy(x => x.CreatedAtUtc)
                .FirstOrDefault();

            if (nextCover != null)
            {
                nextCover.IsCover = true;
                nextCover.LastModifiedAtUtc = _clock.UtcNow;
                nextCover.LastModifiedByUserId = deletedByUserId;
                socialEvent.CoverPhotoId = nextCover.Id;
            }
        }

        socialEvent.LastModifiedAtUtc = _clock.UtcNow;
        socialEvent.LastModifiedByUserId = deletedByUserId;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(
                ex,
                "Concurrency conflict while deleting social media event photo {PhotoId} for event {EventId}",
                photoId,
                eventId);
            return SocialMediaEventPhotoDeletionResult.Concurrency();
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(
                ex,
                "Failed to delete social media event photo metadata for event {EventId} photo {PhotoId}",
                eventId,
                photoId);
            var userMessage = SocialMediaEventPhotoErrorTranslator.GetUserFacingMessage(ex);
            return SocialMediaEventPhotoDeletionResult.Failed(userMessage);
        }

        await DeletePhysicalAssetsAsync(eventId, photoId, cancellationToken);
        return SocialMediaEventPhotoDeletionResult.Success();
    }

    public async Task<SocialMediaEventPhotoSetCoverResult> SetCoverAsync(
        Guid eventId,
        Guid photoId,
        byte[] rowVersion,
        string modifiedByUserId,
        CancellationToken cancellationToken)
    {
        if (rowVersion == null)
        {
            throw new ArgumentNullException(nameof(rowVersion));
        }

        if (string.IsNullOrWhiteSpace(modifiedByUserId))
        {
            throw new ArgumentException("User identifier is required.", nameof(modifiedByUserId));
        }

        var socialEvent = await _db.SocialMediaEvents.Include(x => x.Photos)
            .FirstOrDefaultAsync(x => x.Id == eventId, cancellationToken);

        if (socialEvent == null)
        {
            return SocialMediaEventPhotoSetCoverResult.NotFound();
        }

        var photo = socialEvent.Photos.FirstOrDefault(x => x.Id == photoId);
        if (photo == null)
        {
            return SocialMediaEventPhotoSetCoverResult.NotFound();
        }

        if (socialEvent.CoverPhotoId == photoId && photo.IsCover)
        {
            return SocialMediaEventPhotoSetCoverResult.Success();
        }

        _db.Entry(socialEvent).Property(x => x.RowVersion).OriginalValue = socialEvent.RowVersion;
        _db.Entry(photo).Property(x => x.RowVersion).OriginalValue = rowVersion;

        foreach (var existing in socialEvent.Photos.Where(x => x.IsCover && x.Id != photoId))
        {
            existing.IsCover = false;
        }

        photo.IsCover = true;
        socialEvent.CoverPhotoId = photoId;
        photo.LastModifiedAtUtc = _clock.UtcNow;
        photo.LastModifiedByUserId = modifiedByUserId;
        socialEvent.LastModifiedAtUtc = photo.LastModifiedAtUtc;
        socialEvent.LastModifiedByUserId = modifiedByUserId;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(
                ex,
                "Concurrency conflict while setting cover photo {PhotoId} for event {EventId}",
                photoId,
                eventId);
            return SocialMediaEventPhotoSetCoverResult.Concurrency();
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(
                ex,
                "Failed to set cover photo {PhotoId} for event {EventId}",
                photoId,
                eventId);
            var userMessage = SocialMediaEventPhotoErrorTranslator.GetUserFacingMessage(ex);
            return SocialMediaEventPhotoSetCoverResult.Failed(userMessage);
        }

        return SocialMediaEventPhotoSetCoverResult.Success();
    }

    public async Task RemoveAllAsync(Guid eventId, IReadOnlyCollection<SocialMediaEventPhoto> photos, CancellationToken cancellationToken)
    {
        if (photos == null || photos.Count == 0)
        {
            return;
        }

        foreach (var photo in photos)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await DeletePhysicalAssetsAsync(eventId, photo.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to delete social media event photo assets for event {EventId} photo {PhotoId}",
                    eventId,
                    photo.Id);
            }
        }
    }

    public async Task<SocialMediaEventPhotoAsset?> OpenAsync(Guid eventId, Guid photoId, string size, CancellationToken cancellationToken)
    {
        var normalizedSize = NormalizeSize(size);
        if (normalizedSize is null)
        {
            return null;
        }

        var photo = await _db.SocialMediaEventPhotos.AsNoTracking()
            .FirstOrDefaultAsync(x => x.SocialMediaEventId == eventId && x.Id == photoId, cancellationToken);

        if (photo == null)
        {
            return null;
        }

        var path = GetAssetPath(eventId, photo.Id, normalizedSize);
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var info = new FileInfo(path);
            var contentType = normalizedSize == "original" ? photo.ContentType : "image/jpeg";
            return new SocialMediaEventPhotoAsset(stream, contentType, info.LastWriteTimeUtc);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to open social media event photo asset {Size} for event {EventId} photo {PhotoId}",
                normalizedSize,
                eventId,
                photoId);
            return null;
        }
    }

    private async Task DeletePhysicalAssetsAsync(Guid eventId, Guid photoId, CancellationToken cancellationToken)
    {
        var photoFolder = GetPhotoFolder(eventId, photoId);
        if (!Directory.Exists(photoFolder))
        {
            return;
        }

        await Task.Run(() => Directory.Delete(photoFolder, true), cancellationToken);
    }

    private string GetPhotoFolder(Guid eventId, Guid photoId)
    {
        var eventRoot = _uploadRootProvider.GetSocialMediaRoot(_options.StoragePrefix, eventId);
        return Path.Combine(eventRoot, "photos", photoId.ToString("D"));
    }

    private static string BuildStorageKey(string prefix, Guid eventId, Guid photoId)
    {
        var normalized = string.IsNullOrWhiteSpace(prefix) ? "org/social/{eventId}" : prefix.Trim();
        normalized = normalized.TrimStart('/', '\\').TrimEnd('/', '\\');

        const string token = "{eventId}";
        if (!normalized.Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Storage prefix must contain the token '{token}'.");
        }

        var replaced = normalized.Replace(token, eventId.ToString("D"), StringComparison.OrdinalIgnoreCase);
        return $"{replaced}/photos/{photoId:D}";
    }

    private static string CombineStoragePath(string storageKey, string fileName)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            return fileName;
        }

        return string.Join('/', storageKey.TrimEnd('/', '\\'), fileName);
    }

    private static string NormalizeDerivativeKey(string key)
    {
        return string.IsNullOrWhiteSpace(key) ? "derivative" : key.Trim().ToLowerInvariant();
    }

    private string? NormalizeSize(string? size)
    {
        if (string.IsNullOrWhiteSpace(size))
        {
            return TryResolveDefaultDerivative();
        }

        var normalized = size.Trim().ToLowerInvariant();
        if (normalized == "original")
        {
            return normalized;
        }

        return _options.Derivatives.ContainsKey(normalized) ? normalized : null;
    }

    private string? TryResolveDefaultDerivative()
    {
        if (_options.Derivatives.ContainsKey("thumb"))
        {
            return "thumb";
        }

        if (_options.Derivatives.ContainsKey("feed"))
        {
            return "feed";
        }

        if (_options.Derivatives.ContainsKey("story"))
        {
            return "story";
        }

        return _options.Derivatives.Keys.FirstOrDefault();
    }

    private string? GetAssetPath(Guid eventId, Guid photoId, string size)
    {
        var folder = GetPhotoFolder(eventId, photoId);
        if (!Directory.Exists(folder))
        {
            return null;
        }

        if (size == "original")
        {
            foreach (var candidate in Directory.EnumerateFiles(folder, "original.*"))
            {
                return candidate;
            }

            return null;
        }

        var derivativeName = $"{NormalizeDerivativeKey(size)}.jpg";
        return Path.Combine(folder, derivativeName);
    }

    private static string NormalizeContentType(string? contentType, IImageFormat format)
    {
        if (!string.IsNullOrWhiteSpace(format?.DefaultMimeType))
        {
            return format.DefaultMimeType;
        }

        var firstMime = format?.MimeTypes?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        if (!string.IsNullOrWhiteSpace(firstMime))
        {
            return firstMime!;
        }

        if (!string.IsNullOrWhiteSpace(contentType))
        {
            return contentType.Trim();
        }

        return "application/octet-stream";
    }

    private static string GetExtension(IImageFormat format)
    {
        var extension = format.FileExtensions.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(extension))
        {
            return $".{extension}";
        }

        return ".bin";
    }
}

public sealed record SocialMediaEventPhotoUploadResult(
    SocialMediaEventPhotoUploadOutcome Outcome,
    SocialMediaEventPhoto? Photo,
    IReadOnlyList<string> Errors)
{
    public static SocialMediaEventPhotoUploadResult Success(SocialMediaEventPhoto photo)
        => new(SocialMediaEventPhotoUploadOutcome.Success, photo, Array.Empty<string>());

    public static SocialMediaEventPhotoUploadResult NotFound()
        => new(SocialMediaEventPhotoUploadOutcome.NotFound, null, Array.Empty<string>());

    public static SocialMediaEventPhotoUploadResult TooLarge(long maxBytes)
        => new(SocialMediaEventPhotoUploadOutcome.FileTooLarge, null, new[]
        {
            $"File exceeds the maximum size of {maxBytes / 1024 / 1024} MB."
        });

    public static SocialMediaEventPhotoUploadResult ImageTooSmall(int minWidth, int minHeight)
        => new(SocialMediaEventPhotoUploadOutcome.ImageTooSmall, null, new[]
        {
            $"Image must be at least {minWidth}x{minHeight} pixels."
        });

    public static SocialMediaEventPhotoUploadResult Invalid(string message)
        => new(SocialMediaEventPhotoUploadOutcome.InvalidImage, null, new[] { message });

    public static SocialMediaEventPhotoUploadResult Concurrency()
        => new(SocialMediaEventPhotoUploadOutcome.ConcurrencyConflict, null, new[]
        {
            "The social media event was modified. Please refresh the page and try again."
        });
}

public enum SocialMediaEventPhotoUploadOutcome
{
    Success,
    NotFound,
    FileTooLarge,
    ImageTooSmall,
    InvalidImage,
    ConcurrencyConflict
}

public sealed record SocialMediaEventPhotoDeletionResult(
    SocialMediaEventPhotoDeletionOutcome Outcome,
    IReadOnlyList<string> Errors)
{
    public static SocialMediaEventPhotoDeletionResult Success()
        => new(SocialMediaEventPhotoDeletionOutcome.Success, Array.Empty<string>());

    public static SocialMediaEventPhotoDeletionResult NotFound()
        => new(SocialMediaEventPhotoDeletionOutcome.NotFound, Array.Empty<string>());

    public static SocialMediaEventPhotoDeletionResult Concurrency()
        => new(SocialMediaEventPhotoDeletionOutcome.ConcurrencyConflict, new[]
        {
            "The photo was modified. Please refresh the page and try again."
        });

    public static SocialMediaEventPhotoDeletionResult Failed(string message)
        => new(SocialMediaEventPhotoDeletionOutcome.Failed, new[] { message });
}

public enum SocialMediaEventPhotoDeletionOutcome
{
    Success,
    NotFound,
    ConcurrencyConflict,
    Failed
}

public sealed record SocialMediaEventPhotoSetCoverResult(
    SocialMediaEventPhotoSetCoverOutcome Outcome,
    IReadOnlyList<string> Errors)
{
    public static SocialMediaEventPhotoSetCoverResult Success()
        => new(SocialMediaEventPhotoSetCoverOutcome.Success, Array.Empty<string>());

    public static SocialMediaEventPhotoSetCoverResult NotFound()
        => new(SocialMediaEventPhotoSetCoverOutcome.NotFound, Array.Empty<string>());

    public static SocialMediaEventPhotoSetCoverResult Concurrency()
        => new(SocialMediaEventPhotoSetCoverOutcome.ConcurrencyConflict, new[]
        {
            "The photo was modified. Please refresh the page and try again."
        });

    public static SocialMediaEventPhotoSetCoverResult Failed(string message)
        => new(SocialMediaEventPhotoSetCoverOutcome.Failed, new[] { message });
}

public enum SocialMediaEventPhotoSetCoverOutcome
{
    Success,
    NotFound,
    ConcurrencyConflict,
    Failed
}

public sealed record SocialMediaEventPhotoAsset(Stream Stream, string ContentType, DateTimeOffset LastModifiedUtc);
