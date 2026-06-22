using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Notebook;

namespace ProjectManagement.Tests;

public sealed class NotebookColourTests
{
    [Fact]
    public async Task Set_colour_updates_only_colour_and_version()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ApplicationDbContext(options);
        var clock = new TestClock { UtcNowValue = new DateTimeOffset(2026, 6, 22, 10, 0, 0, TimeSpan.Zero) };
        var item = new NotebookItem
        {
            OwnerId = "owner-1",
            Title = "Original title",
            BodyMarkdown = "Original body",
            Type = NotebookItemType.Note,
            Status = NotebookItemStatus.Active,
            Priority = NotebookPriority.High,
            ReminderAtUtc = clock.UtcNowValue.AddDays(1),
            IsPinned = true,
            IsFavorite = true,
            ColorKey = "white",
            CreatedAtUtc = clock.UtcNowValue,
            UpdatedAtUtc = clock.UtcNowValue,
            Version = Guid.NewGuid()
        };
        item.ChecklistItems.Add(new NotebookChecklistItem
        {
            Text = "Existing row",
            SortOrder = 0,
            CreatedAtUtc = clock.UtcNowValue
        });
        db.NotebookItems.Add(item);
        await db.SaveChangesAsync();

        var originalVersion = item.Version;
        var originalUpdatedAt = item.UpdatedAtUtc;
        clock.UtcNowValue = clock.UtcNowValue.AddMinutes(5);
        var service = new NotebookService(db, new NoOpAuditService(), clock, NullLogger<NotebookService>.Instance);

        var updated = await service.SetColourAsync("owner-1", item.Id, "amber", originalVersion);

        Assert.Equal("amber", updated.ColorKey);
        Assert.NotEqual(originalVersion, updated.Version);
        Assert.Equal(clock.UtcNowValue, updated.UpdatedAtUtc);
        Assert.Equal("Original title", updated.Title);
        Assert.Equal("Original body", updated.BodyMarkdown);
        Assert.Equal(NotebookPriority.High, updated.Priority);
        Assert.True(updated.IsPinned);
        Assert.True(updated.IsFavorite);
        Assert.Equal(item.ReminderAtUtc, updated.ReminderAtUtc);
        Assert.Single(updated.ChecklistItems);
        Assert.NotEqual(originalUpdatedAt, updated.UpdatedAtUtc);
    }

    [Fact]
    public async Task Set_colour_with_stale_version_returns_authoritative_item()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ApplicationDbContext(options);
        var clock = new TestClock { UtcNowValue = DateTimeOffset.UtcNow };
        var item = new NotebookItem
        {
            OwnerId = "owner-1",
            Title = "Current",
            Type = NotebookItemType.Note,
            Status = NotebookItemStatus.Active,
            ColorKey = "blue",
            CreatedAtUtc = clock.UtcNowValue,
            UpdatedAtUtc = clock.UtcNowValue,
            Version = Guid.NewGuid()
        };
        db.NotebookItems.Add(item);
        await db.SaveChangesAsync();
        var service = new NotebookService(db, new NoOpAuditService(), clock, NullLogger<NotebookService>.Instance);

        var conflict = await Assert.ThrowsAsync<NotebookConcurrencyException>(() =>
            service.SetColourAsync("owner-1", item.Id, "rose", Guid.NewGuid()));

        Assert.NotNull(conflict.CurrentItem);
        Assert.Equal(item.Version, conflict.CurrentVersion);
        Assert.Equal("blue", conflict.CurrentItem!.ColorKey);
    }

    [Fact]
    public async Task Set_colour_cannot_modify_another_owners_item()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ApplicationDbContext(options);
        var clock = new TestClock { UtcNowValue = DateTimeOffset.UtcNow };
        var item = new NotebookItem
        {
            OwnerId = "owner-1",
            Title = "Private",
            Type = NotebookItemType.Note,
            Status = NotebookItemStatus.Active,
            CreatedAtUtc = clock.UtcNowValue,
            UpdatedAtUtc = clock.UtcNowValue,
            Version = Guid.NewGuid()
        };
        db.NotebookItems.Add(item);
        await db.SaveChangesAsync();
        var service = new NotebookService(db, new NoOpAuditService(), clock, NullLogger<NotebookService>.Instance);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.SetColourAsync("owner-2", item.Id, "green", item.Version));
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNowValue { get; set; }
        public DateTimeOffset UtcNow => UtcNowValue;
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
