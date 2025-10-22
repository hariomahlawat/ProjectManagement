using System;
using System.Collections.Generic;
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
