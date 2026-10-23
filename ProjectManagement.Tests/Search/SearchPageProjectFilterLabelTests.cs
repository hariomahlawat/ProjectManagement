using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
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
/// The "Project: …" filter chip must name the project even when the project is not among
/// the returned facets (for example when the filters match nothing).
/// </summary>
public sealed class SearchPageProjectFilterLabelTests
{
    [Fact]
    public async Task SelectedProjects_AreNamed_AndDeletedProjectsAreNot()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ApplicationDbContext(options);
        var live = new Project { Name = "Hexacopter (Ethiopia 2025)", Description = "", CreatedByUserId = "creator", CreatedAt = DateTime.UtcNow };
        var deleted = new Project { Name = "Withdrawn drone", Description = "", CreatedByUserId = "creator", CreatedAt = DateTime.UtcNow, IsDeleted = true };
        db.Projects.AddRange(live, deleted);
        await db.SaveChangesAsync();

        var page = new IndexModel(new EmptyGateway(), new TestEnvironment(), new DenyAll(), db)
        {
            Q = "drone",
            Project = new[] { live.Id, deleted.Id, 0 },
            PageContext = new PageContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity("Test")) } }
        };

        await page.OnGetAsync(CancellationToken.None);

        Assert.Equal("Hexacopter (Ethiopia 2025)", page.SelectedProjectNames[live.Id]);
        Assert.False(page.SelectedProjectNames.ContainsKey(deleted.Id));
    }

    private sealed class EmptyGateway : ISearchGateway
    {
        public Task<SearchGatewayResponse> SearchAsync(SearchRequest request, ClaimsPrincipal user, CancellationToken cancellationToken)
            => Task.FromResult(SearchGatewayResponse.Empty(request.Query));

        public Task<IReadOnlyList<SearchSuggestion>> SuggestAsync(string query, ClaimsPrincipal user, int? limit, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<SearchSuggestion>>(Array.Empty<SearchSuggestion>());

        public Task LogClickAsync(string query, string entityType, string entityKey, int rank, string sourceModule, CancellationToken cancellationToken)
            => Task.CompletedTask;
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
