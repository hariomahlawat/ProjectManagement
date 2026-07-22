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
    public async Task GetAsync_ClassifiesUserStatesAndPrioritisesCriticalAttention()
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
        Assert.Equal(AdminAttentionSeverity.Critical, result.AttentionItems[0].Severity);
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
