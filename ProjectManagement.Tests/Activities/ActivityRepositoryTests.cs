using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Contracts.Activities;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure.Activities;
using ProjectManagement.Models.Activities;
using ProjectManagement.Models;
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

        var creator = new ApplicationUser
        {
            Id = "user-1",
            UserName = "creator",
            NormalizedUserName = "CREATOR",
            Email = "creator@example.test",
            NormalizedEmail = "CREATOR@EXAMPLE.TEST",
            FullName = "Casey Creator"
        };

        var modifier = new ApplicationUser
        {
            Id = "user-2",
            UserName = "modifier",
            NormalizedUserName = "MODIFIER",
            Email = "modifier@example.test",
            NormalizedEmail = "MODIFIER@EXAMPLE.TEST",
            FullName = "Morgan Modifier"
        };

        context.Users.AddRange(creator, modifier);
        await context.SaveChangesAsync();

        context.Activities.AddRange(
            new Activity
            {
                Title = "Second",
                ActivityTypeId = type.Id,
                CreatedByUserId = creator.Id!,
                LastModifiedByUserId = modifier.Id!,
                ScheduledStartUtc = DateTimeOffset.UtcNow.AddDays(1)
            },
            new Activity
            {
                Title = "First",
                ActivityTypeId = type.Id,
                CreatedByUserId = creator.Id!,
                LastModifiedByUserId = modifier.Id!,
                ScheduledStartUtc = DateTimeOffset.UtcNow.AddDays(2)
            },
            new Activity
            {
                Title = "Deleted",
                ActivityTypeId = type.Id,
                CreatedByUserId = creator.Id!,
                IsDeleted = true
            });
        await context.SaveChangesAsync();

        var results = await repository.ListByTypeAsync(type.Id);

        Assert.Equal(2, results.Count);
        Assert.Equal("First", results[0].Title);
        Assert.Equal("Second", results[1].Title);
        Assert.DoesNotContain(results, x => x.Title == "Deleted");
        Assert.All(results, x =>
        {
            Assert.NotNull(x.CreatedByUser);
            Assert.Equal(creator.FullName, x.CreatedByUser!.FullName);
            Assert.NotNull(x.LastModifiedByUser);
            Assert.Equal(modifier.FullName, x.LastModifiedByUser!.FullName);
        });
    }

    [Fact]
    public async Task GetByIdAsync_IncludesUserNavigationProperties()
    {
        await using var context = CreateContext();
        var repository = new ActivityRepository(context);

        var type = new ActivityType
        {
            Name = "Operations",
            CreatedByUserId = "system"
        };
        context.ActivityTypes.Add(type);

        var creator = new ApplicationUser
        {
            Id = "creator-1",
            UserName = "creator",
            NormalizedUserName = "CREATOR",
            Email = "creator@example.test",
            NormalizedEmail = "CREATOR@EXAMPLE.TEST",
            FullName = "Casey Creator"
        };

        var modifier = new ApplicationUser
        {
            Id = "modifier-1",
            UserName = "modifier",
            NormalizedUserName = "MODIFIER",
            Email = "modifier@example.test",
            NormalizedEmail = "MODIFIER@EXAMPLE.TEST",
            FullName = "Morgan Modifier"
        };

        var deleter = new ApplicationUser
        {
            Id = "deleter-1",
            UserName = "deleter",
            NormalizedUserName = "DELETER",
            Email = "deleter@example.test",
            NormalizedEmail = "DELETER@EXAMPLE.TEST",
            FullName = "Devin Deleter"
        };

        context.Users.AddRange(creator, modifier, deleter);
        await context.SaveChangesAsync();

        var activity = new Activity
        {
            Title = "Mission planning",
            ActivityTypeId = type.Id,
            CreatedByUserId = creator.Id!,
            LastModifiedByUserId = modifier.Id!,
            LastModifiedAtUtc = DateTimeOffset.UtcNow,
            DeletedByUserId = deleter.Id!,
            DeletedAtUtc = DateTimeOffset.UtcNow,
            IsDeleted = true
        };

        context.Activities.Add(activity);
        await context.SaveChangesAsync();

        var stored = await repository.GetByIdAsync(activity.Id);

        Assert.NotNull(stored);
        Assert.Equal(creator.FullName, stored!.CreatedByUser?.FullName);
        Assert.Equal(modifier.FullName, stored.LastModifiedByUser?.FullName);
        Assert.Equal(deleter.FullName, stored.DeletedByUser?.FullName);
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
            StorageKey = "files/brief.pdf",
            OriginalFileName = "brief.pdf",
            ContentType = "application/pdf",
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

    [Fact]
    public async Task ListAsync_FiltersAndSortsCorrectly()
    {
        await using var context = CreateContext();
        var repository = new ActivityRepository(context);

        var creator = new ApplicationUser
        {
            Id = "creator-1",
            UserName = "creator",
            NormalizedUserName = "CREATOR",
            Email = "creator@example.test",
            NormalizedEmail = "CREATOR@EXAMPLE.TEST",
            FullName = "Casey Creator"
        };

        context.Users.Add(creator);

        var typeA = new ActivityType { Name = "Briefing", CreatedByUserId = "system" };
        var typeB = new ActivityType { Name = "Training", CreatedByUserId = "system" };
        context.ActivityTypes.AddRange(typeA, typeB);
        await context.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;

        var activityWithPdf = new Activity
        {
            Title = "Security briefing",
            CreatedByUserId = creator.Id!,
            CreatedAtUtc = now.AddHours(-2),
            ScheduledStartUtc = now.AddDays(1),
            ActivityTypeId = typeA.Id,
            Attachments =
            {
                new ActivityAttachment
                {
                    StorageKey = "files/brief.pdf",
                    OriginalFileName = "brief.pdf",
                    ContentType = "application/pdf",
                    UploadedByUserId = creator.Id!,
                    UploadedAtUtc = now.AddHours(-2)
                }
            }
        };

        var activityWithPhoto = new Activity
        {
            Title = "Team workshop",
            CreatedByUserId = creator.Id!,
            CreatedAtUtc = now.AddHours(-1),
            ActivityTypeId = typeB.Id,
            Attachments =
            {
                new ActivityAttachment
                {
                    StorageKey = "files/workshop.jpg",
                    OriginalFileName = "workshop.jpg",
                    ContentType = "image/jpeg",
                    UploadedByUserId = creator.Id!,
                    UploadedAtUtc = now.AddHours(-1)
                }
            }
        };

        var deletedActivity = new Activity
        {
            Title = "Obsolete",
            CreatedByUserId = creator.Id!,
            CreatedAtUtc = now.AddHours(-3),
            ActivityTypeId = typeA.Id,
            IsDeleted = true
        };

        context.Activities.AddRange(activityWithPdf, activityWithPhoto, deletedActivity);
        await context.SaveChangesAsync();

        var request = new ActivityListRequest(
            Page: 1,
            PageSize: 10,
            Sort: ActivityListSort.CreatedAt,
            SortDescending: false,
            FromDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            ToDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
            ActivityTypeId: typeA.Id,
            CreatedByUserId: null,
            AttachmentType: ActivityAttachmentTypeFilter.Pdf);

        var result = await repository.ListAsync(request);

        Assert.Equal(1, result.TotalCount);
        var item = Assert.Single(result.Items);
        Assert.Equal(activityWithPdf.Title, item.Title);
        Assert.Equal(1, item.PdfAttachmentCount);
        Assert.Equal(0, item.PhotoAttachmentCount);
        Assert.Equal(1, item.AttachmentCount);
        Assert.Equal(typeA.Name, item.ActivityTypeName);
    }
}
