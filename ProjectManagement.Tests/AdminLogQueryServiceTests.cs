using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Admin;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class AdminLogQueryServiceTests
{
    [Fact]
    public async Task GetAsync_UsesExactUserIdAndIstCalendarBoundaries()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);
        db.AuditLogs.AddRange(
            new AuditLog
            {
                TimeUtc = new DateTime(2026, 7, 11, 18, 29, 59, DateTimeKind.Utc),
                Level = "Info",
                Action = "BeforeBoundary",
                UserId = "user-1",
                UserName = "alpha"
            },
            new AuditLog
            {
                TimeUtc = new DateTime(2026, 7, 11, 18, 30, 0, DateTimeKind.Utc),
                Level = "Info",
                Action = "AtStartBoundary",
                UserId = "user-1",
                UserName = "alpha"
            },
            new AuditLog
            {
                TimeUtc = new DateTime(2026, 7, 12, 18, 29, 59, DateTimeKind.Utc),
                Level = "Info",
                Action = "BeforeEndBoundary",
                UserId = "user-1",
                UserName = "alpha"
            },
            new AuditLog
            {
                TimeUtc = new DateTime(2026, 7, 12, 18, 30, 0, DateTimeKind.Utc),
                Level = "Info",
                Action = "AtEndBoundary",
                UserId = "user-1",
                UserName = "alpha"
            },
            new AuditLog
            {
                TimeUtc = new DateTime(2026, 7, 12, 6, 0, 0, DateTimeKind.Utc),
                Level = "Info",
                Action = "WrongUser",
                UserId = "user-10",
                UserName = "alpha similar"
            });
        await db.SaveChangesAsync();

        var time = new AdminTimeService(
            new FixedClock(new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero)));
        var service = new AdminLogQueryService(db, time);

        var result = await service.GetAsync(new AdminLogQuery(
            Level: null,
            Action: null,
            UserName: "alpha",
            UserId: "user-1",
            Ip: null,
            Contains: null,
            From: new DateOnly(2026, 7, 12),
            To: new DateOnly(2026, 7, 12)));

        Assert.Equal(2, result.Total);
        Assert.Equal(
            new[] { "BeforeEndBoundary", "AtStartBoundary" },
            result.Rows.Select(row => row.Action).ToArray());
        Assert.Single(result.SeriesLabels);
        Assert.Equal("12 Jul 2026", result.SeriesLabels[0]);
        Assert.Equal(2, result.SeriesCounts[0]);
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; }
    }
}
