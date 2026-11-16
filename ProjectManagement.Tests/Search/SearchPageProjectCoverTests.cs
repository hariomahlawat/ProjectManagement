using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using ProjectManagement.Areas.Common.Pages.Search;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.SearchV2.Models;
using ProjectManagement.Services.SearchV2.Query;
using Xunit;

namespace ProjectManagement.Tests.Search;

/// <summary>
/// Project results show the project's cover thumbnail (the small "xs" preview the dashboard
/// uses); projects without a cover, trashed projects and other record types get none.
/// </summary>
public sealed class SearchPageProjectCoverTests
{
    [Fact]
    public async Task ProjectResults_GetTheirCoverThumbnail_OnlyWhenTheProjectHasOne()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ApplicationDbContext(options);
        var withCover = new Project { Name = "Kestrel trainer", Description = "", CreatedByUserId = "creator", CreatedAt = DateTime.UtcNow, CoverPhotoId = 41, CoverPhotoVersion = 3 };
        var withoutCover = new Project { Name = "Kestrel radar", Description = "", CreatedByUserId = "creator", CreatedAt = DateTime.UtcNow };
        var trashed = new Project { Name = "Kestrel drone", Description = "", CreatedByUserId = "creator", CreatedAt = DateTime.UtcNow, CoverPhotoId = 42, IsDeleted = true };
        db.Projects.AddRange(withCover, withoutCover, trashed);
        await db.SaveChangesAsync();

        var gateway = new FixedGateway(
            Result("Project", withCover.Id.ToString()),
            Result("Project", withoutCover.Id.ToString()),
            Result("Project", trashed.Id.ToString()),
            Result("ProjectTot", withCover.Id.ToString()));
        var urls = new RecordingUrlHelper();
        var page = new IndexModel(gateway, new TestEnvironment(), new DenyAll(), db)
        {
            Q = "kestrel",
            PageContext = new PageContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity("Test")) } },
            Url = urls
        };

        await page.OnGetAsync(CancellationToken.None);

        Assert.Equal(new[] { withCover.Id }, page.ProjectCovers.Keys.ToArray());
        Assert.Equal($"/Projects/{withCover.Id}/Photos/41/View/xs?v=3", page.ProjectCovers[withCover.Id]);
    }

    // The page shows the first category as the selected tab; a hand-edited URL with a second
    // one must not filter on a tab that is not shown.
    [Fact]
    public async Task OnlyTheSelectedTabFilters()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options);
        var gateway = new FixedGateway();
        var page = new IndexModel(gateway, new TestEnvironment(), new DenyAll(), db)
        {
            Q = "kestrel",
            Category = new[] { " ", "Projects", "Documents" },
            PageContext = new PageContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity("Test")) } },
            Url = new RecordingUrlHelper()
        };

        await page.OnGetAsync(CancellationToken.None);

        Assert.Equal(new[] { "Projects" }, gateway.LastRequest!.Categories);
    }

    private static SearchResult Result(string entityType, string key) => new(
        1, entityType, key, "Project", key, null, "Projects", "Projects", "Kestrel", Array.Empty<SearchTextSegment>(),
        null, "/x", null, Array.Empty<SearchTextSegment>(), null, null, null, null, 1, 1, Array.Empty<SearchRelatedResult>(), null);

    private sealed class FixedGateway : ISearchGateway
    {
        private readonly SearchResult[] _results;
        public FixedGateway(params SearchResult[] results) => _results = results;

        public SearchRequest? LastRequest { get; private set; }

        public Task<SearchGatewayResponse> SearchAsync(SearchRequest request, ClaimsPrincipal user, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(SearchGatewayResponse.Empty(request.Query) with { Results = _results });
        }

        public Task<IReadOnlyList<SearchSuggestion>> SuggestAsync(string query, ClaimsPrincipal user, int? limit, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<SearchSuggestion>>(Array.Empty<SearchSuggestion>());

        public Task LogClickAsync(string query, string entityType, string entityKey, int rank, string sourceModule, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    // Stands in for routing: builds the cover URL from the page route values.
    private sealed class RecordingUrlHelper : IUrlHelper
    {
        public ActionContext ActionContext { get; } = new(new DefaultHttpContext(), new RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
        public string? Action(UrlActionContext actionContext) => null;
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => true;
        public string? Link(string? routeName, object? values) => null;

        public string? RouteUrl(UrlRouteContext routeContext)
        {
            var values = new RouteValueDictionary(routeContext.Values);
            return $"/Projects/{values["id"]}/Photos/{values["photoId"]}/View/{values["size"]}?v={values["v"]}";
        }
    }

    private sealed class DenyAll : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(AuthorizationResult.Failed());
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
