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
using ProjectManagement.Services;
using ProjectManagement.Services.Storage;
using ProjectManagement.Utilities;
using SixLabors.ImageSharp;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public interface IMiscActivityService
{
    Task<IReadOnlyList<MiscActivityListItem>> SearchAsync(MiscActivityQueryOptions options, CancellationToken cancellationToken);

    Task<IReadOnlyList<MiscActivityExportRow>> ExportAsync(MiscActivityQueryOptions options, CancellationToken cancellationToken);

    Task<MiscActivity?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<MiscActivityMutationResult> CreateAsync(MiscActivityCreateRequest request, CancellationToken cancellationToken);

    Task<MiscActivityMutationResult> UpdateAsync(Guid id, MiscActivityUpdateRequest request, CancellationToken cancellationToken);

    Task<MiscActivityDeletionResult> DeleteAsync(Guid id, byte[] rowVersion, CancellationToken cancellationToken);

    Task<ActivityMediaUploadResult> UploadMediaAsync(ActivityMediaUploadRequest request, CancellationToken cancellationToken);

    Task<ActivityMediaDeletionResult> DeleteMediaAsync(ActivityMediaDeletionRequest request, CancellationToken cancellationToken);
}

public sealed class MiscActivityService : IMiscActivityService
{
    private static readonly string[] ManagerRoles =
    {
        "Admin",
        "HoD",
        "ProjectOffice",
        "Project Office",
        "Project Officer",
        "TA"
    };

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly IUserContext _userContext;
    private readonly IUploadRootProvider _uploadRootProvider;
    private readonly MiscActivityMediaOptions _options;
    private readonly ILogger<MiscActivityService>? _logger;

    public MiscActivityService(
        ApplicationDbContext db,
        IClock clock,
        IAuditService audit,
        IUserContext userContext,
        IUploadRootProvider uploadRootProvider,
        IOptions<MiscActivityMediaOptions> options,
        ILogger<MiscActivityService>? logger = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
        _uploadRootProvider = uploadRootProvider ?? throw new ArgumentNullException(nameof(uploadRootProvider));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
    }

    public async Task<IReadOnlyList<MiscActivityListItem>> SearchAsync(MiscActivityQueryOptions options, CancellationToken cancellationToken)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var query = CreateFilteredQuery(options);
        query = ApplySorting(query, options);

        return await query
            .Select(x => new MiscActivityListItem(
                x.Id,
                x.ActivityTypeId,
                x.ActivityType != null ? x.ActivityType.Name : null,
                x.Nomenclature,
                x.OccurrenceDate,
                x.Description,
                x.ExternalLink,
                x.Media.Count,
                x.DeletedUtc != null,
                x.CapturedAtUtc,
                x.CapturedByUserId,
                x.LastModifiedAtUtc,
                x.LastModifiedByUserId,
                x.RowVersion))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MiscActivityExportRow>> ExportAsync(MiscActivityQueryOptions options, CancellationToken cancellationToken)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var query = CreateFilteredQuery(options);
        query = ApplySorting(query, options with { SortDescending = false });

        return await query
            .Select(x => new MiscActivityExportRow(
                x.OccurrenceDate,
                x.Nomenclature,
                x.ActivityType != null ? x.ActivityType.Name : null,
                x.Description,
                x.ExternalLink,
                x.Media.Count))
            .ToListAsync(cancellationToken);
    }

    public async Task<MiscActivity?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var activity = await _db.MiscActivities.AsNoTracking()
            .Include(x => x.ActivityType)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (activity == null)
        {
            return null;
        }

        var media = await _db.ActivityMedia.AsNoTracking()
            .Where(m => m.ActivityId == id)
            .OrderBy(m => m.UploadedAtUtc)
            .ToListAsync(cancellationToken);

        activity.Media = media;
        return activity;
    }

    public async Task<MiscActivityMutationResult> CreateAsync(MiscActivityCreateRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (!TryGetManagerUserId(out var userId))
        {
            return MiscActivityMutationResult.Unauthorized();
        }

        var validationError = ValidateMutation(request.Nomenclature, request.Description, request.ExternalLink, request.OccurrenceDate);
        if (validationError != null)
        {
            return MiscActivityMutationResult.Invalid(validationError);
        }

        ActivityType? activityType = null;
        if (request.ActivityTypeId.HasValue)
        {
            activityType = await _db.ActivityTypes.FirstOrDefaultAsync(x => x.Id == request.ActivityTypeId.Value, cancellationToken);
            if (activityType == null)
            {
                return MiscActivityMutationResult.ActivityTypeNotFound();
            }

            if (!activityType.IsActive)
            {
                return MiscActivityMutationResult.ActivityTypeInactive();
            }
        }

        var now = _clock.UtcNow;
        var entity = new MiscActivity
        {
            Id = Guid.NewGuid(),
            ActivityTypeId = request.ActivityTypeId,
            Nomenclature = request.Nomenclature.Trim(),
            OccurrenceDate = request.OccurrenceDate,
            Description = NormalizeNullable(request.Description),
            ExternalLink = NormalizeNullable(request.ExternalLink),
            CapturedAtUtc = now,
            CapturedByUserId = userId,
            LastModifiedAtUtc = now,
            LastModifiedByUserId = userId
        };

        _db.MiscActivities.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        await Audit.Events.MiscActivityCreated(entity.Id, entity.Nomenclature, userId).WriteAsync(_audit);

        return MiscActivityMutationResult.Success(entity, entity.RowVersion);
    }

    public async Task<MiscActivityMutationResult> UpdateAsync(Guid id, MiscActivityUpdateRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (!TryGetManagerUserId(out var userId))
        {
            return MiscActivityMutationResult.Unauthorized();
        }

        var validationError = ValidateMutation(request.Nomenclature, request.Description, request.ExternalLink, request.OccurrenceDate);
        if (validationError != null)
        {
            return MiscActivityMutationResult.Invalid(validationError);
        }

        var entity = await _db.MiscActivities.Include(x => x.ActivityType).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity == null)
        {
            return MiscActivityMutationResult.NotFound();
        }

        if (entity.DeletedUtc != null)
        {
            return MiscActivityMutationResult.Deleted();
        }

        ActivityType? activityType = null;
        if (request.ActivityTypeId.HasValue)
        {
            activityType = await _db.ActivityTypes.FirstOrDefaultAsync(x => x.Id == request.ActivityTypeId.Value, cancellationToken);
            if (activityType == null)
            {
                return MiscActivityMutationResult.ActivityTypeNotFound();
            }

            if (!activityType.IsActive)
            {
                return MiscActivityMutationResult.ActivityTypeInactive();
            }
        }

        _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = request.RowVersion;

        entity.ActivityTypeId = request.ActivityTypeId;
        entity.Nomenclature = request.Nomenclature.Trim();
        entity.OccurrenceDate = request.OccurrenceDate;
        entity.Description = NormalizeNullable(request.Description);
        entity.ExternalLink = NormalizeNullable(request.ExternalLink);
        entity.LastModifiedAtUtc = _clock.UtcNow;
        entity.LastModifiedByUserId = userId;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return MiscActivityMutationResult.Concurrency();
        }

        await Audit.Events.MiscActivityUpdated(entity.Id, entity.Nomenclature, userId).WriteAsync(_audit);
        return MiscActivityMutationResult.Success(entity, entity.RowVersion);
    }

    public async Task<MiscActivityDeletionResult> DeleteAsync(Guid id, byte[] rowVersion, CancellationToken cancellationToken)
    {
        if (!TryGetManagerUserId(out var userId))
        {
            return MiscActivityDeletionResult.Unauthorized();
        }

        var entity = await _db.MiscActivities.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity == null)
        {
            return MiscActivityDeletionResult.NotFound();
        }

        if (entity.DeletedUtc != null)
        {
            return MiscActivityDeletionResult.AlreadyDeleted();
        }

        _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = rowVersion;

        var now = _clock.UtcNow;
        entity.DeletedUtc = now;
        entity.DeletedByUserId = userId;
        entity.LastModifiedAtUtc = now;
        entity.LastModifiedByUserId = userId;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return MiscActivityDeletionResult.Concurrency();
        }

        await Audit.Events.MiscActivityDeleted(entity.Id, userId).WriteAsync(_audit);
        return MiscActivityDeletionResult.Success();
    }

    public async Task<ActivityMediaUploadResult> UploadMediaAsync(ActivityMediaUploadRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (!TryGetManagerUserId(out var userId))
        {
            return ActivityMediaUploadResult.Unauthorized();
        }

        if (request.Content == null)
        {
            throw new ArgumentNullException(nameof(request.Content));
        }

        if (string.IsNullOrWhiteSpace(request.OriginalFileName))
        {
            return ActivityMediaUploadResult.Invalid("A file name is required.");
        }

        var caption = string.IsNullOrWhiteSpace(request.Caption) ? null : request.Caption.Trim();
        if (caption != null && caption.Length > 256)
        {
            return ActivityMediaUploadResult.Invalid("Caption cannot exceed 256 characters.");
        }

        var activity = await _db.MiscActivities.Include(x => x.Media)
            .FirstOrDefaultAsync(x => x.Id == request.ActivityId, cancellationToken);
        if (activity == null)
        {
            return ActivityMediaUploadResult.ActivityNotFound();
        }

        if (activity.DeletedUtc != null)
        {
            return ActivityMediaUploadResult.ActivityDeleted();
        }

        _db.Entry(activity).Property(x => x.RowVersion).OriginalValue = request.ActivityRowVersion;

        if (request.Content.CanSeek)
        {
            request.Content.Position = 0;
        }

        var sanitizedName = FileNameSanitizer.Sanitize(request.OriginalFileName);
        var normalizedContentType = NormalizeContentType(request.ContentType, sanitizedName);
        if (normalizedContentType == null)
        {
            return ActivityMediaUploadResult.Invalid("Unable to determine the file type.");
        }

        if (!_options.AllowedContentTypes.Contains(normalizedContentType, StringComparer.OrdinalIgnoreCase))
        {
            return ActivityMediaUploadResult.UnsupportedType(_options.AllowedContentTypes);
        }

        var mediaId = Guid.NewGuid();
        var storageKey = BuildStorageKey(activity.Id, mediaId, sanitizedName);
        var absolutePath = ResolvePhysicalPath(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        long totalBytes = 0;
        var buffer = new byte[81920];
        var exceeded = false;

        try
        {
            await using var destination = new FileStream(absolutePath, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, useAsync: true);
            while (true)
            {
                var read = await request.Content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read <= 0)
                {
                    break;
                }

                totalBytes += read;
                if (totalBytes > _options.MaxFileSizeBytes)
                {
                    exceeded = true;
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            await destination.FlushAsync(cancellationToken);
        }
        catch
        {
            SafeDelete(absolutePath);
            throw;
        }

        if (exceeded)
        {
            SafeDelete(absolutePath);
            return ActivityMediaUploadResult.TooLarge(_options.MaxFileSizeBytes);
        }

        if (totalBytes == 0)
        {
            SafeDelete(absolutePath);
            return ActivityMediaUploadResult.Invalid("Uploaded file is empty.");
        }

        int? width = null;
        int? height = null;
        if (normalizedContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await using var imageStream = File.OpenRead(absolutePath);
                var info = await Image.IdentifyAsync(imageStream, cancellationToken);
                if (info != null)
                {
                    width = info.Width;
                    height = info.Height;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to read image metadata for activity {ActivityId}.", activity.Id);
            }
        }

        var now = _clock.UtcNow;
        var media = new ActivityMedia
        {
            Id = mediaId,
            ActivityId = activity.Id,
            StorageKey = storageKey,
            OriginalFileName = sanitizedName,
            MediaType = normalizedContentType,
            FileSize = totalBytes,
            Caption = caption,
            Width = width,
            Height = height,
            UploadedAtUtc = now,
            UploadedByUserId = userId
        };

        activity.LastModifiedAtUtc = now;
        activity.LastModifiedByUserId = userId;
        activity.Media.Add(media);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            SafeDelete(absolutePath);
            return ActivityMediaUploadResult.Concurrency();
        }

        await Audit.Events.ActivityMediaUploaded(activity.Id, media.Id, userId, sanitizedName).WriteAsync(_audit);
        return ActivityMediaUploadResult.Success(media, activity.RowVersion);
    }

    public async Task<ActivityMediaDeletionResult> DeleteMediaAsync(ActivityMediaDeletionRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (!TryGetManagerUserId(out var userId))
        {
            return ActivityMediaDeletionResult.Unauthorized();
        }

        var activity = await _db.MiscActivities.Include(x => x.Media)
            .FirstOrDefaultAsync(x => x.Id == request.ActivityId, cancellationToken);

        if (activity == null)
        {
            return ActivityMediaDeletionResult.ActivityNotFound();
        }

        if (activity.DeletedUtc != null)
        {
            return ActivityMediaDeletionResult.ActivityDeleted();
        }

        var media = activity.Media.FirstOrDefault(x => x.Id == request.MediaId);
        if (media == null)
        {
            return ActivityMediaDeletionResult.MediaNotFound();
        }

        _db.Entry(activity).Property(x => x.RowVersion).OriginalValue = request.ActivityRowVersion;
        _db.Entry(media).Property(x => x.RowVersion).OriginalValue = request.MediaRowVersion;

        var absolutePath = ResolvePhysicalPath(media.StorageKey);
        activity.Media.Remove(media);
        _db.ActivityMedia.Remove(media);

        var now = _clock.UtcNow;
        activity.LastModifiedAtUtc = now;
        activity.LastModifiedByUserId = userId;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ActivityMediaDeletionResult.Concurrency();
        }

        SafeDelete(absolutePath);
        await Audit.Events.ActivityMediaDeleted(activity.Id, media.Id, userId).WriteAsync(_audit);
        return ActivityMediaDeletionResult.Success(activity.RowVersion);
    }

    private IQueryable<MiscActivity> CreateFilteredQuery(MiscActivityQueryOptions options)
    {
        var query = _db.MiscActivities.AsNoTracking().Include(x => x.ActivityType).Include(x => x.Media);

        if (!options.IncludeDeleted)
        {
            query = query.Where(x => x.DeletedUtc == null);
        }

        if (options.ActivityTypeId.HasValue)
        {
            var typeId = options.ActivityTypeId.Value;
            query = query.Where(x => x.ActivityTypeId == typeId);
        }

        if (options.StartDate.HasValue)
        {
            var start = options.StartDate.Value;
            query = query.Where(x => x.OccurrenceDate >= start);
        }

        if (options.EndDate.HasValue)
        {
            var end = options.EndDate.Value;
            query = query.Where(x => x.OccurrenceDate <= end);
        }

        if (!string.IsNullOrWhiteSpace(options.SearchText))
        {
            var text = options.SearchText.Trim();
            if (_db.Database.IsNpgsql())
            {
                query = query.Where(x =>
                    EF.Functions.ILike(x.Nomenclature, $"%{text}%") ||
                    (x.Description != null && EF.Functions.ILike(x.Description, $"%{text}%")));
            }
            else
            {
                query = query.Where(x =>
                    x.Nomenclature.Contains(text) ||
                    (x.Description != null && x.Description.Contains(text)));
            }
        }

        return query;
    }

    private static IQueryable<MiscActivity> ApplySorting(IQueryable<MiscActivity> query, MiscActivityQueryOptions options)
    {
        return options.SortField switch
        {
            MiscActivitySortField.Nomenclature => options.SortDescending
                ? query.OrderByDescending(x => x.Nomenclature).ThenByDescending(x => x.OccurrenceDate)
                : query.OrderBy(x => x.Nomenclature).ThenBy(x => x.OccurrenceDate),
            MiscActivitySortField.CreatedAt => options.SortDescending
                ? query.OrderByDescending(x => x.CapturedAtUtc).ThenByDescending(x => x.OccurrenceDate)
                : query.OrderBy(x => x.CapturedAtUtc).ThenBy(x => x.OccurrenceDate),
            _ => options.SortDescending
                ? query.OrderByDescending(x => x.OccurrenceDate).ThenByDescending(x => x.CapturedAtUtc)
                : query.OrderBy(x => x.OccurrenceDate).ThenBy(x => x.CapturedAtUtc)
        };
    }

    private bool TryGetManagerUserId(out string userId)
    {
        userId = string.Empty;
        var principal = _userContext.User;
        if (principal == null)
        {
            return false;
        }

        if (!ManagerRoles.Any(principal.IsInRole))
        {
            return false;
        }

        var value = _userContext.UserId;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        userId = value;
        return true;
    }

    private string? ValidateMutation(string nomenclature, string? description, string? externalLink, DateOnly occurrenceDate)
    {
        if (string.IsNullOrWhiteSpace(nomenclature))
        {
            return "Nomenclature is required.";
        }

        if (nomenclature.Trim().Length > 256)
        {
            return "Nomenclature cannot exceed 256 characters.";
        }

        var today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        if (occurrenceDate > today)
        {
            return "Occurrence date cannot be in the future.";
        }

        if (!string.IsNullOrWhiteSpace(description) && description.Trim().Length > 4000)
        {
            return "Description cannot exceed 4000 characters.";
        }

        if (!string.IsNullOrWhiteSpace(externalLink) && externalLink.Trim().Length > 1024)
        {
            return "External link cannot exceed 1024 characters.";
        }

        return null;
    }

    private static string? NormalizeNullable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private string BuildStorageKey(Guid activityId, Guid mediaId, string fileName)
    {
        var prefix = string.IsNullOrWhiteSpace(_options.StoragePrefix)
            ? "project-office/misc-activities"
            : _options.StoragePrefix.Trim().Trim('/', '\\');

        return $"{prefix}/{activityId:D}/{mediaId:D}/{fileName}";
    }

    private string ResolvePhysicalPath(string storageKey)
    {
        var relative = storageKey
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        return Path.Combine(_uploadRootProvider.RootPath, relative);
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static string? NormalizeContentType(string? contentType, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            return contentType.Trim().ToLowerInvariant();
        }

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            _ => null
        };
    }
}

public sealed record MiscActivityQueryOptions(
    Guid? ActivityTypeId,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? SearchText,
    bool IncludeDeleted,
    MiscActivitySortField SortField = MiscActivitySortField.OccurrenceDate,
    bool SortDescending = true);

public enum MiscActivitySortField
{
    OccurrenceDate,
    CreatedAt,
    Nomenclature
}

public sealed record MiscActivityListItem(
    Guid Id,
    Guid? ActivityTypeId,
    string? ActivityTypeName,
    string Nomenclature,
    DateOnly OccurrenceDate,
    string? Description,
    string? ExternalLink,
    int MediaCount,
    bool IsDeleted,
    DateTimeOffset CapturedAtUtc,
    string CapturedByUserId,
    DateTimeOffset? LastModifiedAtUtc,
    string? LastModifiedByUserId,
    byte[] RowVersion);

public sealed record MiscActivityExportRow(
    DateOnly OccurrenceDate,
    string Nomenclature,
    string? ActivityTypeName,
    string? Description,
    string? ExternalLink,
    int MediaCount);

public sealed record MiscActivityCreateRequest(
    Guid? ActivityTypeId,
    DateOnly OccurrenceDate,
    string Nomenclature,
    string? Description,
    string? ExternalLink);

public sealed record MiscActivityUpdateRequest(
    Guid? ActivityTypeId,
    DateOnly OccurrenceDate,
    string Nomenclature,
    string? Description,
    string? ExternalLink,
    byte[] RowVersion);

public sealed record MiscActivityMutationResult(
    MiscActivityMutationOutcome Outcome,
    MiscActivity? Entity,
    byte[]? RowVersion,
    IReadOnlyList<string> Errors)
{
    public static MiscActivityMutationResult Success(MiscActivity entity, byte[] rowVersion)
        => new(MiscActivityMutationOutcome.Success, entity, rowVersion, Array.Empty<string>());

    public static MiscActivityMutationResult Unauthorized()
        => new(MiscActivityMutationOutcome.Unauthorized, null, null, new[] { "You are not allowed to manage miscellaneous activities." });

    public static MiscActivityMutationResult Invalid(string message)
        => new(MiscActivityMutationOutcome.Invalid, null, null, new[] { message });

    public static MiscActivityMutationResult NotFound()
        => new(MiscActivityMutationOutcome.NotFound, null, null, new[] { "The requested activity could not be found." });

    public static MiscActivityMutationResult Deleted()
        => new(MiscActivityMutationOutcome.Deleted, null, null, new[] { "The activity has already been deleted." });

    public static MiscActivityMutationResult ActivityTypeNotFound()
        => new(MiscActivityMutationOutcome.ActivityTypeNotFound, null, null, new[] { "The selected activity type could not be found." });

    public static MiscActivityMutationResult ActivityTypeInactive()
        => new(MiscActivityMutationOutcome.ActivityTypeInactive, null, null, new[] { "The selected activity type is inactive." });

    public static MiscActivityMutationResult Concurrency()
        => new(MiscActivityMutationOutcome.ConcurrencyConflict, null, null, new[] { "The activity was modified by another user. Please reload and try again." });
}

public enum MiscActivityMutationOutcome
{
    Success,
    Unauthorized,
    Invalid,
    NotFound,
    Deleted,
    ActivityTypeNotFound,
    ActivityTypeInactive,
    ConcurrencyConflict
}

public sealed record MiscActivityDeletionResult(MiscActivityDeletionOutcome Outcome, IReadOnlyList<string> Errors)
{
    public static MiscActivityDeletionResult Success()
        => new(MiscActivityDeletionOutcome.Success, Array.Empty<string>());

    public static MiscActivityDeletionResult Unauthorized()
        => new(MiscActivityDeletionOutcome.Unauthorized, new[] { "You are not allowed to manage miscellaneous activities." });

    public static MiscActivityDeletionResult NotFound()
        => new(MiscActivityDeletionOutcome.NotFound, new[] { "The requested activity could not be found." });

    public static MiscActivityDeletionResult AlreadyDeleted()
        => new(MiscActivityDeletionOutcome.AlreadyDeleted, new[] { "The activity has already been deleted." });

    public static MiscActivityDeletionResult Concurrency()
        => new(MiscActivityDeletionOutcome.ConcurrencyConflict, new[] { "The activity was modified by another user. Please reload and try again." });
}

public enum MiscActivityDeletionOutcome
{
    Success,
    Unauthorized,
    NotFound,
    AlreadyDeleted,
    ConcurrencyConflict
}

public sealed record ActivityMediaUploadRequest(
    Guid ActivityId,
    byte[] ActivityRowVersion,
    Stream Content,
    string OriginalFileName,
    string? ContentType,
    string? Caption);

public sealed record ActivityMediaUploadResult(
    ActivityMediaUploadOutcome Outcome,
    ActivityMedia? Media,
    byte[]? ActivityRowVersion,
    IReadOnlyList<string> Errors)
{
    public static ActivityMediaUploadResult Success(ActivityMedia media, byte[] activityRowVersion)
        => new(ActivityMediaUploadOutcome.Success, media, activityRowVersion, Array.Empty<string>());

    public static ActivityMediaUploadResult Unauthorized()
        => new(ActivityMediaUploadOutcome.Unauthorized, null, null, new[] { "You are not allowed to manage miscellaneous activities." });

    public static ActivityMediaUploadResult ActivityNotFound()
        => new(ActivityMediaUploadOutcome.ActivityNotFound, null, null, new[] { "The activity could not be found." });

    public static ActivityMediaUploadResult ActivityDeleted()
        => new(ActivityMediaUploadOutcome.ActivityDeleted, null, null, new[] { "The activity has already been deleted." });

    public static ActivityMediaUploadResult Invalid(string message)
        => new(ActivityMediaUploadOutcome.Invalid, null, null, new[] { message });

    public static ActivityMediaUploadResult TooLarge(long limitBytes)
        => new(ActivityMediaUploadOutcome.TooLarge, null, null, new[] { $"Files cannot exceed {limitBytes} bytes." });

    public static ActivityMediaUploadResult UnsupportedType(IEnumerable<string> allowed)
        => new(ActivityMediaUploadOutcome.UnsupportedType, null, null, new[] { $"Only the following content types are allowed: {string.Join(", ", allowed)}." });

    public static ActivityMediaUploadResult Concurrency()
        => new(ActivityMediaUploadOutcome.ConcurrencyConflict, null, null, new[] { "The activity was modified by another user. Please reload and try again." });
}

public enum ActivityMediaUploadOutcome
{
    Success,
    Unauthorized,
    ActivityNotFound,
    ActivityDeleted,
    Invalid,
    TooLarge,
    UnsupportedType,
    ConcurrencyConflict
}

public sealed record ActivityMediaDeletionRequest(
    Guid ActivityId,
    Guid MediaId,
    byte[] ActivityRowVersion,
    byte[] MediaRowVersion);

public sealed record ActivityMediaDeletionResult(
    ActivityMediaDeletionOutcome Outcome,
    byte[]? ActivityRowVersion,
    IReadOnlyList<string> Errors)
{
    public static ActivityMediaDeletionResult Success(byte[] activityRowVersion)
        => new(ActivityMediaDeletionOutcome.Success, activityRowVersion, Array.Empty<string>());

    public static ActivityMediaDeletionResult Unauthorized()
        => new(ActivityMediaDeletionOutcome.Unauthorized, null, new[] { "You are not allowed to manage miscellaneous activities." });

    public static ActivityMediaDeletionResult ActivityNotFound()
        => new(ActivityMediaDeletionOutcome.ActivityNotFound, null, new[] { "The activity could not be found." });

    public static ActivityMediaDeletionResult ActivityDeleted()
        => new(ActivityMediaDeletionOutcome.ActivityDeleted, null, new[] { "The activity has already been deleted." });

    public static ActivityMediaDeletionResult MediaNotFound()
        => new(ActivityMediaDeletionOutcome.MediaNotFound, null, new[] { "The media item could not be found." });

    public static ActivityMediaDeletionResult Concurrency()
        => new(ActivityMediaDeletionOutcome.ConcurrencyConflict, null, new[] { "The activity or media item was modified by another user. Please reload and try again." });
}

public enum ActivityMediaDeletionOutcome
{
    Success,
    Unauthorized,
    ActivityNotFound,
    ActivityDeleted,
    MediaNotFound,
    ConcurrencyConflict
}
