using System;
using System.Linq;
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

            await roleManager.CreateAsync(new IdentityRole("HoD"));

            var result = await service.UpdateUserAsync(admin.Id, admin.FullName, admin.Rank, new[] { "HoD" });

            Assert.False(result.Succeeded);
            Assert.Contains("last active Admin", Assert.Single(result.Errors).Description);
            Assert.Contains("Admin", await userManager.GetRolesAsync(admin));
        }

        // One rule for an active administrator (docs/ux/admin-review-2-fixes.md, PR 5): a disabled
        // or pending-deletion Admin does not count, so the last active Admin keeps the role.
        [Fact]
        public async Task CannotRemoveAdminRole_WhenTheOtherAdminsAreDisabledOrPendingDeletion()
        {
            var service = CreateService("admin", out var context, out var userManager, out var roleManager);
            await roleManager.CreateAsync(new IdentityRole("Admin"));
            await roleManager.CreateAsync(new IdentityRole("HoD"));
            var admin = new ApplicationUser { UserName = "admin" };
            var disabled = new ApplicationUser { UserName = "disabled-admin", IsDisabled = true, LockoutEnabled = true, LockoutEnd = DateTimeOffset.MaxValue };
            var leaving = new ApplicationUser { UserName = "leaving-admin", IsDisabled = true, PendingDeletion = true };
            foreach (var user in new[] { admin, disabled, leaving })
            {
                await userManager.CreateAsync(user, "Passw0rd!");
                await userManager.AddToRoleAsync(user, "Admin");
            }

            var result = await service.UpdateUserAsync(admin.Id, admin.FullName, admin.Rank, new[] { "HoD" });

            Assert.False(result.Succeeded);
            Assert.Contains("last active Admin", Assert.Single(result.Errors).Description);
            Assert.Contains("Admin", await userManager.GetRolesAsync(admin));
        }

        // A temporary lockout ends by itself, so a locked Admin still counts and the other Admin
        // can lose the role (the guard is not made stricter than before).
        [Fact]
        public async Task CanRemoveAdminRole_WhenTheOtherAdminIsOnlyTemporarilyLocked()
        {
            var service = CreateService("admin", out var context, out var userManager, out var roleManager);
            await roleManager.CreateAsync(new IdentityRole("Admin"));
            await roleManager.CreateAsync(new IdentityRole("HoD"));
            var admin = new ApplicationUser { UserName = "admin" };
            var locked = new ApplicationUser { UserName = "locked-admin", LockoutEnabled = true, LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15) };
            foreach (var user in new[] { admin, locked })
            {
                await userManager.CreateAsync(user, "Passw0rd!");
                await userManager.AddToRoleAsync(user, "Admin");
            }

            var result = await service.UpdateUserAsync(admin.Id, admin.FullName, admin.Rank, new[] { "HoD" });

            Assert.True(result.Succeeded);
            Assert.DoesNotContain("Admin", await userManager.GetRolesAsync(admin));
        }

        [Fact]
        public async Task Unlock_ClearsATemporaryLockoutAndFailedCount()
        {
            var service = CreateService("admin", out var context, out var userManager, out var roleManager);
            var user = new ApplicationUser { UserName = "locked", LockoutEnabled = true };
            await userManager.CreateAsync(user, "Passw0rd!");
            await userManager.AccessFailedAsync(user);
            await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(5));

            var result = await service.UnlockAsync(user.Id);

            Assert.True(result.Succeeded);
            var reloaded = await userManager.FindByIdAsync(user.Id);
            Assert.Null(reloaded!.LockoutEnd);
            Assert.Equal(0, reloaded.AccessFailedCount);
            Assert.Contains(context.AuditLogs, log => log.Action == "AdminUserUnlocked");
        }

        [Fact]
        public async Task Unlock_RefusesADisabledAccount()
        {
            var service = CreateService("admin", out var context, out var userManager, out var roleManager);
            var user = new ApplicationUser { UserName = "disabled", IsDisabled = true, LockoutEnabled = true, LockoutEnd = DateTimeOffset.MaxValue };
            await userManager.CreateAsync(user, "Passw0rd!");

            var result = await service.UnlockAsync(user.Id);

            Assert.False(result.Succeeded);
            Assert.Equal(DateTimeOffset.MaxValue, (await userManager.FindByIdAsync(user.Id))!.LockoutEnd);
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
        // An account holding a role outside the catalogue (an integration role) used to be
        // impossible to edit: the page sent that role back and the service rejected it.
        [Fact]
        public async Task UpdateUser_KeepsAnExtensionRoleTheAccountAlreadyHolds()
        {
            var service = CreateService("admin", out var context, out var userManager, out var roleManager);
            await roleManager.CreateAsync(new IdentityRole("Admin"));
            await roleManager.CreateAsync(new IdentityRole("HoD"));
            await roleManager.CreateAsync(new IdentityRole("Auditor"));
            var user = new ApplicationUser { UserName = "integrator", FullName = "Old Name", Rank = "Maj" };
            await userManager.CreateAsync(user, "Passw0rd!");
            await userManager.AddToRolesAsync(user, new[] { "HoD", "Auditor" });

            // As the Edit page used to send it (catalogue roles plus the held extension role)...
            var withExtension = await service.UpdateUserAsync(user.Id, "New Name", "Lt Col", new[] { "HoD", "Auditor" });
            // ...and as it sends it now (catalogue roles only).
            var catalogueOnly = await service.UpdateUserAsync(user.Id, "New Name", "Lt Col", new[] { "Admin" });

            Assert.True(withExtension.Succeeded, string.Join("; ", withExtension.Errors.Select(e => e.Description)));
            Assert.True(catalogueOnly.Succeeded, string.Join("; ", catalogueOnly.Errors.Select(e => e.Description)));
            var roles = await userManager.GetRolesAsync((await userManager.FindByIdAsync(user.Id))!);
            Assert.Contains("Admin", roles);
            Assert.Contains("Auditor", roles);
            Assert.DoesNotContain("HoD", roles);
        }

        [Fact]
        public async Task UpdateUser_StillRejectsAnExtensionRoleTheAccountDoesNotHold()
        {
            var service = CreateService("admin", out var context, out var userManager, out var roleManager);
            await roleManager.CreateAsync(new IdentityRole("HoD"));
            await roleManager.CreateAsync(new IdentityRole("Auditor"));
            var user = new ApplicationUser { UserName = "officer", FullName = "Name", Rank = "Maj" };
            await userManager.CreateAsync(user, "Passw0rd!");
            await userManager.AddToRoleAsync(user, "HoD");

            var result = await service.UpdateUserAsync(user.Id, "Name", "Maj", new[] { "HoD", "Auditor" });

            Assert.False(result.Succeeded);
            Assert.DoesNotContain("Auditor", await userManager.GetRolesAsync((await userManager.FindByIdAsync(user.Id))!));
        }

        // Two administrators editing the same account: the second save used to win silently,
        // removing a role the first had just added.
        [Fact]
        public async Task UpdateUser_RefusesAStaleFormVersion()
        {
            var service = CreateService("admin", out var context, out var userManager, out var roleManager);
            await roleManager.CreateAsync(new IdentityRole("HoD"));
            await roleManager.CreateAsync(new IdentityRole("Admin"));
            var user = new ApplicationUser { UserName = "officer", FullName = "Name", Rank = "Maj" };
            await userManager.CreateAsync(user, "Passw0rd!");
            await userManager.AddToRoleAsync(user, "HoD");
            var openedVersion = UserEditVersion.Compute("Name", "Maj", user.AccountKind, new[] { "HoD" });

            // Another administrator adds Admin after this form was opened.
            await userManager.AddToRoleAsync(user, "Admin");

            var result = await service.UpdateUserAsync(user.Id, "Name", "Maj", new[] { "HoD" }, expectedVersion: openedVersion);

            Assert.False(result.Succeeded);
            Assert.Equal(nameof(IdentityErrorDescriber.ConcurrencyFailure), Assert.Single(result.Errors).Code);
            Assert.Contains("Admin", await userManager.GetRolesAsync((await userManager.FindByIdAsync(user.Id))!));
        }

        [Fact]
        public async Task UpdateUser_AcceptsTheCurrentVersion_EvenAfterASignInLevelChange()
        {
            var service = CreateService("admin", out var context, out var userManager, out var roleManager);
            await roleManager.CreateAsync(new IdentityRole("HoD"));
            await roleManager.CreateAsync(new IdentityRole("Admin"));
            var user = new ApplicationUser { UserName = "officer", FullName = "Name", Rank = "Maj" };
            await userManager.CreateAsync(user, "Passw0rd!");
            await userManager.AddToRoleAsync(user, "HoD");
            var openedVersion = UserEditVersion.Compute("Name", "Maj", user.AccountKind, new[] { "HoD" });

            // Sign-in bookkeeping rotates Identity's ConcurrencyStamp but changes nothing the
            // form shows, so it must not block the save.
            await userManager.UpdateSecurityStampAsync(user);
            await userManager.AccessFailedAsync(user);

            var result = await service.UpdateUserAsync(user.Id, "New Name", "Maj", new[] { "HoD", "Admin" }, expectedVersion: openedVersion);

            Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
            Assert.Contains("Admin", await userManager.GetRolesAsync((await userManager.FindByIdAsync(user.Id))!));
        }

        [Fact]
        public void EditVersion_IgnoresWhitespaceRoleOrderAndCase()
        {
            var a = UserEditVersion.Compute(" Name ", "Maj", UserAccountKind.Human, new[] { "HoD", "Admin" });
            var b = UserEditVersion.Compute("Name", " Maj", UserAccountKind.Human, new[] { "admin", "HOD" });

            Assert.Equal(a, b);
            Assert.NotEqual(a, UserEditVersion.Compute("Name", "Maj", UserAccountKind.Human, new[] { "HoD" }));
            Assert.True(UserEditVersion.Matches(a.ToLowerInvariant(), b));
            Assert.False(UserEditVersion.Matches(null, b));
        }

        private sealed class NullRoleNotificationService : IRoleNotificationService
        {
            public Task NotifyRolesUpdatedAsync(ApplicationUser user, IReadOnlyCollection<string> addedRoles, IReadOnlyCollection<string> removedRoles, string actorUserId, CancellationToken cancellationToken = default)
                => Task.CompletedTask;
        }
    }
}
