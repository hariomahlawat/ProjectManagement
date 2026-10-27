using System.Security.Claims;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using Xunit;
using WorkspaceIndexModel = ProjectManagement.Pages.Workspace.IndexModel;
using AnalyticsIndexModel = ProjectManagement.Pages.Analytics.IndexModel;

namespace ProjectManagement.Tests.Workspace;

/// <summary>
/// Project analytics replaced the Command workspace's Project portfolio (27 Oct 2026): the rail
/// opens the Analytics page, and the old portfolio address lands there with its categories.
/// </summary>
public sealed class CommandProjectAnalyticsTests
{
    [Theory]
    [InlineData(RoleNames.HoD)]
    [InlineData(RoleNames.Comdt)]
    public async Task OldPortfolioAddress_RedirectsCommandUsersToAnalyticsWithTheirCategories(string role)
    {
        await using var db = CreateContext();
        var page = CreatePage(db, role, "?mode=command&view=portfolio&ParentCategoryIds=3&ParentCategoryIds=7");
        page.Mode = "command";
        page.View = "portfolio";
        page.ParentCategoryIds = new List<int> { 3, 7 };

        var result = await page.OnGetAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Analytics/Index", redirect.PageName);
        Assert.Equal(new[] { 3, 7 }, Assert.IsAssignableFrom<IEnumerable<int>>(redirect.RouteValues!["ongoingStageParentCategoryIds"]));
    }

    [Fact]
    public void Analytics_IsOpenToEverySignedInUser_SoCommandUsersCanFollowTheRail()
    {
        // Only [Authorize] (no role or policy); the ARPP mode checks its own policy in OnGetAsync.
        var attributes = typeof(AnalyticsIndexModel).GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();

        var attribute = Assert.Single(attributes);
        Assert.Null(attribute.Roles);
        Assert.Null(attribute.Policy);
    }

    [Fact]
    public void Rail_OffersProjectAnalyticsInsteadOfProjectPortfolio()
    {
        var rail = ReadRepoFile("Pages", "Workspace", "_CommandWorkspaceRail.cshtml");

        Assert.Contains("<a asp-page=\"/Analytics/Index\"", rail, StringComparison.Ordinal);
        Assert.Contains("<span>Project analytics</span>", rail, StringComparison.Ordinal);
        Assert.DoesNotContain("Project portfolio", rail, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-route-view=\"portfolio\"", rail, StringComparison.Ordinal);
    }

    private static WorkspaceIndexModel CreatePage(ApplicationDbContext db, string role, string queryString)
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var userManager = new UserManager<ApplicationUser>(
            new UserStore<ApplicationUser>(db),
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            services,
            NullLogger<UserManager<ApplicationUser>>.Instance);

        // The redirect happens before any workspace data is read, so those services are not needed.
        var page = new WorkspaceIndexModel(null!, null!, null!, userManager, new DenyAllAuthorization(), null!, NullLogger<WorkspaceIndexModel>.Instance);
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, "command-user"), new Claim(ClaimTypes.Role, role) },
                "test"))
        };
        httpContext.Request.QueryString = new QueryString(queryString);
        page.PageContext = new PageContext { HttpContext = httpContext, RouteData = new RouteData() };
        return page;
    }

    private static ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class DenyAllAuthorization : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(AuthorizationResult.Failed());
    }

    private static string ReadRepoFile(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ProjectManagement.csproj")))
            {
                return File.ReadAllText(Path.Combine(new[] { current.FullName }.Concat(segments).ToArray()));
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the ProjectManagement repository root.");
    }
}
