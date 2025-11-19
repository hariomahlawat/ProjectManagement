using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
using ProjectManagement.Application.Ipr;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Pages.Ipr;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure.Data;
using ProjectManagement.Models;
using ProjectManagement.Tests.Fakes;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Services.Storage;

namespace ProjectManagement.Tests;

public sealed class IprIndexPageTests
{
    [Fact]
    public async Task OnGetAsync_WhenUserCannotEdit_DisablesEditing()
    {
        await using var db = CreateDbContext();
        var readService = new StubIprReadService();
        var writeService = new StubIprWriteService();
        var authorizationService = new DenyAuthorizationService();
        using var userManager = CreateUserManager(db);
        var exportService = new StubIprExportService();

        var page = new IndexModel(db, readService, writeService, authorizationService, userManager, exportService);
        ConfigurePageContext(page, CreatePrincipal("viewer", "Viewer"));
        page.Mode = "create";

        var result = await page.OnGetAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.False(page.CanEdit);
        Assert.Null(page.Mode);
    }

    [Fact]
    public async Task OnPostCreateAsync_WhenAuthorizationFails_ReturnsForbid()
    {
        await using var db = CreateDbContext();
        var readService = new StubIprReadService();
        var writeService = new StubIprWriteService();
        var authorizationService = new DenyAuthorizationService();
        using var userManager = CreateUserManager(db);
        var exportService = new StubIprExportService();

        var page = new IndexModel(db, readService, writeService, authorizationService, userManager, exportService)
        {
            Input = new IndexModel.RecordInput
            {
                FilingNumber = "IPR-500",
                Type = IprType.Patent,
                Status = IprStatus.FilingUnderProcess
            }
        };

        ConfigurePageContext(page, CreatePrincipal("viewer", "Viewer"));

        var result = await page.OnPostCreateAsync(CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task OnPostCreateAsync_WhenAuthorizedAndValid_RedirectsToEdit()
    {
        await using var db = CreateDbContext();
        var readService = new StubIprReadService();
        var (writeService, root) = CreateWriteService(db, new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero));
        var authorizationService = new AllowAuthorizationService();
        using var userManager = CreateUserManager(db);
        var exportService = new StubIprExportService();

        try
        {
            var page = new IndexModel(db, readService, writeService, authorizationService, userManager, exportService)
            {
                Input = new IndexModel.RecordInput
                {
                    FilingNumber = "IPR-600",
                    Type = IprType.Copyright,
                    Status = IprStatus.Granted,
                    FiledBy = "  Analyst  ",
                    FiledOn = new DateOnly(2024, 2, 1),
                    GrantedOn = new DateOnly(2024, 2, 20)
                }
            };

            ConfigurePageContext(page, CreatePrincipal("editor", Policies.Ipr.EditAllowedRoles[0]));

            var result = await page.OnPostCreateAsync(CancellationToken.None);

            var redirect = Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal("edit", redirect.RouteValues?["mode"]);
            var createdId = Assert.IsType<int>(redirect.RouteValues?["id"]);
            Assert.Equal("IPR record created.", page.TempData["ToastMessage"]);

            var created = await db.IprRecords.AsNoTracking().SingleAsync(r => r.Id == createdId);
            Assert.Equal("IPR-600", created.IprFilingNumber);
            Assert.Equal(IprType.Copyright, created.Type);
            Assert.Equal(IprStatus.Granted, created.Status);
            Assert.Equal("Analyst", created.FiledBy);
            Assert.Equal(new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero), created.FiledAtUtc);
            Assert.Equal(new DateTimeOffset(2024, 2, 20, 0, 0, 0, TimeSpan.Zero), created.GrantedAtUtc);
        }
        finally
        {
            CleanupRoot(root);
        }
    }

    [Fact]
    public async Task OnPostCreateAsync_WhenFiledStatusMissingFiledOn_AddsFieldError()
    {
        await using var db = CreateDbContext();
        var readService = new StubIprReadService();
        var (writeService, root) = CreateWriteService(db);
        var authorizationService = new AllowAuthorizationService();
        using var userManager = CreateUserManager(db);
        var exportService = new StubIprExportService();

        try
        {
            var page = new IndexModel(db, readService, writeService, authorizationService, userManager, exportService)
            {
                Input = new IndexModel.RecordInput
                {
                    FilingNumber = "IPR-700",
                    Type = IprType.Patent,
                    Status = IprStatus.Filed
                }
            };

            ConfigurePageContext(page, CreatePrincipal("editor", Policies.Ipr.EditAllowedRoles[0]));

            var result = await page.OnPostCreateAsync(CancellationToken.None);

            Assert.IsType<PageResult>(result);

            var key = $"{nameof(IndexModel.Input)}.{nameof(IndexModel.RecordInput.FiledOn)}";
            Assert.True(page.ModelState.TryGetValue(key, out var entry));
            var message = Assert.Single(entry.Errors).ErrorMessage;
            Assert.Equal("Filed date is required once the record is not under filing.", message);
            Assert.False(page.ModelState.TryGetValue(string.Empty, out _));
        }
        finally
        {
            CleanupRoot(root);
        }
    }

    [Fact]
    public async Task OnPostCreateAsync_WhenGrantDateBeforeFiledDate_AddsErrorsToBothFields()
    {
        await using var db = CreateDbContext();
        var readService = new StubIprReadService();
        var (writeService, root) = CreateWriteService(db);
        var authorizationService = new AllowAuthorizationService();
        using var userManager = CreateUserManager(db);
        var exportService = new StubIprExportService();

        try
        {
            var page = new IndexModel(db, readService, writeService, authorizationService, userManager, exportService)
            {
                Input = new IndexModel.RecordInput
                {
                    FilingNumber = "IPR-710",
                    Type = IprType.Patent,
                    Status = IprStatus.Granted,
                    FiledOn = new DateOnly(2024, 4, 10),
                    GrantedOn = new DateOnly(2024, 3, 15)
                }
            };

            ConfigurePageContext(page, CreatePrincipal("editor", Policies.Ipr.EditAllowedRoles[0]));

            var result = await page.OnPostCreateAsync(CancellationToken.None);

            Assert.IsType<PageResult>(result);

            var filedKey = $"{nameof(IndexModel.Input)}.{nameof(IndexModel.RecordInput.FiledOn)}";
            Assert.True(page.ModelState.TryGetValue(filedKey, out var filedEntry));
            var filedMessage = Assert.Single(filedEntry.Errors).ErrorMessage;
            Assert.Equal("Grant date cannot be earlier than the filing date.", filedMessage);

            var grantedKey = $"{nameof(IndexModel.Input)}.{nameof(IndexModel.RecordInput.GrantedOn)}";
            Assert.True(page.ModelState.TryGetValue(grantedKey, out var grantedEntry));
            var grantedMessage = Assert.Single(grantedEntry.Errors).ErrorMessage;
            Assert.Equal("Grant date cannot be earlier than the filing date.", grantedMessage);
        }
        finally
        {
            CleanupRoot(root);
        }
    }

    [Fact]
    public async Task OnGetAsync_PopulatesPatentAndCopyrightTypeOptions()
    {
        await using var db = CreateDbContext();
        var readService = new StubIprReadService();
        var writeService = new StubIprWriteService();
        var authorizationService = new AllowAuthorizationService();
        using var userManager = CreateUserManager(db);
        var exportService = new StubIprExportService();

        var page = new IndexModel(db, readService, writeService, authorizationService, userManager, exportService);
        ConfigurePageContext(page, CreatePrincipal("editor", Policies.Ipr.EditAllowedRoles[0]));

        var result = await page.OnGetAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);

        var expectedValues = new[] { IprType.Patent.ToString(), IprType.Copyright.ToString() };
        var optionValues = page.TypeOptions.Select(o => o.Value).ToArray();
        Assert.Equal(expectedValues, optionValues);

        var formOptionValues = page.TypeFormOptions.Select(o => o.Value).ToArray();
        Assert.Equal(expectedValues, formOptionValues);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
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

    private static (IIprWriteService Service, string RootPath) CreateWriteService(ApplicationDbContext db, DateTimeOffset? now = null)
    {
        var clock = FakeClock.AtUtc(now ?? new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.Zero));
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var options = Options.Create(new IprAttachmentOptions
        {
            MaxFileSizeBytes = 1024 * 1024,
            AllowedContentTypes = new List<string> { "application/pdf" }
        });

        var uploadRootProvider = new TestUploadRootProvider(root);
        var pathResolver = new UploadPathResolver(uploadRootProvider);
        var storage = new IprAttachmentStorage(uploadRootProvider, pathResolver, options);
        var ingestion = new StubDocRepoIngestionService();
        var service = new IprWriteService(db, clock, storage, options, ingestion, NullLogger<IprWriteService>.Instance);
        return (service, root);
    }

    private static void CleanupRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch
        {
            // best effort cleanup for temp directories
        }
    }

    private sealed class DictionaryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();

        public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
        {
        }
    }

    private sealed class StubDocRepoIngestionService : IDocRepoIngestionService
    {
        public Task<Guid> IngestExternalPdfAsync(Stream pdfStream, string originalFileName, string sourceModule, string sourceItemId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Guid.NewGuid());
        }
    }

    private sealed class StubIprReadService : IIprReadService
    {
        public Task<PagedResult<IprListRowDto>> SearchAsync(IprFilter filter, CancellationToken cancellationToken = default)
            => Task.FromResult(new PagedResult<IprListRowDto>(Array.Empty<IprListRowDto>(), 0, filter.Page, filter.PageSize));

        public Task<IprRecord?> GetAsync(int id, CancellationToken cancellationToken = default)
            => Task.FromResult<IprRecord?>(null);

        public Task<IprKpis> GetKpisAsync(IprFilter filter, CancellationToken cancellationToken = default)
            => Task.FromResult(new IprKpis(0, 0, 0, 0, 0, 0));

        public Task<IReadOnlyList<IprExportRowDto>> GetExportAsync(IprFilter filter, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<IprExportRowDto>>(Array.Empty<IprExportRowDto>());
    }

    private sealed class StubIprWriteService : IIprWriteService
    {
        public Task<IprRecord> CreateAsync(IprRecord record, CancellationToken cancellationToken = default)
            => Task.FromResult(record);

        public Task<IprRecord?> UpdateAsync(IprRecord record, CancellationToken cancellationToken = default)
            => Task.FromResult<IprRecord?>(null);

        public Task<bool> DeleteAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<IprAttachment> AddAttachmentAsync(int iprRecordId, System.IO.Stream content, string originalFileName, string? contentType, string uploadedByUserId, CancellationToken cancellationToken = default)
            => Task.FromResult(new IprAttachment());

        public Task<bool> DeleteAttachmentAsync(int attachmentId, byte[] rowVersion, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }

    private sealed class StubIprExportService : IIprExportService
    {
        public Task<IprExportFile> ExportAsync(IprFilter filter, CancellationToken cancellationToken)
            => Task.FromResult(new IprExportFile("export.xlsx", "application/octet-stream", Array.Empty<byte>()));
    }

    private sealed class DenyAuthorizationService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(AuthorizationResult.Failed());
    }

    private sealed class AllowAuthorizationService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Success());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(AuthorizationResult.Success());
    }
}
