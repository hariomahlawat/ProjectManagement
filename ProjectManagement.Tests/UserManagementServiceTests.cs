using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using Xunit;

namespace ProjectManagement.Tests
{
    public class UserManagementServiceTests
    {
        private static UserManagementService CreateService(string currentUserName, out ApplicationDbContext context, out UserManager<ApplicationUser> userManager, out RoleManager<IdentityRole> roleManager)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            context = new ApplicationDbContext(options);

            var services = new ServiceCollection()
                .AddLogging()
                .BuildServiceProvider();

            userManager = new UserManager<ApplicationUser>(
                new UserStore<ApplicationUser>(context),
                Options.Create(new IdentityOptions()),
                new PasswordHasher<ApplicationUser>(),
                Array.Empty<IUserValidator<ApplicationUser>>(),
                Array.Empty<IPasswordValidator<ApplicationUser>>(),
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                services,
                new Logger<UserManager<ApplicationUser>>(new LoggerFactory()));

            roleManager = new RoleManager<IdentityRole>(
                new RoleStore<IdentityRole>(context),
                new IRoleValidator<IdentityRole>[] { new RoleValidator<IdentityRole>() },
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                new Logger<RoleManager<IdentityRole>>(new LoggerFactory()));

            var httpContext = new DefaultHttpContext();
            if (!string.IsNullOrEmpty(currentUserName))
            {
                httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, currentUserName) }, "test"));
            }
            var accessor = new HttpContextAccessor { HttpContext = httpContext };
            var audit = new AuditService(context, accessor);

            return new UserManagementService(context, userManager, roleManager, accessor, audit, new NullRoleNotificationService());
        }

        [Fact]
        public async Task CannotRemoveAdminRoleFromLastActiveAdmin()
        {
            var service = CreateService("admin", out var context, out var userManager, out var roleManager);
            await roleManager.CreateAsync(new IdentityRole("Admin"));
            var admin = new ApplicationUser { UserName = "admin" };
            await userManager.CreateAsync(admin, "Passw0rd!");
            await userManager.AddToRoleAsync(admin, "Admin");

            var result = await service.UpdateUserRolesAsync(admin.Id, Array.Empty<string>());

            Assert.False(result.Succeeded);
        }

        [Fact]
        public async Task CannotDeleteLastActiveAdmin()
        {
            var service = CreateService("other", out var context, out var userManager, out var roleManager);
            await roleManager.CreateAsync(new IdentityRole("Admin"));
            var admin = new ApplicationUser { UserName = "admin" };
            await userManager.CreateAsync(admin, "Passw0rd!");
            await userManager.AddToRoleAsync(admin, "Admin");

            var result = await service.DeleteUserAsync(admin.Id);

            Assert.False(result.Succeeded);
        }

        [Fact]
        public async Task UnknownRoleDoesNotLeavePartialAccount()
        {
            var service = CreateService("admin", out var context, out var userManager, out var roleManager);
            await roleManager.CreateAsync(new IdentityRole("Admin"));

            var result = await service.CreateUserAsync(
                "newuser",
                "Passw0rd!",
                "New User",
                "Lt",
                new[] { "MissingRole" });

            Assert.False(result.Succeeded);
            Assert.Null(await userManager.FindByNameAsync("newuser"));
        }

        [Fact]
        public async Task UpdateUserChangesProfileAndRolesTogether()
        {
            var service = CreateService("admin", out var context, out var userManager, out var roleManager);
            await roleManager.CreateAsync(new IdentityRole("Admin"));
            await roleManager.CreateAsync(new IdentityRole("HoD"));
            var user = new ApplicationUser { UserName = "officer", FullName = "Old Name", Rank = "Maj" };
            await userManager.CreateAsync(user, "Passw0rd!");
            await userManager.AddToRoleAsync(user, "HoD");

            var result = await service.UpdateUserAsync(user.Id, "New Name", "Lt Col", new[] { "Admin", "HoD" });

            Assert.True(result.Succeeded);
            var updated = await userManager.FindByIdAsync(user.Id);
            Assert.Equal("New Name", updated!.FullName);
            Assert.Equal("Lt Col", updated.Rank);
            var roles = await userManager.GetRolesAsync(updated);
            Assert.Contains("Admin", roles);
            Assert.Contains("HoD", roles);
        }
        private sealed class NullRoleNotificationService : IRoleNotificationService
        {
            public Task NotifyRolesUpdatedAsync(ApplicationUser user, IReadOnlyCollection<string> addedRoles, IReadOnlyCollection<string> removedRoles, string actorUserId, CancellationToken cancellationToken = default)
                => Task.CompletedTask;
        }
    }
}
