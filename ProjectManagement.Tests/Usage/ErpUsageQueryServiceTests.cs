using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Models.Usage;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Scheduling;
using ProjectManagement.Services.Usage;
using ProjectManagement.Tests.Fakes;

namespace ProjectManagement.Tests.Usage;

public sealed class ErpUsageQueryServiceTests
{
    [Fact]
    public async Task Usage_IsMeasuredAcrossDaysWithoutAdditionalLogins_AndUsesOfficeCalendar()
    {
        await using var db = CreateContext();
        db.Users.Add(new ApplicationUser
        {
            Id = "u1",
            UserName = "daily.user",
            FullName = "Daily User",
            Rank = "Lt Col",
            CreatedUtc = UtcFromIst(2026, 7, 1, 9)
        });
        db.Holidays.AddRange(
            new Holiday
            {
                Date = new DateOnly(2026, 7, 9),
                Name = "Informational RH",
                Type = HolidayType.Restricted,
                IsObservedAsOfficeHoliday = false
            },
            new Holiday
            {
                Date = new DateOnly(2026, 7, 10),
                Name = "Observed RH",
                Type = HolidayType.Restricted,
                IsObservedAsOfficeHoliday = true
            });
        db.UserActivityBuckets.AddRange(
            Bucket("u1", new DateOnly(2026, 7, 9), 4, "projects", navigation: true),
            Bucket("u1", new DateOnly(2026, 7, 14), 5, "documents", heartbeat: true));
        db.AuditLogs.AddRange(
            new AuditLog
            {
                UserId = "u1",
                UserName = "daily.user",
                Action = "Projects.MetaChangedDirect",
                TimeUtc = UtcFromIst(2026, 7, 13, 11),
                Level = "Info"
            },
            new AuditLog
            {
                UserId = "u1",
                UserName = "daily.user",
                Action = "LoginSuccess",
                TimeUtc = UtcFromIst(2026, 7, 11, 9),
                Level = "Info"
            });
        await db.SaveChangesAsync();

        var service = CreateService(db, new DateTime(2026, 7, 14, 12, 0, 0));
        var result = await service.GetAsync(new ErpUsageQuery { Days = 7 });

        var row = Assert.Single(result.Users);
        Assert.True(row.UsedToday);
        Assert.Equal(5, row.AvailableWorkingDays); // Sunday and the office-observed RH are excluded.
        Assert.Equal(3, row.ActiveWorkingDays);    // 9 Jul navigation, 13 Jul action, 14 Jul heartbeat.
        Assert.Equal(60, row.ActivePercentage);
        Assert.Equal(10, row.ApproximateActiveMinutes);
        Assert.Equal(1, row.RecordedActionCount); // Authentication activity is not a business action.
        Assert.Equal("Occasional user", row.Posture);
        Assert.Equal(
            ErpUsageHeatmapState.BusinessAction,
            row.Heatmap.Single(cell => cell.Date == new DateOnly(2026, 7, 13)).State);
        Assert.Equal(
            ErpUsageHeatmapState.NonWorkingDay,
            row.Heatmap.Single(cell => cell.Date == new DateOnly(2026, 7, 10)).State);
    }

    [Fact]
    public async Task CommandSummary_FlagsOnlyUsersExposedToSevenWorkingDays()
    {
        await using var db = CreateContext();
        db.Users.AddRange(
            new ApplicationUser
            {
                Id = "old",
                UserName = "old.user",
                FullName = "Old User",
                Rank = "Col",
                CreatedUtc = UtcFromIst(2026, 6, 1, 9)
            },
            new ApplicationUser
            {
                Id = "new",
                UserName = "new.user",
                FullName = "New User",
                Rank = "Maj",
                CreatedUtc = UtcFromIst(2026, 7, 13, 9)
            });
        await db.SaveChangesAsync();

        var service = CreateService(db, new DateTime(2026, 7, 14, 12, 0, 0));
        var summary = await service.GetCommandSummaryAsync();

        Assert.Equal(2, summary.TotalUsers);
        Assert.Equal(1, summary.NoUsageSevenWorkingDays);
    }

    private static ErpUsageQueryService CreateService(ApplicationDbContext db, DateTime nowIst)
    {
        var clock = FakeClock.ForIst(nowIst);
        return new ErpUsageQueryService(
            db,
            new OfficeCalendarService(db),
            new ErpUsageModuleCatalog(),
            new AdminTimeService(clock),
            Options.Create(new ErpUsageOptions()));
    }

    private static UserActivityBucket Bucket(
        string userId,
        DateOnly date,
        int utcHour,
        string module,
        bool navigation = false,
        bool heartbeat = false)
    {
        var start = new DateTime(date.Year, date.Month, date.Day, utcHour, 0, 0, DateTimeKind.Utc);
        return new UserActivityBucket
        {
            UserId = userId,
            ActivityDateIst = date,
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

    private static DateTime UtcFromIst(int year, int month, int day, int hour)
        => FakeClock.ForIst(new DateTime(year, month, day, hour, 0, 0)).UtcNow.UtcDateTime;

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }
}
