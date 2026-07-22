using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Notebook;

namespace ProjectManagement.Tests;

public sealed class NotebookReminderValidationTests
{
    [Fact]
    public async Task Reminder_create_requires_a_schedule()
    {
        await using var fixture = CreateFixture();

        var error = await Assert.ThrowsAsync<NotebookValidationException>(() => fixture.Service.CreateAsync(
            "owner-1",
            new NotebookCreateInput
            {
                ClientRequestId = Guid.NewGuid(),
                Title = "Review briefing",
                Type = NotebookItemType.Reminder
            }));

        Assert.Equal("Choose a reminder date and time.", error.Message);
        Assert.Empty(fixture.Db.NotebookItems);
    }

    [Fact]
    public async Task Reminder_create_rejects_a_past_schedule()
    {
        await using var fixture = CreateFixture();

        var error = await Assert.ThrowsAsync<NotebookValidationException>(() => fixture.Service.CreateAsync(
            "owner-1",
            new NotebookCreateInput
            {
                ClientRequestId = Guid.NewGuid(),
                Title = "Review briefing",
                Type = NotebookItemType.Reminder,
                ReminderAtUtc = fixture.Clock.UtcNow.AddMinutes(-1)
            }));

        Assert.Equal("Choose a future reminder date and time.", error.Message);
        Assert.Empty(fixture.Db.NotebookItems);
    }

    [Fact]
    public async Task Reminder_create_accepts_a_future_schedule()
    {
        await using var fixture = CreateFixture();
        var dueAt = fixture.Clock.UtcNow.AddHours(2);

        var created = await fixture.Service.CreateAsync(
            "owner-1",
            new NotebookCreateInput
            {
                ClientRequestId = Guid.NewGuid(),
                Title = "Review briefing",
                Type = NotebookItemType.Reminder,
                ReminderAtUtc = dueAt
            });

        Assert.Equal(dueAt, created.ReminderAtUtc);
        Assert.Single(fixture.Db.NotebookItems);
    }

    private static Fixture CreateFixture()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ApplicationDbContext(options);
        var clock = new TestClock(new DateTimeOffset(2026, 7, 18, 2, 30, 0, TimeSpan.Zero));
        var service = new NotebookService(db, new NoOpAuditService(), clock, NullLogger<NotebookService>.Instance);
        return new Fixture(db, service, clock);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public Fixture(ApplicationDbContext db, NotebookService service, TestClock clock)
        {
            Db = db;
            Service = service;
            Clock = clock;
        }

        public ApplicationDbContext Db { get; }
        public NotebookService Service { get; }
        public TestClock Clock { get; }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class TestClock : IClock
    {
        public TestClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class NoOpAuditService : IAuditService
    {
        public Task LogAsync(
            string action,
            string? message = null,
            string level = "Info",
            string? userId = null,
            string? userName = null,
            IDictionary<string, string?>? data = null,
            Microsoft.AspNetCore.Http.HttpContext? http = null) => Task.CompletedTask;
    }
}
