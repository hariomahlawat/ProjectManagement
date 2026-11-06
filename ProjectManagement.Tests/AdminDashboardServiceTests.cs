using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class AdminDashboardServiceTests
{
    [Fact]
    public async Task GetAsync_ClassifiesUserStatesAndListsOnlyWhatNeedsAnAdministrator()
    {
        var now = new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);
        db.Users.AddRange(
            User("active", mustChangePassword: false),
            User("password", mustChangePassword: true),
            User("disabled", mustChangePassword: false, disabled: true),
            User("locked", mustChangePassword: false, lockoutEnd: now.AddHours(1)),
            User("pending", mustChangePassword: false, pendingDeletion: true));
        db.AuditLogs.Add(new AuditLog
        {
            TimeUtc = now.AddMinutes(-20).UtcDateTime,
            Level = "Error",
            Action = "Admin.TestFailure",
            UserName = "administrator",
            Message = "A controlled test error was recorded."
        });
        await db.SaveChangesAsync();

        var service = new AdminDashboardService(
            db,
            new AdminTimeService(FakeClock.AtUtc(now)));

        var result = await service.GetAsync();

        Assert.Equal(5, result.Metrics.TotalUsers);
        Assert.Equal(1, result.Metrics.ActiveUsers);
        Assert.Equal(1, result.Metrics.MustChangePasswordUsers);
        Assert.Equal(1, result.Metrics.DisabledUsers);
        Assert.Equal(1, result.Metrics.LockedUsers);
        Assert.Equal(1, result.Metrics.PendingDeletionUsers);
        // Only what an administrator must act on: the locked and pending accounts and the error.
        // The pending password change and the disabled account are normal and are not listed.
        Assert.Equal(
            new[]
            {
                "1 account is locked after failed sign-ins",
                "1 account is waiting to be deleted",
                "1 error in the audit log in the last 24 hours"
            },
            result.AttentionItems.Select(item => item.Title).ToArray());
        Assert.All(result.AttentionItems, item => Assert.Equal(AdminAttentionSeverity.Warning, item.Severity));
        Assert.Equal("administrator", result.RecentActions[0].Actor);
    }

    private static ApplicationUser User(
        string userName,
        bool mustChangePassword,
        bool disabled = false,
        bool pendingDeletion = false,
        DateTimeOffset? lockoutEnd = null) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        UserName = userName,
        NormalizedUserName = userName.ToUpperInvariant(),
        FullName = userName,
        Rank = "Test",
        MustChangePassword = mustChangePassword,
        IsDisabled = disabled,
        PendingDeletion = pendingDeletion,
        LockoutEnd = lockoutEnd,
        CreatedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };
}
