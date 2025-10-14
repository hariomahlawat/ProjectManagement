using System;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;

namespace ProjectManagement.Tests;

internal static class SocialMediaTestData
{
    public static SocialMediaEventType CreateEventType(
        Guid? id = null,
        string? name = null,
        string? description = null,
        bool isActive = true,
        DateTimeOffset? createdAtUtc = null,
        string createdByUserId = "seed")
    {
        var typeId = id ?? Guid.NewGuid();
        return new SocialMediaEventType
        {
            Id = typeId,
            Name = name ?? "Campaign Launch",
            Description = description ?? "Default description.",
            IsActive = isActive,
            CreatedAtUtc = createdAtUtc ?? new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            CreatedByUserId = createdByUserId,
            LastModifiedAtUtc = createdAtUtc ?? DateTimeOffset.UtcNow,
            LastModifiedByUserId = createdByUserId,
            RowVersion = Guid.NewGuid().ToByteArray()
        };
    }

    public static SocialMediaEvent CreateEvent(
        Guid eventTypeId,
        Guid? id = null,
        DateOnly? dateOfEvent = null,
        string? title = null,
        string? platform = "Instagram",
        int reach = 500,
        string? description = null,
        Guid? coverPhotoId = null,
        DateTimeOffset? timestamp = null,
        string createdByUserId = "seed")
    {
        var createdAt = timestamp ?? new DateTimeOffset(2024, 4, 1, 8, 0, 0, TimeSpan.Zero);
        var entityId = id ?? Guid.NewGuid();
        return new SocialMediaEvent
        {
            Id = entityId,
            SocialMediaEventTypeId = eventTypeId,
            DateOfEvent = dateOfEvent ?? new DateOnly(2024, 4, 15),
            Title = title ?? "Launch Day", 
            Platform = platform,
            Reach = reach,
            Description = description ?? "Highlights from the launch.",
            CoverPhotoId = coverPhotoId,
            CreatedAtUtc = createdAt,
            CreatedByUserId = createdByUserId,
            LastModifiedAtUtc = createdAt,
            LastModifiedByUserId = createdByUserId,
            RowVersion = Guid.NewGuid().ToByteArray()
        };
    }

    public static SocialMediaEventPhoto CreatePhoto(
        Guid eventId,
        Guid? id = null,
        string? storageKey = null,
        string? storagePath = null,
        string contentType = "image/jpeg",
        int width = 1200,
        int height = 900,
        string? caption = null,
        bool isCover = false,
        string createdByUserId = "seed",
        DateTimeOffset? createdAtUtc = null)
    {
        var photoId = id ?? Guid.NewGuid();
        var createdAt = createdAtUtc ?? new DateTimeOffset(2024, 4, 1, 9, 0, 0, TimeSpan.Zero);
        var key = storageKey ?? $"org/social/{eventId:D}/photos/{photoId:D}";
        var path = storagePath ?? $"{key}/original.jpg";

        return new SocialMediaEventPhoto
        {
            Id = photoId,
            SocialMediaEventId = eventId,
            StorageKey = key,
            StoragePath = path,
            ContentType = contentType,
            Width = width,
            Height = height,
            Caption = caption,
            VersionStamp = Guid.NewGuid().ToString("N"),
            IsCover = isCover,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = createdAt,
            LastModifiedByUserId = createdByUserId,
            LastModifiedAtUtc = createdAt,
            RowVersion = Guid.NewGuid().ToByteArray()
        };
    }
}
