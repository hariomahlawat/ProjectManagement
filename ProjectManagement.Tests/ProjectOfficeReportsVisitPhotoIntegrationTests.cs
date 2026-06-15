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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Pages.Visits;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Storage;
using ProjectManagement.Tests.Fakes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace ProjectManagement.Tests;

public class ProjectOfficeReportsVisitPhotoIntegrationTests
{
    [Theory]
    [InlineData("image/jpeg", false)]
    [InlineData("image/jpg", false)]
    [InlineData("image/pjpeg", false)]
    [InlineData("image/jpeg", true)]
    [InlineData("image/jpg", true)]
    [InlineData("image/pjpeg", true)]
    public async Task UploadingPhotoImmediatelyAfterCreatingVisitSucceeds(string reportedContentType, bool withEditValidationErrors)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        var now = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);
        var visitType = new VisitType
        {
            Id = Guid.NewGuid(),
            Name = "Site Visit",
            CreatedAtUtc = now,
            CreatedByUserId = "creator",
            IsActive = true
        };

        var visit = new Visit
        {
            Id = Guid.NewGuid(),
            VisitTypeId = visitType.Id,
            DateOfVisit = new DateOnly(2024, 6, 14),
            VisitorName = "Integration Tester",
            Strength = 12,
            CreatedAtUtc = now,
            CreatedByUserId = "creator",
            LastModifiedAtUtc = now,
            LastModifiedByUserId = "creator"
        };

        db.VisitTypes.Add(visitType);
        db.Visits.Add(visit);
        await db.SaveChangesAsync();

        var tempRoot = Path.Combine(Path.GetTempPath(), "pm-visit-photos-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var photoOptions = Options.Create(new VisitPhotoOptions
            {
                Derivatives = new Dictionary<string, VisitPhotoDerivativeOptions>(StringComparer.OrdinalIgnoreCase)
                {
                    ["sm"] = new VisitPhotoDerivativeOptions { Width = 800, Height = 600, Quality = 80 }
                }
            });

            var uploadRoot = new TestUploadRootProvider(tempRoot);
            var clock = new TestClock(now.AddMinutes(5));
            var audit = new RecordingAudit();
            var photoService = new VisitPhotoService(db, clock, audit, photoOptions, uploadRoot, NullLogger<VisitPhotoService>.Instance);

            var userManager = CreateUserManager(db);
            var page = new EditModel(null!, null!, photoService, userManager, Options.Create(new VisitPhotoOptions()));

            ConfigurePageContext(page, CreatePrincipal("creator", "Admin"));

            await using var imageStream = await CreateImageStreamAsync(1024, 768);
            var formFile = new FormFile(imageStream, 0, imageStream.Length, "upload", "visit-photo.jpg")
            {
                Headers = new HeaderDictionary(),
                ContentType = reportedContentType
            };

            page.Upload = formFile;
            page.UploadCaption = "Entrance";

            if (withEditValidationErrors)
            {
                page.ModelState.AddModelError("Input.VisitTypeId", "Visit type required");
                page.ModelState.AddModelError("Input.DateOfVisit", "Date required");
                page.ModelState.AddModelError("Input.VisitorName", "Visitor required");
                page.ModelState.AddModelError("Input.Strength", "Strength required");
                page.ModelState.AddModelError("Input.Remarks", "Remarks invalid");
            }

            var result = await page.OnPostUploadAsync(visit.Id, CancellationToken.None);

            var redirect = Assert.IsType<RedirectToPageResult>(result);
            Assert.NotNull(redirect.RouteValues);
            Assert.Equal(visit.Id, redirect.RouteValues!["id"]);
            Assert.Equal("1 photo uploaded.", page.TempData["ToastMessage"]);
            Assert.False(page.TempData.ContainsKey("ToastError"));
            Assert.True(page.ModelState.IsValid);

            var persistedVisit = await db.Visits.Include(x => x.Photos).SingleAsync(x => x.Id == visit.Id);
            Assert.Single(persistedVisit.Photos);
            Assert.DoesNotContain(
                page.ModelState.Values.SelectMany(v => v.Errors),
                error => string.Equals(error.ErrorMessage, "Unable to save photo metadata.", StringComparison.Ordinal));

            var photo = Assert.Single(persistedVisit.Photos);
            Assert.Equal("image/jpeg", photo.ContentType);
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
        var visitType = new VisitType
        {
            Id = Guid.NewGuid(),
            Name = "Site Visit",
            CreatedAtUtc = now,
            CreatedByUserId = "creator",
            IsActive = true
        };

        var visit = new Visit
        {
            Id = Guid.NewGuid(),
            VisitTypeId = visitType.Id,
            DateOfVisit = new DateOnly(2024, 6, 30),
            VisitorName = "Integration Tester",
            Strength = 5,
            CreatedAtUtc = now,
            CreatedByUserId = "creator",
            LastModifiedAtUtc = now,
            LastModifiedByUserId = "creator"
        };

        db.VisitTypes.Add(visitType);
        db.Visits.Add(visit);
        await db.SaveChangesAsync();

        var tempRoot = Path.Combine(Path.GetTempPath(), "pm-visit-photos-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var photoOptions = Options.Create(new VisitPhotoOptions
            {
                Derivatives = new Dictionary<string, VisitPhotoDerivativeOptions>(StringComparer.OrdinalIgnoreCase)
                {
                    ["sm"] = new VisitPhotoDerivativeOptions { Width = 800, Height = 600, Quality = 80 }
                }
            });

            var uploadRoot = new TestUploadRootProvider(tempRoot);
            var clock = new TestClock(now.AddMinutes(5));
            var audit = new RecordingAudit();
            var photoService = new VisitPhotoService(db, clock, audit, photoOptions, uploadRoot, NullLogger<VisitPhotoService>.Instance);
            var userManager = CreateUserManager(db);
            var page = new EditModel(null!, null!, photoService, userManager, Options.Create(new VisitPhotoOptions()));

            ConfigurePageContext(page, CreatePrincipal("creator", "Admin"));

            await using var firstImageStream = await CreateImageStreamAsync(1024, 768);
            var firstFile = new FormFile(firstImageStream, 0, firstImageStream.Length, "upload", "first-photo.jpg")
            {
                Headers = new HeaderDictionary(),
                ContentType = firstContentType
            };

            page.Upload = firstFile;
            page.UploadCaption = "First";

            var firstResult = await page.OnPostUploadAsync(visit.Id, CancellationToken.None);
            Assert.IsType<RedirectToPageResult>(firstResult);

            page.TempData.Clear();

            await using var secondImageStream = await CreateImageStreamAsync(1024, 768);
            var secondFile = new FormFile(secondImageStream, 0, secondImageStream.Length, "upload", "second-photo.jpg")
            {
                Headers = new HeaderDictionary(),
                ContentType = secondContentType
            };

            page.Upload = secondFile;
            page.UploadCaption = "Second";

            var secondResult = await page.OnPostUploadAsync(visit.Id, CancellationToken.None);
            Assert.IsType<RedirectToPageResult>(secondResult);

            var persistedVisit = await db.Visits.Include(x => x.Photos).SingleAsync(x => x.Id == visit.Id);
            Assert.Equal(2, persistedVisit.Photos.Count);
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
    public async Task UploadingPhotoFromEditPageFallsBackToRequestFiles()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        var now = new DateTimeOffset(2024, 8, 20, 14, 0, 0, TimeSpan.Zero);
        var visitType = new VisitType
        {
            Id = Guid.NewGuid(),
            Name = "Protocol Visit",
            CreatedAtUtc = now,
            CreatedByUserId = "creator",
            IsActive = true
        };

        var visit = new Visit
        {
            Id = Guid.NewGuid(),
            VisitTypeId = visitType.Id,
            DateOfVisit = new DateOnly(2024, 8, 19),
            VisitorName = "Fallback Tester",
            Strength = 3,
            CreatedAtUtc = now,
            CreatedByUserId = "creator",
            LastModifiedAtUtc = now,
            LastModifiedByUserId = "creator"
        };

        db.VisitTypes.Add(visitType);
        db.Visits.Add(visit);
        await db.SaveChangesAsync();

        var tempRoot = Path.Combine(Path.GetTempPath(), "pm-visit-photos-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var photoOptions = Options.Create(new VisitPhotoOptions
            {
                Derivatives = new Dictionary<string, VisitPhotoDerivativeOptions>(StringComparer.OrdinalIgnoreCase)
                {
                    ["sm"] = new VisitPhotoDerivativeOptions { Width = 800, Height = 600, Quality = 80 }
                }
            });

            var uploadRoot = new TestUploadRootProvider(tempRoot);
            var clock = new TestClock(now.AddMinutes(10));
            var audit = new RecordingAudit();
            var photoService = new VisitPhotoService(db, clock, audit, photoOptions, uploadRoot, NullLogger<VisitPhotoService>.Instance);
            var userManager = CreateUserManager(db);
            var page = new EditModel(null!, null!, photoService, userManager, Options.Create(new VisitPhotoOptions()));

            ConfigurePageContext(page, CreatePrincipal("creator", "Admin"));

            await using var imageStream = await CreateImageStreamAsync(1024, 768);
            var fallbackFile = new FormFile(imageStream, 0, imageStream.Length, "Uploads", "fallback-photo.jpg")
            {
                Headers = new HeaderDictionary(),
                ContentType = "image/jpeg"
            };

            var files = new FormFileCollection { fallbackFile };
            var formData = new Dictionary<string, StringValues>
            {
                ["id"] = visit.Id.ToString()
            };

            page.PageContext.HttpContext.Request.Form = new FormCollection(formData, files);

            page.Upload = null;
            page.Uploads = new List<IFormFile>();
            page.UploadCaption = "Fallback caption";

            var result = await page.OnPostUploadAsync(visit.Id, CancellationToken.None);
            Assert.IsType<RedirectToPageResult>(result);

            var persistedVisit = await db.Visits.Include(x => x.Photos).SingleAsync(x => x.Id == visit.Id);
            Assert.Single(persistedVisit.Photos);
            Assert.Equal("Fallback caption", persistedVisit.Photos.Single().Caption);
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
    public async Task UploadingOverLimitMetadataDimensionsFailsBeforeDecodingPixels()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        var now = new DateTimeOffset(2024, 9, 10, 8, 0, 0, TimeSpan.Zero);
        var visit = await CreateVisitAsync(db, now);
        var tempRoot = Path.Combine(Path.GetTempPath(), "pm-visit-photos-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var photoOptions = Options.Create(new VisitPhotoOptions
            {
                MaxWidthPixels = 1000,
                MaxHeightPixels = 1000,
                MaxMegapixels = 1
            });

            await using var imageStream = CreatePngMetadataStream(width: 5000, height: 5000);
            var photoService = new VisitPhotoService(
                db,
                new TestClock(now.AddMinutes(5)),
                new RecordingAudit(),
                photoOptions,
                new TestUploadRootProvider(tempRoot),
                NullLogger<VisitPhotoService>.Instance);

            var result = await photoService.UploadAsync(
                visit.Id,
                imageStream,
                "oversized.png",
                "image/png",
                null,
                "creator",
                CancellationToken.None);

            Assert.Equal(VisitPhotoUploadOutcome.InvalidImage, result.Outcome);
            Assert.Contains("Image dimensions are too large", result.Errors.Single());
            Assert.Empty(await db.VisitPhotos.ToListAsync());
            Assert.Empty(Directory.EnumerateFileSystemEntries(tempRoot));
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
    public async Task UploadingExifRotatedPhotoUsesOrientedDimensionsForLimitChecks()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        var now = new DateTimeOffset(2024, 9, 10, 9, 0, 0, TimeSpan.Zero);
        var visit = await CreateVisitAsync(db, now);
        var tempRoot = Path.Combine(Path.GetTempPath(), "pm-visit-photos-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var photoOptions = Options.Create(new VisitPhotoOptions
            {
                MaxWidthPixels = 5000,
                MaxHeightPixels = 3000,
                MaxMegapixels = 12
            });

            await using var imageStream = await CreateExifRotatedImageStreamAsync(width: 2500, height: 4500);
            var photoService = new VisitPhotoService(
                db,
                new TestClock(now.AddMinutes(5)),
                new RecordingAudit(),
                photoOptions,
                new TestUploadRootProvider(tempRoot),
                NullLogger<VisitPhotoService>.Instance);

            var result = await photoService.UploadAsync(
                visit.Id,
                imageStream,
                "portrait-rotated.jpg",
                "image/jpeg",
                null,
                "creator",
                CancellationToken.None);

            Assert.Equal(VisitPhotoUploadOutcome.Success, result.Outcome);
            var photo = Assert.Single(await db.VisitPhotos.ToListAsync());
            Assert.Equal(4500, photo.Width);
            Assert.Equal(2500, photo.Height);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    // SECTION: Visit fixture helpers
    private static async Task<Visit> CreateVisitAsync(ApplicationDbContext db, DateTimeOffset now)
    {
        var visitType = new VisitType
        {
            Id = Guid.NewGuid(),
            Name = "Site Visit",
            CreatedAtUtc = now,
            CreatedByUserId = "creator",
            IsActive = true
        };

        var visit = new Visit
        {
            Id = Guid.NewGuid(),
            VisitTypeId = visitType.Id,
            DateOfVisit = DateOnly.FromDateTime(now.Date),
            VisitorName = "Dimension Tester",
            Strength = 4,
            CreatedAtUtc = now,
            CreatedByUserId = "creator",
            LastModifiedAtUtc = now,
            LastModifiedByUserId = "creator"
        };

        db.VisitTypes.Add(visitType);
        db.Visits.Add(visit);
        await db.SaveChangesAsync();
        return visit;
    }

    // SECTION: PNG metadata fixture helpers
    private static MemoryStream CreatePngMetadataStream(int width, int height)
    {
        var stream = new MemoryStream();
        stream.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

        var ihdr = new byte[13];
        WriteBigEndianInt32(ihdr, 0, width);
        WriteBigEndianInt32(ihdr, 4, height);
        ihdr[8] = 8;
        ihdr[9] = 2;

        WritePngChunk(stream, "IHDR", ihdr);
        WritePngChunk(stream, "IEND", Array.Empty<byte>());
        stream.Position = 0;
        return stream;
    }

    private static void WritePngChunk(Stream stream, string chunkType, byte[] data)
    {
        WriteBigEndianInt32(stream, data.Length);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(chunkType);
        stream.Write(typeBytes);
        stream.Write(data);
        WriteBigEndianInt32(stream, unchecked((int)CalculateCrc(typeBytes, data)));
    }

    private static uint CalculateCrc(byte[] typeBytes, byte[] data)
    {
        var crc = 0xffffffffu;
        foreach (var value in typeBytes.Concat(data))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) == 1 ? 0xedb88320u ^ (crc >> 1) : crc >> 1;
            }
        }

        return crc ^ 0xffffffffu;
    }

    private static void WriteBigEndianInt32(Stream stream, int value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static void WriteBigEndianInt32(byte[] target, int offset, int value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }

    private static void ConfigurePageContext(PageModel page, ClaimsPrincipal user)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.User = user;

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

    // SECTION: Image fixture helpers
    private static async Task<MemoryStream> CreateExifRotatedImageStreamAsync(int width, int height)
    {
        var stream = new MemoryStream();
        using var image = new Image<Rgba32>(width, height, new Rgba32(80, 140, 220));

        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Orientation, (ushort)6);
        image.Metadata.ExifProfile = exif;

        await image.SaveAsync(stream, new JpegEncoder { Quality = 85 });
        stream.Position = 0;
        return stream;
    }

    private static async Task<MemoryStream> CreateImageStreamAsync(int width, int height)
    {
        var stream = new MemoryStream();
        using var image = new Image<Rgba32>(width, height, new Rgba32(80, 140, 220));
        await image.SaveAsync(stream, new JpegEncoder { Quality = 90 });
        stream.Position = 0;
        return stream;
    }

    private sealed class RecordingAudit : IAuditService
    {
        public List<(string Action, IDictionary<string, string?> Data, string? UserId)> Entries { get; } = new();

        public Task LogAsync(string action, string? message = null, string level = "Info", string? userId = null, string? userName = null, IDictionary<string, string?>? data = null, HttpContext? http = null)
        {
            Entries.Add((action, data ?? new Dictionary<string, string?>(), userId));
            return Task.CompletedTask;
        }
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
