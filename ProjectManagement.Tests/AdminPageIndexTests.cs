using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Pages.AllPages;
using ProjectManagement.Configuration;
using ProjectManagement.Models.Navigation;
using ProjectManagement.Services.Navigation;
using ProjectManagement.Services.Navigation.ModuleNav;
using Xunit;

namespace ProjectManagement.Tests;

// "Every admin page" and the sidebar share one access filter (docs/ux/admin-module.md, PR 3).
public sealed class AdminPageIndexTests
{
    [Fact]
    public async Task Access_ListsOnlyPagesWhosePolicyTheViewerMeets()
    {
        var access = new AdminNavigationAccess(new PolicyAuthorization(AdminPolicies.Access));

        var keys = (await access.GetOpenableEntriesAsync(new ClaimsPrincipal(new ClaimsIdentity())))
            .Select(entry => entry.Key)
            .ToArray();

        Assert.Contains(AdminNavigationKeys.Dashboard, keys);
        Assert.Contains(AdminNavigationKeys.AllPages, keys);
        Assert.DoesNotContain(AdminNavigationKeys.Users, keys);
        Assert.DoesNotContain(AdminNavigationKeys.MediaSources, keys);
        Assert.All(
            AdminNavigationCatalog.Entries.Where(entry => keys.Contains(entry.Key)),
            entry => Assert.Equal(AdminPolicies.Access, entry.Item.AuthorizationPolicy));
    }

    [Fact]
    public async Task Index_GroupsEveryOpenablePageByTaskWithItsSentence()
    {
        var page = CreatePage(AdminNavigationCatalog.Entries.ToArray());

        await page.OnGetAsync();

        Assert.Equal(
            AdminNavigationGroups.Ordered,
            page.Groups.Select(group => group.Name).ToArray());
        Assert.Equal(AdminNavigationCatalog.Entries.Count, page.PageCount);
        Assert.All(page.Groups.SelectMany(group => group.Pages), item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Description));
            Assert.Equal("/test/" + item.Key, item.Url);
            Assert.Contains(item.Text.ToLowerInvariant(), item.SearchLabel, StringComparison.Ordinal);
        });
        Assert.Equal("Every admin page", page.Header.Title);
    }

    [Fact]
    public async Task Index_SaysWhereAHubPageIsReachedFromAndWhichModuleItOpensIn()
    {
        var page = CreatePage(AdminNavigationCatalog.Entries.ToArray());

        await page.OnGetAsync();
        var pages = page.Groups.SelectMany(group => group.Pages).ToDictionary(item => item.Key);

        Assert.Equal("Lists used in forms", pages[AdminNavigationKeys.SponsoringUnits].OpensFrom);
        Assert.Null(pages[AdminNavigationKeys.SponsoringUnits].OwnModule);
        Assert.Equal("the Decision Centre", pages[AdminNavigationKeys.DocumentDeleteRequests].OwnModule);
        Assert.Equal("the Document Repository", pages[AdminNavigationKeys.RepositoryTrash].OwnModule);
        Assert.Equal("the media library", pages[AdminNavigationKeys.MediaSources].OwnModule);
        Assert.Null(pages[AdminNavigationKeys.Users].OpensFrom);
    }

    [Fact]
    public async Task Index_LeavesOutPagesTheViewerCannotOpen_AndHubsTheyCannotSee()
    {
        var openable = AdminNavigationCatalog.Entries
            .Where(entry => entry.Key is AdminNavigationKeys.SponsoringUnits or AdminNavigationKeys.Help)
            .ToArray();
        var page = CreatePage(openable);

        await page.OnGetAsync();
        var pages = page.Groups.SelectMany(group => group.Pages).ToArray();

        Assert.Equal(2, page.PageCount);
        Assert.Equal(
            new[] { AdminNavigationGroups.ReferenceLists, AdminNavigationGroups.Help },
            page.Groups.Select(group => group.Name).ToArray());
        // The hub is not openable, so the page does not point to it.
        Assert.Null(pages.Single(item => item.Key == AdminNavigationKeys.SponsoringUnits).OpensFrom);
    }

    private static IndexModel CreatePage(IReadOnlyList<AdminNavigationEntry> openable)
    {
        var page = new IndexModel(new FixedAccess(openable), new KeyUrlBuilder());
        page.PageContext = new PageContext { HttpContext = new DefaultHttpContext() };
        return page;
    }

    private sealed class FixedAccess(IReadOnlyList<AdminNavigationEntry> openable) : IAdminNavigationAccess
    {
        public Task<bool> CanOpenAsync(ClaimsPrincipal user, AdminNavigationEntry entry) =>
            Task.FromResult(openable.Contains(entry));

        public Task<IReadOnlyList<AdminNavigationEntry>> GetOpenableEntriesAsync(ClaimsPrincipal user) =>
            Task.FromResult(openable);
    }

    private sealed class KeyUrlBuilder : IAdminNavigationUrlBuilder
    {
        public string? GetPath(HttpContext httpContext, string navigationKey, IReadOnlyDictionary<string, object?>? additionalRouteValues = null) =>
            "/test/" + navigationKey;

        public string? GetPath(HttpContext httpContext, NavigationItem item, IReadOnlyDictionary<string, object?>? additionalRouteValues = null) =>
            "/test/" + AdminNavigationCatalog.Entries.Single(entry => ReferenceEquals(entry.Item, item)).Key;
    }

    private sealed class PolicyAuthorization(string allowedPolicy) : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) =>
            Task.FromResult(string.Equals(policyName, allowedPolicy, StringComparison.Ordinal)
                ? AuthorizationResult.Success()
                : AuthorizationResult.Failed());
    }
}
