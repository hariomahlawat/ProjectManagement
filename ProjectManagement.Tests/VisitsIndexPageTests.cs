using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
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
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Pages.Visits;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class VisitsIndexPageTests
{
    [Fact]
    public async Task OnPostExportAsync_AllowsUsersWithoutManagePermission()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();
        var visitType = new VisitType
        {
            Id = Guid.NewGuid(),
            Name = "Industry Visit",
            CreatedAtUtc = clock.UtcNow,
            CreatedByUserId = "seed-user",
            IsActive = true,
            RowVersion = new byte[] { 0x01 }
        };

        var visit = new Visit
        {
            Id = Guid.NewGuid(),
            VisitTypeId = visitType.Id,
            VisitType = visitType,
            DateOfVisit = new DateOnly(2024, 1, 5),
            VisitorName = "Guest of Honour",
            Strength = 8,
            Remarks = "Plant tour.",
            CreatedAtUtc = clock.UtcNow,
            CreatedByUserId = "seed-user",
            LastModifiedAtUtc = clock.UtcNow,
            LastModifiedByUserId = "seed-user",
            RowVersion = new byte[] { 0x02 }
        };

        visitType.Visits.Add(visit);

        db.VisitTypes.Add(visitType);
        db.Visits.Add(visit);
        await db.SaveChangesAsync();

        var visitPhotoService = new StubVisitPhotoService();
        var visitService = new VisitService(db, clock, audit, visitPhotoService);
        var visitTypeService = new VisitTypeService(db, clock, audit);

        var exportFile = new VisitExportFile(
            "visits-all-20240101T000000Z.xlsx",
            new byte[] { 0x10, 0x20, 0x30 },
            VisitExportFile.ExcelContentType);

        var exportService = new StubVisitExportService(
            VisitExportResult.FromFile(exportFile),
            VisitExportResult.Failure("Unexpected PDF export"));

        using var userManager = CreateUserManager(db);
        var page = new IndexModel(visitService, visitTypeService, exportService, userManager);

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
    public async Task OnGetAsync_PagesSortsAndKeepsWholeRegisterFiguresWhenFiltered()
    {
        await using var db = CreateDb();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var (dignitaries, courses) = await SeedTypesAsync(db);

        // 30 course visits over the last 30 days and one dignitary visit two years ago.
        for (var i = 0; i < 30; i++)
        {
            db.Visits.Add(NewVisit(courses, today.AddDays(-i), $"Course {i:00}", 10 + i));
        }

        db.Visits.Add(NewVisit(dignitaries, today.AddYears(-2), "Old dignitary", 3));
        await db.SaveChangesAsync();

        using var userManager = CreateUserManager(db);
        var page = CreatePage(db, userManager);
        page.VisitTypeId = courses.Id;
        page.Sort = "strength";
        page.PageNumber = 2;

        await page.OnGetAsync(CancellationToken.None);

        // Filtered register: 30 course visits, the second page of 25 by largest group.
        Assert.Equal(30, page.TotalItems);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal(5, page.Items.Count);
        Assert.Equal(new[] { 14, 13, 12, 11, 10 }, page.Items.Select(v => v.Strength));
        Assert.False(page.GroupByMonth);

        // Headline figures cover the whole register: the old visit counts as on record only.
        Assert.Equal(31, page.Summary.TotalVisits);
        Assert.Equal(30, page.Summary.VisitsLast30);
        Assert.Equal(today, page.Summary.LatestVisit);

        // Both types stay filterable and keep their colour slots (by name).
        Assert.Equal(1, page.ColourSlotFor(courses.Id));
        Assert.Equal(2, page.ColourSlotFor(dignitaries.Id));
    }

    [Fact]
    public async Task OnGetAsync_ClampsAPageBeyondTheLastToTheLastPage()
    {
        await using var db = CreateDb();
        var (_, courses) = await SeedTypesAsync(db);
        db.Visits.Add(NewVisit(courses, DateOnly.FromDateTime(DateTime.Today), "Only visit", 4));
        await db.SaveChangesAsync();

        using var userManager = CreateUserManager(db);
        var page = CreatePage(db, userManager);
        page.PageNumber = 9;
        page.PageSize = 7;

        await page.OnGetAsync(CancellationToken.None);

        Assert.Equal(25, page.PageSize);
        Assert.Equal(1, page.PageNumber);
        Assert.Single(page.Items);
        Assert.True(page.GroupByMonth);
    }

    [Fact]
    public async Task OnGetAsync_RemarksPreviewDropsTheRepeatedVisitorName()
    {
        await using var db = CreateDb();
        var (_, courses) = await SeedTypesAsync(db);
        var visit = NewVisit(courses, DateOnly.FromDateTime(DateTime.Today), "Long Gunnery Staff Course - 5003", 60);
        visit.Remarks = "Long Gunnery Staff Course - 5003. 48 x Indian Student Officers visited SDD.";
        db.Visits.Add(visit);
        await db.SaveChangesAsync();

        using var userManager = CreateUserManager(db);
        var page = CreatePage(db, userManager);

        await page.OnGetAsync(CancellationToken.None);

        Assert.Equal("48 x Indian Student Officers visited SDD.", Assert.Single(page.Items).RemarksPreview);
    }

    [Fact]
    public async Task OnPostExportAsync_FailureReturnsToTheRegisterWithFiltersAndMessage()
    {
        await using var db = CreateDb();
        using var userManager = CreateUserManager(db);
        var exportService = new StubVisitExportService(
            VisitExportResult.Failure("Start date must be before end date."),
            VisitExportResult.Failure("unused"));
        var page = new IndexModel(new VisitService(db, FakeClock.AtUtc(DateTimeOffset.UtcNow), new RecordingAudit(), new StubVisitPhotoService()),
            new VisitTypeService(db, FakeClock.AtUtc(DateTimeOffset.UtcNow), new RecordingAudit()),
            exportService,
            userManager);
        ConfigurePageContext(page, CreatePrincipal("viewer", "Viewer"));
        page.Q = "course";
        page.From = "2026-05-01";
        page.To = "2026-01-01";

        var result = await page.OnPostExportAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("course", redirect.RouteValues?["Q"]);
        Assert.Equal("2026-05-01", redirect.RouteValues?["From"]);
        Assert.Equal("Start date must be before end date.", page.TempData["ToastError"]);
    }

    [Fact]
    public void AllPage_RedirectsToTheRegisterKeepingFilters()
    {
        var page = new AllModel { Q = "NDC", PageNumber = 3, PageSize = 50, From = " " };

        var redirect = Assert.IsType<RedirectToPageResult>(page.OnGet());

        Assert.Equal("Index", redirect.PageName);
        Assert.Equal("NDC", redirect.RouteValues?["Q"]);
        Assert.Equal(3, redirect.RouteValues?["PageNumber"]);
        Assert.Equal(50, redirect.RouteValues?["PageSize"]);
        Assert.Null(redirect.RouteValues?["From"]);
    }

    [Fact]
    public async Task RecordPage_StartsFromTodayInIstAndOpensTheNewVisitAfterSaving()
    {
        await using var db = CreateDb();
        var (_, courses) = await SeedTypesAsync(db);
        using var userManager = CreateUserManager(db);
        var clock = FakeClock.AtUtc(DateTimeOffset.UtcNow);
        var page = new NewModel(
            new VisitService(db, clock, new RecordingAudit(), new StubVisitPhotoService()),
            new VisitTypeService(db, clock, new RecordingAudit()),
            new StubVisitPhotoService(),
            userManager,
            Options.Create(new VisitPhotoOptions()));
        ConfigurePageContext(page, CreatePrincipal("officer", "Admin"));

        await page.OnGetAsync(CancellationToken.None);

        var todayIst = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ProjectManagement.Utilities.TimeZoneHelper.GetIst()).DateTime);
        Assert.Equal(todayIst, page.Input.DateOfVisit);

        page.Input = new NewModel.InputModel
        {
            DateOfVisit = todayIst,
            VisitTypeId = courses.Id,
            VisitorName = "Long Gunnery Staff Course 5003",
            Strength = 48
        };
        page.SaveMode = "save";

        var result = await page.OnPostAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Details", redirect.PageName);
        var saved = Assert.Single(db.Visits);
        Assert.Equal(saved.Id, redirect.RouteValues?["id"]);
        Assert.Equal("Visit recorded.", page.TempData["ToastSuccess"]);
    }

    [Fact]
    public async Task EditPage_KeepsWhatWasTypedWhenTheSaveIsRejected()
    {
        await using var db = CreateDb();
        var (_, courses) = await SeedTypesAsync(db);
        var visit = NewVisit(courses, new DateOnly(2026, 5, 9), "Original name", 60);
        db.Visits.Add(visit);
        await db.SaveChangesAsync();

        using var userManager = CreateUserManager(db);
        var clock = FakeClock.AtUtc(DateTimeOffset.UtcNow);
        var page = new EditModel(
            new VisitService(db, clock, new RecordingAudit(), new StubVisitPhotoService()),
            new VisitTypeService(db, clock, new RecordingAudit()),
            new StubVisitPhotoService(),
            userManager,
            Options.Create(new VisitPhotoOptions()));
        ConfigurePageContext(page, CreatePrincipal("officer", "Admin"));
        page.Input = new EditModel.InputModel
        {
            DateOfVisit = new DateOnly(2026, 5, 10),
            VisitTypeId = courses.Id,
            VisitorName = "Corrected name",
            Strength = 0
        };
        page.ModelState.AddModelError("Input.Strength", "Enter at least one visitor.");

        var result = await page.OnPostAsync(visit.Id, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal("Corrected name", page.Input.VisitorName);
        Assert.Equal(new DateOnly(2026, 5, 10), page.Input.DateOfVisit);
        Assert.Equal("Original name", page.SavedVisitorName);
    }

    [Theory]
    [InlineData("image/jpeg", "jpg")]
    [InlineData("image/png", "png")]
    [InlineData("image/webp", "webp")]
    [InlineData(null, "jpg")]
    public void PhotoDownload_IsNamedByVisitDateAndPhoto(string? contentType, string extension)
    {
        var photoId = Guid.Parse("1a2b3c4d-5e6f-4a1b-9c2d-3e4f5a6b7c8d");

        var name = ViewPhotoModel.DownloadName(new DateOnly(2026, 6, 15), photoId, contentType);

        Assert.Equal($"visit-2026-06-15-1a2b3c4d.{extension}", name);
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task<(VisitType Dignitaries, VisitType Courses)> SeedTypesAsync(ApplicationDbContext db)
    {
        VisitType Type(string name) => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = "seed-user",
            IsActive = true,
            RowVersion = new byte[] { 0x01 }
        };

        var dignitaries = Type("Mil Dignitaries");
        var courses = Type("Course Visits");
        db.VisitTypes.AddRange(dignitaries, courses);
        await db.SaveChangesAsync();
        return (dignitaries, courses);
    }

    private static Visit NewVisit(VisitType type, DateOnly date, string visitor, int strength) => new()
    {
        Id = Guid.NewGuid(),
        VisitTypeId = type.Id,
        DateOfVisit = date,
        VisitorName = visitor,
        Strength = strength,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        CreatedByUserId = "seed-user",
        RowVersion = new byte[] { 0x02 }
    };

    private static IndexModel CreatePage(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        var clock = FakeClock.AtUtc(DateTimeOffset.UtcNow);
        var page = new IndexModel(
            new VisitService(db, clock, new RecordingAudit(), new StubVisitPhotoService()),
            new VisitTypeService(db, clock, new RecordingAudit()),
            new StubVisitExportService(VisitExportResult.Failure("unused"), VisitExportResult.Failure("unused")),
            userManager);
        ConfigurePageContext(page, CreatePrincipal("viewer", "Viewer"));
        return page;
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

    private sealed class StubVisitPhotoService : IVisitPhotoService
    {
        public Task<IReadOnlyList<VisitPhoto>> GetPhotosAsync(Guid visitId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<VisitPhoto>>(Array.Empty<VisitPhoto>());

        public Task<VisitPhotoUploadResult> UploadAsync(Guid visitId, System.IO.Stream content, string originalFileName, string? contentType, string? caption, string userId, CancellationToken cancellationToken)
            => Task.FromResult(VisitPhotoUploadResult.Invalid("Not supported"));

        public Task<VisitPhotoDeletionResult> RemoveAsync(Guid visitId, Guid photoId, string userId, CancellationToken cancellationToken)
            => Task.FromResult(VisitPhotoDeletionResult.Failed());

        public Task<VisitPhotoSetCoverResult> SetCoverAsync(Guid visitId, Guid photoId, string userId, CancellationToken cancellationToken)
            => Task.FromResult(VisitPhotoSetCoverResult.Failed());

        public Task RemoveAllAsync(Guid visitId, IReadOnlyCollection<VisitPhoto> photos, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<VisitPhotoAsset?> OpenAsync(Guid visitId, Guid photoId, string size, CancellationToken cancellationToken)
            => Task.FromResult<VisitPhotoAsset?>(null);
    }

    private sealed class StubVisitExportService : IVisitExportService
    {
        private readonly VisitExportResult _excelResult;
        private readonly VisitExportResult _pdfResult;

        public StubVisitExportService(VisitExportResult excelResult, VisitExportResult pdfResult)
        {
            _excelResult = excelResult;
            _pdfResult = pdfResult;
        }

        public VisitExportRequest? LastExcelRequest { get; private set; }

        public VisitExportRequest? LastPdfRequest { get; private set; }

        public Task<VisitExportResult> ExportAsync(VisitExportRequest request, CancellationToken cancellationToken)
        {
            LastExcelRequest = request;
            return Task.FromResult(_excelResult);
        }

        public Task<VisitExportResult> ExportPdfAsync(VisitExportRequest request, CancellationToken cancellationToken)
        {
            LastPdfRequest = request;
            return Task.FromResult(_pdfResult);
        }
    }
}
