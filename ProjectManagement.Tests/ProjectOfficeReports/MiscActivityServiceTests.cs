using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Services;
using ProjectManagement.Services.Storage;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports;

public sealed class MiscActivityServiceTests
{
    private static readonly byte[] SamplePng =
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x60, 0x00, 0x00, 0x00,
        0x02, 0x00, 0x01, 0xE2, 0x26, 0x05, 0x9B, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    };

    [Fact]
    public async Task CreateAsync_WhenFutureDate_ReturnsInvalid()
    {
        await using var context = CreateContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 1, 8, 0, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();
        var userContext = new StubUserContext(CreatePrincipal("manager-1", "ProjectOffice"), "manager-1");
        using var temp = new TempFolder();
        var service = CreateService(context, clock, audit, userContext, temp.Path);

        var request = new MiscActivityCreateRequest(
            null,
            new DateOnly(2024, 6, 1),
            "Compile reports",
            null,
            null);

        var result = await service.CreateAsync(request, CancellationToken.None);

        Assert.Equal(MiscActivityMutationOutcome.Invalid, result.Outcome);
        Assert.Contains("future", result.Errors.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_WithValidInput_PersistsEntity()
    {
        await using var context = CreateContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 1, 8, 0, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();
        var userContext = new StubUserContext(CreatePrincipal("manager-2", "Admin"), "manager-2");
        using var temp = new TempFolder();
        var service = CreateService(context, clock, audit, userContext, temp.Path);

        var request = new MiscActivityCreateRequest(
            null,
            new DateOnly(2024, 4, 30),
            "Compile reports",
            "Prepared monthly summary",
            "https://intranet/reports");

        var result = await service.CreateAsync(request, CancellationToken.None);

        Assert.Equal(MiscActivityMutationOutcome.Success, result.Outcome);
        var entity = await context.MiscActivities.Include(x => x.Media).SingleAsync();
        Assert.Equal("Compile reports", entity.Nomenclature);
        Assert.Equal("Prepared monthly summary", entity.Description);
        Assert.Equal("https://intranet/reports", entity.ExternalLink);
        Assert.Equal(clock.UtcNow, entity.CapturedAtUtc);
        Assert.Equal("manager-2", entity.CapturedByUserId);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("ProjectOfficeReports.MiscActivityCreated", entry.Action);
        Assert.Equal(entity.Id.ToString(), entry.Data["ActivityId"]);
    }

    [Fact]
    public async Task SearchAsync_FiltersByTypeAndText()
    {
        await using var context = CreateContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 1, 8, 0, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();
        var userContext = new StubUserContext(CreatePrincipal("manager-3", "HoD"), "manager-3");
        using var temp = new TempFolder();
        var service = CreateService(context, clock, audit, userContext, temp.Path);

        var typeId = Guid.NewGuid();
        context.ActivityTypes.Add(new ActivityType
        {
            Id = typeId,
            Name = "Operations",
            IsActive = true,
            Ordinal = 1,
            CreatedAtUtc = clock.UtcNow,
            CreatedByUserId = "seed"
        });
        context.MiscActivities.AddRange(
            new MiscActivity
            {
                Id = Guid.NewGuid(),
                ActivityTypeId = typeId,
                Nomenclature = "Compile reports",
                OccurrenceDate = new DateOnly(2024, 4, 28),
                Description = "Summaries shared with stakeholders",
                CapturedAtUtc = clock.UtcNow,
                CapturedByUserId = "seed"
            },
            new MiscActivity
            {
                Id = Guid.NewGuid(),
                Nomenclature = "Prepare presentation",
                OccurrenceDate = new DateOnly(2024, 4, 29),
                Description = "Compiled slides",
                CapturedAtUtc = clock.UtcNow,
                CapturedByUserId = "seed"
            });
        await context.SaveChangesAsync();

        var options = new MiscActivityQueryOptions(
            typeId,
            new DateOnly(2024, 4, 1),
            new DateOnly(2024, 4, 30),
            "reports",
            IncludeDeleted: false,
            SortField: MiscActivitySortField.OccurrenceDate,
            SortDescending: true,
            CapturedByUserId: null,
            AttachmentType: MiscActivityAttachmentTypeFilter.Any,
            PageNumber: 1,
            PageSize: 25);

        var results = await service.SearchAsync(options, CancellationToken.None);

        var item = Assert.Single(results);
        Assert.Equal("Compile reports", item.Nomenclature);
        Assert.Equal("Operations", item.ActivityTypeName);
    }

    [Fact]
    public async Task UploadMediaAsync_SavesFileAndMetadata()
    {
        await using var context = CreateContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 1, 8, 0, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();
        var userContext = new StubUserContext(CreatePrincipal("manager-4", "ProjectOffice"), "manager-4");
        using var temp = new TempFolder();
        var service = CreateService(context, clock, audit, userContext, temp.Path);

        var createResult = await service.CreateAsync(new MiscActivityCreateRequest(
            null,
            new DateOnly(2024, 4, 30),
            "Compile reports",
            null,
            null),
            CancellationToken.None);
        Assert.Equal(MiscActivityMutationOutcome.Success, createResult.Outcome);
        var activity = await context.MiscActivities.SingleAsync();

        await using var content = new MemoryStream(SamplePng);
        var uploadRequest = new ActivityMediaUploadRequest(
            activity.Id,
            activity.RowVersion,
            content,
            "evidence.png",
            "image/png",
            "Monthly summary");

        var result = await service.UploadMediaAsync(uploadRequest, CancellationToken.None);

        Assert.Equal(ActivityMediaUploadOutcome.Success, result.Outcome);
        var media = Assert.NotNull(result.Media);
        Assert.Equal("image/png", media.MediaType);
        Assert.Equal("Monthly summary", media.Caption);
        Assert.True(File.Exists(Path.Combine(temp.Path, media.StorageKey.Replace('/', Path.DirectorySeparatorChar))));

        var entry = audit.Entries.Last();
        Assert.Equal("ProjectOfficeReports.ActivityMediaUploaded", entry.Action);
        Assert.Equal(media.Id.ToString(), entry.Data["MediaId"]);
    }

    [Fact]
    public async Task UploadMediaAsync_WhenTooLarge_ReturnsTooLarge()
    {
        await using var context = CreateContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 1, 8, 0, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();
        var userContext = new StubUserContext(CreatePrincipal("manager-5", "ProjectOffice"), "manager-5");
        using var temp = new TempFolder();
        var options = Options.Create(new MiscActivityMediaOptions
        {
            MaxFileSizeBytes = 10,
            AllowedContentTypes = new[] { "image/png" },
            StoragePrefix = "project-office/misc-activities"
        });
        var service = new MiscActivityService(
            context,
            clock,
            audit,
            userContext,
            new TestUploadRootProvider(temp.Path),
            options,
            NullLogger<MiscActivityService>.Instance);

        var createResult = await service.CreateAsync(new MiscActivityCreateRequest(
            null,
            new DateOnly(2024, 4, 30),
            "Compile reports",
            null,
            null),
            CancellationToken.None);
        Assert.Equal(MiscActivityMutationOutcome.Success, createResult.Outcome);
        var activity = await context.MiscActivities.SingleAsync();

        await using var content = new MemoryStream(new byte[32]);
        var uploadRequest = new ActivityMediaUploadRequest(
            activity.Id,
            activity.RowVersion,
            content,
            "evidence.png",
            "image/png",
            null);

        var result = await service.UploadMediaAsync(uploadRequest, CancellationToken.None);

        Assert.Equal(ActivityMediaUploadOutcome.TooLarge, result.Outcome);
    }

    [Fact]
    public async Task DeleteMediaAsync_RemovesMetadataAndFile()
    {
        await using var context = CreateContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 2, 8, 0, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();
        var userContext = new StubUserContext(CreatePrincipal("manager-6", "Admin"), "manager-6");
        using var temp = new TempFolder();
        var service = CreateService(context, clock, audit, userContext, temp.Path);

        var createResult = await service.CreateAsync(new MiscActivityCreateRequest(
            null,
            new DateOnly(2024, 4, 30),
            "Compile reports",
            null,
            null),
            CancellationToken.None);
        Assert.Equal(MiscActivityMutationOutcome.Success, createResult.Outcome);
        var activity = await context.MiscActivities.SingleAsync();

        await using (var content = new MemoryStream(SamplePng))
        {
            var uploadResult = await service.UploadMediaAsync(new ActivityMediaUploadRequest(
                activity.Id,
                activity.RowVersion,
                content,
                "evidence.png",
                "image/png",
                null),
                CancellationToken.None);
            Assert.Equal(ActivityMediaUploadOutcome.Success, uploadResult.Outcome);
            await context.Entry(activity).ReloadAsync();
        }

        var media = await context.ActivityMedia.SingleAsync();
        var mediaPath = Path.Combine(temp.Path, media.StorageKey.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(mediaPath));

        var deleteResult = await service.DeleteMediaAsync(new ActivityMediaDeletionRequest(
            activity.Id,
            media.Id,
            activity.RowVersion,
            media.RowVersion),
            CancellationToken.None);

        Assert.Equal(ActivityMediaDeletionOutcome.Success, deleteResult.Outcome);
        Assert.False(File.Exists(mediaPath));
        Assert.Empty(context.ActivityMedia);
        var entry = audit.Entries.Last();
        Assert.Equal("ProjectOfficeReports.ActivityMediaDeleted", entry.Action);
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletesActivity()
    {
        await using var context = CreateContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 3, 8, 0, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();
        var userContext = new StubUserContext(CreatePrincipal("manager-7", "ProjectOffice"), "manager-7");
        using var temp = new TempFolder();
        var service = CreateService(context, clock, audit, userContext, temp.Path);

        var createResult = await service.CreateAsync(new MiscActivityCreateRequest(
            null,
            new DateOnly(2024, 4, 29),
            "Compile reports",
            null,
            null),
            CancellationToken.None);
        Assert.Equal(MiscActivityMutationOutcome.Success, createResult.Outcome);
        var activity = await context.MiscActivities.SingleAsync();

        var deleteResult = await service.DeleteAsync(activity.Id, activity.RowVersion, CancellationToken.None);

        Assert.Equal(MiscActivityDeletionOutcome.Success, deleteResult.Outcome);
        var deleted = await context.MiscActivities.SingleAsync();
        Assert.NotNull(deleted.DeletedUtc);
        Assert.Equal("manager-7", deleted.DeletedByUserId);
        var entry = audit.Entries.Last();
        Assert.Equal("ProjectOfficeReports.MiscActivityDeleted", entry.Action);
    }

    private static MiscActivityService CreateService(
        ApplicationDbContext context,
        IClock clock,
        RecordingAudit audit,
        IUserContext userContext,
        string rootPath)
    {
        var options = Options.Create(new MiscActivityMediaOptions
        {
            MaxFileSizeBytes = 1024 * 1024,
            AllowedContentTypes = new[] { "image/png", "application/pdf" },
            StoragePrefix = "project-office/misc-activities"
        });

        return new MiscActivityService(
            context,
            clock,
            audit,
            userContext,
            new TestUploadRootProvider(rootPath),
            options,
            NullLogger<MiscActivityService>.Instance);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ClaimsPrincipal CreatePrincipal(string userId, params string[] roles)
    {
        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId));
        foreach (var role in roles)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        return new ClaimsPrincipal(identity);
    }

    private sealed class StubUserContext : IUserContext
    {
        public StubUserContext(ClaimsPrincipal user, string? userId)
        {
            User = user;
            UserId = userId;
        }

        public ClaimsPrincipal User { get; }

        public string? UserId { get; }
    }

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
