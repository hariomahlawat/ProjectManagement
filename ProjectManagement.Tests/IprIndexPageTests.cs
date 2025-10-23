using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Linq;
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
using ProjectManagement.Areas.ProjectOfficeReports.Pages.Ipr;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure.Data;
using ProjectManagement.Models;

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

        var page = new IndexModel(db, readService, writeService, authorizationService, userManager);
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

        var page = new IndexModel(db, readService, writeService, authorizationService, userManager)
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
        var writeService = new RecordingIprWriteService();
        var authorizationService = new AllowAuthorizationService();
        using var userManager = CreateUserManager(db);

        var page = new IndexModel(db, readService, writeService, authorizationService, userManager)
        {
            Input = new IndexModel.RecordInput
            {
                FilingNumber = "IPR-600",
                Type = IprType.Copyright,
                Status = IprStatus.Filed,
                FiledBy = "  Analyst  ",
                FiledOn = new DateOnly(2024, 2, 1),
                GrantedOn = new DateOnly(2024, 3, 1)
            }
        };

        ConfigurePageContext(page, CreatePrincipal("editor", Policies.Ipr.AllowedRoles[0]));

        var result = await page.OnPostCreateAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("edit", redirect.RouteValues?["mode"]);
        Assert.Equal(writeService.CreatedRecord?.Id, redirect.RouteValues?["id"]);
        Assert.Equal("IPR record created.", page.TempData["ToastMessage"]);

        var created = Assert.IsType<IprRecord>(writeService.CreatedRecord);
        Assert.Equal("IPR-600", created.IprFilingNumber);
        Assert.Equal(IprType.Copyright, created.Type);
        Assert.Equal(IprStatus.Filed, created.Status);
        Assert.Equal(new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero), created.FiledAtUtc);
        Assert.Equal("Analyst", created.FiledBy);
        Assert.Equal(new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero), created.GrantedAtUtc);
    }

    [Fact]
    public async Task OnGetAsync_PopulatesPatentAndCopyrightTypeOptions()
    {
        await using var db = CreateDbContext();
        var readService = new StubIprReadService();
        var writeService = new StubIprWriteService();
        var authorizationService = new AllowAuthorizationService();
        using var userManager = CreateUserManager(db);

        var page = new IndexModel(db, readService, writeService, authorizationService, userManager);
        ConfigurePageContext(page, CreatePrincipal("editor", Policies.Ipr.AllowedRoles[0]));

        var result = await page.OnGetAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);

        var expectedValues = new[] { IprType.Patent.ToString(), IprType.Copyright.ToString() };
        var optionValues = page.TypeOptions.Select(o => o.Value).ToArray();
        Assert.Equal(expectedValues, optionValues);

        var formOptionValues = page.TypeFormOptions.Skip(1).Select(o => o.Value).ToArray();
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

    private sealed class DictionaryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();

        public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
        {
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

    private sealed class RecordingIprWriteService : IIprWriteService
    {
        public IprRecord? CreatedRecord { get; private set; }

        public Task<IprRecord> CreateAsync(IprRecord record, CancellationToken cancellationToken = default)
        {
            record.Id = 321;
            CreatedRecord = record;
            return Task.FromResult(record);
        }

        public Task<IprRecord?> UpdateAsync(IprRecord record, CancellationToken cancellationToken = default)
            => Task.FromResult<IprRecord?>(null);

        public Task<bool> DeleteAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<IprAttachment> AddAttachmentAsync(int iprRecordId, System.IO.Stream content, string originalFileName, string? contentType, string uploadedByUserId, CancellationToken cancellationToken = default)
            => Task.FromResult(new IprAttachment());

        public Task<bool> DeleteAttachmentAsync(int attachmentId, byte[] rowVersion, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
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
