using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Services;
using ProjectManagement.Services.Notebook;
using ProjectManagement.Services.Notifications;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class NotebookCollaborationNotificationTests
{
    [Fact]
    public async Task AddCollaborator_PersistsCollaborationAndDurableDispatchTogether()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Service.AddCollaboratorAsync(
            fixture.Owner.Id,
            fixture.Item.Id,
            fixture.Collaborator.Id,
            NotebookCollaborationRole.Editor,
            fixture.Item.Version);

        Assert.Single(await fixture.Db.NotebookItemCollaborators.ToListAsync());
        var dispatch = Assert.Single(await fixture.Db.NotificationDispatches.ToListAsync());
        Assert.Equal(NotificationKind.NotebookShared, dispatch.Kind);
        Assert.Equal(fixture.Collaborator.Id, dispatch.RecipientUserId);
        Assert.Equal("Notebook", dispatch.Module);
        Assert.Equal("NotebookShared", dispatch.EventType);
        Assert.Equal($"/Notebook?view=shared&note={fixture.Item.Id:D}", dispatch.Route);
        Assert.Contains("shared", dispatch.Title!, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(fixture.Item.Version, result.Version);
    }

    [Fact]
    public async Task RemoveThenReshare_UsesDistinctCollaborationEventFingerprints()
    {
        await using var fixture = await Fixture.CreateAsync();

        var added = await fixture.Service.AddCollaboratorAsync(
            fixture.Owner.Id,
            fixture.Item.Id,
            fixture.Collaborator.Id,
            NotebookCollaborationRole.Editor,
            fixture.Item.Version);
        var removed = await fixture.Service.RemoveCollaboratorAsync(
            fixture.Owner.Id,
            fixture.Item.Id,
            fixture.Collaborator.Id,
            added.Version);
        await fixture.Service.AddCollaboratorAsync(
            fixture.Owner.Id,
            fixture.Item.Id,
            fixture.Collaborator.Id,
            NotebookCollaborationRole.Editor,
            removed.Version);

        var dispatches = await fixture.Db.NotificationDispatches
            .OrderBy(row => row.Id)
            .ToListAsync();
        Assert.Equal(3, dispatches.Count);
        Assert.Equal(NotificationKind.NotebookShared, dispatches[0].Kind);
        Assert.Equal(NotificationKind.NotebookAccessRemoved, dispatches[1].Kind);
        Assert.Equal(NotificationKind.NotebookShared, dispatches[2].Kind);
        Assert.NotEqual(dispatches[0].Fingerprint, dispatches[2].Fingerprint);
    }

    [Fact]
    public async Task LeaveCollaboration_NotifiesOwnerAndRemovesAccess()
    {
        await using var fixture = await Fixture.CreateAsync();

        await fixture.Service.AddCollaboratorAsync(
            fixture.Owner.Id,
            fixture.Item.Id,
            fixture.Collaborator.Id,
            NotebookCollaborationRole.Editor,
            fixture.Item.Version);
        fixture.Db.ChangeTracker.Clear();

        await fixture.Service.LeaveCollaborationAsync(fixture.Collaborator.Id, fixture.Item.Id);

        Assert.Empty(await fixture.Db.NotebookItemCollaborators.ToListAsync());
        var leftDispatch = await fixture.Db.NotificationDispatches
            .OrderByDescending(row => row.Id)
            .FirstAsync();
        Assert.Equal(NotificationKind.NotebookCollaborationLeft, leftDispatch.Kind);
        Assert.Equal(fixture.Owner.Id, leftDispatch.RecipientUserId);
        Assert.Equal($"/Notebook?view=home&note={fixture.Item.Id:D}", leftDispatch.Route);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            ApplicationDbContext db,
            NotebookService service,
            ApplicationUser owner,
            ApplicationUser collaborator,
            NotebookItem item)
        {
            Db = db;
            Service = service;
            Owner = owner;
            Collaborator = collaborator;
            Item = item;
        }

        public ApplicationDbContext Db { get; }
        public NotebookService Service { get; }
        public ApplicationUser Owner { get; }
        public ApplicationUser Collaborator { get; }
        public NotebookItem Item { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"notebook-notifications-{Guid.NewGuid():N}")
                .Options;
            var db = new ApplicationDbContext(options);
            var now = new DateTimeOffset(2026, 7, 2, 4, 0, 0, TimeSpan.Zero);
            var clock = new FixedClock(now);
            var owner = new ApplicationUser
            {
                Id = "owner-1",
                UserName = "owner",
                FullName = "Owner One"
            };
            var collaborator = new ApplicationUser
            {
                Id = "collaborator-1",
                UserName = "collaborator",
                FullName = "Collaborator One"
            };
            var item = new NotebookItem
            {
                Id = Guid.NewGuid(),
                OwnerId = owner.Id,
                Owner = owner,
                Title = "Operational note",
                Type = NotebookItemType.Note,
                Status = NotebookItemStatus.Active,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                Version = Guid.NewGuid()
            };

            db.Users.AddRange(owner, collaborator);
            db.NotebookItems.Add(item);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var publisher = new NotificationPublisher(
                db,
                clock,
                NullLogger<NotificationPublisher>.Instance);
            var notebookNotifications = new NotebookNotificationService(publisher);
            var service = new NotebookService(
                db,
                new NoOpAuditService(),
                clock,
                NullLogger<NotebookService>.Instance,
                notebookNotifications);

            return new Fixture(db, service, owner, collaborator, item);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
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
            Microsoft.AspNetCore.Http.HttpContext? http = null)
            => Task.CompletedTask;
    }
}
