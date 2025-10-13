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
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
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

            var uploadRoot = new StubUploadRootProvider(tempRoot);
            var clock = new TestClock(now.AddMinutes(5));
            var audit = new RecordingAudit();
            var photoService = new VisitPhotoService(db, clock, audit, photoOptions, uploadRoot, NullLogger<VisitPhotoService>.Instance);

            var userManager = CreateUserManager(db);
            var page = new EditModel(null!, null!, photoService, userManager);

            ConfigurePageContext(page, CreatePrincipal("creator", "Admin"));

            await using var imageStream = await CreateImageStreamAsync(1024, 768);
            var formFile = new FormFile(imageStream, 0, imageStream.Length, "upload", "visit-photo.jpg")
            {
                Headers = new HeaderDictionary(),
                ContentType = reportedContentType
            };

            page.Uploads = new List<IFormFile> { formFile };
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
            Assert.Equal("Photo uploaded.", page.TempData["ToastMessage"]);
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

            var uploadRoot = new StubUploadRootProvider(tempRoot);
            var clock = new TestClock(now.AddMinutes(5));
            var audit = new RecordingAudit();
            var photoService = new VisitPhotoService(db, clock, audit, photoOptions, uploadRoot, NullLogger<VisitPhotoService>.Instance);
            var userManager = CreateUserManager(db);
            var page = new EditModel(null!, null!, photoService, userManager);

            ConfigurePageContext(page, CreatePrincipal("creator", "Admin"));

            await using var firstImageStream = await CreateImageStreamAsync(1024, 768);
            var firstFile = new FormFile(firstImageStream, 0, firstImageStream.Length, "upload", "first-photo.jpg")
            {
                Headers = new HeaderDictionary(),
                ContentType = firstContentType
            };

            page.Uploads = new List<IFormFile> { firstFile };
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

            page.Uploads = new List<IFormFile> { secondFile };
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

    private static async Task<MemoryStream> CreateImageStreamAsync(int width, int height)
    {
        var stream = new MemoryStream();
        using var image = new Image<Rgba32>(width, height, new Rgba32(80, 140, 220));
        await image.SaveAsync(stream, new JpegEncoder { Quality = 90 });
        stream.Position = 0;
        return stream;
    }

    private sealed class StubUploadRootProvider : IUploadRootProvider
    {
        public StubUploadRootProvider(string rootPath)
        {
            RootPath = rootPath;
        }

        public string RootPath { get; }

        public string GetProjectRoot(int projectId) => RootPath;

        public string GetProjectPhotosRoot(int projectId) => RootPath;

        public string GetProjectDocumentsRoot(int projectId) => RootPath;

        public string GetProjectCommentsRoot(int projectId) => RootPath;

        public string GetProjectVideosRoot(int projectId) => RootPath;
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
