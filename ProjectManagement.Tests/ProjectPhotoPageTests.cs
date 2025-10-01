using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.ViewFeatures.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using ProjectManagement.Configuration;
using Microsoft.Net.Http.Headers;
using ProjectManagement.Data;
using ProjectManagement.Models;
using PhotosIndexModel = ProjectManagement.Pages.Projects.Photos.IndexModel;
using PhotoViewModel = ProjectManagement.Pages.Projects.Photos.ViewModel;
using ProjectsOverviewModel = ProjectManagement.Pages.Projects.OverviewModel;
using UploadModel = ProjectManagement.Pages.Projects.Photos.UploadModel;
using EditModel = ProjectManagement.Pages.Projects.Photos.EditModel;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Storage;
using ProjectManagement.Services.Stages;
using ProjectManagement.Tests.Fakes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;
using static ProjectManagement.Pages.Projects.Photos.EditModel;
using static ProjectManagement.Pages.Projects.Photos.UploadModel;

namespace ProjectManagement.Tests;

public sealed class ProjectPhotoPageTests
{
    [Fact]
    public async Task Index_AllowsAdmins()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 1);

        var page = CreateIndexPage(db, new FakeUserContext("admin-1", isAdmin: true));
        var result = await page.OnGetAsync(1, CancellationToken.None);

        Assert.IsType<PageResult>(result);
    }

    [Fact]
    public async Task Index_AllowsAssignedHod()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 2, hodUserId: "hod-2");

        var page = CreateIndexPage(db, new FakeUserContext("hod-2", isHoD: true));
        var result = await page.OnGetAsync(2, CancellationToken.None);

        Assert.IsType<PageResult>(result);
    }

    [Fact]
    public async Task Index_AllowsAssignedProjectOfficer()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 3, leadPoUserId: "po-3");

        var page = CreateIndexPage(db, new FakeUserContext("po-3", isProjectOfficer: true));
        var result = await page.OnGetAsync(3, CancellationToken.None);

        Assert.IsType<PageResult>(result);
    }

    [Fact]
    public async Task Index_ForbidsUnassignedUser()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 4, hodUserId: "hod-owner", leadPoUserId: "po-owner");

        var page = CreateIndexPage(db, new FakeUserContext("po-guest", isProjectOfficer: true));
        var result = await page.OnGetAsync(4, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Index_RemoveAction_ForbidsUnrelatedUser()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 5, hodUserId: "hod-owner", leadPoUserId: "po-owner");
        var project = await db.Projects.SingleAsync(p => p.Id == 5);

        var page = CreateIndexPage(db, new FakeUserContext("intruder", isProjectOfficer: true));
        ConfigurePageContext(page);

        var rowVersion = Convert.ToBase64String(project.RowVersion);
        var result = await page.OnPostRemoveAsync(5, photoId: 10, rowVersion, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Overview_ReflectsGalleryChanges()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 6);

        var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 8, 0, 0, TimeSpan.Zero));
        var options = CreateOptions();
        var root = CreateTempRoot();
        SetUploadRoot(root);
        try
        {
            var optionsWrapper = Options.Create(options);
            var documentOptions = Options.Create(new ProjectDocumentOptions());
            var uploadRoot = new UploadRootProvider(optionsWrapper, documentOptions);
            var photoService = new ProjectPhotoService(db, clock, new RecordingAudit(), optionsWrapper, uploadRoot, NullLogger<ProjectPhotoService>.Instance);

            await using var stream = await CreateImageStreamAsync(1600, 1200);
            var added = await photoService.AddAsync(6, stream, "cover.png", "image/png", "creator", true, "Cover", CancellationToken.None);

            var overview = CreateOverviewPage(db, clock);
            ConfigurePageContext(overview);

            var firstResult = await overview.OnGetAsync(6, CancellationToken.None);
            Assert.IsType<PageResult>(firstResult);
            Assert.Single(overview.Photos);
            Assert.Equal(added.Id, overview.CoverPhoto?.Id);
            Assert.Equal(added.Version, overview.CoverPhotoVersion);

            await photoService.RemoveAsync(6, added.Id, "creator", CancellationToken.None);
            db.ChangeTracker.Clear();

            var secondResult = await overview.OnGetAsync(6, CancellationToken.None);
            Assert.IsType<PageResult>(secondResult);
            Assert.Empty(overview.Photos);
            Assert.Null(overview.CoverPhoto);
            Assert.Null(overview.CoverPhotoVersion);
        }
        finally
        {
            ResetUploadRoot();
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task View_SetsExtendedCacheHeaders_ForPhotos()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 7, leadPoUserId: "viewer");
        var photo = new ProjectPhoto
        {
            Id = 8,
            ProjectId = 7,
            StorageKey = "photos/7/8.png",
            OriginalFileName = "cover.png",
            ContentType = "image/png",
            Width = 800,
            Height = 600,
            Ordinal = 1,
            UpdatedUtc = new DateTime(2024, 10, 8, 12, 0, 0, DateTimeKind.Utc)
        };
        db.ProjectPhotos.Add(photo);
        await db.SaveChangesAsync();

        var userContext = new FakeUserContext("viewer", isProjectOfficer: true);
        var photoService = new StubPhotoService
        {
            DerivativeToReturn = (new MemoryStream(new byte[] { 1, 2, 3 }), "image/webp")
        };
        var page = new PhotoViewModel(db, userContext, photoService);
        ConfigurePageContext(page, userContext.User);
        page.Request.Headers[HeaderNames.Accept] = "image/webp";

        var result = await page.OnGetAsync(7, 8, "sm", CancellationToken.None);

        Assert.IsType<FileStreamResult>(result);
        var headers = page.Response.GetTypedHeaders();
        Assert.Equal(TimeSpan.FromDays(7), headers.CacheControl?.MaxAge);
        Assert.Equal("Accept", page.Response.Headers[HeaderNames.Vary]);
        Assert.NotNull(headers.ETag);
        Assert.Contains("webp", headers.ETag!.Tag.Value, StringComparison.OrdinalIgnoreCase);
        Assert.True(photoService.OpenDerivativeCalled);
        Assert.True(photoService.PreferWebpRequested);
    }

    [Fact]
    public async Task View_SetsExtendedCacheHeaders_ForPlaceholder()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 9, leadPoUserId: "viewer");

        var userContext = new FakeUserContext("viewer", isProjectOfficer: true);
        var photoService = new StubPhotoService();
        var page = new PhotoViewModel(db, userContext, photoService);
        ConfigurePageContext(page, userContext.User);

        var result = await page.OnGetAsync(9, 999, "sm", CancellationToken.None);

        Assert.IsType<FileContentResult>(result);
        var headers = page.Response.GetTypedHeaders();
        Assert.Equal(TimeSpan.FromDays(7), headers.CacheControl?.MaxAge);
        Assert.Equal("Accept", page.Response.Headers[HeaderNames.Vary]);
    }

    [Fact]
    public async Task View_ReturnsNotModified_WhenIfNoneMatchMatches()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 11, leadPoUserId: "viewer");
        var photo = new ProjectPhoto
        {
            Id = 12,
            ProjectId = 11,
            StorageKey = "photos/11/12.png",
            OriginalFileName = "cover.png",
            ContentType = "image/png",
            Width = 800,
            Height = 600,
            Ordinal = 1,
            Version = 3,
            UpdatedUtc = new DateTime(2024, 10, 9, 15, 30, 0, DateTimeKind.Utc)
        };
        db.ProjectPhotos.Add(photo);
        await db.SaveChangesAsync();

        var userContext = new FakeUserContext("viewer", isProjectOfficer: true);
        var photoService = new StubPhotoService();
        var page = new PhotoViewModel(db, userContext, photoService);
        ConfigurePageContext(page, userContext.User);

        var etag = $"\"pp-{photo.ProjectId}-{photo.Id}-v{photo.Version}-sm\"";
        page.Request.Headers["If-None-Match"] = etag;

        var result = await page.OnGetAsync(11, 12, "sm", CancellationToken.None);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status304NotModified, status.StatusCode);
        Assert.False(photoService.OpenDerivativeCalled);

        var headers = page.Response.GetTypedHeaders();
        Assert.Equal(TimeSpan.FromDays(7), headers.CacheControl?.MaxAge);
        Assert.Equal(etag, headers.ETag?.Tag);
        Assert.Equal(DateTime.SpecifyKind(photo.UpdatedUtc, DateTimeKind.Utc), headers.LastModified?.UtcDateTime);
    }

    [Fact]
    public async Task View_ReturnsNotModified_ForPlaceholderWhenIfNoneMatchMatches()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 13, leadPoUserId: "viewer");

        var userContext = new FakeUserContext("viewer", isProjectOfficer: true);
        var photoService = new StubPhotoService();
        var page = new PhotoViewModel(db, userContext, photoService);
        ConfigurePageContext(page, userContext.User);

        const string etag = "\"pp-placeholder-sm\"";
        page.Request.Headers["If-None-Match"] = etag;

        var result = await page.OnGetAsync(13, 999, "sm", CancellationToken.None);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status304NotModified, status.StatusCode);
        Assert.False(photoService.OpenDerivativeCalled);

        var headers = page.Response.GetTypedHeaders();
        Assert.Equal(TimeSpan.FromDays(7), headers.CacheControl?.MaxAge);
        Assert.Equal(etag, headers.ETag?.Tag);
    }

    [Fact]
    public async Task Upload_ForbidsUnassignedProjectOfficer_OnGet()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 21, hodUserId: "hod-owner", leadPoUserId: "lead-owner");

        var userContext = new FakeUserContext("intruder", isProjectOfficer: true);
        var page = CreateUploadPage(db, userContext);
        ConfigurePageContext(page, userContext.User);

        var result = await page.OnGetAsync(21, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Upload_ForbidsUnassignedProjectOfficer_OnPost()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 22, hodUserId: "hod-owner", leadPoUserId: "lead-owner");

        var userContext = new FakeUserContext("intruder", isProjectOfficer: true);
        var page = CreateUploadPage(db, userContext);
        ConfigurePageContext(page, userContext.User);

        page.Input = new UploadInput
        {
            ProjectId = 22,
            RowVersion = Convert.ToBase64String(db.Projects.Single(p => p.Id == 22).RowVersion)
        };

        var result = await page.OnPostAsync(22, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Edit_ForbidsUnassignedProjectOfficer_OnGet()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 23, hodUserId: "hod-owner", leadPoUserId: "lead-owner");

        db.ProjectPhotos.Add(new ProjectPhoto
        {
            Id = 24,
            ProjectId = 23,
            StorageKey = "photos/23/24.png",
            OriginalFileName = "cover.png",
            ContentType = "image/png",
            Width = 800,
            Height = 600,
            Ordinal = 1,
            Version = 1
        });
        await db.SaveChangesAsync();

        var userContext = new FakeUserContext("intruder", isProjectOfficer: true);
        var page = CreateEditPage(db, userContext);
        ConfigurePageContext(page, userContext.User);

        var result = await page.OnGetAsync(23, 24, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Edit_ForbidsUnassignedProjectOfficer_OnPost()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 25, hodUserId: "hod-owner", leadPoUserId: "lead-owner");

        db.ProjectPhotos.Add(new ProjectPhoto
        {
            Id = 26,
            ProjectId = 25,
            StorageKey = "photos/25/26.png",
            OriginalFileName = "cover.png",
            ContentType = "image/png",
            Width = 800,
            Height = 600,
            Ordinal = 1,
            Version = 1
        });
        await db.SaveChangesAsync();

        var userContext = new FakeUserContext("intruder", isProjectOfficer: true);
        var page = CreateEditPage(db, userContext);
        ConfigurePageContext(page, userContext.User);

        page.Input = new EditInput
        {
            ProjectId = 25,
            PhotoId = 26,
            RowVersion = Convert.ToBase64String(db.Projects.Single(p => p.Id == 25).RowVersion)
        };

        var result = await page.OnPostAsync(25, 26, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    private static PhotosIndexModel CreateIndexPage(ApplicationDbContext db, FakeUserContext userContext)
    {
        return new PhotosIndexModel(db, userContext, new ThrowingPhotoService(), NullLogger<PhotosIndexModel>.Instance);
    }

    private static UploadModel CreateUploadPage(ApplicationDbContext db, FakeUserContext userContext)
    {
        return new UploadModel(db, userContext, new StubPhotoService(), NullLogger<UploadModel>.Instance);
    }

    private static EditModel CreateEditPage(ApplicationDbContext db, FakeUserContext userContext)
    {
        return new EditModel(db, userContext, new StubPhotoService(), NullLogger<EditModel>.Instance);
    }

    private static ProjectsOverviewModel CreateOverviewPage(ApplicationDbContext db, IClock clock)
    {
        var procure = new ProjectProcurementReadService(db);
        var timeline = new ProjectTimelineReadService(db, clock);
        var planRead = new PlanReadService(db);
        var planCompare = new PlanCompareService(db);
        var userManager = CreateUserManager(db);
        return new ProjectsOverviewModel(db, procure, timeline, userManager, planRead, planCompare, NullLogger<ProjectsOverviewModel>.Instance, clock);
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

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task SeedProjectAsync(ApplicationDbContext db, int projectId, string? hodUserId = null, string? leadPoUserId = null)
    {
        db.Projects.Add(new Project
        {
            Id = projectId,
            Name = $"Project {projectId}",
            CreatedByUserId = "creator",
            HodUserId = hodUserId,
            LeadPoUserId = leadPoUserId,
            RowVersion = new byte[] { 2 }
        });
        await db.SaveChangesAsync();
    }

    private static void ConfigurePageContext(PageModel page, ClaimsPrincipal? user = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = new ServiceCollection()
            .AddSingleton<ITempDataProvider, SessionStateTempDataProvider>()
            .BuildServiceProvider();
        httpContext.User = user ?? new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "page-user")
        }, "Test"));

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        page.PageContext = new PageContext(actionContext)
        {
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        };
        page.TempData = new TempDataDictionary(httpContext, httpContext.RequestServices.GetRequiredService<ITempDataProvider>());
        page.Url = new SimpleUrlHelper(page.PageContext);
    }

    private static ProjectPhotoOptions CreateOptions()
    {
        return new ProjectPhotoOptions
        {
            Derivatives = new Dictionary<string, ProjectPhotoDerivativeOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["xl"] = new ProjectPhotoDerivativeOptions { Width = 1600, Height = 1200, Quality = 90 }
            }
        };
    }

    private static async Task<MemoryStream> CreateImageStreamAsync(int width, int height)
    {
        var stream = new MemoryStream();
        using var image = new Image<Rgba32>(width, height, new Rgba32(40, 120, 220, 255));
        await image.SaveAsync(stream, new PngEncoder());
        stream.Position = 0;
        return stream;
    }

    private static string CreateTempRoot()
    {
        return Path.Combine(Path.GetTempPath(), "pm-photos-tests", Guid.NewGuid().ToString("N"));
    }

    private static void SetUploadRoot(string root)
    {
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("PM_UPLOAD_ROOT", root);
    }

    private static void ResetUploadRoot()
    {
        Environment.SetEnvironmentVariable("PM_UPLOAD_ROOT", null);
    }

    private static void CleanupTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }

    private sealed class ThrowingPhotoService : IProjectPhotoService
    {
        public Task<ProjectPhoto> AddAsync(int projectId, Stream content, string originalFileName, string? contentType, string userId, bool setAsCover, string? caption, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<ProjectPhoto> AddAsync(int projectId, Stream content, string originalFileName, string? contentType, string userId, bool setAsCover, string? caption, ProjectPhotoCrop crop, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<ProjectPhoto?> ReplaceAsync(int projectId, int photoId, Stream content, string originalFileName, string? contentType, string userId, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<ProjectPhoto?> ReplaceAsync(int projectId, int photoId, Stream content, string originalFileName, string? contentType, string userId, ProjectPhotoCrop crop, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<ProjectPhoto?> UpdateCaptionAsync(int projectId, int photoId, string? caption, string userId, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<ProjectPhoto?> UpdateCropAsync(int projectId, int photoId, ProjectPhotoCrop crop, string userId, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<bool> RemoveAsync(int projectId, int photoId, string userId, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task ReorderAsync(int projectId, IReadOnlyList<int> orderedPhotoIds, string userId, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<(Stream Stream, string ContentType)?> OpenDerivativeAsync(int projectId,
                                                                             int photoId,
                                                                             string sizeKey,
                                                                             bool preferWebp,
                                                                             CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public string GetDerivativePath(ProjectPhoto photo, string sizeKey, bool preferWebp)
            => throw new NotImplementedException();
    }

    private sealed class StubPhotoService : IProjectPhotoService
    {
        public (Stream Stream, string ContentType)? DerivativeToReturn { get; set; }

        public bool OpenDerivativeCalled { get; private set; }

        public bool? PreferWebpRequested { get; private set; }

        public Task<ProjectPhoto> AddAsync(int projectId, Stream content, string originalFileName, string? contentType, string userId, bool setAsCover, string? caption, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<ProjectPhoto> AddAsync(int projectId, Stream content, string originalFileName, string? contentType, string userId, bool setAsCover, string? caption, ProjectPhotoCrop crop, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<ProjectPhoto?> ReplaceAsync(int projectId, int photoId, Stream content, string originalFileName, string? contentType, string userId, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<ProjectPhoto?> ReplaceAsync(int projectId, int photoId, Stream content, string originalFileName, string? contentType, string userId, ProjectPhotoCrop crop, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<ProjectPhoto?> UpdateCaptionAsync(int projectId, int photoId, string? caption, string userId, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<ProjectPhoto?> UpdateCropAsync(int projectId, int photoId, ProjectPhotoCrop crop, string userId, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<bool> RemoveAsync(int projectId, int photoId, string userId, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task ReorderAsync(int projectId, IReadOnlyList<int> orderedPhotoIds, string userId, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<(Stream Stream, string ContentType)?> OpenDerivativeAsync(int projectId,
                                                                             int photoId,
                                                                             string sizeKey,
                                                                             bool preferWebp,
                                                                             CancellationToken cancellationToken)
        {
            OpenDerivativeCalled = true;
            PreferWebpRequested = preferWebp;
            return Task.FromResult(DerivativeToReturn);
        }

        public string GetDerivativePath(ProjectPhoto photo, string sizeKey, bool preferWebp)
            => throw new NotImplementedException();
    }

    private sealed class FakeUserContext : IUserContext
    {
        public FakeUserContext(string userId, bool isAdmin = false, bool isHoD = false, bool isProjectOfficer = false)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId)
            };

            if (isAdmin)
            {
                claims.Add(new Claim(ClaimTypes.Role, "Admin"));
            }

            if (isHoD)
            {
                claims.Add(new Claim(ClaimTypes.Role, "HoD"));
            }

            if (isProjectOfficer)
            {
                claims.Add(new Claim(ClaimTypes.Role, "Project Officer"));
            }

            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
            UserId = userId;
        }

        public ClaimsPrincipal User { get; }

        public string? UserId { get; }
    }

    private sealed class SimpleUrlHelper : IUrlHelper
    {
        public SimpleUrlHelper(ActionContext context)
        {
            ActionContext = context;
        }

        public ActionContext ActionContext { get; }

        public string? Action(UrlActionContext actionContext) => throw new NotImplementedException();

        public string? Content(string? contentPath) => contentPath;

        public bool IsLocalUrl(string? url) => true;

        public string? Link(string? routeName, object? values) => throw new NotImplementedException();

        public string? RouteUrl(UrlRouteContext routeContext) => throw new NotImplementedException();

        public string? RouteUrl(string? routeName, object? values, string? protocol, string? host, string? fragment) => throw new NotImplementedException();

        public string? RouteUrl(string? routeName, object? values) => throw new NotImplementedException();

        public string? RouteUrl(string? routeName, object? values, string? protocol, string? host) => throw new NotImplementedException();

        public string? Page(string? pageName, string? pageHandler, object? values, string? protocol, string? host, string? fragment)
        {
            return $"/Pages{pageName}?{values}";
        }

        public string? Page(string? pageName, string? pageHandler, object? values, string? protocol) => throw new NotImplementedException();

        public string? Page(string? pageName, string? pageHandler, object? values) => throw new NotImplementedException();

        public string? Page(string? pageName, string? pageHandler, object? values, string? protocol, string? host) => throw new NotImplementedException();

        public string? Page(string? pageName, object? values) => Page(pageName, null, values, null, null, null);

        public string? RouteUrl(string? routeName, object? values, string? protocol) => throw new NotImplementedException();

        public string? Action(string? action, string? controller, object? values, string? protocol, string? host, string? fragment) => throw new NotImplementedException();

        public string? Action(string? action, string? controller, object? values) => throw new NotImplementedException();

        public string? Action(string? action, string? controller, object? values, string? protocol) => throw new NotImplementedException();
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now)
        {
            UtcNow = now;
        }

        public DateTimeOffset UtcNow { get; }
    }
}
