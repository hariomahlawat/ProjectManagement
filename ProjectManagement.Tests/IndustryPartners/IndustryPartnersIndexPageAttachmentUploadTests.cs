using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using ProjectManagement.Pages.IndustryPartners;
using ProjectManagement.Services.IndustryPartners;
using Xunit;

namespace ProjectManagement.Tests.IndustryPartners;

public sealed class IndustryPartnersIndexPageAttachmentUploadTests
{
    [Fact]
    public async Task OnPostUploadAttachmentAsync_WithRequestFormFile_UsesRequestFormFileCollection()
    {
        // SECTION: Arrange page and dependencies
        var attachmentManager = new RecordingAttachmentManager();
        var page = CreatePage(attachmentManager);

        // SECTION: Arrange multipart form file in HttpContext request
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("attachment-content"));
        var formFile = new FormFile(stream, 0, stream.Length, "attachment", "partner-notes.txt")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/plain"
        };

        var files = new FormFileCollection { formFile };
        var form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(), files);
        page.HttpContext.Features.Set<IFormFeature>(new FormFeature(form));

        // SECTION: Act
        var result = await page.OnPostUploadAttachmentAsync(partnerId: 42, CancellationToken.None);

        // SECTION: Assert
        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(42, redirect.RouteValues?["id"]);
        Assert.Equal("Attachment uploaded.", page.TempData["Message"]);

        var uploaded = Assert.Single(attachmentManager.UploadedFiles);
        Assert.Equal("partner-notes.txt", uploaded.FileName);
        Assert.Equal(42, uploaded.PartnerId);
    }

    private static IndexModel CreatePage(RecordingAttachmentManager attachmentManager)
    {
        var page = new IndexModel(new StubIndustryPartnerService(), attachmentManager, new AllowAuthorizationService());

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "manager-1"),
            new(ClaimTypes.Role, "Admin")
        };

        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

        var httpContext = new DefaultHttpContext { User = user };
        var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor());

        page.PageContext = new PageContext(actionContext)
        {
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        };

        page.TempData = new TempDataDictionary(httpContext, new DictionaryTempDataProvider());
        return page;
    }

    // SECTION: Test doubles
    private sealed class RecordingAttachmentManager : IIndustryPartnerAttachmentManager
    {
        public List<(int PartnerId, IFormFile File)> UploadedFiles { get; } = new();

        public Task<Guid> UploadAsync(int partnerId, IFormFile file, ClaimsPrincipal user, CancellationToken cancellationToken = default)
        {
            UploadedFiles.Add((partnerId, file));
            return Task.FromResult(Guid.NewGuid());
        }

        public Task<(Stream Stream, string FileName, string ContentType)> DownloadAsync(int partnerId, Guid attachmentId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(int partnerId, Guid attachmentId, ClaimsPrincipal user, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubIndustryPartnerService : IIndustryPartnerService
    {
        public Task<IndustryPartnerSearchResult> SearchAsync(string? query, int page, int pageSize, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IndustryPartnerDto?> GetAsync(int id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> CreateAsync(CreateIndustryPartnerRequest req, ClaimsPrincipal user, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UpdateFieldAsync(int id, string field, string? value, ClaimsPrincipal user, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> AddContactAsync(int partnerId, ContactRequest req, ClaimsPrincipal user, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UpdateContactAsync(int partnerId, int contactId, ContactRequest req, ClaimsPrincipal user, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteContactAsync(int partnerId, int contactId, ClaimsPrincipal user, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task LinkProjectAsync(int partnerId, int projectId, ClaimsPrincipal user, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UnlinkProjectAsync(int partnerId, int projectId, ClaimsPrincipal user, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeletePartnerAsync(int partnerId, ClaimsPrincipal user, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class AllowAuthorizationService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Success());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(AuthorizationResult.Success());
    }

    private sealed class DictionaryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();

        public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
        {
        }
    }
}
