using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Notebook;

namespace ProjectManagement.Tests;

public sealed class NotebookLabelsTests
{
    [Fact]
    public async Task Set_labels_creates_reuses_and_removes_item_links()
    {
        var (db, service, item) = await CreateFixtureAsync();
        var first = await service.SetLabelsAsync("owner-1", item.Id, ["Procurement", "Docs", "docs"], item.Version);
        Assert.Equal(new[] { "Docs", "Procurement" }, first.Tags.OrderBy(x => x));
        Assert.Equal(2, await db.NotebookTags.CountAsync());

        var second = await service.SetLabelsAsync("owner-1", item.Id, ["Docs", "Operations"], first.Version);
        Assert.Equal(new[] { "Docs", "Operations" }, second.Tags.OrderBy(x => x));
        Assert.Equal(2, await db.NotebookItemTags.CountAsync(x => x.NotebookItemId == item.Id));
        Assert.Equal(3, await db.NotebookTags.CountAsync());
    }

    [Fact]
    public async Task Set_labels_with_stale_version_returns_authoritative_item()
    {
        var (_, service, item) = await CreateFixtureAsync();
        var conflict = await Assert.ThrowsAsync<NotebookConcurrencyException>(() =>
            service.SetLabelsAsync("owner-1", item.Id, ["Docs"], Guid.NewGuid()));
        Assert.NotNull(conflict.CurrentItem);
        Assert.Equal(item.Version, conflict.CurrentVersion);
    }

    [Fact]
    public async Task Rename_label_merges_case_insensitive_duplicates_without_duplicate_links()
    {
        var (db, service, item) = await CreateFixtureAsync();
        var first = await service.SetLabelsAsync("owner-1", item.Id, ["Docs", "Operations"], item.Version);
        var docs = await db.NotebookTags.SingleAsync(x => x.NormalizedName == "DOCS");
        await service.RenameLabelAsync("owner-1", docs.Id, "operations");
        Assert.Single(await db.NotebookTags.ToListAsync());
        Assert.Single(await db.NotebookItemTags.Where(x => x.NotebookItemId == item.Id).ToListAsync());
    }

    [Fact]
    public async Task Delete_label_removes_links_but_not_notes()
    {
        var (db, service, item) = await CreateFixtureAsync();
        await service.SetLabelsAsync("owner-1", item.Id, ["Docs"], item.Version);
        var tag = await db.NotebookTags.SingleAsync();
        await service.DeleteLabelAsync("owner-1", tag.Id);
        Assert.Empty(await db.NotebookTags.ToListAsync());
        Assert.Empty(await db.NotebookItemTags.ToListAsync());
        Assert.NotNull(await db.NotebookItems.FindAsync(item.Id));
    }

    private static async Task<(ApplicationDbContext Db, NotebookService Service, NotebookItem Item)> CreateFixtureAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new ApplicationDbContext(options);
        var clock = new TestClock { UtcNowValue = DateTimeOffset.UtcNow };
        var item = new NotebookItem { OwnerId = "owner-1", Title = "Note", Type = NotebookItemType.Note, Status = NotebookItemStatus.Active, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow, Version = Guid.NewGuid() };
        db.NotebookItems.Add(item); await db.SaveChangesAsync();
        return (db, new NotebookService(db, new NoOpAuditService(), clock, NullLogger<NotebookService>.Instance), item);
    }

    private sealed class TestClock : IClock { public DateTimeOffset UtcNowValue { get; set; } public DateTimeOffset UtcNow => UtcNowValue; }
    private sealed class NoOpAuditService : IAuditService
    {
        public Task LogAsync(string action, string? message = null, string level = "Info", string? userId = null, string? userName = null, IDictionary<string, string?>? data = null, Microsoft.AspNetCore.Http.HttpContext? http = null) => Task.CompletedTask;
    }
}
