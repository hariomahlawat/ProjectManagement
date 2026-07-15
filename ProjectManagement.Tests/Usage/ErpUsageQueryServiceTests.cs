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
    public async Task ColdStart_UsesTrackingInceptionAndDoesNotInferEarlierNonUse()
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
        db.UserActivityBuckets.Add(Bucket(
            "u1",
            new DateOnly(2026, 7, 14),
            14,
            "documents",
            heartbeat: true));
        db.AuditLogs.Add(new AuditLog
        {
            UserId = "u1",
            UserName = "daily.user",
            Action = "Projects.MetaChangedDirect",
            TimeUtc = UtcFromIst(2026, 7, 13, 11),
            Level = "Info"
        });
        await db.SaveChangesAsync();

        var service = CreateService(
            db,
            new DateTime(2026, 7, 14, 18, 0, 0),
            new ErpUsageOptions());
        var result = await service.GetAsync(new ErpUsageQuery { Days = 7 });

        var row = Assert.Single(result.Users);
        Assert.Equal(new DateOnly(2026, 7, 14), row.EffectiveTrackingStart);
        Assert.True(row.UsedToday);
        Assert.Equal(1, row.AvailableWorkingDays);
        Assert.Equal(1, row.ActiveWorkingDays);
        Assert.Equal(100, row.ActivePercentage);
        Assert.Equal(5, row.ApproximateActiveMinutes);
        Assert.Equal(1, row.OperationalActionCount);
        Assert.Equal(0, row.AdministrativeActionCount);
        Assert.Equal("Occasional user", row.Posture);
        Assert.False(result.RegularClassificationAvailable);
        Assert.False(result.SevenDayReviewAvailable);
        Assert.False(result.ThirtyDayReviewAvailable);
        Assert.Equal(0, result.Summary.NoUsageSevenWorkingDays);
        Assert.Equal(
            ErpUsageHeatmapState.BusinessAction,
            row.Heatmap.Single(cell => cell.Date == new DateOnly(2026, 7, 13)).State);
        Assert.Contains(
            "Historical audited operational action",
            row.Heatmap.Single(cell => cell.Date == new DateOnly(2026, 7, 13)).Label);
    }

    [Fact]
    public async Task MatureTracking_UsesOfficeCalendarAndSeparatesActionTypes()
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
            Bucket("u1", new DateOnly(2026, 7, 9), 10, "projects", navigation: true),
            Bucket("u1", new DateOnly(2026, 7, 14), 10, "documents", heartbeat: true));
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
                Action = "AdminUserUpdated",
                TimeUtc = UtcFromIst(2026, 7, 14, 12),
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

        var options = new ErpUsageOptions
        {
            TrackingInceptionUtc = UtcOffsetFromIst(2026, 7, 1, 0)
        };
        var service = CreateService(db, new DateTime(2026, 7, 14, 18, 0, 0), options);
        var result = await service.GetAsync(new ErpUsageQuery { Days = 7 });

        var row = Assert.Single(result.Users);
        Assert.Equal(5, row.AvailableWorkingDays); // Sunday and office-observed RH are excluded.
        Assert.Equal(3, row.ActiveWorkingDays);    // Informational RH remains a working day.
        Assert.Equal(60, row.ActivePercentage);
        Assert.Equal(10, row.ApproximateActiveMinutes);
        Assert.Equal(1, row.OperationalActionCount);
        Assert.Equal(1, row.AdministrativeActionCount);
        Assert.True(result.RegularClassificationAvailable);
        Assert.True(result.SevenDayReviewAvailable);
        Assert.Equal(1, result.Summary.BusinessContributors);
        Assert.Equal(
            ErpUsageHeatmapState.NonWorkingDay,
            row.Heatmap.Single(cell => cell.Date == new DateOnly(2026, 7, 10)).State);
    }

    [Fact]
    public async Task DefaultScope_ExcludesDisabledNonHumanAndPendingDeletionAccounts()
    {
        await using var db = CreateContext();
        db.Users.AddRange(
            User("human", UserAccountKind.Human),
            User("disabled", UserAccountKind.Human, disabled: true),
            User("service", UserAccountKind.Service),
            User("test", UserAccountKind.Test),
            User("pending", UserAccountKind.Human, pendingDeletion: true));
        await db.SaveChangesAsync();

        var options = new ErpUsageOptions
        {
            TrackingInceptionUtc = UtcOffsetFromIst(2026, 7, 1, 0)
        };
        var service = CreateService(db, new DateTime(2026, 7, 14, 18, 0, 0), options);

        var defaultResult = await service.GetAsync(new ErpUsageQuery { Days = 7 });
        Assert.Equal("human", Assert.Single(defaultResult.Users).UserId);

        var expandedResult = await service.GetAsync(new ErpUsageQuery
        {
            Days = 7,
            IncludeDisabledAccounts = true,
            IncludeNonHumanAccounts = true
        });
        Assert.Equal(4, expandedResult.TotalUsers);
        Assert.DoesNotContain(expandedResult.Users, row => row.UserId == "pending");
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

        var options = new ErpUsageOptions
        {
            TrackingInceptionUtc = UtcOffsetFromIst(2026, 6, 1, 0)
        };
        var service = CreateService(db, new DateTime(2026, 7, 14, 18, 0, 0), options);
        var summary = await service.GetCommandSummaryAsync();

        Assert.Equal(2, summary.TotalUsers);
        Assert.True(summary.RegularClassificationAvailable);
        Assert.True(summary.SevenDayReviewAvailable);
        Assert.Equal(1, summary.NoUsageSevenWorkingDays);
    }

    [Fact]
    public async Task ActivityYear_ReturnsRollingYearInFiftyThreeWeekGridAndRecentThirtyDays()
    {
        await using var db = CreateContext();
        db.Users.Add(new ApplicationUser
        {
            Id = "u1",
            UserName = "project.officer",
            FullName = "Project Officer",
            Rank = "Lt Col",
            CreatedUtc = UtcFromIst(2025, 1, 1, 9)
        });
        db.UserActivityBuckets.Add(Bucket(
            "u1",
            new DateOnly(2026, 7, 15),
            10,
            "projects",
            heartbeat: true));
        await db.SaveChangesAsync();

        var options = new ErpUsageOptions
        {
            TrackingInceptionUtc = UtcOffsetFromIst(2025, 1, 1, 0),
            RetentionDays = 400,
            MaximumLookbackDays = 365
        };
        var service = CreateService(db, new DateTime(2026, 7, 15, 18, 0, 0), options);

        var result = await service.GetActivityYearAsync("u1");

        Assert.Equal(new DateOnly(2025, 7, 16), result.Year.StartDate);
        Assert.Equal(new DateOnly(2026, 7, 15), result.Year.EndDate);
        Assert.Equal(365, result.Year.Days.Count);
        Assert.Equal(30, result.Recent.Days.Count);
        Assert.Equal(53, result.Weeks.Count);
        Assert.All(result.Weeks, week => Assert.Equal(7, week.Days.Count));
        Assert.Equal("Interactive use", result.Year.LastActivityTypeLabel);
        Assert.Equal(1, result.Year.ActiveWorkingDays);
    }

    [Fact]
    public async Task ActivityYear_MarksDatesBeforeComprehensiveMonitoringAsNotMonitored()
    {
        await using var db = CreateContext();
        db.Users.Add(new ApplicationUser
        {
            Id = "u1",
            UserName = "project.officer",
            FullName = "Project Officer",
            Rank = "Lt Col",
            CreatedUtc = UtcFromIst(2026, 1, 1, 9)
        });
        await db.SaveChangesAsync();

        var options = new ErpUsageOptions
        {
            TrackingInceptionUtc = UtcOffsetFromIst(2026, 7, 14, 0),
            RetentionDays = 400,
            MaximumLookbackDays = 365
        };
        var service = CreateService(db, new DateTime(2026, 7, 15, 18, 0, 0), options);

        var result = await service.GetActivityYearAsync("u1");

        var beforeMonitoring = Assert.Single(result.Year.Days.Where(day => day.Date == new DateOnly(2026, 7, 13)));
        Assert.False(beforeMonitoring.IsMonitored);
        Assert.Equal("Not monitored", beforeMonitoring.StateLabel);

        var monitoredDay = Assert.Single(result.Year.Days.Where(day => day.Date == new DateOnly(2026, 7, 14)));
        Assert.True(monitoredDay.IsMonitored);
    }


    [Fact]
    public async Task ActivityYear_CalendarYearUsesPermanentDailySummary()
    {
        await using var db = CreateContext();
        db.Users.Add(new ApplicationUser
        {
            Id = "u1",
            UserName = "project.officer",
            FullName = "Project Officer",
            Rank = "Lt Col",
            CreatedUtc = UtcFromIst(2024, 1, 1, 9)
        });
        db.UserActivityDailySummaries.Add(new UserActivityDailySummary
        {
            UserId = "u1",
            ActivityDateIst = new DateOnly(2025, 4, 15),
            HadInteractiveHeartbeat = true,
            FirstSeenUtc = UtcFromIst(2025, 4, 15, 10),
            LastSeenUtc = UtcFromIst(2025, 4, 15, 11),
            HeartbeatCount = 4
        });
        await db.SaveChangesAsync();

        var options = new ErpUsageOptions
        {
            TrackingInceptionUtc = UtcOffsetFromIst(2024, 1, 1, 0),
            RetentionDays = 400,
            MaximumLookbackDays = 365
        };
        var service = CreateService(db, new DateTime(2026, 7, 15, 18, 0, 0), options);

        var result = await service.GetActivityYearAsync("u1", period: "2025");

        Assert.Equal(new DateOnly(2025, 1, 1), result.Year.StartDate);
        Assert.Equal(new DateOnly(2025, 12, 31), result.Year.EndDate);
        Assert.Equal(365, result.Year.Days.Count);
        Assert.Equal(2025, result.SelectedCalendarYear);
        Assert.Equal("2025", result.SelectedPeriodKey);
        var activeDay = Assert.Single(result.Year.Days.Where(day => day.Date == new DateOnly(2025, 4, 15)));
        Assert.True(activeDay.HasActivity);
        Assert.Equal("Interactive use", activeDay.StateLabel);
    }

    [Fact]
    public async Task ActivityYear_DoesNotPromotePreInceptionAuditOnMonitoringStartDate()
    {
        await using var db = CreateContext();
        db.Users.Add(new ApplicationUser
        {
            Id = "u1",
            UserName = "project.officer",
            FullName = "Project Officer",
            Rank = "Lt Col",
            CreatedUtc = UtcFromIst(2026, 1, 1, 9)
        });
        db.AuditLogs.Add(new AuditLog
        {
            UserId = "u1",
            UserName = "project.officer",
            Action = "Projects.MetaChangedDirect",
            TimeUtc = UtcFromIst(2026, 7, 14, 10),
            Level = "Info"
        });
        db.UserActivityBuckets.Add(Bucket(
            "u1",
            new DateOnly(2026, 7, 14),
            14,
            "projects",
            navigation: true));
        await db.SaveChangesAsync();

        var options = new ErpUsageOptions
        {
            TrackingInceptionUtc = UtcOffsetFromIst(2026, 7, 14, 13)
        };
        var service = CreateService(db, new DateTime(2026, 7, 15, 18, 0, 0), options);

        var result = await service.GetActivityYearAsync("u1", period: "2026");

        var startDate = Assert.Single(result.Year.Days.Where(day => day.Date == new DateOnly(2026, 7, 14)));
        Assert.Equal(1, startDate.Level);
        Assert.Equal("Navigation or read-only use", startDate.StateLabel);
    }

    [Fact]
    public async Task ActivityYear_ExposesFiveRecentYearsAndKeepsOlderYearsSelectable()
    {
        await using var db = CreateContext();
        db.Users.Add(new ApplicationUser
        {
            Id = "u1",
            UserName = "project.officer",
            FullName = "Project Officer",
            Rank = "Lt Col",
            CreatedUtc = UtcFromIst(2020, 1, 1, 9)
        });
        await db.SaveChangesAsync();

        var options = new ErpUsageOptions
        {
            TrackingInceptionUtc = UtcOffsetFromIst(2020, 1, 1, 0)
        };
        var service = CreateService(db, new DateTime(2026, 7, 15, 18, 0, 0), options);

        var result = await service.GetActivityYearAsync("u1", period: "2020");

        Assert.Equal(
            new[] { "rolling", "2026", "2025", "2024", "2023", "2022" },
            result.PrimaryPeriodOptions.Select(option => option.Key));
        Assert.Equal(new[] { "2021", "2020" }, result.OlderPeriodOptions.Select(option => option.Key));
        Assert.True(result.OlderPeriodOptions.Single(option => option.Key == "2020").IsSelected);
    }

    [Fact]
    public async Task ActivityYear_TreatsSundayAsNonWorkingEvenWhenMisconfigured()
    {
        await using var db = CreateContext();
        db.Users.Add(new ApplicationUser
        {
            Id = "u1",
            UserName = "project.officer",
            FullName = "Project Officer",
            Rank = "Lt Col",
            CreatedUtc = UtcFromIst(2026, 1, 1, 9)
        });
        await db.SaveChangesAsync();

        var options = new ErpUsageOptions
        {
            TrackingInceptionUtc = UtcOffsetFromIst(2026, 1, 1, 0),
            WorkingDays =
            [
                DayOfWeek.Monday,
                DayOfWeek.Tuesday,
                DayOfWeek.Wednesday,
                DayOfWeek.Thursday,
                DayOfWeek.Friday,
                DayOfWeek.Saturday,
                DayOfWeek.Sunday
            ]
        };
        var service = CreateService(db, new DateTime(2026, 7, 15, 18, 0, 0), options);

        var result = await service.GetActivityYearAsync("u1", period: "2026");

        var sunday = Assert.Single(result.Year.Days.Where(day => day.Date == new DateOnly(2026, 7, 12)));
        Assert.False(sunday.IsWorkingDay);
        Assert.Equal("Non-working day", sunday.StateLabel);
    }

    private static ErpUsageQueryService CreateService(
        ApplicationDbContext db,
        DateTime nowIst,
        ErpUsageOptions options)
    {
        var clock = FakeClock.ForIst(nowIst);
        return new ErpUsageQueryService(
            db,
            new OfficeCalendarService(db),
            new ErpUsageModuleCatalog(),
            new AdminTimeService(clock),
            Options.Create(options));
    }

    private static ApplicationUser User(
        string id,
        UserAccountKind kind,
        bool disabled = false,
        bool pendingDeletion = false) => new()
    {
        Id = id,
        UserName = $"{id}.user",
        FullName = id,
        Rank = "Maj",
        CreatedUtc = UtcFromIst(2026, 7, 1, 9),
        AccountKind = kind,
        IsDisabled = disabled,
        PendingDeletion = pendingDeletion
    };

    private static UserActivityBucket Bucket(
        string userId,
        DateOnly date,
        int istHour,
        string module,
        bool navigation = false,
        bool heartbeat = false)
    {
        var start = UtcFromIst(date.Year, date.Month, date.Day, istHour);
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

    private static DateTimeOffset UtcOffsetFromIst(int year, int month, int day, int hour)
        => FakeClock.ForIst(new DateTime(year, month, day, hour, 0, 0)).UtcNow;

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }
}
