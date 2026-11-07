using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Admin;
using ProjectManagement.Tests.Fakes;
using Xunit;
using LoginsModel = ProjectManagement.Areas.Admin.Pages.Analytics.LoginsModel;

namespace ProjectManagement.Tests.Admin;

/// <summary>
/// Third review, C3 and C7. Sign-in activity used to keep the newest N events and filter
/// afterwards, so an older failed sign-in could be counted on its tile and missing from the
/// list. Home counted a rolling UTC week and showed user names.
/// </summary>
public sealed class AdminSignInCountsWhatItShowsTests
{
    // 7 Nov 2026, 12:00 IST.
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 6, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task FailedSignInsOlderThanTheCap_AreListedAndCounted()
    {
        await using var db = NewDb();
        SeedNewerDaytimeSuccesses(db, count: 10);
        db.AuditLogs.AddRange(
            Failed("first", Now.AddDays(-5)),
            Failed("second", Now.AddDays(-6)));
        await db.SaveChangesAsync();

        var service = Service(db, cap: 5);

        var failed = await service.GetAsync(new AdminLoginMonitoringRequest(Days: 30, Outcome: "failed"));
        Assert.Equal(2, failed.Failed);
        Assert.Equal(2, failed.PatternPoints.Count);
        Assert.All(failed.PatternPoints, row => Assert.Equal(AdminLoginOutcome.Failed, row.Outcome));
        Assert.False(failed.IsTruncated);

        // The review queue is found by the review rule in the query, not inside the newest five.
        var all = await service.GetAsync(new AdminLoginMonitoringRequest(Days: 30));
        Assert.Equal(2, all.ReviewTotal);
        Assert.Equal(2, all.ReviewSignals);
        Assert.Equal(new[] { "first", "second" }, all.ReviewRows.Select(row => row.LoginName));
    }

    [Fact]
    public async Task OnlySignInsToLookAt_FindsALateNightSignInOlderThanTheCap()
    {
        await using var db = NewDb();
        db.Users.Add(new ApplicationUser { Id = "u-late", UserName = "late", NormalizedUserName = "LATE", FullName = "Late Worker" });
        SeedNewerDaytimeSuccesses(db, count: 10);
        // 3 Nov 2026, 23:30 IST.
        db.AuthEvents.Add(Success("u-late", new DateTimeOffset(2026, 11, 3, 18, 0, 0, TimeSpan.Zero)));
        await db.SaveChangesAsync();

        var service = Service(db, cap: 5);
        var reviewOnly = await service.GetAsync(new AdminLoginMonitoringRequest(Days: 30, ReviewOnly: true));

        var row = Assert.Single(reviewOnly.PatternPoints);
        Assert.Equal("Late Worker", row.DisplayName);
        Assert.True(row.RequiresReview);
        Assert.Equal(1, reviewOnly.ReviewTotal);
    }

    [Fact]
    public async Task Export_HasTheSameRowsAsTheList()
    {
        await using var db = NewDb();
        SeedNewerDaytimeSuccesses(db, count: 10);
        db.AuditLogs.AddRange(
            Failed("first", Now.AddDays(-5)),
            Failed("second", Now.AddDays(-6)),
            Failed("third", Now.AddDays(-7)));
        await db.SaveChangesAsync();

        var service = Service(db, cap: 5);
        var page = new LoginsModel(
            service,
            new AdminClientDescriptorService(),
            Time(),
            new SafeCsvWriter(),
            Options.Create(new AdminLoginMonitoringOptions()))
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
            Outcome = "failed"
        };

        // What the page lists for the same filters.
        var snapshot = await service.GetAsync(new AdminLoginMonitoringRequest(Days: 30, Outcome: "failed"));
        var listed = snapshot.PatternPoints.Count;
        var exports = new RecordingExportAudit();
        var file = (FileContentResult)await page.OnGetExportCsvAsync(exports, CancellationToken.None);

        var lines = new UTF8Encoding(false).GetString(file.FileContents).TrimStart('﻿')
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, snapshot.Failed);
        Assert.Equal(3, listed);
        Assert.Equal(listed, lines.Length - 1); // one header row
        Assert.Equal(listed, Assert.Single(exports.Records).RowCount);
        Assert.False(exports.Records[0].Truncated);
    }

    [Fact]
    public void LateNightRule_TranslatesToPostgreSql()
    {
        // Built with the real provider; ToQueryString does not open the connection.
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=prism_metadata_only;Username=unused;Password=unused")
            .Options);

        var sql = db.AuthEvents
            .Where(AdminLoginMonitoringService.LateNightSuccessfulSignIn)
            .OrderByDescending(authEvent => authEvent.WhenUtc)
            .Take(10)
            .ToQueryString();

        Assert.Contains("'hour'", sql);
    }

    [Fact]
    public async Task HomeSevenDays_AreTodayAndTheSixIstDaysBefore()
    {
        // 8 Nov 2026, 00:30 IST: the IST week starts 2 Nov 00:00 IST (1 Nov 18:30 UTC); a
        // rolling UTC week would have started on 31 Oct 19:00 UTC.
        var now = new DateTimeOffset(2026, 11, 7, 19, 0, 0, TimeSpan.Zero);
        await using var db = NewDb();
        db.AuthEvents.AddRange(
            Success("u-1", new DateTimeOffset(2026, 11, 1, 18, 29, 0, TimeSpan.Zero)), // 1 Nov 23:59 IST: out
            Success("u-2", new DateTimeOffset(2026, 11, 1, 18, 31, 0, TimeSpan.Zero))); // 2 Nov 00:01 IST: in
        db.AuditLogs.AddRange(
            Failed("early", new DateTimeOffset(2026, 11, 1, 18, 0, 0, TimeSpan.Zero)),
            Failed("inside", new DateTimeOffset(2026, 11, 1, 19, 0, 0, TimeSpan.Zero)));
        await db.SaveChangesAsync();

        var time = new AdminTimeService(FakeClock.AtUtc(now));
        var home = await new AdminDashboardService(db, time).GetAsync();
        var signIns = await new AdminLoginMonitoringService(db, time, Options.Create(new AdminLoginMonitoringOptions()))
            .GetAsync(new AdminLoginMonitoringRequest(Days: 7));

        Assert.Equal(1, home.Metrics.LoginsLast7Days);
        Assert.Equal(1, home.Metrics.UniqueUsersLast7Days);
        Assert.Equal(1, home.Metrics.FailedLoginsLast7Days);
        Assert.Equal(signIns.Successful, home.Metrics.LoginsLast7Days);
        Assert.Equal(signIns.Failed, home.Metrics.FailedLoginsLast7Days);
    }

    [Fact]
    public async Task HomeRecentChanges_ShowPeopleByDisplayName()
    {
        await using var db = NewDb();
        db.Users.Add(new ApplicationUser { Id = "u-asha", UserName = "asha", NormalizedUserName = "ASHA", FullName = "Maj Asha Rao" });
        db.AuditLogs.AddRange(
            new AuditLog { TimeUtc = Now.AddMinutes(-1).UtcDateTime, Level = "Info", Action = "Admin.UserUpdated", UserId = "u-asha", UserName = "asha" },
            new AuditLog { TimeUtc = Now.AddMinutes(-2).UtcDateTime, Level = "Info", Action = "Admin.RoleChanged", UserName = "ASHA" },
            new AuditLog { TimeUtc = Now.AddMinutes(-3).UtcDateTime, Level = "Info", Action = "Admin.Cleanup", UserName = "System" });
        await db.SaveChangesAsync();

        var home = await new AdminDashboardService(db, Time()).GetAsync();

        Assert.Equal(
            new[] { "Maj Asha Rao", "Maj Asha Rao", AuditActorNames.SystemActor },
            home.RecentActions.Select(action => action.Actor).ToArray());
    }

    private static ApplicationDbContext NewDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
        .Options);

    private static AdminTimeService Time() => new(FakeClock.AtUtc(Now));

    private static AdminLoginMonitoringService Service(ApplicationDbContext db, int cap) => new(
        db,
        Time(),
        Options.Create(new AdminLoginMonitoringOptions { MaximumChartPoints = cap }));

    // Newer than any failure, at 11:30 IST: in working hours, so none needs a look.
    private static void SeedNewerDaytimeSuccesses(ApplicationDbContext db, int count)
    {
        db.Users.Add(new ApplicationUser { Id = "u-day", UserName = "day", NormalizedUserName = "DAY", FullName = "Day Worker" });
        for (var i = 0; i < count; i++)
        {
            db.AuthEvents.Add(Success("u-day", new DateTimeOffset(2026, 11, 6, 6, 0, 0, TimeSpan.Zero).AddMinutes(-i)));
        }
    }

    private static AuthEvent Success(string userId, DateTimeOffset whenUtc) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Event = AuthenticationEventNames.LoginSucceeded,
        WhenUtc = whenUtc,
        Ip = "10.0.0.5"
    };

    private static AuditLog Failed(string typedName, DateTimeOffset whenUtc) => new()
    {
        TimeUtc = whenUtc.UtcDateTime,
        Level = "Warning",
        Action = AuthenticationEventNames.AuditLoginFailed,
        UserName = typedName,
        Ip = "10.0.0.9"
    };

    private sealed class RecordingExportAudit : IAdminExportAuditService
    {
        public List<AdminExportRecord> Records { get; } = new();

        public Task RecordAsync(AdminExportRecord export, CancellationToken cancellationToken = default)
        {
            Records.Add(export);
            return Task.CompletedTask;
        }
    }
}
