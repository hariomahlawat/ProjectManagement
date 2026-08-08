using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Notebook;

namespace ProjectManagement.Tests;

public sealed class NotebookReorderTests
{
    [Fact]
    public async Task Reorder_persists_requested_order_without_touching_versions_or_timestamps()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ApplicationDbContext(options);
        var now = new DateTimeOffset(2026, 6, 23, 10, 0, 0, TimeSpan.Zero);
        var first = CreateItem("owner", "First", now, 1000);
        var second = CreateItem("owner", "Second", now, 2000);
        db.NotebookItems.AddRange(first, second);
        await db.SaveChangesAsync();
        var service = CreateService(db, now.AddHours(1));

        await service.ReorderAsync("owner", NotebookBoardSection.Others,
        [
            new NotebookOrderItem(second.Id, second.Version),
            new NotebookOrderItem(first.Id, first.Version)
        ]);

        Assert.Equal(0, second.SortOrder);
        Assert.Equal(NotebookLimits.SortOrderStep, first.SortOrder);
        Assert.Equal(now, first.UpdatedAtUtc);
        Assert.Equal(now, second.UpdatedAtUtc);
        Assert.Equal(first.Version, db.Entry(first).Property(x => x.Version).CurrentValue);
        Assert.Equal(second.Version, db.Entry(second).Property(x => x.Version).CurrentValue);
    }


    [Fact]
    public async Task Index_and_reorder_support_more_than_eighty_active_items()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ApplicationDbContext(options);
        var now = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var items = Enumerable.Range(0, 120)
            .Select(index => CreateItem("owner", $"Note {index + 1}", now.AddMinutes(-index), index * NotebookLimits.SortOrderStep))
            .ToArray();
        db.NotebookItems.AddRange(items);
        await db.SaveChangesAsync();
        var service = CreateService(db, now);

        var indexVm = await service.GetIndexAsync("owner", "home", null, null, null, null);

        Assert.Equal(120, indexVm.OtherItems.Count);

        var reversed = items
            .OrderByDescending(item => item.SortOrder)
            .Select(item => new NotebookOrderItem(item.Id, item.Version))
            .ToArray();
        await service.ReorderAsync("owner", NotebookBoardSection.Others, reversed);

        var persisted = await db.NotebookItems
            .OrderBy(item => item.SortOrder)
            .Select(item => item.Id)
            .ToArrayAsync();
        Assert.Equal(reversed.Select(item => item.Id), persisted);
    }

    [Fact]
    public async Task Sequential_creates_keep_newest_item_at_top_with_distinct_sparse_orders()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ApplicationDbContext(options);
        var now = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var service = CreateService(db, now);

        for (var index = 1; index <= 100; index++)
        {
            await service.CreateAsync("owner", new NotebookCreateInput
            {
                ClientRequestId = Guid.NewGuid(),
                Title = $"Note {index}",
                Type = NotebookItemType.Note
            });
        }

        var persistedOrders = await db.NotebookItems
            .Select(item => item.SortOrder)
            .ToArrayAsync();
        Assert.Equal(100, persistedOrders.Distinct().Count());

        var indexVm = await service.GetIndexAsync("owner", "home", null, null, null, null);
        Assert.Equal(100, indexVm.OtherItems.Count);
        Assert.Equal("Note 100", indexVm.OtherItems[0].Title);
        Assert.Equal("Note 1", indexVm.OtherItems[^1].Title);
    }

    [Fact]
    public async Task Reorder_rejects_incomplete_or_mixed_board_payloads()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ApplicationDbContext(options);
        var now = DateTimeOffset.UtcNow;
        var first = CreateItem("owner", "First", now, 1000);
        var second = CreateItem("owner", "Second", now, 2000);
        second.IsPinned = true;
        db.NotebookItems.AddRange(first, second);
        await db.SaveChangesAsync();
        var service = CreateService(db, now);

        await Assert.ThrowsAsync<NotebookValidationException>(() =>
            service.ReorderAsync("owner", NotebookBoardSection.Others,
            [new NotebookOrderItem(first.Id, first.Version), new NotebookOrderItem(second.Id, second.Version)]));
    }

    [Fact]
    public async Task Reorder_rejects_stale_version()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ApplicationDbContext(options);
        var now = DateTimeOffset.UtcNow;
        var item = CreateItem("owner", "First", now, 1000);
        db.NotebookItems.Add(item);
        await db.SaveChangesAsync();
        var service = CreateService(db, now);

        await Assert.ThrowsAsync<NotebookConcurrencyException>(() =>
            service.ReorderAsync("owner", NotebookBoardSection.Others,
            [new NotebookOrderItem(item.Id, Guid.NewGuid())]));
    }

    private static NotebookItem CreateItem(string owner, string title, DateTimeOffset now, int sortOrder) => new()
    {
        OwnerId = owner,
        Title = title,
        Type = NotebookItemType.Note,
        Status = NotebookItemStatus.Active,
        SortOrder = sortOrder,
        CreatedAtUtc = now,
        UpdatedAtUtc = now,
        Version = Guid.NewGuid()
    };

    private static NotebookService CreateService(ApplicationDbContext db, DateTimeOffset now) =>
        new(db, new NoOpAuditService(), new TestClock(now), NullLogger<NotebookService>.Instance);

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
