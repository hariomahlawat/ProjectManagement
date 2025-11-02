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
using ProjectManagement.Services.Navigation;
using Xunit;

namespace ProjectManagement.Tests;

public class RoleBasedNavigationProviderTests
{
    [Fact]
    public async Task AdminNavigation_IncludesLifecycleShortcuts()
    {
        var user = new ApplicationUser
        {
            Id = "admin-1",
            UserName = "admin"
        };

        using var services = new ServiceCollection().BuildServiceProvider();
        var userManager = new StubUserManager(user, services, "Admin");
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

        var provider = CreateProvider(userManager, httpContextAccessor);
        var navigation = await provider.GetNavigationAsync();

        var adminPanel = navigation.Single(item => item.Text == "Admin Panel");
        var children = adminPanel.Children.ToList();

        Assert.Contains(children, c => c.Text == "Project trash");
        Assert.Contains(children, c => c.Text == "Document recycle bin");
        Assert.Contains(children, c => c.Text == "Calendar deleted events");
        Assert.Contains(children, c => c.Text == "Activity types" && c.Page == "/ActivityTypes/Index");

        var archivedProjects = children.Single(c => c.Text == "Archived projects");
        Assert.True(archivedProjects.RouteValues?.ContainsKey("IncludeArchived"));
        Assert.Equal(true, archivedProjects.RouteValues?["IncludeArchived"]);
    }

    [Fact]
    public async Task Navigation_ProjectOfficeReportsIncludesAuthorizedChildren()
    {
        var user = new ApplicationUser
        {
            Id = "project-office-1",
            UserName = "projectoffice"
        };

        using var services = new ServiceCollection().BuildServiceProvider();
        var userManager = new StubUserManager(user, services, "ProjectOffice");
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

        var authorizedPolicies = new[]
        {
            ProjectOfficeReportsPolicies.ViewVisits,
            ProjectOfficeReportsPolicies.ManageSocialMediaEvents,
            ProjectOfficeReportsPolicies.ViewTrainingTracker,
            ProjectOfficeReportsPolicies.ViewTotTracker,
            ProjectOfficeReportsPolicies.ViewProliferationTracker,
            Policies.Ipr.View
        };

        var authorizationService = new TestAuthorizationService(authorizedPolicies);
        var provider = CreateProvider(userManager, httpContextAccessor, authorizationService);
        var navigation = await provider.GetNavigationAsync();

        var projectOfficeReports = navigation.Single(item => item.Text == "Project office reports");
        var children = projectOfficeReports.Children.ToList();

        Assert.Collection(children,
            item =>
            {
                Assert.Equal("Visits tracker", item.Text);
                Assert.Equal(ProjectOfficeReportsPolicies.ViewVisits, item.AuthorizationPolicy);
            },
            item =>
            {
                Assert.Equal("Social media tracker", item.Text);
                Assert.Equal(ProjectOfficeReportsPolicies.ManageSocialMediaEvents, item.AuthorizationPolicy);
            },
            item =>
            {
                Assert.Equal("Training tracker", item.Text);
                Assert.Equal(ProjectOfficeReportsPolicies.ViewTrainingTracker, item.AuthorizationPolicy);
                Assert.Equal("TrainingApprovalsBadge", item.BadgeViewComponentName);
            },
            item =>
            {
                Assert.Equal("ToT tracker", item.Text);
                Assert.Equal(ProjectOfficeReportsPolicies.ViewTotTracker, item.AuthorizationPolicy);
            },
            item =>
            {
                Assert.Equal("Proliferation tracker", item.Text);
                Assert.Equal(ProjectOfficeReportsPolicies.ViewProliferationTracker, item.AuthorizationPolicy);
            },
            item =>
            {
                Assert.Equal("Patent tracker", item.Text);
                Assert.Equal(Policies.Ipr.View, item.AuthorizationPolicy);
            },
            item =>
            {
                Assert.Equal("FFC simulators", item.Text);
                Assert.Null(item.AuthorizationPolicy);
            });

        Assert.All(children, item => Assert.Equal("ProjectOfficeReports", item.Area));
    }

    [Fact]
    public async Task Navigation_ProjectOfficeReportsExcludesUnauthorizedChildren()
    {
        var user = new ApplicationUser
        {
            Id = "project-office-unauthorized",
            UserName = "limited"
        };

        using var services = new ServiceCollection().BuildServiceProvider();
        var userManager = new StubUserManager(user, services, "ProjectOffice");
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

        var authorizationService = new TestAuthorizationService(Array.Empty<string>());
        var provider = CreateProvider(userManager, httpContextAccessor, authorizationService);
        var navigation = await provider.GetNavigationAsync();

        var projectOfficeReports = navigation.Single(item => item.Text == "Project office reports");
        var children = projectOfficeReports.Children.ToList();

        var child = Assert.Single(children);
        Assert.Equal("FFC simulators", child.Text);
        Assert.Null(child.AuthorizationPolicy);
    }

    [Fact]
    public async Task Navigation_DoesNotIncludeActivityDeleteApprovals()
    {
        var user = new ApplicationUser
        {
            Id = "admin-approver",
            UserName = "admin"
        };

        using var services = new ServiceCollection().BuildServiceProvider();
        var userManager = new StubUserManager(user, services, "Admin", "HoD");
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

        var provider = CreateProvider(userManager, httpContextAccessor);
        var navigation = await provider.GetNavigationAsync();

        var projectOfficeReports = navigation.Single(item => item.Text == "Project office reports");

        Assert.DoesNotContain(projectOfficeReports.Children, c => string.Equals(c.Text, "Activity delete approvals", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Navigation_IncludesActivityTypesForHod()
    {
        var user = new ApplicationUser
        {
            Id = "hod-1",
            UserName = "hod"
        };

        using var services = new ServiceCollection().BuildServiceProvider();
        var userManager = new StubUserManager(user, services, "HoD");
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

        var provider = CreateProvider(userManager, httpContextAccessor);
        var navigation = await provider.GetNavigationAsync();

        Assert.Contains(navigation, item => item.Text == "Activity types" && item.Page == "/ActivityTypes/Index");
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
