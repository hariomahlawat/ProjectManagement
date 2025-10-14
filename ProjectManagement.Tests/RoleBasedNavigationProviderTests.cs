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
using Microsoft.Extensions.Options;
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

        var provider = new RoleBasedNavigationProvider(userManager, httpContextAccessor);
        var navigation = await provider.GetNavigationAsync();

        var adminPanel = navigation.Single(item => item.Text == "Admin Panel");
        var children = adminPanel.Children.ToList();

        Assert.Contains(children, c => c.Text == "Project trash");
        Assert.Contains(children, c => c.Text == "Document recycle bin");
        Assert.Contains(children, c => c.Text == "Calendar deleted events");

        var archivedProjects = children.Single(c => c.Text == "Archived projects");
        Assert.True(archivedProjects.RouteValues?.ContainsKey("IncludeArchived"));
        Assert.Equal(true, archivedProjects.RouteValues?["IncludeArchived"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Navigation_IncludesProjectOfficeReportsVisitsNode(bool isAdmin)
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

        var provider = new RoleBasedNavigationProvider(userManager, httpContextAccessor);
        var navigation = await provider.GetNavigationAsync();

        var projectOfficeReports = navigation.Single(item => item.Text == "Project office reports");
        var children = projectOfficeReports.Children.ToList();

        Assert.Contains(children, c => c.Text == "Visits" && c.Page == "/Visits/Index");

        if (isAdmin)
        {
            Assert.Contains(children, c => c.Text == "Social media tracker" && c.Page == "/SocialMedia/Index");
        }
        else
        {
            Assert.DoesNotContain(children, c => c.Text == "Social media tracker");
        }

        if (isAdmin)
        {
            var visitTypes = Assert.Single(children.Where(c => c.Text == "Visit types"));
            Assert.Equal("/VisitTypes/Index", visitTypes.Page);
            Assert.Equal(new[] { "Admin" }, visitTypes.RequiredRoles);

            var socialMediaTypes = Assert.Single(children.Where(c => c.Text == "Social media event types"));
            Assert.Equal("/Admin/SocialMediaTypes/Index", socialMediaTypes.Page);
            Assert.Equal(new[] { "Admin" }, socialMediaTypes.RequiredRoles);
        }
        else
        {
            Assert.DoesNotContain(children, c => c.Text == "Visit types");
            Assert.DoesNotContain(children, c => c.Text == "Social media event types");
        }
    }

    [Theory]
    [InlineData("HoD")]
    [InlineData("ProjectOffice")]
    [InlineData("Project Office")]
    public async Task Navigation_IncludesSocialMediaTrackerForProjectOfficeManagers(string role)
    {
        var user = new ApplicationUser
        {
            Id = $"user-{role}",
            UserName = role.ToLowerInvariant()
        };

        using var services = new ServiceCollection().BuildServiceProvider();
        var userManager = new StubUserManager(user, services, role);
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

        var provider = new RoleBasedNavigationProvider(userManager, httpContextAccessor);
        var navigation = await provider.GetNavigationAsync();

        var projectOfficeReports = navigation.Single(item => item.Text == "Project office reports");
        var children = projectOfficeReports.Children.ToList();

        Assert.Contains(children, c => c.Text == "Social media tracker" && c.Page == "/SocialMedia/Index");
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
