using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Pages.SocialMedia;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Storage;
using ProjectManagement.Tests.Fakes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace ProjectManagement.Tests;

public sealed class ProjectOfficeReportsSocialMediaPhotoIntegrationTests
{
    [Theory]
    [InlineData("image/jpeg", false)]
    [InlineData("image/jpg", false)]
    [InlineData("image/png", false)]
    [InlineData("image/jpeg", true)]
    [InlineData("image/png", true)]
    public async Task UploadingPhotoImmediatelyAfterCreatingEventSucceeds(string reportedContentType, bool withEditValidationErrors)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        var now = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);
        var eventType = SocialMediaTestData.CreateEventType(name: "Campaign Launch", createdByUserId: "creator", createdAtUtc: now);
        var socialEvent = SocialMediaTestData.CreateEvent(
            eventType.Id,
            id: Guid.NewGuid(),
            dateOfEvent: new DateOnly(2024, 6, 14),
            title: "Integration test",
            platform: "Instagram",
            description: "Test coverage.",
            timestamp: now,
            createdByUserId: "creator");

        db.SocialMediaEventTypes.Add(eventType);
        db.SocialMediaEvents.Add(socialEvent);
        await db.SaveChangesAsync();

        var tempRoot = Path.Combine(Path.GetTempPath(), "pm-social-media-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var photoOptions = Options.Create(new SocialMediaPhotoOptions
            {
                MinWidth = 800,
                MinHeight = 600,
                Derivatives = new Dictionary<string, SocialMediaPhotoDerivativeOptions>(StringComparer.OrdinalIgnoreCase)
                {
                    ["feed"] = new SocialMediaPhotoDerivativeOptions { Width = 1200, Height = 1200, Quality = 85 },
                    ["thumb"] = new SocialMediaPhotoDerivativeOptions { Width = 600, Height = 600, Quality = 80 }
                },
                StoragePrefix = "org/social/{eventId}"
            });

            var uploadRoot = new TestUploadRootProvider(tempRoot);
            var clock = new TestClock(now.AddMinutes(5));
            var photoService = new SocialMediaEventPhotoService(db, clock, photoOptions, uploadRoot, NullLogger<SocialMediaEventPhotoService>.Instance);
            var eventService = new SocialMediaEventService(db, clock, photoService);
            var userManager = CreateUserManager(db);
            var page = new EditModel(eventService, photoService, userManager);

            ConfigurePageContext(page, CreatePrincipal("creator", "Admin"));

            await using var imageStream = await CreateImageStreamAsync(1280, 1280);
            var formFile = new FormFile(imageStream, 0, imageStream.Length, "upload", "social-media.jpg")
            {
                Headers = new HeaderDictionary(),
                ContentType = reportedContentType
            };

            page.Uploads = new List<IFormFile> { formFile };
            page.UploadCaption = "Stage";

            if (withEditValidationErrors)
            {
                page.ModelState.AddModelError("Input.Title", "Title required");
                page.ModelState.AddModelError("Input.Description", "Description required");
            }

            var result = await page.OnPostUploadAsync(socialEvent.Id, CancellationToken.None);

            Assert.IsType<PageResult>(result);
            Assert.Equal("1 photo uploaded.", page.TempData["ToastMessage"]);
            Assert.False(page.TempData.ContainsKey("ToastError"));
            Assert.Equal(!withEditValidationErrors, page.ModelState.IsValid);

            var persistedEvent = await db.SocialMediaEvents.Include(x => x.Photos).SingleAsync(x => x.Id == socialEvent.Id);
            Assert.Single(persistedEvent.Photos);
            Assert.DoesNotContain(
                page.ModelState.Values.SelectMany(v => v.Errors),
                error => string.Equals(error.ErrorMessage, "Unable to upload one of the selected photos. Please try again.", StringComparison.Ordinal));

            var photo = Assert.Single(persistedEvent.Photos);
            Assert.Equal("image/jpeg", photo.ContentType);
            Assert.True(photo.IsCover);
            Assert.Equal("creator", photo.CreatedByUserId);

            var photoFolder = Path.Combine(tempRoot, photo.StorageKey.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(Path.Combine(photoFolder, "feed.jpg")));

            Assert.Equal(1, page.PhotoGallery.Photos.Count);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [Fact]
    public async Task UploadingSmallPhotoSucceedsWhenNoMinimumDimensionsConfigured()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        var now = new DateTimeOffset(2024, 7, 2, 8, 15, 0, TimeSpan.Zero);
        var eventType = SocialMediaTestData.CreateEventType(name: "Community Update", createdByUserId: "creator", createdAtUtc: now);
        var socialEvent = SocialMediaTestData.CreateEvent(
            eventType.Id,
            id: Guid.NewGuid(),
            dateOfEvent: new DateOnly(2024, 7, 1),
            title: "Smaller asset upload",
            platform: "Instagram",
            description: "Verifies relaxed size guard.",
            timestamp: now,
            createdByUserId: "creator");

        db.SocialMediaEventTypes.Add(eventType);
        db.SocialMediaEvents.Add(socialEvent);
        await db.SaveChangesAsync();

        var tempRoot = Path.Combine(Path.GetTempPath(), "pm-social-media-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var photoOptions = Options.Create(new SocialMediaPhotoOptions
            {
                Derivatives = new Dictionary<string, SocialMediaPhotoDerivativeOptions>(StringComparer.OrdinalIgnoreCase)
                {
                    ["feed"] = new SocialMediaPhotoDerivativeOptions { Width = 1200, Height = 1200, Quality = 85 },
                    ["thumb"] = new SocialMediaPhotoDerivativeOptions { Width = 600, Height = 600, Quality = 80 }
                },
                StoragePrefix = "org/social/{eventId}"
            });

            var uploadRoot = new TestUploadRootProvider(tempRoot);
            var clock = new TestClock(now.AddMinutes(10));
            var photoService = new SocialMediaEventPhotoService(db, clock, photoOptions, uploadRoot, NullLogger<SocialMediaEventPhotoService>.Instance);
            var eventService = new SocialMediaEventService(db, clock, photoService);
            var userManager = CreateUserManager(db);
            var page = new EditModel(eventService, photoService, userManager);

            ConfigurePageContext(page, CreatePrincipal("creator", "Admin"));

            await using var imageStream = await CreateImageStreamAsync(640, 640);
            var formFile = new FormFile(imageStream, 0, imageStream.Length, "upload", "small-photo.jpg")
            {
                Headers = new HeaderDictionary(),
                ContentType = "image/jpeg"
            };

            page.Uploads = new List<IFormFile> { formFile };
            page.UploadCaption = "Smaller test";

            var result = await page.OnPostUploadAsync(socialEvent.Id, CancellationToken.None);

            Assert.IsType<PageResult>(result);
            Assert.Equal("1 photo uploaded.", page.TempData["ToastMessage"]);
            Assert.False(page.TempData.ContainsKey("ToastError"));

            var persistedEvent = await db.SocialMediaEvents.Include(x => x.Photos).SingleAsync(x => x.Id == socialEvent.Id);
            var photo = Assert.Single(persistedEvent.Photos);

            var photoFolder = Path.Combine(tempRoot, photo.StorageKey.Replace('/', Path.DirectorySeparatorChar));
            var feedDerivative = Path.Combine(photoFolder, "feed.jpg");
            var thumbDerivative = Path.Combine(photoFolder, "thumb.jpg");

            Assert.True(File.Exists(feedDerivative));
            Assert.True(File.Exists(thumbDerivative));
            Assert.True(new FileInfo(feedDerivative).Length > 0);
            Assert.True(new FileInfo(thumbDerivative).Length > 0);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [Theory]
    [InlineData("image/jpeg", "image/png")]
    [InlineData("image/png", "image/jpeg")]
    public async Task UploadingMultiplePhotosFromEditPageSucceeds(string firstContentType, string secondContentType)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        var now = new DateTimeOffset(2024, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var eventType = SocialMediaTestData.CreateEventType(name: "Community Engagement", createdByUserId: "creator", createdAtUtc: now);
        var socialEvent = SocialMediaTestData.CreateEvent(
            eventType.Id,
            id: Guid.NewGuid(),
            dateOfEvent: new DateOnly(2024, 6, 30),
            title: "Community meet",
            platform: "YouTube",
            description: "Highlights",
            timestamp: now,
            createdByUserId: "creator");

        db.SocialMediaEventTypes.Add(eventType);
        db.SocialMediaEvents.Add(socialEvent);
        await db.SaveChangesAsync();

        var tempRoot = Path.Combine(Path.GetTempPath(), "pm-social-media-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var photoOptions = Options.Create(new SocialMediaPhotoOptions
            {
                MinWidth = 800,
                MinHeight = 600,
                Derivatives = new Dictionary<string, SocialMediaPhotoDerivativeOptions>(StringComparer.OrdinalIgnoreCase)
                {
                    ["feed"] = new SocialMediaPhotoDerivativeOptions { Width = 1200, Height = 1200, Quality = 85 },
                    ["thumb"] = new SocialMediaPhotoDerivativeOptions { Width = 600, Height = 600, Quality = 80 }
                },
                StoragePrefix = "org/social/{eventId}"
            });

            var uploadRoot = new TestUploadRootProvider(tempRoot);
            var clock = new TestClock(now.AddMinutes(5));
            var photoService = new SocialMediaEventPhotoService(db, clock, photoOptions, uploadRoot, NullLogger<SocialMediaEventPhotoService>.Instance);
            var eventService = new SocialMediaEventService(db, clock, photoService);
            var userManager = CreateUserManager(db);
            var page = new EditModel(eventService, photoService, userManager);

            ConfigurePageContext(page, CreatePrincipal("creator", "Admin"));

            await using var firstStream = await CreateImageStreamAsync(1280, 1280);
            await using var secondStream = await CreateImageStreamAsync(1920, 1080);

            var firstFile = new FormFile(firstStream, 0, firstStream.Length, "upload", "first.jpg")
            {
                Headers = new HeaderDictionary(),
                ContentType = firstContentType
            };

            var secondFile = new FormFile(secondStream, 0, secondStream.Length, "upload", "second.png")
            {
                Headers = new HeaderDictionary(),
                ContentType = secondContentType
            };

            page.Uploads = new List<IFormFile> { firstFile, secondFile };
            page.UploadCaption = "Highlights";

            var result = await page.OnPostUploadAsync(socialEvent.Id, CancellationToken.None);

            Assert.IsType<PageResult>(result);
            Assert.Equal("2 photos uploaded.", page.TempData["ToastMessage"]);
            Assert.False(page.TempData.ContainsKey("ToastError"));
            Assert.True(page.ModelState.IsValid);

            var persistedEvent = await db.SocialMediaEvents.Include(x => x.Photos).SingleAsync(x => x.Id == socialEvent.Id);
            Assert.Equal(2, persistedEvent.Photos.Count);

            var ordered = persistedEvent.Photos.OrderBy(x => x.CreatedAtUtc).ToList();
            Assert.True(ordered[0].IsCover);
            Assert.False(ordered[1].IsCover);

            var firstFolder = Path.Combine(tempRoot, ordered[0].StorageKey.Replace('/', Path.DirectorySeparatorChar));
            var secondFolder = Path.Combine(tempRoot, ordered[1].StorageKey.Replace('/', Path.DirectorySeparatorChar));

            Assert.True(File.Exists(Path.Combine(firstFolder, "feed.jpg")));
            Assert.True(File.Exists(Path.Combine(secondFolder, "feed.jpg")));

            Assert.Equal(2, page.PhotoGallery.Photos.Count);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [Fact]
    public async Task ExistingPhotoAssetsRemainAccessibleWhenStoragePrefixChanges()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        var now = new DateTimeOffset(2024, 8, 5, 14, 0, 0, TimeSpan.Zero);
        var eventType = SocialMediaTestData.CreateEventType(name: "Prefix change", createdByUserId: "creator", createdAtUtc: now);
        var socialEvent = SocialMediaTestData.CreateEvent(
            eventType.Id,
            id: Guid.NewGuid(),
            dateOfEvent: new DateOnly(2024, 8, 4),
            title: "Prefix regression",
            platform: "Threads",
            description: "Verifies stored keys",
            timestamp: now,
            createdByUserId: "creator");

        db.SocialMediaEventTypes.Add(eventType);
        db.SocialMediaEvents.Add(socialEvent);
        await db.SaveChangesAsync();

        var tempRoot = Path.Combine(Path.GetTempPath(), "pm-social-media-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var derivatives = new Dictionary<string, SocialMediaPhotoDerivativeOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["feed"] = new SocialMediaPhotoDerivativeOptions { Width = 1200, Height = 1200, Quality = 85 },
                ["thumb"] = new SocialMediaPhotoDerivativeOptions { Width = 600, Height = 600, Quality = 80 }
            };

            var initialOptions = Options.Create(new SocialMediaPhotoOptions
            {
                MinWidth = 800,
                MinHeight = 600,
                Derivatives = derivatives,
                StoragePrefix = "org/social/{eventId}"
            });

            var uploadRoot = new TestUploadRootProvider(tempRoot);
            var initialClock = new TestClock(now.AddMinutes(1));
            var initialService = new SocialMediaEventPhotoService(
                db,
                initialClock,
                initialOptions,
                uploadRoot,
                NullLogger<SocialMediaEventPhotoService>.Instance);

            await using var stream = await CreateImageStreamAsync(1600, 1200);
            var uploadResult = await initialService.UploadAsync(
                socialEvent.Id,
                stream,
                "prefix.jpg",
                "image/jpeg",
                "Stage",
                "creator",
                CancellationToken.None);

            Assert.Equal(SocialMediaEventPhotoUploadOutcome.Success, uploadResult.Outcome);
            var uploadedPhoto = Assert.NotNull(uploadResult.Photo);

            var persistedPhoto = await db.SocialMediaEventPhotos.AsNoTracking().SingleAsync(x => x.Id == uploadedPhoto.Id);
            var physicalFolder = Path.Combine(tempRoot, persistedPhoto.StorageKey.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(Directory.Exists(physicalFolder));

            var refreshedOptions = Options.Create(new SocialMediaPhotoOptions
            {
                MinWidth = 800,
                MinHeight = 600,
                Derivatives = new Dictionary<string, SocialMediaPhotoDerivativeOptions>(derivatives, StringComparer.OrdinalIgnoreCase),
                StoragePrefix = "alternate/social/{eventId}"
            });

            db.ChangeTracker.Clear();

            var refreshedClock = new TestClock(now.AddMinutes(2));
            var refreshedService = new SocialMediaEventPhotoService(
                db,
                refreshedClock,
                refreshedOptions,
                uploadRoot,
                NullLogger<SocialMediaEventPhotoService>.Instance);

            var asset = await refreshedService.OpenAsync(socialEvent.Id, persistedPhoto.Id, "original", CancellationToken.None);
            Assert.NotNull(asset);
            Assert.Equal("image/jpeg", asset!.ContentType);
            asset.Stream.Dispose();

            var deleteResult = await refreshedService.RemoveAsync(
                socialEvent.Id,
                persistedPhoto.Id,
                persistedPhoto.RowVersion,
                "deleter",
                CancellationToken.None);

            Assert.Equal(SocialMediaEventPhotoDeletionOutcome.Success, deleteResult.Outcome);
            Assert.False(Directory.Exists(physicalFolder));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [Fact]
    public async Task DeletingEventFromDeletePageRemovesPhotos()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        var now = new DateTimeOffset(2024, 9, 1, 18, 0, 0, TimeSpan.Zero);
        var eventType = SocialMediaTestData.CreateEventType(name: "Deletion flow", createdByUserId: "creator", createdAtUtc: now);
        var socialEvent = SocialMediaTestData.CreateEvent(
            eventType.Id,
            id: Guid.NewGuid(),
            dateOfEvent: new DateOnly(2024, 8, 31),
            title: "Delete from page",
            platform: "Instagram",
            description: "Ensures deletion cleans up assets.",
            timestamp: now,
            createdByUserId: "creator");

        db.SocialMediaEventTypes.Add(eventType);
        db.SocialMediaEvents.Add(socialEvent);
        await db.SaveChangesAsync();

        var tempRoot = Path.Combine(Path.GetTempPath(), "pm-social-media-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var photoOptions = Options.Create(new SocialMediaPhotoOptions
            {
                MinWidth = 800,
                MinHeight = 600,
                Derivatives = new Dictionary<string, SocialMediaPhotoDerivativeOptions>(StringComparer.OrdinalIgnoreCase)
                {
                    ["feed"] = new SocialMediaPhotoDerivativeOptions { Width = 1200, Height = 1200, Quality = 85 },
                    ["thumb"] = new SocialMediaPhotoDerivativeOptions { Width = 600, Height = 600, Quality = 80 }
                },
                StoragePrefix = "org/social/{eventId}"
            });

            var uploadRoot = new TestUploadRootProvider(tempRoot);

            var earlyClock = new TestClock(now.AddHours(-2));
            var earlyPhotoService = new SocialMediaEventPhotoService(
                db,
                earlyClock,
                photoOptions,
                uploadRoot,
                NullLogger<SocialMediaEventPhotoService>.Instance);

            await using (var firstStream = await CreateImageStreamAsync(1600, 1200))
            {
                var firstUpload = await earlyPhotoService.UploadAsync(
                    socialEvent.Id,
                    firstStream,
                    "early.jpg",
                    "image/jpeg",
                    "Early shot",
                    "creator",
                    CancellationToken.None);

                Assert.Equal(SocialMediaEventPhotoUploadOutcome.Success, firstUpload.Outcome);
            }

            var lateClock = new TestClock(now.AddHours(-1));
            var latePhotoService = new SocialMediaEventPhotoService(
                db,
                lateClock,
                photoOptions,
                uploadRoot,
                NullLogger<SocialMediaEventPhotoService>.Instance);

            await using (var secondStream = await CreateImageStreamAsync(1920, 1080))
            {
                var secondUpload = await latePhotoService.UploadAsync(
                    socialEvent.Id,
                    secondStream,
                    "late.jpg",
                    "image/jpeg",
                    "Late shot",
                    "creator",
                    CancellationToken.None);

                Assert.Equal(SocialMediaEventPhotoUploadOutcome.Success, secondUpload.Outcome);
            }

            var storageKeys = await db.SocialMediaEventPhotos.AsNoTracking()
                .Where(x => x.SocialMediaEventId == socialEvent.Id)
                .Select(x => x.StorageKey)
                .ToListAsync();

            Assert.Equal(2, storageKeys.Count);

            var physicalFolders = storageKeys
                .Select(key => Path.Combine(tempRoot, key.Replace('/', Path.DirectorySeparatorChar)))
                .ToList();

            foreach (var folder in physicalFolders)
            {
                Assert.True(Directory.Exists(folder));
            }

            var persistedEvent = await db.SocialMediaEvents.AsNoTracking().SingleAsync(x => x.Id == socialEvent.Id);
            var rowVersion = Convert.ToBase64String(persistedEvent.RowVersion);

            db.ChangeTracker.Clear();

            var deletionClock = new TestClock(now.AddHours(1));
            var deletionPhotoService = new SocialMediaEventPhotoService(
                db,
                deletionClock,
                photoOptions,
                uploadRoot,
                NullLogger<SocialMediaEventPhotoService>.Instance);
            var eventService = new SocialMediaEventService(db, deletionClock, deletionPhotoService);
            var userManager = CreateUserManager(db);
            var page = new DeleteModel(eventService, userManager);

            ConfigurePageContext(page, CreatePrincipal("deleter", "Admin"));
            page.RowVersion = rowVersion;

            var result = await page.OnPostAsync(socialEvent.Id, CancellationToken.None);

            var redirect = Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal("Index", redirect.PageName);
            Assert.Equal("Social media event deleted.", page.TempData["ToastMessage"]);

            Assert.False(await db.SocialMediaEvents.AnyAsync());
            Assert.False(await db.SocialMediaEventPhotos.AnyAsync());

            foreach (var folder in physicalFolders)
            {
                Assert.False(Directory.Exists(folder));
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    private static void ConfigurePageContext(PageModel page, ClaimsPrincipal user)
    {
        var httpContext = new DefaultHttpContext { User = user };
        var tempDataProvider = new DictionaryTempDataProvider();
        var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor());

        page.PageContext = new PageContext(actionContext)
        {
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        };
        page.TempData = new TempDataDictionary(httpContext, tempDataProvider);
    }

    private static ClaimsPrincipal CreatePrincipal(string userId, string role)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, userId),
            new(ClaimTypes.Role, role)
        };

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static UserManager<ApplicationUser> CreateUserManager(ApplicationDbContext db)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<ILookupNormalizer, UpperInvariantLookupNormalizer>()
            .BuildServiceProvider();

        return new UserManager<ApplicationUser>(
            new UserStore<ApplicationUser>(db),
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            services.GetRequiredService<ILookupNormalizer>(),
            new IdentityErrorDescriber(),
            services,
            NullLogger<UserManager<ApplicationUser>>.Instance);
    }

    private static async Task<MemoryStream> CreateImageStreamAsync(int width, int height)
    {
        var stream = new MemoryStream();
        using var image = new Image<Rgba32>(width, height, new Rgba32(80, 140, 220));
        await image.SaveAsync(stream, new JpegEncoder { Quality = 90 });
        stream.Position = 0;
        return stream;
    }

    private sealed class TestClock : IClock
    {
        public TestClock(DateTimeOffset now)
        {
            UtcNow = now;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class DictionaryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();

        public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
        {
        }
    }
}
