using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public sealed class SocialMediaEventService
{
    private static readonly TimeSpan IstOffset = TimeSpan.FromHours(5) + TimeSpan.FromMinutes(30);

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly ISocialMediaEventPhotoService _photoService;

    public SocialMediaEventService(ApplicationDbContext db, IClock clock, ISocialMediaEventPhotoService photoService)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _photoService = photoService ?? throw new ArgumentNullException(nameof(photoService));
    }

    public async Task<IReadOnlyList<SocialMediaEventType>> GetEventTypesAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        var query = _db.SocialMediaEventTypes.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(x => x.IsActive);
        }

        return await query
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }

    public IQueryable<SocialMediaEventListItem> CreateListQuery(SocialMediaEventQueryOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        return CreateFilteredQuery(options)
            .OrderByDescending(x => x.DateOfEvent)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new SocialMediaEventListItem(
                x.Id,
                x.DateOfEvent,
                x.Title,
                x.SocialMediaEventTypeId,
                x.SocialMediaEventType!.Name,
                x.SocialMediaEventType.IsActive,
                x.Platform,
                x.Reach,
                x.Photos.Count,
                x.CoverPhotoId,
                x.CreatedAtUtc,
                x.LastModifiedAtUtc,
                x.RowVersion));
    }

    public async Task<IReadOnlyList<SocialMediaEventListItem>> SearchAsync(SocialMediaEventQueryOptions options, CancellationToken cancellationToken)
    {
        return await CreateListQuery(options).ToListAsync(cancellationToken);
    }

    public IQueryable<SocialMediaEventExportRow> CreateExportQuery(SocialMediaEventQueryOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        return CreateFilteredQuery(options)
            .OrderBy(x => x.DateOfEvent)
            .ThenBy(x => x.CreatedAtUtc)
            .Select(x => new SocialMediaEventExportRow(
                x.DateOfEvent,
                x.SocialMediaEventType!.Name,
                x.Title,
                x.Platform,
                x.Reach,
                x.Photos.Count,
                x.CoverPhotoId.HasValue,
                x.Description));
    }

    public async Task<IReadOnlyList<SocialMediaEventExportRow>> ExportAsync(SocialMediaEventQueryOptions options, CancellationToken cancellationToken)
    {
        return await CreateExportQuery(options).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SocialMediaEventPdfExportRow>> ExportForPdfAsync(SocialMediaEventQueryOptions options, CancellationToken cancellationToken)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        return await CreateFilteredQuery(options)
            .OrderBy(x => x.DateOfEvent)
            .ThenBy(x => x.CreatedAtUtc)
            .Select(x => new SocialMediaEventPdfExportRow(
                x.Id,
                x.DateOfEvent,
                x.SocialMediaEventType!.Name,
                x.Title,
                x.Platform,
                x.Reach,
                x.Photos.Count,
                x.Description,
                x.CoverPhotoId))
            .ToListAsync(cancellationToken);
    }

    public async Task<SocialMediaEventDetails?> GetDetailsAsync(Guid id, CancellationToken cancellationToken)
    {
        var socialEvent = await _db.SocialMediaEvents.AsNoTracking()
            .Include(x => x.SocialMediaEventType)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (socialEvent == null)
        {
            return null;
        }

        if (socialEvent.SocialMediaEventType == null)
        {
            return null;
        }

        var photos = await _photoService.GetPhotosAsync(id, cancellationToken);
        var ordered = photos
            .OrderBy(x => x.CreatedAtUtc.Add(IstOffset))
            .ToList();

        return new SocialMediaEventDetails(socialEvent, socialEvent.SocialMediaEventType, ordered);
    }

    public async Task<SocialMediaEventMutationResult> CreateAsync(
        Guid socialMediaEventTypeId,
        DateOnly dateOfEvent,
        string title,
        string? platform,
        int reach,
        string? description,
        string createdByUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return SocialMediaEventMutationResult.Invalid("Title is required.");
        }

        if (reach < 0)
        {
            return SocialMediaEventMutationResult.Invalid("Reach cannot be negative.");
        }

        var trimmedTitle = title.Trim();
        var trimmedPlatform = string.IsNullOrWhiteSpace(platform) ? null : platform.Trim();
        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        var eventType = await _db.SocialMediaEventTypes.FirstOrDefaultAsync(x => x.Id == socialMediaEventTypeId, cancellationToken);
        if (eventType == null)
        {
            return SocialMediaEventMutationResult.EventTypeNotFound();
        }

        if (!eventType.IsActive)
        {
            return SocialMediaEventMutationResult.EventTypeInactive();
        }

        var now = _clock.UtcNow;
        var entity = new SocialMediaEvent
        {
            Id = Guid.NewGuid(),
            SocialMediaEventTypeId = socialMediaEventTypeId,
            DateOfEvent = dateOfEvent,
            Title = trimmedTitle,
            Platform = trimmedPlatform,
            Reach = reach,
            Description = trimmedDescription,
            CreatedAtUtc = now,
            CreatedByUserId = createdByUserId,
            LastModifiedAtUtc = now,
            LastModifiedByUserId = createdByUserId
        };

        _db.SocialMediaEvents.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return SocialMediaEventMutationResult.Success(entity);
    }

    public async Task<SocialMediaEventMutationResult> UpdateAsync(
        Guid id,
        Guid socialMediaEventTypeId,
        DateOnly dateOfEvent,
        string title,
        string? platform,
        int reach,
        string? description,
        byte[] rowVersion,
        string modifiedByUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rowVersion);

        if (string.IsNullOrWhiteSpace(title))
        {
            return SocialMediaEventMutationResult.Invalid("Title is required.");
        }

        if (reach < 0)
        {
            return SocialMediaEventMutationResult.Invalid("Reach cannot be negative.");
        }

        var trimmedTitle = title.Trim();
        var trimmedPlatform = string.IsNullOrWhiteSpace(platform) ? null : platform.Trim();
        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        var entity = await _db.SocialMediaEvents.Include(x => x.Photos).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity == null)
        {
            return SocialMediaEventMutationResult.NotFound();
        }

        var eventType = await _db.SocialMediaEventTypes.FirstOrDefaultAsync(x => x.Id == socialMediaEventTypeId, cancellationToken);
        if (eventType == null)
        {
            return SocialMediaEventMutationResult.EventTypeNotFound();
        }

        if (!eventType.IsActive)
        {
            return SocialMediaEventMutationResult.EventTypeInactive();
        }

        _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = rowVersion;

        entity.SocialMediaEventTypeId = socialMediaEventTypeId;
        entity.DateOfEvent = dateOfEvent;
        entity.Title = trimmedTitle;
        entity.Platform = trimmedPlatform;
        entity.Reach = reach;
        entity.Description = trimmedDescription;
        entity.LastModifiedAtUtc = _clock.UtcNow;
        entity.LastModifiedByUserId = modifiedByUserId;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return SocialMediaEventMutationResult.Concurrency();
        }

        return SocialMediaEventMutationResult.Success(entity);
    }

    public async Task<SocialMediaEventDeletionResult> DeleteAsync(
        Guid id,
        byte[] rowVersion,
        string deletedByUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rowVersion);

        if (string.IsNullOrWhiteSpace(deletedByUserId))
        {
            throw new ArgumentException("User ID is required.", nameof(deletedByUserId));
        }

        var entity = await _db.SocialMediaEvents.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity == null)
        {
            return SocialMediaEventDeletionResult.NotFound();
        }

        _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = rowVersion;

        var photoSnapshots = await _db.SocialMediaEventPhotos.AsNoTracking()
            .Where(x => x.SocialMediaEventId == id)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new SocialMediaEventPhoto
            {
                Id = x.Id,
                SocialMediaEventId = x.SocialMediaEventId,
                StorageKey = x.StorageKey,
                StoragePath = x.StoragePath,
                ContentType = x.ContentType,
                Width = x.Width,
                Height = x.Height,
                Caption = x.Caption,
                VersionStamp = x.VersionStamp,
                IsCover = x.IsCover,
                CreatedAtUtc = x.CreatedAtUtc,
                CreatedByUserId = x.CreatedByUserId,
                LastModifiedAtUtc = x.LastModifiedAtUtc,
                LastModifiedByUserId = x.LastModifiedByUserId
            })
            .ToListAsync(cancellationToken);

        photoSnapshots = photoSnapshots
            .OrderBy(x => x.CreatedAtUtc.Add(IstOffset))
            .ToList();

        _db.SocialMediaEvents.Remove(entity);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return SocialMediaEventDeletionResult.Concurrency();
        }

        if (photoSnapshots.Count > 0)
        {
            await _photoService.RemoveAllAsync(id, photoSnapshots, cancellationToken);
        }

        return SocialMediaEventDeletionResult.Success(photoSnapshots);
    }

    private IQueryable<SocialMediaEvent> CreateFilteredQuery(SocialMediaEventQueryOptions options)
    {
        var query = _db.SocialMediaEvents.AsNoTracking();

        if (options.EventTypeId.HasValue)
        {
            query = query.Where(x => x.SocialMediaEventTypeId == options.EventTypeId.Value);
        }

        if (options.StartDate.HasValue)
        {
            query = query.Where(x => x.DateOfEvent >= options.StartDate.Value);
        }

        if (options.EndDate.HasValue)
        {
            query = query.Where(x => x.DateOfEvent <= options.EndDate.Value);
        }

        if (!string.IsNullOrWhiteSpace(options.Platform))
        {
            var platform = options.Platform.Trim();
            if (_db.Database.IsNpgsql())
            {
                query = query.Where(x => x.Platform != null && EF.Functions.ILike(x.Platform, platform));
            }
            else
            {
                var normalized = platform.ToLowerInvariant();
                query = query.Where(x => x.Platform != null && x.Platform.ToLower() == normalized);
            }
        }

        if (!string.IsNullOrWhiteSpace(options.SearchQuery))
        {
            var text = options.SearchQuery.Trim();
            if (_db.Database.IsNpgsql())
            {
                query = query.Where(x =>
                    EF.Functions.ILike(x.Title, $"%{text}%") ||
                    (x.Description != null && EF.Functions.ILike(x.Description, $"%{text}%")));
            }
            else
            {
                query = query.Where(x =>
                    x.Title.Contains(text) ||
                    (x.Description != null && x.Description.Contains(text)));
            }
        }

        if (options.OnlyActiveEventTypes)
        {
            query = query.Where(x => x.SocialMediaEventType != null && x.SocialMediaEventType.IsActive);
        }

        return query;
    }
}

public sealed record SocialMediaEventQueryOptions(
    Guid? EventTypeId,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? SearchQuery,
    string? Platform,
    bool OnlyActiveEventTypes = false);

public sealed record SocialMediaEventListItem(
    Guid Id,
    DateOnly DateOfEvent,
    string Title,
    Guid EventTypeId,
    string EventTypeName,
    bool EventTypeIsActive,
    string? Platform,
    int Reach,
    int PhotoCount,
    Guid? CoverPhotoId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastModifiedAtUtc,
    byte[] RowVersion);

public sealed record SocialMediaEventExportRow(
    DateOnly DateOfEvent,
    string EventTypeName,
    string Title,
    string? Platform,
    int Reach,
    int PhotoCount,
    bool HasCoverPhoto,
    string? Description);

public sealed record SocialMediaEventPdfExportRow(
    Guid EventId,
    DateOnly DateOfEvent,
    string EventTypeName,
    string Title,
    string? Platform,
    int Reach,
    int PhotoCount,
    string? Description,
    Guid? CoverPhotoId);

public sealed record SocialMediaEventDetails(
    SocialMediaEvent Event,
    SocialMediaEventType EventType,
    IReadOnlyList<SocialMediaEventPhoto> Photos);

public sealed record SocialMediaEventMutationResult(
    SocialMediaEventMutationOutcome Outcome,
    SocialMediaEvent? Entity,
    IReadOnlyList<string> Errors)
{
    public static SocialMediaEventMutationResult Success(SocialMediaEvent entity)
        => new(SocialMediaEventMutationOutcome.Success, entity, Array.Empty<string>());

    public static SocialMediaEventMutationResult NotFound()
        => new(SocialMediaEventMutationOutcome.NotFound, null, Array.Empty<string>());

    public static SocialMediaEventMutationResult EventTypeNotFound()
        => new(SocialMediaEventMutationOutcome.EventTypeNotFound, null, new[] { "Selected event type could not be found." });

    public static SocialMediaEventMutationResult EventTypeInactive()
        => new(SocialMediaEventMutationOutcome.EventTypeInactive, null, new[] { "The selected event type is inactive." });

    public static SocialMediaEventMutationResult Invalid(string error)
        => new(SocialMediaEventMutationOutcome.Invalid, null, new[] { error });

    public static SocialMediaEventMutationResult Concurrency()
        => new(SocialMediaEventMutationOutcome.ConcurrencyConflict, null, new[] { "The event was modified by another user. Please reload and try again." });
}

public enum SocialMediaEventMutationOutcome
{
    Success,
    NotFound,
    EventTypeNotFound,
    EventTypeInactive,
    Invalid,
    ConcurrencyConflict
}

public sealed record SocialMediaEventDeletionResult(
    SocialMediaEventDeletionOutcome Outcome,
    IReadOnlyList<SocialMediaEventPhoto> Photos)
{
    public static SocialMediaEventDeletionResult Success(IReadOnlyList<SocialMediaEventPhoto> photos)
        => new(SocialMediaEventDeletionOutcome.Success, photos);

    public static SocialMediaEventDeletionResult NotFound()
        => new(SocialMediaEventDeletionOutcome.NotFound, Array.Empty<SocialMediaEventPhoto>());

    public static SocialMediaEventDeletionResult Concurrency()
        => new(SocialMediaEventDeletionOutcome.ConcurrencyConflict, Array.Empty<SocialMediaEventPhoto>());
}

public enum SocialMediaEventDeletionOutcome
{
    Success,
    NotFound,
    ConcurrencyConflict
}
