using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure.Activities;
using ProjectManagement.Models.Activities;
using Xunit;

namespace ProjectManagement.Tests.Activities;

public class ActivityRepositoryTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task AddAsync_PersistsActivityWithAttachments()
    {
        await using var context = CreateContext();
        var repository = new ActivityRepository(context);

        var type = new ActivityType
        {
            Name = "Test",
            CreatedByUserId = "system"
        };
        context.ActivityTypes.Add(type);
        await context.SaveChangesAsync();

        var activity = new Activity
        {
            Title = "Initial planning",
            ActivityTypeId = type.Id,
            CreatedByUserId = "user-1",
            Attachments =
            {
                new ActivityAttachment
                {
                    StorageKey = "files/plan.pdf",
                    OriginalFileName = "plan.pdf",
                    ContentType = "application/pdf",
                    FileSize = 128,
                    UploadedByUserId = "user-1"
                }
            }
        };

        await repository.AddAsync(activity);

        var stored = await context.Activities
            .Include(x => x.Attachments)
            .FirstAsync();

        Assert.Equal("Initial planning", stored.Title);
        Assert.Single(stored.Attachments);
        Assert.Equal("files/plan.pdf", stored.Attachments.First().StorageKey);
    }

    [Fact]
    public async Task ListByTypeAsync_FiltersDeletedAndOrdersBySchedule()
    {
        await using var context = CreateContext();
        var repository = new ActivityRepository(context);

        var type = new ActivityType
        {
            Name = "Engagement",
            CreatedByUserId = "system"
        };
        context.ActivityTypes.Add(type);
        await context.SaveChangesAsync();

        context.Activities.AddRange(
            new Activity
            {
                Title = "Second",
                ActivityTypeId = type.Id,
                CreatedByUserId = "user-1",
                ScheduledStartUtc = DateTimeOffset.UtcNow.AddDays(1)
            },
            new Activity
            {
                Title = "First",
                ActivityTypeId = type.Id,
                CreatedByUserId = "user-1",
                ScheduledStartUtc = DateTimeOffset.UtcNow.AddDays(2)
            },
            new Activity
            {
                Title = "Deleted",
                ActivityTypeId = type.Id,
                CreatedByUserId = "user-1",
                IsDeleted = true
            });
        await context.SaveChangesAsync();

        var results = await repository.ListByTypeAsync(type.Id);

        Assert.Equal(2, results.Count);
        Assert.Equal("First", results[0].Title);
        Assert.Equal("Second", results[1].Title);
        Assert.DoesNotContain(results, x => x.Title == "Deleted");
    }

    [Fact]
    public async Task RemoveAttachmentAsync_DeletesAttachment()
    {
        await using var context = CreateContext();
        var repository = new ActivityRepository(context);

        var type = new ActivityType
        {
            Name = "Training",
            CreatedByUserId = "system"
        };
        context.ActivityTypes.Add(type);
        await context.SaveChangesAsync();

        var attachment = new ActivityAttachment
        {
            StorageKey = "files/brief.docx",
            OriginalFileName = "brief.docx",
            ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            FileSize = 64,
            UploadedByUserId = "user-1"
        };

        var activity = new Activity
        {
            Title = "Briefing",
            ActivityTypeId = type.Id,
            CreatedByUserId = "user-1",
            Attachments = { attachment }
        };
        context.Activities.Add(activity);
        await context.SaveChangesAsync();

        var storedAttachment = await repository.GetAttachmentByIdAsync(attachment.Id);
        Assert.NotNull(storedAttachment);

        await repository.RemoveAttachmentAsync(storedAttachment!);

        Assert.Empty(context.ActivityAttachments);
    }
}
