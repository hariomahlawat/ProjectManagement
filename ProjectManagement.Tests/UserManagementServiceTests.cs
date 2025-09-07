using System;
using System.Security.Claims;
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

            return new UserManagementService(userManager, roleManager, accessor);
        }

        [Fact]
        public async Task CannotDisableOwnAccount()
        {
            var service = CreateService("admin", out var context, out var userManager, out var roleManager);
            await roleManager.CreateAsync(new IdentityRole("Admin"));
            var admin = new ApplicationUser { UserName = "admin" };
            await userManager.CreateAsync(admin, "Passw0rd!");
            await userManager.AddToRoleAsync(admin, "Admin");

            var result = await service.ToggleUserActivationAsync(admin.Id, false);

            Assert.False(result.Succeeded);
        }

        [Fact]
        public async Task CannotDisableLastActiveAdmin()
        {
            var service = CreateService("other", out var context, out var userManager, out var roleManager);
            await roleManager.CreateAsync(new IdentityRole("Admin"));
            var admin = new ApplicationUser { UserName = "admin" };
            await userManager.CreateAsync(admin, "Passw0rd!");
            await userManager.AddToRoleAsync(admin, "Admin");

            var result = await service.ToggleUserActivationAsync(admin.Id, false);

            Assert.False(result.Succeeded);
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
    }
}
