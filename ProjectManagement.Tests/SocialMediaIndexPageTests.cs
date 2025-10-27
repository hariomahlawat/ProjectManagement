using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Pages.SocialMedia;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class SocialMediaIndexPageTests
{
    [Fact]
    public async Task OnPostExportAsync_AllowsUsersWithoutManagePermission()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        var eventType = SocialMediaTestData.CreateEventType(name: "Milestone update");
        var platform = SocialMediaTestData.CreatePlatform(name: "Threads");
        var socialEvent = SocialMediaTestData.CreateEvent(eventType.Id, platform.Id, platform: platform);

        db.SocialMediaEventTypes.Add(eventType);
        db.SocialMediaPlatforms.Add(platform);
        db.SocialMediaEvents.Add(socialEvent);
        await db.SaveChangesAsync();

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero));
        var photoService = new StubSocialMediaEventPhotoService();
        var eventService = new SocialMediaEventService(db, clock, photoService);
        var audit = new RecordingAudit();
        var platformService = new SocialMediaPlatformService(db, clock, audit);

        var exportFile = new SocialMediaExportFile(
            "social-media-events-all-20240101T080000Z.xlsx",
            new byte[] { 0x01, 0x02, 0x03 },
            SocialMediaExportFile.ExcelContentType);
        var exportService = new StubSocialMediaExportService(
            SocialMediaExportResult.FromFile(exportFile),
            SocialMediaExportResult.Failure("Unexpected PDF call"));

        var authorization = new DenyManageAuthorizationService();
        using var userManager = CreateUserManager(db);
        var page = new IndexModel(eventService, exportService, platformService, authorization, userManager);

        ConfigurePageContext(page, CreatePrincipal("viewer", "Viewer"));

        var result = await page.OnPostExportAsync(CancellationToken.None);

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal(exportFile.ContentType, fileResult.ContentType);
        Assert.Equal(exportFile.FileName, fileResult.FileDownloadName);
        Assert.Equal(exportFile.Content, fileResult.FileContents);

        Assert.False(page.CanManage);
        Assert.NotNull(exportService.LastExcelRequest);
        Assert.Equal("viewer", exportService.LastExcelRequest?.RequestedByUserId);
    }

    [Fact]
    public async Task OnPostExportPdfAsync_AllowsUsersWithoutManagePermission()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        var eventType = SocialMediaTestData.CreateEventType(name: "Campaign kickoff");
        var platform = SocialMediaTestData.CreatePlatform(name: "Instagram");
        var socialEvent = SocialMediaTestData.CreateEvent(eventType.Id, platform.Id, platform: platform);

        db.SocialMediaEventTypes.Add(eventType);
        db.SocialMediaPlatforms.Add(platform);
        db.SocialMediaEvents.Add(socialEvent);
        await db.SaveChangesAsync();

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 2, 1, 8, 0, 0, TimeSpan.Zero));
        var photoService = new StubSocialMediaEventPhotoService();
        var eventService = new SocialMediaEventService(db, clock, photoService);
        var audit = new RecordingAudit();
        var platformService = new SocialMediaPlatformService(db, clock, audit);

        var pdfFile = new SocialMediaExportFile(
            "social-media-events-all-20240201T080000Z.pdf",
            new byte[] { 0x10, 0x20, 0x30 },
            SocialMediaExportFile.PdfContentType);
        var exportService = new StubSocialMediaExportService(
            SocialMediaExportResult.Failure("Unexpected Excel call"),
            SocialMediaExportResult.FromFile(pdfFile));

        var authorization = new DenyManageAuthorizationService();
        using var userManager = CreateUserManager(db);
        var page = new IndexModel(eventService, exportService, platformService, authorization, userManager);

        ConfigurePageContext(page, CreatePrincipal("viewer", "Viewer"));

        var result = await page.OnPostExportPdfAsync(CancellationToken.None);

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal(pdfFile.ContentType, fileResult.ContentType);
        Assert.Equal(pdfFile.FileName, fileResult.FileDownloadName);
        Assert.Equal(pdfFile.Content, fileResult.FileContents);

        Assert.False(page.CanManage);
        Assert.NotNull(exportService.LastPdfRequest);
        Assert.Equal("viewer", exportService.LastPdfRequest?.RequestedByUserId);
    }

    [Fact]
    public async Task OnPostExportAsync_WhenExportFails_ShowsValidationErrors()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        var eventType = SocialMediaTestData.CreateEventType(name: "Recap");
        var platform = SocialMediaTestData.CreatePlatform(name: "Instagram");
        var socialEvent = SocialMediaTestData.CreateEvent(eventType.Id, platform.Id, platform: platform);

        db.SocialMediaEventTypes.Add(eventType);
        db.SocialMediaPlatforms.Add(platform);
        db.SocialMediaEvents.Add(socialEvent);
        await db.SaveChangesAsync();

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 3, 1, 7, 0, 0, TimeSpan.Zero));
        var photoService = new StubSocialMediaEventPhotoService();
        var eventService = new SocialMediaEventService(db, clock, photoService);
        var audit = new RecordingAudit();
        var platformService = new SocialMediaPlatformService(db, clock, audit);

        var exportService = new StubSocialMediaExportService(
            SocialMediaExportResult.Failure("Unable to generate Excel export."),
            SocialMediaExportResult.Failure("Unexpected PDF call"));

        var authorization = new DenyManageAuthorizationService();
        using var userManager = CreateUserManager(db);
        var page = new IndexModel(eventService, exportService, platformService, authorization, userManager);

        ConfigurePageContext(page, CreatePrincipal("author", "Editor"));

        var result = await page.OnPostExportAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.True(page.ViewData.ContainsKey("ShowExportModal"));
        var showModal = Assert.IsType<bool>(page.ViewData["ShowExportModal"]);
        Assert.True(showModal);
        Assert.False(page.ModelState.IsValid);
        var errors = page.ModelState[string.Empty].Errors;
        Assert.Contains(errors, error => error.ErrorMessage == "Unable to generate Excel export.");
        var toast = Assert.IsType<string>(page.TempData["ToastError"]);
        Assert.Equal("Unable to generate Excel export.", toast);
        Assert.NotNull(exportService.LastExcelRequest);
        Assert.Equal("author", exportService.LastExcelRequest?.RequestedByUserId);
        Assert.NotEmpty(page.Events);
    }

    private static void ConfigurePageContext(PageModel page, ClaimsPrincipal user)
    {
        var httpContext = new DefaultHttpContext { User = user };
        var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor());

        page.PageContext = new PageContext(actionContext)
        {
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        };

        page.TempData = new TempDataDictionary(httpContext, new DictionaryTempDataProvider());
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

    private sealed class DictionaryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();

        public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
        {
        }
    }

    private sealed class DenyManageAuthorizationService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(AuthorizationResult.Failed());
    }

    private sealed class StubSocialMediaExportService : ISocialMediaExportService
    {
        private readonly SocialMediaExportResult _excelResult;
        private readonly SocialMediaExportResult _pdfResult;

        public StubSocialMediaExportService(SocialMediaExportResult excelResult, SocialMediaExportResult pdfResult)
        {
            _excelResult = excelResult;
            _pdfResult = pdfResult;
        }

        public SocialMediaExportRequest? LastExcelRequest { get; private set; }

        public SocialMediaExportRequest? LastPdfRequest { get; private set; }

        public Task<SocialMediaExportResult> ExportAsync(SocialMediaExportRequest request, CancellationToken cancellationToken)
        {
            LastExcelRequest = request;
            return Task.FromResult(_excelResult);
        }

        public Task<SocialMediaExportResult> ExportPdfAsync(SocialMediaExportRequest request, CancellationToken cancellationToken)
        {
            LastPdfRequest = request;
            return Task.FromResult(_pdfResult);
        }
    }
}
