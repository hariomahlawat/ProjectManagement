using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Models.Navigation;
using ProjectManagement.Services.Navigation;
using ProjectManagement.Services.Navigation.ModuleNav;
using Xunit;

namespace ProjectManagement.Tests;

public class RoleBasedNavigationProviderTests
{
    // The drawer's layout and rules: docs/ux/navigation-drawer.md.

    private static readonly string[] AllTrackerPolicies =
    {
        Policies.ActionTracker.Access,
        ProjectOfficeReportsPolicies.ViewProgressReview,
        ProjectOfficeReportsPolicies.ViewVisits,
        ProjectOfficeReportsPolicies.ManageSocialMediaEvents,
        ProjectOfficeReportsPolicies.ViewTrainingTracker,
        ProjectOfficeReportsPolicies.ViewTotTracker,
        ProjectOfficeReportsPolicies.ViewProliferationTracker,
        Policies.Ipr.View,
        ProjectOfficeReportsPolicies.ViewArpp
    };

    [Fact]
    public async Task Navigation_IsFixedSectionsOfLinksWithNoDeeperNesting()
    {
        var navigation = await NavigateAsAsync(new[] { "Admin" });

        Assert.Equal(
            new[]
            {
                NavigationSectionKeys.Main,
                NavigationSectionKeys.Projects,
                NavigationSectionKeys.Trackers,
                NavigationSectionKeys.Tools,
                NavigationSectionKeys.Administration
            },
            navigation.Select(section => section.Key));

        Assert.All(navigation, section =>
        {
            Assert.Null(section.Page);
            Assert.NotEmpty(section.Children);
            Assert.All(section.Children, link =>
            {
                Assert.Empty(link.Children);
                Assert.False(string.IsNullOrEmpty(link.Page));
                Assert.False(link.IsAction);
            });
        });

        // Every link names a distinct destination, and no icon is used twice.
        var links = navigation.SelectMany(section => section.Children).ToList();
        Assert.Equal(links.Count, links.Select(link => $"{link.Area}|{link.Page}").Distinct().Count());
        Assert.Equal(links.Count, links.Select(link => link.Icon).Distinct().Count());
    }

    [Fact]
    public async Task Navigation_MainMirrorsTheTopBarInItsOrder()
    {
        // Below 768px the top tabs are hidden, so Main is the only primary navigation on phones.
        var main = Section(await NavigateAsAsync(Array.Empty<string>()), NavigationSectionKeys.Main);

        Assert.Equal(
            new[] { "Dashboard", "My Workspace", "Calendar", "Notebook", "Photos", "FFC", "Documents" },
            main.Children.Select(link => link.Text));

        var ffc = main.Children.Single(link => link.Text == "FFC");
        Assert.Equal("ProjectOfficeReports", ffc.Area);
        Assert.Equal("/FFC/MapTableDetailed", ffc.Page);

        var documents = main.Children.Single(link => link.Text == "Documents");
        Assert.Equal("DocumentRepository", documents.Area);
        Assert.Equal(Policies.Documents.View, documents.AuthorizationPolicy);
        Assert.Equal("AotsUnreadBadge", documents.BadgeViewComponentName);
    }

    [Fact]
    public async Task Navigation_ProjectsFollowsTheSubNavWithoutItsActions()
    {
        var projects = Section(await NavigateAsAsync(new[] { "HoD" }), NavigationSectionKeys.Projects);
        var texts = projects.Children.Select(link => link.Text).ToList();

        var subNavDestinations = ProjectModuleNavDefinition.Build()
            .Where(item => !item.IsAction)
            .Select(item => item.Text)
            .ToList();

        // The sub-nav's destinations keep their order, with project pages that have no tab
        // of their own added before the Pending approvals queue.
        Assert.Equal(
            subNavDestinations.Take(subNavDestinations.Count - 1)
                .Concat(new[] { "Proliferation compendium", "Project ideas", "IT projects", "MOLTI projects", "SRE projects" })
                .Append("Pending approvals"),
            texts);
        Assert.DoesNotContain("Create project", texts);
    }

    [Fact]
    public async Task Navigation_OffersItProjectsBehindItsViewPolicy()
    {
        var allowed = await NavigateAsAsync(Array.Empty<string>(), new[] { Policies.ItProjects.View });
        var itProjects = Section(allowed, NavigationSectionKeys.Projects).Children.Single(link => link.Text == "IT projects");
        Assert.Equal("/ItProjects/Index", itProjects.Page);
        Assert.Equal(Policies.ItProjects.View, itProjects.AuthorizationPolicy);
        Assert.Equal("bi-pc-display", itProjects.Icon);

        var denied = await NavigateAsAsync(Array.Empty<string>(), Array.Empty<string>());
        Assert.DoesNotContain(AllLinks(denied), link => link.Text == "IT projects");
    }

    [Fact]
    public async Task Navigation_TrackersListsEachTrackerBehindItsPolicy()
    {
        var trackers = Section(await NavigateAsAsync(new[] { "Project Officer" }, AllTrackerPolicies), NavigationSectionKeys.Trackers);

        Assert.Collection(trackers.Children,
            link => AssertLink(link, "Task management", null, Policies.ActionTracker.Access),
            link => AssertLink(link, "Progress review", "ProjectOfficeReports", ProjectOfficeReportsPolicies.ViewProgressReview),
            link => AssertLink(link, "Visits", "ProjectOfficeReports", ProjectOfficeReportsPolicies.ViewVisits),
            link => AssertLink(link, "Social media", "ProjectOfficeReports", ProjectOfficeReportsPolicies.ManageSocialMediaEvents),
            link =>
            {
                AssertLink(link, "Training", "ProjectOfficeReports", ProjectOfficeReportsPolicies.ViewTrainingTracker);
                Assert.Equal("TrainingApprovalsBadge", link.BadgeViewComponentName);
            },
            link => AssertLink(link, "ToT", "ProjectOfficeReports", ProjectOfficeReportsPolicies.ViewTotTracker),
            link => AssertLink(link, "Proliferation", "ProjectOfficeReports", ProjectOfficeReportsPolicies.ViewProliferationTracker),
            link => AssertLink(link, "IPR register", "ProjectOfficeReports", Policies.Ipr.View),
            link => AssertLink(link, "Activities", null, null),
            link =>
            {
                // Named apart from the Projects section's "ARPP / PPP" library.
                AssertLink(link, "ARPP administration", "ProjectOfficeReports", ProjectOfficeReportsPolicies.ViewArpp);
                Assert.Equal("/ARPP/Index", link.Page);
            });
    }

    [Fact]
    public async Task Navigation_TrackersExcludesUnauthorizedTrackers()
    {
        var navigation = await NavigateAsAsync(new[] { "ProjectOffice" }, Array.Empty<string>());

        var trackers = Section(navigation, NavigationSectionKeys.Trackers);
        var link = Assert.Single(trackers.Children);
        Assert.Equal("Activities", link.Text);
        Assert.Null(link.AuthorizationPolicy);
    }

    [Fact]
    public async Task Navigation_ListsAreasNotTheirSettingsOrActions()
    {
        var links = AllLinks(await NavigateAsAsync(new[] { "Admin", "HoD" }));

        // These open from the module that owns them (see NavigationReachabilityTests).
        Assert.DoesNotContain(links, link => link.Page == "/Projects/Create");
        Assert.DoesNotContain(links, link => link.Page == "/FFC/Index");
        Assert.DoesNotContain(links, link => link.Page == "/VisitTypes/Index");
        Assert.DoesNotContain(links, link => link.Page is not null && link.Page.StartsWith("/Admin/SocialMediaTypes/", StringComparison.Ordinal));
        Assert.DoesNotContain(links, link => link.Area == "DocumentRepository" && link.Page != "/Documents/Index");
        Assert.DoesNotContain(links, link => link.Text == "Activity delete approvals");
    }

    [Fact]
    public async Task Navigation_AdministrationIsAFewEntryPointsForAdminsOnly()
    {
        var administration = Section(await NavigateAsAsync(new[] { "Admin" }), NavigationSectionKeys.Administration);

        Assert.Equal(AdminPolicies.Access, administration.AuthorizationPolicy);
        Assert.Equal(
            new[]
            {
                AdminNavigationKeys.Dashboard,
                AdminNavigationKeys.Users,
                AdminNavigationKeys.Logs,
                AdminNavigationKeys.DatabaseHealth,
                AdminNavigationKeys.AllPages
            }.Select(key => AdminNavigationCatalog.Get(key).Text),
            administration.Children.Select(link => link.Text));
        Assert.All(administration.Children, link => Assert.Equal("Admin", link.Area));

        // Pages inside an entry's area of Administration light that entry.
        Assert.Equal("/Users/", administration.Children.Single(link => link.Page == "/Users/Index").ActivePagePrefix);

        Assert.DoesNotContain(
            await NavigateAsAsync(new[] { "HoD" }),
            section => section.Key == NavigationSectionKeys.Administration);
    }

    [Fact]
    public async Task Navigation_IncludesActivityTypesForHodWithoutAdmin()
    {
        var hodTrackers = Section(await NavigateAsAsync(new[] { "HoD" }), NavigationSectionKeys.Trackers);
        Assert.Contains(hodTrackers.Children, link => link.Text == "Activity types" && link.Page == "/ActivityTypes/Index");

        // Admins reach activity types through Administration's own pages.
        Assert.DoesNotContain(
            AllLinks(await NavigateAsAsync(new[] { "Admin", "HoD" })),
            link => link.Page == "/ActivityTypes/Index");
    }

    [Fact]
    public async Task Navigation_OffersPdfToolsToEverySignedInUserInANewTab()
    {
        var tools = Section(await NavigateAsAsync(Array.Empty<string>()), NavigationSectionKeys.Tools);

        var pdfTools = Assert.Single(tools.Children);
        Assert.Equal("PDF Tools", pdfTools.Text);
        Assert.Equal("/Tools/Pdf/Index", pdfTools.Page);
        Assert.True(pdfTools.OpensInNewTab);
        Assert.Null(pdfTools.AuthorizationPolicy);
        Assert.Null(pdfTools.RequiredRoles);
    }

    [Fact]
    public async Task Navigation_DropsSectionsWhoseLinksAreAllUnauthorized()
    {
        // Every Administration link needs an admin policy; the section must vanish rather
        // than render as a heading with nothing under it.
        var navigation = await NavigateAsAsync(new[] { "Admin" }, Array.Empty<string>());

        Assert.DoesNotContain(navigation, section => section.Key == NavigationSectionKeys.Administration);
        Assert.DoesNotContain(AllLinks(navigation), link => link.Text == "Documents");
        Assert.All(navigation, section => Assert.NotEmpty(section.Children));
    }

    private static async Task<IReadOnlyList<NavigationItem>> NavigateAsAsync(
        string[] roles,
        string[]? allowedPolicies = null)
    {
        var user = new ApplicationUser
        {
            Id = "user-1",
            UserName = "user"
        };

        using var services = new ServiceCollection().BuildServiceProvider();
        var userManager = new StubUserManager(user, services, roles);
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id!)
                }, "Test"))
            }
        };

        var authorization = allowedPolicies is null
            ? new TestAuthorizationService()
            : new TestAuthorizationService(allowedPolicies);
        return await CreateProvider(userManager, httpContextAccessor, authorization).GetNavigationAsync();
    }

    private static NavigationItem Section(IReadOnlyList<NavigationItem> navigation, string key) =>
        navigation.Single(section => section.Key == key);

    private static List<NavigationItem> AllLinks(IReadOnlyList<NavigationItem> navigation) =>
        navigation.SelectMany(section => section.Children).ToList();

    private static void AssertLink(NavigationItem link, string text, string? area, string? policy)
    {
        Assert.Equal(text, link.Text);
        Assert.Equal(area, link.Area);
        Assert.Equal(policy, link.AuthorizationPolicy);
        Assert.False(string.IsNullOrEmpty(link.ActivePagePrefix));
    }

    private static RoleBasedNavigationProvider CreateProvider(
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor httpContextAccessor,
        IAuthorizationService? authorizationService = null)
    {
        return new RoleBasedNavigationProvider(
            userManager,
            httpContextAccessor,
            authorizationService ?? new TestAuthorizationService());
    }

    private sealed class StubUserManager : UserManager<ApplicationUser>
    {
        private readonly ApplicationUser _user;
        private readonly IList<string> _roles;

        public StubUserManager(ApplicationUser user, IServiceProvider services, params string[] roles)
            : base(
                new StubUserStore(),
                Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
                new PasswordHasher<ApplicationUser>(),
                Array.Empty<IUserValidator<ApplicationUser>>(),
                Array.Empty<IPasswordValidator<ApplicationUser>>(),
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                services,
                NullLogger<UserManager<ApplicationUser>>.Instance)
        {
            _user = user;
            _roles = roles;
        }

        public override Task<ApplicationUser?> GetUserAsync(ClaimsPrincipal principal) => Task.FromResult<ApplicationUser?>(_user);

        public override Task<IList<string>> GetRolesAsync(ApplicationUser user) => Task.FromResult(_roles);
    }

    private sealed class TestAuthorizationService : IAuthorizationService
    {
        private readonly bool _allowAllPolicies;
        private readonly HashSet<string> _allowedPolicies;

        public TestAuthorizationService()
        {
            _allowAllPolicies = true;
            _allowedPolicies = new HashSet<string>(StringComparer.Ordinal);
        }

        public TestAuthorizationService(IEnumerable<string> allowedPolicies)
        {
            _allowAllPolicies = false;
            _allowedPolicies = new HashSet<string>(allowedPolicies, StringComparer.Ordinal);
        }

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements)
        {
            return Task.FromResult(AuthorizationResult.Success());
        }

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
        {
            if (_allowAllPolicies || _allowedPolicies.Contains(policyName))
            {
                return Task.FromResult(AuthorizationResult.Success());
            }

            return Task.FromResult(AuthorizationResult.Failed());
        }
    }

    private sealed class StubUserStore : IUserStore<ApplicationUser>
    {
        public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);

        public void Dispose()
        {
        }

        public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) => Task.FromResult<ApplicationUser?>(null);

        public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) => Task.FromResult<ApplicationUser?>(null);

        public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);

        public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.Id!);

        public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);

        public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
    }
}
