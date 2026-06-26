using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Navigation;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class DefaultLandingPageResolverTests
{
    [Fact]
    public async Task ResolveAsync_ProjectOfficer_ReturnsWorkspaceRazorPageName()
    {
        await using var fixture = await IdentityFixture.CreateAsync(RoleNames.ProjectOfficer);
        var resolver = new DefaultLandingPageResolver(fixture.UserManager);

        var pageName = await resolver.ResolveAsync(fixture.User);

        Assert.Equal("/Workspace/Index", pageName);
    }

    [Fact]
    public async Task ResolveAsync_HodAndProjectOfficer_PrefersDashboard()
    {
        await using var fixture = await IdentityFixture.CreateAsync(RoleNames.HoD, RoleNames.ProjectOfficer);
        var resolver = new DefaultLandingPageResolver(fixture.UserManager);

        var pageName = await resolver.ResolveAsync(fixture.User);

        Assert.Equal("/Dashboard/Index", pageName);
    }

    private sealed class IdentityFixture : IAsyncDisposable
    {
        private IdentityFixture(ApplicationDbContext db, UserManager<ApplicationUser> userManager, ApplicationUser user)
        {
            Db = db;
            UserManager = userManager;
            User = user;
        }

        public ApplicationDbContext Db { get; }
        public UserManager<ApplicationUser> UserManager { get; }
        public ApplicationUser User { get; }

        public static async Task<IdentityFixture> CreateAsync(params string[] roles)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var db = new ApplicationDbContext(options);
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
                new Logger<UserManager<ApplicationUser>>(new LoggerFactory()));

            foreach (var role in roles.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                db.Roles.Add(new IdentityRole
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = role,
                    NormalizedName = role.ToUpperInvariant()
                });
            }
            await db.SaveChangesAsync();

            var user = new ApplicationUser
            {
                Id = "landing-user",
                UserName = "landing.user",
                FullName = "Landing User"
            };
            await userManager.CreateAsync(user);
            foreach (var role in roles)
            {
                await userManager.AddToRoleAsync(user, role);
            }

            return new IdentityFixture(db, userManager, user);
        }

        public async ValueTask DisposeAsync()
        {
            UserManager.Dispose();
            await Db.DisposeAsync();
        }
    }
}
