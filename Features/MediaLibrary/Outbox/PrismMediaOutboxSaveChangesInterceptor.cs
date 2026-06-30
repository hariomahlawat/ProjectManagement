using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ProjectManagement.Data;
using ProjectManagement.Contracts.Activities;
using ProjectManagement.Models.Activities;
using ProjectManagement.Services.Activities;

namespace ProjectManagement.Features.MediaLibrary.Outbox;

/// <summary>
/// Creates durable media-ingestion events before ApplicationDbContext commits. This guarantees
/// that a committed Activity media change cannot be lost even when the media worker is offline.
/// </summary>
public sealed class PrismMediaOutboxSaveChangesInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Enqueue(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Enqueue(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Enqueue(DbContext? context)
    {
        if (context is not ApplicationDbContext applicationDb)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var messages = new List<PrismMediaOutboxMessage>();

        foreach (var entry in applicationDb.ChangeTracker.Entries<ActivityAttachment>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Deleted))
            {
                continue;
            }

            var attachment = entry.Entity;
            if (!ActivityAttachmentClassifier.IsPhoto(
                    attachment.OriginalFileName,
                    attachment.ContentType))
            {
                continue;
            }

            messages.Add(new PrismMediaOutboxMessage
            {
                EventType = entry.State == EntityState.Added
                    ? PrismMediaOutboxEventType.ActivityPhotoUpsert
                    : PrismMediaOutboxEventType.ActivityPhotoRemoved,
                ActivityId = attachment.ActivityId > 0 ? attachment.ActivityId : null,
                AttachmentId = attachment.Id > 0 ? attachment.Id : null,
                StorageKey = NullIfBlank(attachment.StorageKey),
                Reason = entry.State == EntityState.Added
                    ? "Activity photo attachment committed"
                    : "Activity photo attachment removed",
                OccurredAtUtc = now,
                AvailableAfterUtc = now
            });
        }

        foreach (var entry in applicationDb.ChangeTracker.Entries<Activity>())
        {
            if (entry.State == EntityState.Deleted)
            {
                messages.Add(CreateActivityMessage(
                    PrismMediaOutboxEventType.ActivityDeleted,
                    entry.Entity.Id,
                    "Activity deleted",
                    now));
                continue;
            }

            if (entry.State != EntityState.Modified || entry.Entity.Id <= 0)
            {
                continue;
            }

            var deletedNow = PropertyChangedToTrue(entry, nameof(Activity.IsDeleted));
            if (deletedNow)
            {
                messages.Add(CreateActivityMessage(
                    PrismMediaOutboxEventType.ActivityDeleted,
                    entry.Entity.Id,
                    "Activity soft-deleted",
                    now));
                continue;
            }

            var restoredNow = PropertyChangedToFalse(entry, nameof(Activity.IsDeleted));
            if (restoredNow)
            {
                messages.Add(CreateActivityMessage(
                    PrismMediaOutboxEventType.ActivityMetadataRefresh,
                    entry.Entity.Id,
                    "Activity restored",
                    now));
                continue;
            }

            if (HasRelevantMetadataChange(entry))
            {
                messages.Add(CreateActivityMessage(
                    PrismMediaOutboxEventType.ActivityMetadataRefresh,
                    entry.Entity.Id,
                    "Activity metadata changed",
                    now));
            }
        }

        if (messages.Count == 0)
        {
            return;
        }

        // A single SaveChanges call can include both attachment and parent metadata changes.
        // Keep all semantically different events, but suppress exact duplicates in that call.
        var existingKeys = applicationDb.ChangeTracker
            .Entries<PrismMediaOutboxMessage>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => BuildKey(entry.Entity))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var message in messages)
        {
            if (existingKeys.Add(BuildKey(message)))
            {
                applicationDb.PrismMediaOutboxMessages.Add(message);
            }
        }
    }

    private static PrismMediaOutboxMessage CreateActivityMessage(
        PrismMediaOutboxEventType type,
        int activityId,
        string reason,
        DateTimeOffset now)
        => new()
        {
            EventType = type,
            ActivityId = activityId,
            Reason = reason,
            OccurredAtUtc = now,
            AvailableAfterUtc = now
        };

    private static bool HasRelevantMetadataChange(EntityEntry<Activity> entry)
        => Changed(entry, nameof(Activity.Title))
           || Changed(entry, nameof(Activity.Location))
           || Changed(entry, nameof(Activity.ActivityTypeId))
           || Changed(entry, nameof(Activity.ScheduledStartUtc))
           || Changed(entry, nameof(Activity.ScheduledEndUtc));

    private static bool PropertyChangedToTrue(EntityEntry<Activity> entry, string propertyName)
    {
        var property = entry.Property(propertyName);
        return property.IsModified
               && property.CurrentValue is bool current
               && current
               && property.OriginalValue is bool original
               && !original;
    }

    private static bool PropertyChangedToFalse(EntityEntry<Activity> entry, string propertyName)
    {
        var property = entry.Property(propertyName);
        return property.IsModified
               && property.CurrentValue is bool current
               && !current
               && property.OriginalValue is bool original
               && original;
    }

    private static bool Changed(EntityEntry<Activity> entry, string propertyName)
        => entry.Property(propertyName).IsModified;

    private static string BuildKey(PrismMediaOutboxMessage message)
        => string.Join('|',
            message.EventType,
            message.ActivityId?.ToString() ?? string.Empty,
            message.AttachmentId?.ToString() ?? string.Empty,
            message.StorageKey ?? string.Empty);

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
