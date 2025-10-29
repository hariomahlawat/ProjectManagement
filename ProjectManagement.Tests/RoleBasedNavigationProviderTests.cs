using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Navigation_ProjectOfficeReportsContainsFfcNode(bool isAdmin)
    {
        var user = new ApplicationUser
        {
            Id = isAdmin ? "admin-2" : "user-1",
            UserName = isAdmin ? "admin" : "user"
        };

        using var services = new ServiceCollection().BuildServiceProvider();
        var roles = isAdmin ? new[] { "Admin" } : Array.Empty<string>();
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

        var provider = CreateProvider(userManager, httpContextAccessor);
        var navigation = await provider.GetNavigationAsync();

        var projectOfficeReports = navigation.Single(item => item.Text == "Project office reports");
        var child = Assert.Single(projectOfficeReports.Children);

        Assert.Equal("FFC simulators", child.Text);
        Assert.Equal("ProjectOfficeReports", child.Area);
        Assert.Equal("/FFC/Index", child.Page);
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
        IHttpContextAccessor httpContextAccessor)
    {
        return new RoleBasedNavigationProvider(userManager, httpContextAccessor);
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
