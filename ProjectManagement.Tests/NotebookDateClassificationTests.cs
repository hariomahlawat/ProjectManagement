using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Notebook;

namespace ProjectManagement.Tests;

public sealed class NotebookDateClassificationTests
{
    [Fact]
    public async Task Today_and_overdue_are_distinct_IST_calendar_buckets()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ApplicationDbContext(options);

        // 08 Aug 2026 12:00 IST. The IST day starts at 07 Aug 18:30 UTC.
        var now = new DateTimeOffset(2026, 8, 8, 6, 30, 0, TimeSpan.Zero);
        var overdue = CreateReminder("Overdue", new DateTimeOffset(2026, 8, 7, 18, 29, 0, TimeSpan.Zero), now);
        var todayStart = CreateReminder("Today start", new DateTimeOffset(2026, 8, 7, 18, 30, 0, TimeSpan.Zero), now);
        var todayLate = CreateReminder("Today late", new DateTimeOffset(2026, 8, 8, 18, 29, 0, TimeSpan.Zero), now);
        var tomorrow = CreateReminder("Tomorrow", new DateTimeOffset(2026, 8, 8, 18, 30, 0, TimeSpan.Zero), now);
        db.NotebookItems.AddRange(overdue, todayStart, todayLate, tomorrow);
        await db.SaveChangesAsync();

        var service = new NotebookService(db, new NoOpAuditService(), new TestClock(now), NullLogger<NotebookService>.Instance);

        var counts = await service.GetCountsAsync("owner");
        Assert.Equal(2, counts["today"]);
        Assert.Equal(1, counts["overdue"]);
        Assert.Equal(4, counts["reminders"]);

        var today = await service.GetIndexAsync("owner", "today", null, null, null, null);
        Assert.Equal(new[] { "Today start", "Today late" }.OrderBy(x => x), today.Items.Select(x => x.Title).OrderBy(x => x));

        var overdueView = await service.GetIndexAsync("owner", "overdue", null, null, null, null);
        Assert.Single(overdueView.Items);
        Assert.Equal("Overdue", overdueView.Items[0].Title);
    }

    private static NotebookItem CreateReminder(string title, DateTimeOffset dueAt, DateTimeOffset now) => new()
    {
        OwnerId = "owner",
        Title = title,
        Type = NotebookItemType.Note,
        Status = NotebookItemStatus.Active,
        Priority = NotebookPriority.Normal,
        ReminderAtUtc = dueAt,
        CreatedAtUtc = now,
        UpdatedAtUtc = now,
        Version = Guid.NewGuid()
    };

    private sealed class TestClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class NoOpAuditService : IAuditService
    {
        public Task LogAsync(string action, string? message = null, string level = "Info", string? userId = null,
            string? userName = null, IDictionary<string, string?>? data = null,
            Microsoft.AspNetCore.Http.HttpContext? http = null) => Task.CompletedTask;
    }
}
