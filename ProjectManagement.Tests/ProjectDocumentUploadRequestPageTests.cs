using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Pages.Projects.Documents;
using ProjectManagement.Services;
using ProjectManagement.Services.Documents;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectDocumentUploadRequestPageTests
{
    [Fact]
    public async Task OnPost_AllowsProjectsWithoutStages()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 1, leadPoUserId: "po-1");

        var userContext = new FakeUserContext("po-1", "Project Officer");
        var documentService = new StubDocumentService();
        var requestService = new RecordingDocumentRequestService();
        var options = Options.Create(new ProjectDocumentOptions());

        var page = new UploadRequestModel(db, userContext, documentService, requestService, options, NullLogger<UploadRequestModel>.Instance)
        {
            Input = new UploadRequestModel.UploadInputModel
            {
                ProjectId = 1,
                StageId = null,
                Nomenclature = "Spec Sheet",
                File = CreateFormFile()
            }
        };

        ConfigurePageContext(page, userContext.User);

        var result = await page.OnPostAsync(1, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("../Overview", redirect.PageName);
        Assert.False(page.HasStageOptions);
        Assert.Null(requestService.LastStageId);
    }

    [Fact]
    public async Task OnPost_ReturnsValidationError_WhenNomenclatureWhitespace()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 1, leadPoUserId: "po-1");

        var userContext = new FakeUserContext("po-1", "Project Officer");
        var documentService = new StubDocumentService();
        var requestService = new RecordingDocumentRequestService();
        var options = Options.Create(new ProjectDocumentOptions());

        var page = new UploadRequestModel(db, userContext, documentService, requestService, options, NullLogger<UploadRequestModel>.Instance)
        {
            Input = new UploadRequestModel.UploadInputModel
            {
                ProjectId = 1,
                StageId = null,
                Nomenclature = "   ",
                File = CreateFormFile()
            }
        };

        ConfigurePageContext(page, userContext.User);

        var result = await page.OnPostAsync(1, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        var nomenclatureState = page.ModelState["Input.Nomenclature"] ?? throw new Xunit.Sdk.XunitException("Expected model state entry for nomenclature.");
        Assert.Contains(nomenclatureState.Errors, error => error.ErrorMessage == "Enter a nomenclature.");
    }

    private static void ConfigurePageContext(PageModel page, ClaimsPrincipal user)
    {
        var httpContext = new DefaultHttpContext
        {
            User = user
        };
        var tempDataProvider = new InMemoryTempDataProvider();
        httpContext.RequestServices = new ServiceCollection()
            .AddSingleton<ITempDataProvider>(tempDataProvider)
            .BuildServiceProvider();

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        page.PageContext = new PageContext(actionContext)
        {
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        };
        page.TempData = new TempDataDictionary(httpContext, tempDataProvider);
    }

    private static IFormFile CreateFormFile()
    {
        var content = new byte[] { 1, 2, 3, 4 };
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "file", "test.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task SeedProjectAsync(ApplicationDbContext db, int projectId, string? leadPoUserId = null)
    {
        db.Projects.Add(new Project
        {
            Id = projectId,
            Name = $"Project {projectId}",
            CreatedByUserId = "creator",
            LeadPoUserId = leadPoUserId,
            RowVersion = new byte[] { 1 }
        });
        await db.SaveChangesAsync();
    }

    private sealed class FakeUserContext : IUserContext
    {
        public FakeUserContext(string userId, string role)
        {
            UserId = userId;
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Role, role)
            }, "Test"));
        }

        public ClaimsPrincipal User { get; }

        public string? UserId { get; }
    }

    private sealed class StubDocumentService : IDocumentService
    {
        public int CreateTempRequestToken() => 123;

        public async Task<DocumentFileDescriptor> SaveTempAsync(int requestId, Stream content, string originalFileName, string? contentType, CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            var length = buffer.Length;
            return new DocumentFileDescriptor("temp-storage", originalFileName, length, contentType ?? "application/octet-stream");
        }

        public Task<ProjectDocument> PublishNewAsync(int projectId, int? stageId, int? totId, string nomenclature, string tempStorageKey, string originalFileName, long fileSize, string contentType, string uploadedByUserId, string performedByUserId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ProjectDocument> OverwriteAsync(int documentId, string tempStorageKey, string originalFileName, long fileSize, string contentType, string uploadedByUserId, string performedByUserId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ProjectDocument> SoftDeleteAsync(int documentId, string performedByUserId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ProjectDocument> RestoreAsync(int documentId, string performedByUserId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ProjectDocument> RetryOcrAsync(int documentId, string performedByUserId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task HardDeleteAsync(int documentId, string performedByUserId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<DocumentStreamResult?> OpenStreamAsync(int documentId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task DeleteTempAsync(string storageKey, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingDocumentRequestService : IDocumentRequestService
    {
        public int? LastStageId { get; private set; }

        public Task<ProjectDocumentRequest> CreateUploadRequestAsync(int projectId, int? stageId, string nomenclature, int? totId, DocumentFileDescriptor file, string requestedByUserId, CancellationToken cancellationToken)
        {
            LastStageId = stageId;
            return Task.FromResult(new ProjectDocumentRequest
            {
                ProjectId = projectId,
                StageId = stageId,
                Title = nomenclature,
                RequestedByUserId = requestedByUserId
            });
        }

        public Task<ProjectDocumentRequest> CreateReplaceRequestAsync(int documentId, string? newTitle, DocumentFileDescriptor file, string requestedByUserId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ProjectDocumentRequest> CreateDeleteRequestAsync(int documentId, string? reason, string requestedByUserId, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class InMemoryTempDataProvider : ITempDataProvider
    {
        private readonly Dictionary<string, object?> _data = new(StringComparer.OrdinalIgnoreCase);

        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>(_data, StringComparer.OrdinalIgnoreCase);

        public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
        {
            _data.Clear();
            foreach (var (key, value) in values)
            {
                _data[key] = value;
            }
        }
    }
}
