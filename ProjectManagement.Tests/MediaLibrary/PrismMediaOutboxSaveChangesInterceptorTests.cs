using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Features.MediaLibrary.Outbox;
using ProjectManagement.Models.Activities;

namespace ProjectManagement.Tests.MediaLibrary;

public sealed class PrismMediaOutboxSaveChangesInterceptorTests
{
    [Fact]
    public async Task ImageAttachmentCommitCreatesDurableUpsertEvent()
    {
        await using var db = CreateContext();
        var activity = CreateActivity();
        db.Activities.Add(activity);
        await db.SaveChangesAsync();

        db.ActivityAttachments.Add(new ActivityAttachment
        {
            ActivityId = activity.Id,
            StorageKey = $"activities/{activity.Id}/portrait.jpg",
            OriginalFileName = "portrait.jpg",
            ContentType = "image/jpeg",
            FileSize = 100,
            UploadedByUserId = "tester",
            UploadedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var message = await db.PrismMediaOutboxMessages.SingleAsync();
        Assert.Equal(PrismMediaOutboxEventType.ActivityPhotoUpsert, message.EventType);
        Assert.Equal(activity.Id, message.ActivityId);
        Assert.Equal($"activities/{activity.Id}/portrait.jpg", message.StorageKey);
        Assert.Equal(PrismMediaOutboxStatus.Pending, message.Status);
    }

    [Fact]
    public async Task AttachmentRemovalAndActivityMetadataChangeCreateIndependentEvents()
    {
        await using var db = CreateContext();
        var activity = CreateActivity();
        var attachment = new ActivityAttachment
        {
            Activity = activity,
            StorageKey = "activities/1/photo.png",
            OriginalFileName = "photo.png",
            ContentType = "image/png",
            FileSize = 200,
            UploadedByUserId = "tester",
            UploadedAtUtc = DateTimeOffset.UtcNow
        };
        activity.Attachments.Add(attachment);
        db.Activities.Add(activity);
        await db.SaveChangesAsync();
        db.PrismMediaOutboxMessages.RemoveRange(db.PrismMediaOutboxMessages);
        await db.SaveChangesAsync();

        db.ActivityAttachments.Remove(attachment);
        activity.Title = "Updated activity";
        db.Entry(activity).Property(item => item.Title).IsModified = true;
        await db.SaveChangesAsync();

        var messages = await db.PrismMediaOutboxMessages
            .OrderBy(item => item.Id)
            .ToListAsync();
        Assert.Contains(messages, item =>
            item.EventType == PrismMediaOutboxEventType.ActivityPhotoRemoved
            && item.AttachmentId == attachment.Id);
        Assert.Contains(messages, item =>
            item.EventType == PrismMediaOutboxEventType.ActivityMetadataRefresh
            && item.ActivityId == activity.Id);
    }


    [Fact]
    public async Task RestoringActivityCreatesMetadataRefreshEvent()
    {
        await using var db = CreateContext();
        var activity = CreateActivity();
        activity.IsDeleted = true;
        db.Activities.Add(activity);
        await db.SaveChangesAsync();
        db.PrismMediaOutboxMessages.RemoveRange(db.PrismMediaOutboxMessages);
        await db.SaveChangesAsync();

        activity.IsDeleted = false;
        db.Entry(activity).Property(item => item.IsDeleted).IsModified = true;
        await db.SaveChangesAsync();

        var message = await db.PrismMediaOutboxMessages.SingleAsync();
        Assert.Equal(PrismMediaOutboxEventType.ActivityMetadataRefresh, message.EventType);
        Assert.Equal(activity.Id, message.ActivityId);
        Assert.Equal("Activity restored", message.Reason);
    }

    [Fact]
    public async Task NonImageAttachmentDoesNotCreateMediaEvent()
    {
        await using var db = CreateContext();
        var activity = CreateActivity();
        db.Activities.Add(activity);
        await db.SaveChangesAsync();

        db.ActivityAttachments.Add(new ActivityAttachment
        {
            ActivityId = activity.Id,
            StorageKey = $"activities/{activity.Id}/note.pdf",
            OriginalFileName = "note.pdf",
            ContentType = "application/pdf",
            FileSize = 100,
            UploadedByUserId = "tester"
        });
        await db.SaveChangesAsync();

        Assert.Empty(await db.PrismMediaOutboxMessages.ToListAsync());
    }

    private static ApplicationDbContext CreateContext()
    {
        var interceptor = new PrismMediaOutboxSaveChangesInterceptor();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .AddInterceptors(interceptor)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static Activity CreateActivity() => new()
    {
        Title = "Activity",
        ActivityTypeId = 1,
        CreatedByUserId = "tester",
        CreatedAtUtc = DateTimeOffset.UtcNow,
        LastModifiedByUserId = "tester",
        LastModifiedAtUtc = DateTimeOffset.UtcNow,
        ActivityType = new ActivityType
        {
            Id = 1,
            Name = "Test",
            CreatedByUserId = "tester",
            CreatedAtUtc = DateTimeOffset.UtcNow
        }
    };
}
