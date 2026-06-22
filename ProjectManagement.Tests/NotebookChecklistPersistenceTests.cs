using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Notebook;

namespace ProjectManagement.Tests;

public sealed class NotebookChecklistPersistenceTests
{
    [Fact]
    public async Task Repeated_checklist_save_preserves_row_identity_and_created_timestamp()
    {
        var fixture = await CreateFixtureAsync();
        await using var db = fixture.Db;
        var service = CreateService(db, fixture.Clock);

        var first = await service.UpdateChecklistAsync(
            fixture.OwnerId,
            fixture.ItemId,
            "Checklist",
            null,
            [new NotebookChecklistEditRow { ClientKey = "local-1", Text = "First row", SortOrder = 0 }],
            fixture.Version);

        var created = Assert.Single(first.ChecklistItems);
        Assert.True(created.Id > 0);
        Assert.Equal("local-1", created.ClientKey);

        var createdEntity = await db.NotebookChecklistItems.AsNoTracking().SingleAsync();
        var originalCreatedAt = createdEntity.CreatedAtUtc;

        fixture.Clock.UtcNowValue = fixture.Clock.UtcNowValue.AddMinutes(5);
        var second = await service.UpdateChecklistAsync(
            fixture.OwnerId,
            fixture.ItemId,
            "Checklist updated",
            "Body",
            [new NotebookChecklistEditRow { Id = created.Id, Text = "Edited row", SortOrder = 0 }],
            first.Version);

        var updated = Assert.Single(second.ChecklistItems);
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("Edited row", updated.Text);

        var persisted = await db.NotebookChecklistItems.AsNoTracking().SingleAsync();
        Assert.Equal(created.Id, persisted.Id);
        Assert.Equal(originalCreatedAt, persisted.CreatedAtUtc);
        Assert.Equal("Edited row", persisted.Text);
    }

    [Fact]
    public async Task Checklist_save_deletes_omitted_rows_without_creating_duplicates()
    {
        var fixture = await CreateFixtureAsync(withRows: true);
        await using var db = fixture.Db;
        var service = CreateService(db, fixture.Clock);
        var existing = await db.NotebookChecklistItems.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync();

        var result = await service.UpdateChecklistAsync(
            fixture.OwnerId,
            fixture.ItemId,
            "Checklist",
            null,
            [new NotebookChecklistEditRow { Id = existing[1].Id, Text = existing[1].Text, SortOrder = 0 }],
            fixture.Version);

        var only = Assert.Single(result.ChecklistItems);
        Assert.Equal(existing[1].Id, only.Id);
        Assert.Equal(1, await db.NotebookChecklistItems.CountAsync());
    }

    [Fact]
    public async Task Checklist_completion_timestamp_is_set_once_and_cleared_when_reopened()
    {
        var fixture = await CreateFixtureAsync(withRows: true);
        await using var db = fixture.Db;
        var service = CreateService(db, fixture.Clock);
        var row = await db.NotebookChecklistItems.AsNoTracking().OrderBy(x => x.Id).FirstAsync();

        var completed = await service.UpdateChecklistAsync(
            fixture.OwnerId,
            fixture.ItemId,
            "Checklist",
            null,
            [new NotebookChecklistEditRow { Id = row.Id, Text = row.Text, IsDone = true, SortOrder = 0 }],
            fixture.Version);

        var completedAt = (await db.NotebookChecklistItems.AsNoTracking().SingleAsync(x => x.Id == row.Id)).CompletedAtUtc;
        Assert.Equal(fixture.Clock.UtcNowValue, completedAt);

        fixture.Clock.UtcNowValue = fixture.Clock.UtcNowValue.AddMinutes(10);
        var stillCompleted = await service.UpdateChecklistAsync(
            fixture.OwnerId,
            fixture.ItemId,
            "Checklist",
            null,
            [new NotebookChecklistEditRow { Id = row.Id, Text = row.Text, IsDone = true, SortOrder = 0 }],
            completed.Version);

        Assert.Equal(completedAt, (await db.NotebookChecklistItems.AsNoTracking().SingleAsync(x => x.Id == row.Id)).CompletedAtUtc);

        await service.UpdateChecklistAsync(
            fixture.OwnerId,
            fixture.ItemId,
            "Checklist",
            null,
            [new NotebookChecklistEditRow { Id = row.Id, Text = row.Text, IsDone = false, SortOrder = 0 }],
            stillCompleted.Version);

        Assert.Null((await db.NotebookChecklistItems.AsNoTracking().SingleAsync(x => x.Id == row.Id)).CompletedAtUtc);
    }

    [Fact]
    public async Task Checklist_update_rejects_foreign_row_id_and_duplicate_client_keys()
    {
        var fixture = await CreateFixtureAsync(withRows: true);
        await using var db = fixture.Db;
        var service = CreateService(db, fixture.Clock);

        var foreignItem = new NotebookItem
        {
            OwnerId = fixture.OwnerId,
            Title = "Other",
            Type = NotebookItemType.Checklist,
            CreatedAtUtc = fixture.Clock.UtcNowValue,
            UpdatedAtUtc = fixture.Clock.UtcNowValue
        };
        foreignItem.ChecklistItems.Add(new NotebookChecklistItem
        {
            Text = "Foreign",
            SortOrder = 0,
            CreatedAtUtc = fixture.Clock.UtcNowValue
        });
        db.NotebookItems.Add(foreignItem);
        await db.SaveChangesAsync();
        var foreignRowId = foreignItem.ChecklistItems.Single().Id;

        await Assert.ThrowsAsync<NotebookValidationException>(() =>
            service.UpdateChecklistAsync(
                fixture.OwnerId,
                fixture.ItemId,
                "Checklist",
                null,
                [new NotebookChecklistEditRow { Id = foreignRowId, Text = "Foreign", SortOrder = 0 }],
                fixture.Version));

        await Assert.ThrowsAsync<NotebookValidationException>(() =>
            service.UpdateChecklistAsync(
                fixture.OwnerId,
                fixture.ItemId,
                "Checklist",
                null,
                [
                    new NotebookChecklistEditRow { ClientKey = "duplicate", Text = "One", SortOrder = 0 },
                    new NotebookChecklistEditRow { ClientKey = "duplicate", Text = "Two", SortOrder = 1 }
                ],
                fixture.Version));
    }

    [Fact]
    public async Task Stale_version_returns_authoritative_item_and_content_update_preserves_metadata()
    {
        var fixture = await CreateFixtureAsync();
        await using var db = fixture.Db;
        var service = CreateService(db, fixture.Clock);

        var item = await db.NotebookItems.SingleAsync(x => x.Id == fixture.ItemId);
        item.IsPinned = true;
        item.IsFavorite = true;
        item.Priority = NotebookPriority.High;
        item.ColorKey = "amber";
        await db.SaveChangesAsync();

        var conflict = await Assert.ThrowsAsync<NotebookConcurrencyException>(() =>
            service.UpdateContentAsync(
                fixture.OwnerId,
                fixture.ItemId,
                "Stale title",
                "Stale body",
                Guid.NewGuid()));

        Assert.NotNull(conflict.CurrentItem);
        Assert.Equal(item.Version, conflict.CurrentVersion);
        Assert.Equal(item.Version, conflict.CurrentItem!.Version);
        Assert.Equal(item.Title, conflict.CurrentItem.Title);

        var updated = await service.UpdateContentAsync(
            fixture.OwnerId,
            fixture.ItemId,
            "New title",
            "New body",
            item.Version);

        Assert.True(updated.IsPinned);
        Assert.True(updated.IsFavorite);
        Assert.Equal(NotebookPriority.High, updated.Priority);
        Assert.Equal("amber", updated.ColorKey);
    }

    private static NotebookService CreateService(ApplicationDbContext db, TestClock clock) =>
        new(db, new NoOpAuditService(), clock, NullLogger<NotebookService>.Instance);

    private static async Task<Fixture> CreateFixtureAsync(bool withRows = false)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ApplicationDbContext(options);
        var clock = new TestClock { UtcNowValue = new DateTimeOffset(2026, 6, 22, 10, 0, 0, TimeSpan.Zero) };
        var ownerId = "owner-1";
        var item = new NotebookItem
        {
            OwnerId = ownerId,
            Title = "Checklist",
            Type = NotebookItemType.Checklist,
            Status = NotebookItemStatus.Active,
            Priority = NotebookPriority.Normal,
            CreatedAtUtc = clock.UtcNowValue,
            UpdatedAtUtc = clock.UtcNowValue,
            Version = Guid.NewGuid()
        };

        if (withRows)
        {
            item.ChecklistItems.Add(new NotebookChecklistItem { Text = "One", SortOrder = 0, CreatedAtUtc = clock.UtcNowValue });
            item.ChecklistItems.Add(new NotebookChecklistItem { Text = "Two", SortOrder = 1, CreatedAtUtc = clock.UtcNowValue });
        }

        db.NotebookItems.Add(item);
        await db.SaveChangesAsync();
        return new Fixture(db, clock, ownerId, item.Id, item.Version);
    }

    private sealed record Fixture(
        ApplicationDbContext Db,
        TestClock Clock,
        string OwnerId,
        Guid ItemId,
        Guid Version);

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
