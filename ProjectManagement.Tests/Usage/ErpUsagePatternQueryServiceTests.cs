using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Usage;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Usage;
using ProjectManagement.Tests.Fakes;

namespace ProjectManagement.Tests.Usage;

public sealed class ErpUsagePatternQueryServiceTests
{
    [Fact]
    public async Task GetAsync_ConsolidatesIntervalsAndUsesOperationalPrecedence()
    {
        await using var db = CreateContext();
        db.Users.Add(User("u1", "Daily User"));
        db.UserActivityBuckets.AddRange(
            Bucket("u1", 10, 0, "projects", navigation: true),
            Bucket("u1", 10, 5, "documents", heartbeat: true),
            Bucket("u1", 10, 20, "dashboard", navigation: true));
        db.AuditLogs.Add(new AuditLog
        {
            UserId = "u1",
            UserName = "daily.user",
            Action = "Projects.MetaChangedDirect",
            TimeUtc = UtcFromIst(2026, 7, 14, 10, 7),
            Level = "Info"
        });
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetAsync(new ErpUsagePatternQuery { Days = 7 });

        Assert.Equal(15, result.AggregationMinutes);
        Assert.Equal(1, result.ActiveUsers);
        Assert.Equal(2, result.ActivityIntervals);
        Assert.Equal(1, result.OperationalIntervals);
        Assert.Equal(1, result.OperationalActionCount);

        var operational = Assert.Single(result.Points, point => point.Signal == ErpUsagePatternSignalNames.Operational);
        Assert.Equal(1, operational.OperationalActionCount);
        Assert.Contains("Projects", operational.Modules);
        Assert.Contains("Documents", operational.Modules);

        Assert.Single(result.Points, point => point.Signal == "navigation");
        var user = Assert.Single(result.Users);
        Assert.Equal(2, user.ActivityIntervals);
        Assert.Equal(1, user.ActiveDays);
    }

    [Fact]
    public async Task GetAsync_InteractiveFilterKeepsInteractiveAndOperationalIntervals()
    {
        await using var db = CreateContext();
        db.Users.Add(User("u1", "Daily User"));
        db.UserActivityBuckets.AddRange(
            Bucket("u1", 9, 0, "dashboard", navigation: true),
            Bucket("u1", 9, 20, "projects", heartbeat: true));
        db.AuditLogs.Add(new AuditLog
        {
            UserId = "u1",
            UserName = "daily.user",
            Action = "Projects.MetaChangedDirect",
            TimeUtc = UtcFromIst(2026, 7, 14, 10, 0),
            Level = "Info"
        });
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetAsync(new ErpUsagePatternQuery
        {
            Days = 7,
            Signal = ErpUsagePatternSignalNames.Interactive
        });

        Assert.Equal(2, result.Points.Count);
        Assert.DoesNotContain(result.Points, point => point.Signal == "navigation");
        Assert.Contains(result.Points, point => point.Signal == ErpUsagePatternSignalNames.Interactive);
        Assert.Contains(result.Points, point => point.Signal == ErpUsagePatternSignalNames.Operational);
    }


    [Fact]
    public async Task GetAsync_RecordsUsageOnEachDayWithoutRequiringAFreshLogin()
    {
        await using var db = CreateContext();
        db.Users.Add(User("u1", "Continuous Session User"));
        db.UserActivityBuckets.AddRange(
            BucketOn(2026, 7, 14, "u1", 9, 0, "dashboard", heartbeat: true),
            BucketOn(2026, 7, 15, "u1", 10, 30, "projects", heartbeat: true));
        await db.SaveChangesAsync();

        var result = await CreateService(
            db,
            FakeClock.ForIst(new DateTime(2026, 7, 15, 18, 0, 0))).GetAsync(
                new ErpUsagePatternQuery { Days = 7 });

        var user = Assert.Single(result.Users);
        Assert.Equal(2, user.ActiveDays);
        Assert.Equal(2, result.Points.Count);
    }

    [Fact]
    public async Task GetAsync_ModuleFilterDoesNotAttachUnclassifiedAuditRowsToWrongModule()
    {
        await using var db = CreateContext();
        db.Users.Add(User("u1", "Daily User"));
        db.UserActivityBuckets.Add(Bucket("u1", 11, 0, "documents", heartbeat: true));
        db.AuditLogs.Add(new AuditLog
        {
            UserId = "u1",
            UserName = "daily.user",
            Action = "Projects.MetaChangedDirect",
            TimeUtc = UtcFromIst(2026, 7, 14, 12, 0),
            Level = "Info"
        });
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetAsync(new ErpUsagePatternQuery
        {
            Days = 7,
            Module = "documents"
        });

        var point = Assert.Single(result.Points);
        Assert.Equal(ErpUsagePatternSignalNames.Interactive, point.Signal);
        Assert.Equal(0, point.OperationalActionCount);
        Assert.Contains("Documents", point.Modules);
    }

    private static ErpUsagePatternQueryService CreateService(ApplicationDbContext db) =>
        CreateService(db, FakeClock.ForIst(new DateTime(2026, 7, 14, 18, 0, 0)));

    private static ErpUsagePatternQueryService CreateService(
        ApplicationDbContext db,
        FakeClock clock)
    {
        return new ErpUsagePatternQueryService(
            db,
            new AdminTimeService(clock),
            new ErpUsageModuleCatalog(),
            Options.Create(new ErpUsageOptions
            {
                TrackingInceptionUtc = UtcFromIst(2026, 7, 14, 0, 0)
            }));
    }

    private static ApplicationUser User(string id, string fullName) => new()
    {
        Id = id,
        UserName = "daily.user",
        FullName = fullName,
        Rank = "Lt Col",
        CreatedUtc = UtcFromIst(2026, 7, 1, 9, 0),
        AccountKind = UserAccountKind.Human
    };

    private static UserActivityBucket Bucket(
        string userId,
        int hour,
        int minute,
        string module,
        bool navigation = false,
        bool heartbeat = false) =>
        BucketOn(2026, 7, 14, userId, hour, minute, module, navigation, heartbeat);

    private static UserActivityBucket BucketOn(
        int year,
        int month,
        int day,
        string userId,
        int hour,
        int minute,
        string module,
        bool navigation = false,
        bool heartbeat = false)
    {
        var start = UtcFromIst(year, month, day, hour, minute);
        return new UserActivityBucket
        {
            UserId = userId,
            ActivityDateIst = new DateOnly(year, month, day),
            BucketStartUtc = start,
            ModuleKey = module,
            HadNavigation = navigation,
            HadInteractiveHeartbeat = heartbeat,
            FirstSeenUtc = start,
            LastSeenUtc = start.AddMinutes(4),
            NavigationCount = navigation ? 1 : 0,
            HeartbeatCount = heartbeat ? 1 : 0
        };
    }

    private static DateTime UtcFromIst(
        int year,
        int month,
        int day,
        int hour,
        int minute)
        => FakeClock.ForIst(new DateTime(year, month, day, hour, minute, 0)).UtcNow.UtcDateTime;

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }
}
