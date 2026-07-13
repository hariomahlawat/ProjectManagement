using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Admin;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class AdminUserQueryServiceTests
{
    [Fact]
    public async Task GetAsync_UsesCanonicalAccountStateAndBatchedRoleProjection()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);
        var adminRole = new IdentityRole("Admin") { Id = "role-admin", NormalizedName = "ADMIN" };
        var poRole = new IdentityRole("Project Officer") { Id = "role-po", NormalizedName = "PROJECT OFFICER" };
        db.Roles.AddRange(adminRole, poRole);

        db.Users.AddRange(
            new ApplicationUser
            {
                Id = "active",
                UserName = "active.user",
                FullName = "Active User",
                Rank = "Lt Col",
                MustChangePassword = false,
                CreatedUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new ApplicationUser
            {
                Id = "pending",
                UserName = "pending.user",
                FullName = "Pending User",
                Rank = "Maj",
                PendingDeletion = true,
                IsDisabled = true,
                MustChangePassword = false,
                CreatedUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        db.UserRoles.AddRange(
            new IdentityUserRole<string> { UserId = "active", RoleId = adminRole.Id },
            new IdentityUserRole<string> { UserId = "active", RoleId = poRole.Id });
        await db.SaveChangesAsync();

        var time = new AdminTimeService(
            new FixedClock(new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero)));
        var service = new AdminUserQueryService(
            db,
            new UserAccountStateResolver(),
            time,
            Options.Create(new UserLifecycleOptions { HardDeleteWindowHours = 24 }));

        var result = await service.GetAsync(new AdminUserListRequest(
            Query: null,
            Role: "Admin",
            Status: "active"));

        var row = Assert.Single(result.Rows);
        Assert.Equal("active", row.Id);
        Assert.Equal(AdminUserAccountState.Active, row.AccountState.State);
        Assert.Equal(new[] { "Admin", "Project Officer" }, row.Roles);
        Assert.Equal(new[] { "Admin", "Project Officer" }, result.Roles);
    }


    [Fact]
    public async Task GetAsync_ReturnsContextualSummaryBeforeStateFilter()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);
        db.Users.AddRange(
            User("active", "Active User"),
            User("password", "Password User", mustChangePassword: true),
            User("locked", "Locked User", lockoutEnd: new DateTimeOffset(2026, 7, 12, 13, 0, 0, TimeSpan.Zero)),
            User("disabled", "Disabled User", disabled: true),
            User("pending", "Pending User", pendingDeletion: true));
        await db.SaveChangesAsync();

        var time = new AdminTimeService(
            new FixedClock(new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero)));
        var service = new AdminUserQueryService(
            db,
            new UserAccountStateResolver(),
            time,
            Options.Create(new UserLifecycleOptions { HardDeleteWindowHours = 24, UndoWindowMinutes = 30 }));

        var result = await service.GetAsync(new AdminUserListRequest(
            Query: null,
            Role: null,
            Status: "disabled"));

        Assert.Single(result.Rows);
        Assert.Equal(5, result.Summary.Total);
        Assert.Equal(1, result.Summary.Active);
        Assert.Equal(1, result.Summary.MustChangePassword);
        Assert.Equal(1, result.Summary.TemporarilyLocked);
        Assert.Equal(1, result.Summary.Disabled);
        Assert.Equal(1, result.Summary.PendingDeletion);
    }

    [Fact]
    public async Task GetDetailsAsync_ReturnsLifecycleAndRoleInformation()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);
        var role = new IdentityRole("Project Officer") { Id = "role-po", NormalizedName = "PROJECT OFFICER" };
        db.Roles.Add(role);
        db.Users.AddRange(
            new ApplicationUser
            {
                Id = "actor",
                UserName = "administrator",
                FullName = "Administrator",
                Rank = "Admin",
                CreatedUtc = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new ApplicationUser
            {
                Id = "target",
                UserName = "target.user",
                FullName = "Target User",
                Rank = "Maj",
                IsDisabled = true,
                DisabledUtc = new DateTime(2026, 7, 12, 10, 0, 0, DateTimeKind.Utc),
                DisabledByUserId = "actor",
                CreatedUtc = new DateTime(2026, 7, 12, 9, 0, 0, DateTimeKind.Utc)
            });
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = "target", RoleId = role.Id });
        await db.SaveChangesAsync();

        var time = new AdminTimeService(
            new FixedClock(new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero)));
        var service = new AdminUserQueryService(
            db,
            new UserAccountStateResolver(),
            time,
            Options.Create(new UserLifecycleOptions { HardDeleteWindowHours = 24, UndoWindowMinutes = 30 }));

        var details = await service.GetDetailsAsync("target");

        Assert.NotNull(details);
        Assert.Equal(AdminUserAccountState.Disabled, details!.AccountState.State);
        Assert.Equal("Administrator", details.DisabledBy);
        Assert.Equal(new[] { "Project Officer" }, details.Roles);
        Assert.True(details.CanRequestHardDelete);
    }

    [Fact]
    public async Task RecentActivityQueries_AreBoundedAndOrdered()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);
        var now = new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);
        db.Users.Add(User("target", "Target User"));
        db.AuthEvents.AddRange(
            new AuthEvent { Id = Guid.NewGuid(), UserId = "target", Event = AuthenticationEventNames.LoginSucceeded, WhenUtc = now.AddMinutes(-2) },
            new AuthEvent { Id = Guid.NewGuid(), UserId = "target", Event = AuthenticationEventNames.LoginSucceeded, WhenUtc = now.AddMinutes(-1) });
        db.AuditLogs.Add(new AuditLog
        {
            TimeUtc = now.AddMinutes(-3).UtcDateTime,
            Level = "Warning",
            Action = AuthenticationEventNames.AuditLoginFailed,
            UserId = "target",
            Message = "Invalid credentials"
        });
        db.AuditLogs.Add(new AuditLog
        {
            TimeUtc = now.AddMinutes(-4).UtcDateTime,
            Level = "Info",
            Action = "AdminUserUpdated",
            UserName = "administrator",
            Message = "target.user",
            DataJson = "{\"EntityId\":\"target\"}"
        });
        db.AuditLogs.Add(new AuditLog
        {
            TimeUtc = now.AddMinutes(-1).UtcDateTime,
            Level = "Info",
            Action = "AdminUserUpdated",
            UserId = "target",
            UserName = "target",
            Message = "another.user",
            DataJson = "{\"EntityId\":\"another\"}"
        });
        await db.SaveChangesAsync();

        var service = new AdminUserQueryService(
            db,
            new UserAccountStateResolver(),
            new AdminTimeService(new FixedClock(now)),
            Options.Create(new UserLifecycleOptions { HardDeleteWindowHours = 24, UndoWindowMinutes = 30 }));

        var authentication = await service.GetRecentLoginActivityAsync("target", limit: 2);
        var administrative = await service.GetRecentAdministrativeActivityAsync("target", limit: 2);

        Assert.Equal(2, authentication.Count);
        Assert.True(authentication[0].WhenUtc >= authentication[1].WhenUtc);
        Assert.Single(administrative);
        Assert.Equal("AdminUserUpdated", administrative[0].Action);
        Assert.Equal("target.user", administrative[0].Message);
    }

    private static ApplicationUser User(
        string id,
        string fullName,
        bool mustChangePassword = false,
        bool disabled = false,
        bool pendingDeletion = false,
        DateTimeOffset? lockoutEnd = null) => new()
    {
        Id = id,
        UserName = id,
        NormalizedUserName = id.ToUpperInvariant(),
        FullName = fullName,
        Rank = "Test",
        MustChangePassword = mustChangePassword,
        IsDisabled = disabled,
        PendingDeletion = pendingDeletion,
        LockoutEnd = lockoutEnd,
        CreatedUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; }
    }
}
