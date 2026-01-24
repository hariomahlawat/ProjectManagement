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

public interface IVisitPhotoService
{
    Task<IReadOnlyList<VisitPhoto>> GetPhotosAsync(Guid visitId, CancellationToken cancellationToken);

    Task<VisitPhotoUploadResult> UploadAsync(Guid visitId, Stream content, string originalFileName, string? contentType, string? caption, string userId, CancellationToken cancellationToken);

    Task<VisitPhotoDeletionResult> RemoveAsync(Guid visitId, Guid photoId, string userId, CancellationToken cancellationToken);

    Task<VisitPhotoSetCoverResult> SetCoverAsync(Guid visitId, Guid photoId, string userId, CancellationToken cancellationToken);

    Task RemoveAllAsync(Guid visitId, IReadOnlyCollection<VisitPhoto> photos, CancellationToken cancellationToken);

    Task<VisitPhotoAsset?> OpenAsync(Guid visitId, Guid photoId, string size, CancellationToken cancellationToken);
}

public sealed class VisitPhotoService : IVisitPhotoService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly VisitPhotoOptions _options;
    private readonly IUploadRootProvider _uploadRootProvider;
    private readonly ILogger<VisitPhotoService> _logger;
    private readonly IVirusScanner? _virusScanner;

    public VisitPhotoService(ApplicationDbContext db,
                             IClock clock,
                             IAuditService audit,
                             IOptions<VisitPhotoOptions> options,
                             IUploadRootProvider uploadRootProvider,
                             ILogger<VisitPhotoService> logger,
                             IVirusScanner? virusScanner = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _uploadRootProvider = uploadRootProvider ?? throw new ArgumentNullException(nameof(uploadRootProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _virusScanner = virusScanner;
    }

    public async Task<IReadOnlyList<VisitPhoto>> GetPhotosAsync(Guid visitId, CancellationToken cancellationToken)
    {
        return await _db.VisitPhotos.AsNoTracking()
            .Where(x => x.VisitId == visitId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<VisitPhotoUploadResult> UploadAsync(
        Guid visitId,
        Stream content,
        string originalFileName,
        string? contentType,
        string? caption,
        string userId,
        CancellationToken cancellationToken)
    {
        if (content == null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        _ = originalFileName;

        var visit = await _db.Visits
            .Include(x => x.Photos)
            .FirstOrDefaultAsync(x => x.Id == visitId, cancellationToken);

        if (visit == null)
        {
            return VisitPhotoUploadResult.NotFound();
        }

        // SECTION: Concurrency tracking
        _db.Entry(visit).Property(x => x.RowVersion).OriginalValue = visit.RowVersion;

        // SECTION: Stream pre-validation
        if (content.CanSeek)
        {
            if (content.Length == 0)
            {
                return VisitPhotoUploadResult.Invalid("Uploaded file is empty.");
            }

            if (content.Length > _options.MaxFileSizeBytes)
            {
                return VisitPhotoUploadResult.TooLarge(_options.MaxFileSizeBytes);
            }

            content.Position = 0;
        }

        // SECTION: Buffering for scanning and image processing
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);

        if (_virusScanner != null)
        {
            buffer.Position = 0;
            await _virusScanner.ScanAsync(buffer, originalFileName, cancellationToken);
            buffer.Position = 0;
        }

        if (buffer.Length == 0)
        {
            return VisitPhotoUploadResult.Invalid("Uploaded file is empty.");
        }

        if (buffer.Length > _options.MaxFileSizeBytes)
        {
            return VisitPhotoUploadResult.TooLarge(_options.MaxFileSizeBytes);
        }

        // SECTION: Detect type
        buffer.Position = 0;
        IImageFormat? detectedFormat = await Image.DetectFormatAsync(buffer, cancellationToken);
        if (detectedFormat == null)
        {
            return VisitPhotoUploadResult.Invalid("Unsupported image format.");
        }

        var normalizedContentType = NormalizeContentType(contentType, detectedFormat);
        if (!_options.AllowedContentTypes.Contains(normalizedContentType))
        {
            return VisitPhotoUploadResult.Invalid("Only JPEG, PNG, or WebP images are supported.");
        }

        // load the image
        buffer.Position = 0;
        using var sourceImage = await Image.LoadAsync<Rgba32>(buffer, cancellationToken);
        sourceImage.Mutate(x => x.AutoOrient());

        // NOTE: we no longer reject small images.
        // If you still want to know it was small, we log it.
        if (sourceImage.Width < _options.MinWidth || sourceImage.Height < _options.MinHeight)
        {
            _logger.LogInformation(
                "Visit photo for {VisitId} is smaller ({Width}x{Height}) than recommended ({MinW}x{MinH}) but was accepted.",
                visitId,
                sourceImage.Width,
                sourceImage.Height,
                _options.MinWidth,
                _options.MinHeight);
        }

        var now = _clock.UtcNow;
        var photoId = Guid.NewGuid();
        var storageKey = BuildStorageKey(_options.StoragePrefix, visitId, photoId);
        var physicalFolder = GetPhysicalFolder(storageKey);
        Directory.CreateDirectory(physicalFolder);

        // save original
        buffer.Position = 0;
        var originalExtension = GetExtension(detectedFormat);
        var originalPath = Path.Combine(physicalFolder, $"original{originalExtension}");
        await using (var fileStream = File.Create(originalPath))
        {
            buffer.Position = 0;
            await buffer.CopyToAsync(fileStream, cancellationToken);
        }

        // save derivatives
        foreach (var derivative in _options.Derivatives)
        {
            var derivativePath = Path.Combine(physicalFolder, $"{derivative.Key}.jpg");
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

        var photo = new VisitPhoto
        {
            Id = photoId,
            VisitId = visitId,
            StorageKey = storageKey,
            ContentType = normalizedContentType,
            Width = sourceImage.Width,
            Height = sourceImage.Height,
            Caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim(),
            VersionStamp = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = now
        };

        _db.VisitPhotos.Add(photo);
        visit.Photos.Add(photo);
        visit.LastModifiedAtUtc = now;
        visit.LastModifiedByUserId = userId;

        // set first photo as cover
        if (!visit.CoverPhotoId.HasValue)
        {
            visit.CoverPhotoId = photoId;
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
            _logger.LogWarning(ex, "Concurrency conflict while saving photo metadata for visit {VisitId}", visitId);
            _db.Entry(photo).State = EntityState.Detached;
            await DeletePhysicalAssetsAsync(storageKey, cancellationToken);
            return VisitPhotoUploadResult.Invalid("The visit was modified. Please refresh the page and try again.");
        }
        catch (DbUpdateException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Failed to persist visit photo metadata for visit {VisitId}", visitId);
            await DeletePhysicalAssetsAsync(storageKey, cancellationToken);
            var userMessage = VisitPhotoErrorTranslator.GetUserFacingMessage(ex);
            return VisitPhotoUploadResult.Invalid(userMessage);
        }

        await Audit.Events.VisitPhotoAdded(visitId, photoId, userId, visit.CoverPhotoId == photoId).WriteAsync(_audit);
        return VisitPhotoUploadResult.Success(photo);
    }

    public async Task<VisitPhotoDeletionResult> RemoveAsync(Guid visitId, Guid photoId, string userId, CancellationToken cancellationToken)
    {
        var visit = await _db.Visits.Include(x => x.Photos).FirstOrDefaultAsync(x => x.Id == visitId, cancellationToken);
        if (visit == null)
        {
            return VisitPhotoDeletionResult.NotFound();
        }

        var photo = visit.Photos.FirstOrDefault(x => x.Id == photoId);
        if (photo == null)
        {
            return VisitPhotoDeletionResult.NotFound();
        }

        var storageKey = photo.StorageKey;
        visit.Photos.Remove(photo);
        _db.VisitPhotos.Remove(photo);

        if (visit.CoverPhotoId == photoId)
        {
            var nextCover = visit.Photos
                .OrderBy(x => x.CreatedAtUtc)
                .Select(x => x.Id)
                .FirstOrDefault();

            visit.CoverPhotoId = nextCover == Guid.Empty ? null : nextCover;
        }

        visit.LastModifiedAtUtc = _clock.UtcNow;
        visit.LastModifiedByUserId = userId;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to delete visit photo metadata for visit {VisitId} photo {PhotoId}", visitId, photoId);
            return VisitPhotoDeletionResult.Failed();
        }

        await DeletePhysicalAssetsAsync(storageKey, cancellationToken);

        await Audit.Events.VisitPhotoDeleted(visitId, photoId, userId).WriteAsync(_audit);
        return VisitPhotoDeletionResult.Success();
    }

    public async Task<VisitPhotoSetCoverResult> SetCoverAsync(Guid visitId, Guid photoId, string userId, CancellationToken cancellationToken)
    {
        var visit = await _db.Visits.Include(x => x.Photos).FirstOrDefaultAsync(x => x.Id == visitId, cancellationToken);
        if (visit == null)
        {
            return VisitPhotoSetCoverResult.NotFound();
        }

        if (visit.Photos.All(x => x.Id != photoId))
        {
            return VisitPhotoSetCoverResult.NotFound();
        }

        if (visit.CoverPhotoId == photoId)
        {
            return VisitPhotoSetCoverResult.Success();
        }

        visit.CoverPhotoId = photoId;
        visit.LastModifiedAtUtc = _clock.UtcNow;
        visit.LastModifiedByUserId = userId;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to set cover photo for visit {VisitId} photo {PhotoId}", visitId, photoId);
            return VisitPhotoSetCoverResult.Failed();
        }

        await Audit.Events.VisitCoverPhotoChanged(visitId, photoId, userId).WriteAsync(_audit);
        return VisitPhotoSetCoverResult.Success();
    }

    public async Task RemoveAllAsync(Guid visitId, IReadOnlyCollection<VisitPhoto> photos, CancellationToken cancellationToken)
    {
        foreach (var photo in photos)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await DeletePhysicalAssetsAsync(photo.StorageKey, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete visit photo assets for visit {VisitId} photo {PhotoId}", visitId, photo.Id);
            }
        }
    }

    public async Task<VisitPhotoAsset?> OpenAsync(Guid visitId, Guid photoId, string size, CancellationToken cancellationToken)
    {
        var normalized = NormalizeSize(size);
        if (normalized is null)
        {
            return null;
        }

        var photo = await _db.VisitPhotos.AsNoTracking()
            .FirstOrDefaultAsync(x => x.VisitId == visitId && x.Id == photoId, cancellationToken);

        if (photo == null)
        {
            return null;
        }

        var path = GetAssetPath(photo.StorageKey, normalized);
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var info = new FileInfo(path);
            var contentType = normalized == "original" ? photo.ContentType : "image/jpeg";
            return new VisitPhotoAsset(stream, contentType, info.LastWriteTimeUtc);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open visit photo asset {Size} for visit {VisitId} photo {PhotoId}", normalized, visitId, photoId);
            return null;
        }
    }

    private async Task DeletePhysicalAssetsAsync(string storageKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            return;
        }

        var physicalFolder = GetPhysicalFolder(storageKey);
        if (!Directory.Exists(physicalFolder))
        {
            return;
        }

        await Task.Run(() => Directory.Delete(physicalFolder, true), cancellationToken);
    }

    private string GetPhysicalFolder(string storageKey)
    {
        var relative = storageKey.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(_uploadRootProvider.RootPath, relative);
    }


    private static string BuildStorageKey(string prefix, Guid visitId, Guid photoId)
    {
        prefix = string.IsNullOrWhiteSpace(prefix) ? "project-office-reports/visits" : prefix.TrimEnd('/');
        return $"{prefix}/{visitId:D}/photos/{photoId:D}";
    }

    private static string? NormalizeSize(string? size)
    {
        if (string.IsNullOrWhiteSpace(size))
        {
            return "sm";
        }

        var normalized = size.Trim().ToLowerInvariant();
        return normalized is "xs" or "sm" or "md" or "xl" or "original" ? normalized : null;
    }

    private string? GetAssetPath(string storageKey, string size)
    {
        var folder = GetPhysicalFolder(storageKey);
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

        return Path.Combine(folder, $"{size}.jpg");
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

public sealed record VisitPhotoUploadResult(VisitPhotoUploadOutcome Outcome, VisitPhoto? Photo, IReadOnlyList<string> Errors)
{
    public static VisitPhotoUploadResult Success(VisitPhoto photo) => new(VisitPhotoUploadOutcome.Success, photo, Array.Empty<string>());

    public static VisitPhotoUploadResult NotFound() => new(VisitPhotoUploadOutcome.NotFound, null, Array.Empty<string>());

    public static VisitPhotoUploadResult TooLarge(long maxBytes) => new(VisitPhotoUploadOutcome.FileTooLarge, null, new[] { $"File exceeds the maximum size of {maxBytes / 1024 / 1024} MB." });

    public static VisitPhotoUploadResult ImageTooSmall(int minWidth, int minHeight) => new(VisitPhotoUploadOutcome.ImageTooSmall, null, new[] { $"Image must be at least {minWidth}x{minHeight} pixels." });

    public static VisitPhotoUploadResult Invalid(string message) => new(VisitPhotoUploadOutcome.InvalidImage, null, new[] { message });
}

public enum VisitPhotoUploadOutcome
{
    Success,
    NotFound,
    FileTooLarge,
    ImageTooSmall,
    InvalidImage
}

public sealed record VisitPhotoDeletionResult(VisitPhotoDeletionOutcome Outcome, IReadOnlyList<string> Errors)
{
    public static VisitPhotoDeletionResult Success() => new(VisitPhotoDeletionOutcome.Success, Array.Empty<string>());

    public static VisitPhotoDeletionResult NotFound() => new(VisitPhotoDeletionOutcome.NotFound, Array.Empty<string>());

    public static VisitPhotoDeletionResult Failed() => new(VisitPhotoDeletionOutcome.Failed, new[] { "Unable to delete the photo. Please try again." });
}

public enum VisitPhotoDeletionOutcome
{
    Success,
    NotFound,
    Failed
}

public sealed record VisitPhotoSetCoverResult(VisitPhotoSetCoverOutcome Outcome)
{
    public static VisitPhotoSetCoverResult Success() => new(VisitPhotoSetCoverOutcome.Success);

    public static VisitPhotoSetCoverResult NotFound() => new(VisitPhotoSetCoverOutcome.NotFound);

    public static VisitPhotoSetCoverResult Failed() => new(VisitPhotoSetCoverOutcome.Failed);
}

public enum VisitPhotoSetCoverOutcome
{
    Success,
    NotFound,
    Failed
}

public sealed record VisitPhotoAsset(Stream Stream, string ContentType, DateTimeOffset LastModifiedUtc);
