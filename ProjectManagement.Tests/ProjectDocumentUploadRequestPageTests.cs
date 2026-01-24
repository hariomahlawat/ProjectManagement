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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Stages;
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
                Files = new List<IFormFile> { CreateFormFile("test.pdf") }
            }
        };

        ConfigurePageContext(page, userContext.User);

        var result = await page.OnPostAsync(1, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("../Overview", redirect.PageName);
        Assert.False(page.HasStageOptions);
        Assert.All(requestService.Requests, request => Assert.Null(request.StageId));
    }

    [Fact]
    public async Task OnPost_AllowsMissingStage_WhenStagesExist()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 1, leadPoUserId: "po-1");
        await SeedStageAsync(db, 1, 10, StageCodes.Planning, sortOrder: 1);

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
                Files = new List<IFormFile> { CreateFormFile("stage-optional.pdf") }
            }
        };

        ConfigurePageContext(page, userContext.User);

        var result = await page.OnPostAsync(1, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Single(requestService.Requests);
        Assert.Null(requestService.Requests[0].StageId);
    }

    [Fact]
    public async Task OnPost_ReturnsValidationError_WhenStageDoesNotBelongToProject()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 1, leadPoUserId: "po-1");
        await SeedProjectAsync(db, 2, leadPoUserId: "po-2");
        await SeedStageAsync(db, 2, 20, StageCodes.Execution, sortOrder: 1);

        var userContext = new FakeUserContext("po-1", "Project Officer");
        var documentService = new StubDocumentService();
        var requestService = new RecordingDocumentRequestService();
        var options = Options.Create(new ProjectDocumentOptions());

        var page = new UploadRequestModel(db, userContext, documentService, requestService, options, NullLogger<UploadRequestModel>.Instance)
        {
            Input = new UploadRequestModel.UploadInputModel
            {
                ProjectId = 1,
                StageId = 20,
                Files = new List<IFormFile> { CreateFormFile("invalid-stage.pdf") }
            }
        };

        ConfigurePageContext(page, userContext.User);

        var result = await page.OnPostAsync(1, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        var stageState = page.ModelState["Input.StageId"] ?? throw new Xunit.Sdk.XunitException("Expected model state entry for stage.");
        Assert.Contains(stageState.Errors, error => error.ErrorMessage == "Select a valid stage.");
        Assert.Empty(requestService.Requests);
    }

    [Fact]
    public async Task OnPost_ReturnsValidationError_WhenNoFilesSelected()
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
                Files = new List<IFormFile>()
            }
        };

        ConfigurePageContext(page, userContext.User);

        var result = await page.OnPostAsync(1, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        var fileState = page.ModelState["Input.Files"] ?? throw new Xunit.Sdk.XunitException("Expected model state entry for files.");
        Assert.Contains(fileState.Errors, error => error.ErrorMessage == "Select at least one file to upload.");
        Assert.Empty(requestService.Requests);
    }

    [Fact]
    public async Task OnPost_CreatesOneRequestPerFile_WithDerivedNomenclature()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 1, leadPoUserId: "po-1");
        await SeedStageAsync(db, 1, 10, StageCodes.Execution, sortOrder: 1);

        var userContext = new FakeUserContext("po-1", "Project Officer");
        var documentService = new StubDocumentService();
        var requestService = new RecordingDocumentRequestService();
        var options = Options.Create(new ProjectDocumentOptions());

        var page = new UploadRequestModel(db, userContext, documentService, requestService, options, NullLogger<UploadRequestModel>.Instance)
        {
            Input = new UploadRequestModel.UploadInputModel
            {
                ProjectId = 1,
                StageId = 10,
                Nomenclature = "Batch Upload",
                Files = new List<IFormFile>
                {
                    CreateFormFile("alpha.pdf"),
                    CreateFormFile("beta.pdf")
                }
            }
        };

        ConfigurePageContext(page, userContext.User);

        var result = await page.OnPostAsync(1, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Submitted 2 file(s) for moderation.", page.TempData["Flash"]);
        Assert.Equal(2, requestService.Requests.Count);
        Assert.All(requestService.Requests, request => Assert.Equal(10, request.StageId));
        Assert.Collection(
            requestService.Requests,
            first => Assert.Equal("Batch Upload - alpha", first.Title),
            second => Assert.Equal("Batch Upload - beta", second.Title));
    }

    [Fact]
    public async Task OnPost_UsesFileNameAsNomenclature_WhenBaseMissing()
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
                Files = new List<IFormFile> { CreateFormFile("gamma.pdf") }
            }
        };

        ConfigurePageContext(page, userContext.User);

        var result = await page.OnPostAsync(1, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Single(requestService.Requests);
        Assert.Equal("gamma", requestService.Requests[0].Title);
    }

    // SECTION: Test helpers
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

    private static IFormFile CreateFormFile(string fileName)
    {
        var content = new byte[] { 1, 2, 3, 4 };
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "file", fileName)
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

    private static async Task SeedStageAsync(ApplicationDbContext db, int projectId, int stageId, string stageCode, int sortOrder)
    {
        db.ProjectStages.Add(new ProjectStage
        {
            Id = stageId,
            ProjectId = projectId,
            StageCode = stageCode,
            SortOrder = sortOrder
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
        public List<ProjectDocumentRequest> Requests { get; } = new();

        public Task<ProjectDocumentRequest> CreateUploadRequestAsync(int projectId, int? stageId, string nomenclature, int? totId, DocumentFileDescriptor file, string requestedByUserId, CancellationToken cancellationToken)
        {
            var request = new ProjectDocumentRequest
            {
                ProjectId = projectId,
                StageId = stageId,
                Title = nomenclature,
                RequestedByUserId = requestedByUserId
            };

            Requests.Add(request);
            return Task.FromResult(request);
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
