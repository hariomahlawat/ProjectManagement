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

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; }
    }
}
