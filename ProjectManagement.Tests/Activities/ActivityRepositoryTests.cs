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


    [Theory]
    [InlineData("safety brief", true)]
    [InlineData("SAFETY BRIEF", true)]
    [InlineData("Different Brief", false)]
    public async Task ExistsByTypeAndTitleAsync_UsesCaseInsensitiveDatabaseLookup(string title, bool expected)
    {
        await using var context = CreateContext();
        var repository = new ActivityRepository(context);

        // SECTION: Arrange activities for duplicate lookup
        var type = new ActivityType
        {
            Name = "Training",
            CreatedByUserId = "system"
        };
        context.ActivityTypes.Add(type);
        await context.SaveChangesAsync();

        context.Activities.Add(new Activity
        {
            Title = "Safety Brief",
            ActivityTypeId = type.Id,
            CreatedByUserId = "user-1"
        });
        await context.SaveChangesAsync();

        // SECTION: Act and assert
        var exists = await repository.ExistsByTypeAndTitleAsync(type.Id, title, null);

        Assert.Equal(expected, exists);
    }

    [Fact]
    public async Task ExistsByTypeAndTitleAsync_ExcludesRequestedActivityAndDeletedRows()
    {
        await using var context = CreateContext();
        var repository = new ActivityRepository(context);

        // SECTION: Arrange active, edited, and deleted activities
        var type = new ActivityType
        {
            Name = "Workshops",
            CreatedByUserId = "system"
        };
        context.ActivityTypes.Add(type);
        await context.SaveChangesAsync();

        var editedActivity = new Activity
        {
            Title = "Editable Brief",
            ActivityTypeId = type.Id,
            CreatedByUserId = "user-1"
        };
        var deletedActivity = new Activity
        {
            Title = "Deleted Brief",
            ActivityTypeId = type.Id,
            CreatedByUserId = "user-1",
            IsDeleted = true
        };
        context.Activities.AddRange(editedActivity, deletedActivity);
        await context.SaveChangesAsync();

        // SECTION: Act and assert
        var selfExists = await repository.ExistsByTypeAndTitleAsync(type.Id, "editable brief", editedActivity.Id);
        var deletedExists = await repository.ExistsByTypeAndTitleAsync(type.Id, "deleted brief", null);

        Assert.False(selfExists);
        Assert.False(deletedExists);
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
        Assert.False(item.HasPendingDelete);
    }


    [Fact]
    public async Task ListAsync_StillHonorsAttachmentTypeForNonIndexWorkflows()
    {
        // SECTION: Arrange activities that prove the legacy attachment-type filter still works when used directly.
        await using var context = CreateContext();
        var repository = new ActivityRepository(context);

        var type = new ActivityType { Name = "Media", CreatedByUserId = "seed" };
        context.ActivityTypes.Add(type);
        await context.SaveChangesAsync();

        context.Activities.AddRange(
            new Activity
            {
                Title = "PDF activity",
                CreatedByUserId = "owner",
                ActivityTypeId = type.Id,
                Attachments =
                {
                    new ActivityAttachment
                    {
                        StorageKey = "files/report.pdf",
                        OriginalFileName = "report.pdf",
                        ContentType = "application/pdf",
                        UploadedByUserId = "owner"
                    }
                }
            },
            new Activity
            {
                Title = "Photo activity",
                CreatedByUserId = "owner",
                ActivityTypeId = type.Id,
                Attachments =
                {
                    new ActivityAttachment
                    {
                        StorageKey = "files/photo.jpg",
                        OriginalFileName = "photo.jpg",
                        ContentType = "image/jpeg",
                        UploadedByUserId = "owner"
                    }
                }
            });
        await context.SaveChangesAsync();

        // SECTION: Act
        var result = await repository.ListAsync(new ActivityListRequest(
            PageSize: 10,
            AttachmentType: ActivityAttachmentTypeFilter.Photo));

        // SECTION: Assert
        var item = Assert.Single(result.Items);
        Assert.Equal("Photo activity", item.Title);
    }

    [Fact]
    public async Task ListAsync_FlagsPendingDeleteRequests()
    {
        await using var context = CreateContext();
        var repository = new ActivityRepository(context);

        var type = new ActivityType
        {
            Name = "Briefing",
            CreatedByUserId = "seed"
        };

        context.ActivityTypes.Add(type);
        await context.SaveChangesAsync();

        var activity = new Activity
        {
            Title = "Pending approval",
            CreatedByUserId = "owner",
            ActivityTypeId = type.Id,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        activity.DeleteRequests.Add(new ActivityDeleteRequest
        {
            Activity = activity,
            RequestedByUserId = "owner",
            RequestedAtUtc = DateTimeOffset.UtcNow
        });

        context.Activities.Add(activity);
        await context.SaveChangesAsync();

        var result = await repository.ListAsync(new ActivityListRequest(PageSize: 10));

        var item = Assert.Single(result.Items);
        Assert.True(item.HasPendingDelete);
    }

    [Fact]
    public async Task ListAsync_FiltersScheduledEventsByIstDayBoundaries()
    {
        await using var context = CreateContext();
        var repository = new ActivityRepository(context);

        var type = new ActivityType
        {
            Name = "IST Review",
            CreatedByUserId = "system"
        };

        context.ActivityTypes.Add(type);
        await context.SaveChangesAsync();

        // SECTION: Events around the 14 Jun 2026 IST filter window
        var includedAtIstStart = new Activity
        {
            Title = "14 Jun 2026 IST event",
            ActivityTypeId = type.Id,
            CreatedByUserId = "user-1",
            ScheduledStartUtc = new DateTimeOffset(2026, 6, 13, 18, 30, 0, TimeSpan.Zero),
            CreatedAtUtc = new DateTimeOffset(2026, 6, 13, 18, 30, 0, TimeSpan.Zero)
        };

        var previousIstDay = new Activity
        {
            Title = "13 Jun 2026 IST event",
            ActivityTypeId = type.Id,
            CreatedByUserId = "user-1",
            ScheduledStartUtc = new DateTimeOffset(2026, 6, 13, 18, 29, 0, TimeSpan.Zero),
            CreatedAtUtc = new DateTimeOffset(2026, 6, 13, 18, 29, 0, TimeSpan.Zero)
        };

        var nextIstDay = new Activity
        {
            Title = "15 Jun 2026 IST event",
            ActivityTypeId = type.Id,
            CreatedByUserId = "user-1",
            ScheduledStartUtc = new DateTimeOffset(2026, 6, 14, 18, 30, 0, TimeSpan.Zero),
            CreatedAtUtc = new DateTimeOffset(2026, 6, 14, 18, 30, 0, TimeSpan.Zero)
        };

        context.Activities.AddRange(includedAtIstStart, previousIstDay, nextIstDay);
        await context.SaveChangesAsync();

        var request = new ActivityListRequest(
            Page: 1,
            PageSize: 10,
            FromDate: new DateOnly(2026, 6, 14),
            ToDate: new DateOnly(2026, 6, 14));

        var result = await repository.ListAsync(request);

        var item = Assert.Single(result.Items);
        Assert.Equal("14 Jun 2026 IST event", item.Title);
    }

    [Theory]
    [InlineData("mou", "MoU with IIT Hyderabad")]
    [InlineData("iit", "MoU with IIT Hyderabad")]
    public async Task ListAsync_SearchUsesCaseInsensitiveMatching(string search, string expectedTitle)
    {
        await using var context = CreateContext();
        var repository = new ActivityRepository(context);

        var type = new ActivityType
        {
            Name = "Industry Collaboration",
            CreatedByUserId = "system"
        };

        var otherType = new ActivityType
        {
            Name = "Operations",
            CreatedByUserId = "system"
        };

        context.ActivityTypes.AddRange(type, otherType);
        await context.SaveChangesAsync();

        // SECTION: Mixed-case search data
        context.Activities.AddRange(
            new Activity
            {
                Title = "MoU with IIT Hyderabad",
                Description = "Strategic academic partnership",
                Location = "Hyderabad",
                ActivityTypeId = type.Id,
                CreatedByUserId = "user-1",
                CreatedAtUtc = DateTimeOffset.UtcNow.AddHours(-1)
            },
            new Activity
            {
                Title = "Routine status meeting",
                Description = "No matching institution",
                Location = "Pune",
                ActivityTypeId = otherType.Id,
                CreatedByUserId = "user-1",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        await context.SaveChangesAsync();

        var result = await repository.ListAsync(new ActivityListRequest(Search: search, PageSize: 10));

        var item = Assert.Single(result.Items);
        Assert.Equal(expectedTitle, item.Title);
    }

    [Fact]
    public async Task ListAsync_ClassifiesAttachmentsWithCaseInsensitiveNormalizedRules()
    {
        await using var context = CreateContext();
        var repository = new ActivityRepository(context);

        var type = new ActivityType
        {
            Name = "Attachments",
            CreatedByUserId = "system"
        };
        context.ActivityTypes.Add(type);
        await context.SaveChangesAsync();

        // SECTION: Mixed-case attachment data used by filters, counts, and previews
        var activity = new Activity
        {
            Title = "Attachment classification",
            ActivityTypeId = type.Id,
            CreatedByUserId = "user-1",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Attachments =
            {
                new ActivityAttachment
                {
                    StorageKey = "files/upper-pdf",
                    OriginalFileName = "brief.PDF",
                    ContentType = "application/octet-stream",
                    UploadedByUserId = "user-1",
                    UploadedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-3)
                },
                new ActivityAttachment
                {
                    StorageKey = "files/photo",
                    OriginalFileName = "photo.jpg",
                    ContentType = "Image/JPEG",
                    UploadedByUserId = "user-1",
                    UploadedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2)
                },
                new ActivityAttachment
                {
                    StorageKey = "files/video",
                    OriginalFileName = "clip.mp4",
                    ContentType = "Video/MP4",
                    UploadedByUserId = "user-1",
                    UploadedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
                },
                new ActivityAttachment
                {
                    StorageKey = "files/office",
                    OriginalFileName = "memo.docx",
                    ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    UploadedByUserId = "user-1",
                    UploadedAtUtc = DateTimeOffset.UtcNow
                }
            }
        };

        context.Activities.Add(activity);
        await context.SaveChangesAsync();

        var result = await repository.ListAsync(new ActivityListRequest(PageSize: 10, MediaFilter: ActivityMediaFilter.Documents));

        var item = Assert.Single(result.Items);
        Assert.Equal(4, item.AttachmentCount);
        Assert.Equal(1, item.PdfAttachmentCount);
        Assert.Equal(1, item.PhotoAttachmentCount);
        Assert.Equal(1, item.VideoAttachmentCount);
        Assert.Contains(item.MediaPreviews, preview => preview.MediaKind == ActivityAttachmentClassifier.PhotoLabel);
        Assert.Contains(item.MediaPreviews, preview => preview.MediaKind == ActivityAttachmentClassifier.VideoLabel);
        Assert.Contains(item.MediaPreviews, preview => preview.MediaKind == ActivityAttachmentClassifier.PdfLabel);
    }

}
