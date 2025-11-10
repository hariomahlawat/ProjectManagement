using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Application.Ffc;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC.Records.Attachments;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Services;
using ProjectManagement.Services.DocRepo;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports;

public sealed class FfcAttachmentsUploadPageTests
{
    [Fact]
    public async Task OnGetAsync_WithUnknownRecord_ReturnsNotFound()
    {
        await using var db = CreateDbContext();
        var page = CreatePage(db);

        var result = await page.OnGetAsync(123);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task OnPostUploadAsync_WithUnknownRecord_ReturnsNotFound()
    {
        await using var db = CreateDbContext();
        var page = CreatePage(db);
        ConfigurePageContext(page, CreateAdminPrincipal());

        var result = await page.OnPostUploadAsync(999);

        Assert.IsType<NotFoundResult>(result);
    }

    private static UploadModel CreatePage(ApplicationDbContext db)
    {
        var storage = new ThrowingAttachmentStorage();
        var options = Options.Create(new FfcAttachmentOptions { MaxFileSizeBytes = 10_000_000 });
        var ingestion = new StubDocRepoIngestionService();
        return new UploadModel(db, storage, options, new StubAuditService(), NullLogger<UploadModel>.Instance, ingestion);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static ClaimsPrincipal CreateAdminPrincipal()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "admin"),
            new(ClaimTypes.Name, "admin"),
            new(ClaimTypes.Role, "Admin")
        };

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
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

    private sealed class ThrowingAttachmentStorage : IFfcAttachmentStorage
    {
        public Task<(bool Success, string? ErrorMessage, FfcAttachment? Attachment)> SaveAsync(
            long recordId,
            IFormFile file,
            FfcAttachmentKind kind,
            string? caption)
            => Task.FromResult<(bool Success, string? ErrorMessage, FfcAttachment? Attachment)>((false, "Should not be called.", (FfcAttachment?)null));

        public Task DeleteAsync(FfcAttachment attachment)
            => throw new InvalidOperationException("Should not be called.");
    }

    // ===== Section: Doc Repo ingestion stub =====
    private sealed class StubDocRepoIngestionService : IDocRepoIngestionService
    {
        public Task<Guid> IngestExternalPdfAsync(Stream pdfStream, string originalFileName, string sourceModule, string sourceItemId, CancellationToken cancellationToken = default)
            => Task.FromResult(Guid.NewGuid());
    }

    private sealed class StubAuditService : IAuditService
    {
        public Task LogAsync(
            string action,
            string? message = null,
            string level = "Info",
            string? userId = null,
            string? userName = null,
            IDictionary<string, string?>? data = null,
            HttpContext? http = null)
            => Task.CompletedTask;
    }

    private sealed class DictionaryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();

        public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
        {
        }
    }
}
