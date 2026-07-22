using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Usage;

namespace ProjectManagement.Tests.Usage;

public sealed class UserActivityRecorderTests
{
    [Fact]
    public async Task SignalsWithinSameBucketAndModule_AreConsolidated()
    {
        await using var db = CreateContext();
        db.Users.Add(new ApplicationUser { Id = "u1", UserName = "user1", FullName = "User One", Rank = "Lt Col" });
        await db.SaveChangesAsync();
        var recorder = new UserActivityRecorder(
            db,
            new ErpUsageModuleCatalog(),
            Options.Create(new ErpUsageOptions { BucketMinutes = 5 }));
        var at = new DateTimeOffset(2026, 7, 14, 6, 1, 0, TimeSpan.Zero);

        await recorder.RecordAsync("u1", "projects", UserActivitySignal.Navigation, at);
        await recorder.RecordAsync("u1", "projects", UserActivitySignal.InteractiveHeartbeat, at.AddMinutes(2));
        await recorder.RecordAsync("u1", "projects", UserActivitySignal.Navigation, at.AddMinutes(3));

        var bucket = Assert.Single(await db.UserActivityBuckets.AsNoTracking().ToListAsync());
        Assert.True(bucket.HadNavigation);
        Assert.True(bucket.HadInteractiveHeartbeat);
        Assert.Equal(2, bucket.NavigationCount);
        Assert.Equal(1, bucket.HeartbeatCount);
        Assert.Equal(new DateTime(2026, 7, 14, 6, 0, 0, DateTimeKind.Utc), bucket.BucketStartUtc);

        var daily = Assert.Single(await db.UserActivityDailySummaries.AsNoTracking().ToListAsync());
        Assert.Equal(bucket.ActivityDateIst, daily.ActivityDateIst);
        Assert.True(daily.HadNavigation);
        Assert.True(daily.HadInteractiveHeartbeat);
        Assert.Equal(2, daily.NavigationCount);
        Assert.Equal(1, daily.HeartbeatCount);
    }

    [Fact]
    public async Task UnknownModule_DoesNotCreateActivity()
    {
        await using var db = CreateContext();
        var recorder = new UserActivityRecorder(db, new ErpUsageModuleCatalog(), Options.Create(new ErpUsageOptions()));
        await recorder.RecordAsync("u1", "not-a-real-module", UserActivitySignal.Navigation);
        Assert.Empty(db.UserActivityBuckets);
        Assert.Empty(db.UserActivityDailySummaries);
    }


    [Fact]
    public async Task AuditedOperationalAction_IsAddedToPermanentDailySummary()
    {
        await using var db = CreateContext();
        db.Users.Add(new ApplicationUser { Id = "u1", UserName = "user1", FullName = "User One", Rank = "Lt Col" });
        await db.SaveChangesAsync();
        var at = new DateTimeOffset(2026, 7, 14, 6, 1, 0, TimeSpan.Zero);
        var audit = new AuditService(
            db,
            new HttpContextAccessor(),
            new FixedClock(at));

        await audit.LogAsync("Projects.MetaChangedDirect", userId: "u1", userName: "user1");

        var summary = Assert.Single(await db.UserActivityDailySummaries.AsNoTracking().ToListAsync());
        Assert.True(summary.HadOperationalAction);
        Assert.False(summary.HadAdministrativeAction);
        Assert.Equal(1, summary.OperationalActionCount);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; }
    }
}
