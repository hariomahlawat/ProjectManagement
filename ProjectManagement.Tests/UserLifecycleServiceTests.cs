using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using Xunit;

namespace ProjectManagement.Tests
{
    public class UserLifecycleServiceTests
    {
        private static UserLifecycleService CreateService(UserLifecycleOptions? options,
            out ApplicationDbContext context,
            out UserManager<ApplicationUser> userManager,
            out RoleManager<IdentityRole> roleManager)
        {
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            context = new ApplicationDbContext(opts);

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

            var http = new HttpContextAccessor();
            var audit = new AuditService(context, http);
            return new UserLifecycleService(context, userManager, audit, Options.Create(options ?? new UserLifecycleOptions()), new FixedClock(DateTimeOffset.UtcNow));
        }

        [Fact]
        public async Task CreationAgeGate()
        {
            var svc = CreateService(new UserLifecycleOptions { HardDeleteWindowHours = 72 }, out var ctx, out var um, out var rm);
            var user1 = new ApplicationUser { UserName = "u1", CreatedUtc = DateTime.UtcNow.AddHours(-71) };
            await um.CreateAsync(user1, "Passw0rd!");
            var res1 = await svc.RequestHardDeleteAsync(user1.Id, "actor");
            Assert.True(res1.Allowed);

            var user2 = new ApplicationUser { UserName = "u2", CreatedUtc = DateTime.UtcNow.AddHours(-73) };
            await um.CreateAsync(user2, "Passw0rd!");
            var res2 = await svc.RequestHardDeleteAsync(user2.Id, "actor");
            Assert.False(res2.Allowed);
        }

        [Fact]
        public async Task UndoWindowHonored()
        {
            var svc = CreateService(null, out var ctx, out var um, out var rm);
            var u = new ApplicationUser { UserName = "u", CreatedUtc = DateTime.UtcNow };
            await um.CreateAsync(u, "Passw0rd!");
            await svc.RequestHardDeleteAsync(u.Id, "actor");
            u.DeletionRequestedUtc = DateTime.UtcNow.AddMinutes(-10);
            ctx.Update(u);
            await ctx.SaveChangesAsync();
            var ok1 = await svc.UndoHardDeleteAsync(u.Id, "actor");
            Assert.True(ok1);

            await svc.RequestHardDeleteAsync(u.Id, "actor");
            u = await um.FindByIdAsync(u.Id);
            u!.DeletionRequestedUtc = DateTime.UtcNow.AddMinutes(-16);
            ctx.Update(u);
            await ctx.SaveChangesAsync();
            var ok2 = await svc.UndoHardDeleteAsync(u.Id, "actor");
            Assert.False(ok2);
        }

        [Fact]
        public async Task SecurityStampChangesOnDisableAndDelete()
        {
            var svc = CreateService(null, out var ctx, out var um, out var rm);
            var u = new ApplicationUser { UserName = "u", CreatedUtc = DateTime.UtcNow };
            await um.CreateAsync(u, "Passw0rd!");
            var stamp1 = u.SecurityStamp;
            await svc.DisableAsync(u.Id, "actor", "reason");
            var stamp2 = (await um.FindByIdAsync(u.Id))!.SecurityStamp;
            Assert.NotEqual(stamp1, stamp2);

            var stamp3 = stamp2;
            await svc.RequestHardDeleteAsync(u.Id, "actor");
            var stamp4 = (await um.FindByIdAsync(u.Id))!.SecurityStamp;
            Assert.NotEqual(stamp3, stamp4);
        }

        [Fact]
        public async Task LastAdminGuard()
        {
            var svc = CreateService(null, out var ctx, out var um, out var rm);
            await rm.CreateAsync(new IdentityRole("Admin"));
            var admin = new ApplicationUser { UserName = "admin", CreatedUtc = DateTime.UtcNow };
            await um.CreateAsync(admin, "Passw0rd!");
            await um.AddToRoleAsync(admin, "Admin");
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DisableAsync(admin.Id, "actor", ""));
            var res = await svc.RequestHardDeleteAsync(admin.Id, "actor");
            Assert.False(res.Allowed);
        }

        [Fact]
        public async Task PurgeRemovesUserOnly()
        {
            var svc = CreateService(new UserLifecycleOptions { UndoWindowMinutes = 15 }, out var ctx, out var um, out var rm);
            var user = new ApplicationUser { UserName = "u", CreatedUtc = DateTime.UtcNow };
            await um.CreateAsync(user, "Passw0rd!");
            ctx.Projects.Add(new Project
            {
                Name = "P",
                CreatedAt = DateTime.UtcNow,
                Description = string.Empty,
                CreatedByUserId = "seed"
            });
            await ctx.SaveChangesAsync();
            await svc.RequestHardDeleteAsync(user.Id, "actor");
            user = await um.FindByIdAsync(user.Id);
            user!.DeletionRequestedUtc = DateTime.UtcNow.AddMinutes(-20);
            ctx.Update(user);
            await ctx.SaveChangesAsync();
            var purged = await svc.PurgeIfDueAsync(user.Id);
            Assert.True(purged);
            Assert.Null(await um.FindByIdAsync(user.Id));
            Assert.Equal(1, await ctx.Projects.CountAsync());
        }

        [Fact]
        public async Task CannotDisableOwnAccount()
        {
            var svc = CreateService(null, out var ctx, out var um, out var rm);
            var user = new ApplicationUser { UserName = "u", CreatedUtc = DateTime.UtcNow };
            await um.CreateAsync(user, "Passw0rd!");
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DisableAsync(user.Id, user.Id, "reason"));
        }

        [Fact]
        public async Task UndoDeletionRestoresPreviouslyActiveAccount()
        {
            var svc = CreateService(null, out var ctx, out var um, out var rm);
            var user = new ApplicationUser
            {
                UserName = "active-user",
                CreatedUtc = DateTime.UtcNow,
                IsDisabled = false,
                LockoutEnabled = true,
                LockoutEnd = null
            };
            await um.CreateAsync(user, "Passw0rd!");

            Assert.True((await svc.RequestHardDeleteAsync(user.Id, "actor")).Allowed);
            Assert.True(await svc.UndoHardDeleteAsync(user.Id, "actor"));

            var restored = await um.FindByIdAsync(user.Id);
            Assert.NotNull(restored);
            Assert.False(restored!.IsDisabled);
            Assert.False(restored.PendingDeletion);
            Assert.Null(restored.LockoutEnd);
            Assert.Null(restored.DeletionPreviousStateJson);
        }

        [Fact]
        public async Task UndoDeletionRestoresPreviouslyDisabledAccount()
        {
            var svc = CreateService(null, out var ctx, out var um, out var rm);
            var originalDisabledUtc = DateTime.UtcNow.AddHours(-2);
            var originalLockoutEnd = DateTimeOffset.UtcNow.AddDays(2);
            var user = new ApplicationUser
            {
                UserName = "disabled-user",
                CreatedUtc = DateTime.UtcNow,
                IsDisabled = true,
                DisabledUtc = originalDisabledUtc,
                DisabledByUserId = "previous-admin",
                LockoutEnabled = true,
                LockoutEnd = originalLockoutEnd,
                AccessFailedCount = 3
            };
            await um.CreateAsync(user, "Passw0rd!");

            Assert.True((await svc.RequestHardDeleteAsync(user.Id, "actor")).Allowed);
            Assert.True(await svc.UndoHardDeleteAsync(user.Id, "actor"));

            var restored = await um.FindByIdAsync(user.Id);
            Assert.NotNull(restored);
            Assert.True(restored!.IsDisabled);
            Assert.Equal(originalDisabledUtc, restored.DisabledUtc);
            Assert.Equal("previous-admin", restored.DisabledByUserId);
            Assert.Equal(originalLockoutEnd, restored.LockoutEnd);
            Assert.Equal(3, restored.AccessFailedCount);
            Assert.False(restored.PendingDeletion);
        }

        // One rule for an active administrator (docs/ux/admin-review-2-fixes.md, PR 5).
        [Fact]
        public async Task Disable_RefusesTheLastActiveAdmin_WhenTheOthersAreDisabledOrPendingDeletion()
        {
            var svc = CreateService(null, out var ctx, out var um, out var rm);
            await rm.CreateAsync(new IdentityRole(RoleNames.Admin));
            var admin = new ApplicationUser { UserName = "admin", CreatedUtc = DateTime.UtcNow };
            var disabled = new ApplicationUser { UserName = "disabled-admin", CreatedUtc = DateTime.UtcNow, IsDisabled = true };
            var leaving = new ApplicationUser { UserName = "leaving-admin", CreatedUtc = DateTime.UtcNow, IsDisabled = true, PendingDeletion = true };
            foreach (var user in new[] { admin, disabled, leaving })
            {
                await um.CreateAsync(user, "Passw0rd!");
                await um.AddToRoleAsync(user, RoleNames.Admin);
            }

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DisableAsync(admin.Id, "actor", "reason"));
            Assert.Contains("last active Admin", error.Message);
            Assert.False((await um.FindByIdAsync(admin.Id))!.IsDisabled);

            var delete = await svc.RequestHardDeleteAsync(admin.Id, "actor");
            Assert.False(delete.Allowed);
            Assert.False((await um.FindByIdAsync(admin.Id))!.PendingDeletion);
        }

        // Disable account reads a typed outcome instead of exception text
        // (docs/ux/admin-review-3-fixes.md, C8).
        [Fact]
        public async Task TryDisable_ReturnsATypedOutcomeForEverySafeguard()
        {
            var svc = CreateService(null, out var ctx, out var um, out var rm);
            await rm.CreateAsync(new IdentityRole(RoleNames.Admin));
            var admin = new ApplicationUser { UserName = "only-admin", CreatedUtc = DateTime.UtcNow };
            var leaving = new ApplicationUser { UserName = "leaving", CreatedUtc = DateTime.UtcNow, PendingDeletion = true };
            var user = new ApplicationUser { UserName = "user", CreatedUtc = DateTime.UtcNow };
            foreach (var account in new[] { admin, leaving, user })
            {
                await um.CreateAsync(account, "Passw0rd!");
            }
            await um.AddToRoleAsync(admin, RoleNames.Admin);

            Assert.Equal(UserDisableOutcome.SelfDisable, await svc.TryDisableAsync(user.Id, user.Id, "reason"));
            Assert.Equal(UserDisableOutcome.NotFound, await svc.TryDisableAsync("missing", "actor", "reason"));
            Assert.Equal(UserDisableOutcome.PendingDeletion, await svc.TryDisableAsync(leaving.Id, "actor", "reason"));
            Assert.Equal(UserDisableOutcome.LastActiveAdmin, await svc.TryDisableAsync(admin.Id, "actor", "reason"));
            Assert.False((await um.FindByIdAsync(admin.Id))!.IsDisabled);

            Assert.Equal(UserDisableOutcome.Disabled, await svc.TryDisableAsync(user.Id, "actor", "reason"));
            Assert.True((await um.FindByIdAsync(user.Id))!.IsDisabled);

            // A second, duplicate request changes nothing and says so.
            Assert.Equal(UserDisableOutcome.AlreadyDisabled, await svc.TryDisableAsync(user.Id, "actor", "reason"));
        }

        [Fact]
        public async Task Disable_Allowed_WhenTheOtherAdminIsOnlyTemporarilyLocked()
        {
            var svc = CreateService(null, out var ctx, out var um, out var rm);
            await rm.CreateAsync(new IdentityRole(RoleNames.Admin));
            var admin = new ApplicationUser { UserName = "admin", CreatedUtc = DateTime.UtcNow };
            var locked = new ApplicationUser
            {
                UserName = "locked-admin",
                CreatedUtc = DateTime.UtcNow,
                LockoutEnabled = true,
                LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15)
            };
            foreach (var user in new[] { admin, locked })
            {
                await um.CreateAsync(user, "Passw0rd!");
                await um.AddToRoleAsync(user, RoleNames.Admin);
            }

            await svc.DisableAsync(admin.Id, "actor", "reason");

            Assert.True((await um.FindByIdAsync(admin.Id))!.IsDisabled);
        }

        private sealed class FixedClock : IClock
        {
            public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;
            public DateTimeOffset UtcNow { get; }
        }
    }
}
