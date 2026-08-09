using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Notebook;
using ProjectManagement.ViewModels.Notebook;

namespace ProjectManagement.Tests;

public sealed class NotebookSystemItemPreferenceServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsNonPersistedDefault_ForAuthorisedCommandUser()
    {
        await using var fixture = await Fixture.CreateAsync();

        var preference = await fixture.SystemPreferences.GetAsync(
            fixture.Command.Id,
            NotebookSystemItemKeys.ConferenceDirections);

        Assert.False(preference.ShowInHome);
        Assert.False(preference.IsPinned);
        Assert.Equal(0, preference.HomePosition);
        Assert.Equal("white", preference.ColorKey);
        Assert.Empty(preference.Labels);
        Assert.Equal(Guid.Empty, preference.Version);
        Assert.Empty(await fixture.Db.NotebookSystemItemPreferences.ToListAsync());
    }

    [Fact]
    public async Task UpdateAsync_PersistsOnlyPersonalPresentationState_AndReusesNotebookLabels()
    {
        await using var fixture = await Fixture.CreateAsync();

        var preference = await fixture.SystemPreferences.UpdateAsync(
            fixture.Command.Id,
            NotebookSystemItemKeys.ConferenceDirections,
            new NotebookSystemItemPreferencePatch
            {
                ShowInHome = true,
                ColorKey = "blue",
                Labels = ["Conference", "Command", "conference"]
            });

        Assert.True(preference.ShowInHome);
        Assert.False(preference.IsPinned);
        Assert.Equal("blue", preference.ColorKey);
        Assert.Equal(new[] { "Command", "Conference" }, preference.Labels);
        Assert.NotEqual(Guid.Empty, preference.Version);

        var row = await fixture.Db.NotebookSystemItemPreferences
            .Include(x => x.Tags)
            .ThenInclude(x => x.NotebookTag)
            .SingleAsync();
        Assert.Equal(2, row.Tags.Count);
        Assert.Equal(2, await fixture.Db.NotebookTags.CountAsync());

        var labels = await fixture.Notebook.GetLabelsAsync(fixture.Command.Id);
        Assert.Contains(labels, x => x.Name == "Conference" && x.Count == 1);
        Assert.Contains(labels, x => x.Name == "Command" && x.Count == 1);
    }

    [Fact]
    public async Task SetPlacementAsync_ClampsVisualPosition_AndDoesNotCreateConferenceContent()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Db.NotebookItems.AddRange(
            NewNote(fixture.Command.Id, false, fixture.Clock.UtcNow),
            NewNote(fixture.Command.Id, false, fixture.Clock.UtcNow),
            NewNote(fixture.Command.Id, true, fixture.Clock.UtcNow));
        await fixture.Db.SaveChangesAsync();

        var preference = await fixture.SystemPreferences.SetPlacementAsync(
            fixture.Command.Id,
            NotebookSystemItemKeys.ConferenceDirections,
            isPinned: false,
            position: 99);

        Assert.True(preference.ShowInHome);
        Assert.False(preference.IsPinned);
        Assert.Equal(2, preference.HomePosition);
        Assert.Single(await fixture.Db.NotebookSystemItemPreferences.ToListAsync());
        Assert.Equal(3, await fixture.Db.NotebookItems.CountAsync());
    }

    [Fact]
    public async Task UpdateAsync_RejectsNonCommandUsers()
    {
        await using var fixture = await Fixture.CreateAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.SystemPreferences.UpdateAsync(
                fixture.Regular.Id,
                NotebookSystemItemKeys.ConferenceDirections,
                new NotebookSystemItemPreferencePatch { ShowInHome = true }));
    }

    [Fact]
    public async Task UpdateAsync_RejectsUnsupportedColour()
    {
        await using var fixture = await Fixture.CreateAsync();

        await Assert.ThrowsAsync<NotebookValidationException>(() =>
            fixture.SystemPreferences.UpdateAsync(
                fixture.Command.Id,
                NotebookSystemItemKeys.ConferenceDirections,
                new NotebookSystemItemPreferencePatch { ColorKey = "neon" }));
    }

    private static NotebookItem NewNote(string ownerId, bool pinned, DateTimeOffset now) => new()
    {
        OwnerId = ownerId,
        Title = Guid.NewGuid().ToString("N"),
        Type = NotebookItemType.Note,
        Status = NotebookItemStatus.Active,
        IsPinned = pinned,
        CreatedAtUtc = now,
        UpdatedAtUtc = now,
        Version = Guid.NewGuid()
    };

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            ApplicationDbContext db,
            ApplicationUser command,
            ApplicationUser regular,
            TestClock clock,
            NotebookSystemItemPreferenceService systemPreferences,
            NotebookService notebook)
        {
            Db = db;
            Command = command;
            Regular = regular;
            Clock = clock;
            SystemPreferences = systemPreferences;
            Notebook = notebook;
        }

        public ApplicationDbContext Db { get; }
        public ApplicationUser Command { get; }
        public ApplicationUser Regular { get; }
        public TestClock Clock { get; }
        public NotebookSystemItemPreferenceService SystemPreferences { get; }
        public NotebookService Notebook { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"notebook-system-item-{Guid.NewGuid():N}")
                .Options;
            var db = new ApplicationDbContext(options);
            var clock = new TestClock(new DateTimeOffset(2026, 8, 9, 6, 30, 0, TimeSpan.Zero));

            var command = new ApplicationUser
            {
                Id = "command-user",
                UserName = "command-user",
                NormalizedUserName = "COMMAND-USER",
                FullName = "Command User"
            };
            var regular = new ApplicationUser
            {
                Id = "regular-user",
                UserName = "regular-user",
                NormalizedUserName = "REGULAR-USER",
                FullName = "Regular User"
            };
            var role = new IdentityRole
            {
                Id = "role-hod",
                Name = RoleNames.HoD,
                NormalizedName = RoleNames.HoD.ToUpperInvariant()
            };

            db.Users.AddRange(command, regular);
            db.Roles.Add(role);
            db.UserRoles.Add(new IdentityUserRole<string> { UserId = command.Id, RoleId = role.Id });
            await db.SaveChangesAsync();

            var systemPreferences = new NotebookSystemItemPreferenceService(db, clock);
            var notebook = new NotebookService(
                db,
                new NoOpAuditService(),
                clock,
                NullLogger<NotebookService>.Instance);

            return new Fixture(db, command, regular, clock, systemPreferences, notebook);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class TestClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
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
