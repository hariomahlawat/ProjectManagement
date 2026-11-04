using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services;
using ProjectManagement.Services.ProjectBriefings;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

// Deleting a shared deck moves it to "Recently deleted" for DeletedRetentionDays, from where any
// Comdt/HoD can restore it with its projects and choices intact.
public sealed class ProjectBriefingDeckDeleteRestoreTests
{
    private const string UserId = "user-1";

    [Fact]
    public async Task DeletingADeck_HidesItButKeepsItsProjects()
    {
        await using var db = CreateDb();
        var clock = new MutableClock();
        var deck = await SeedAsync(db, "Monthly review", clock.UtcNow);
        var service = Service(db, clock);

        await service.DeleteAsync(deck.Id, UserId, Version(deck));

        Assert.Empty(await service.ListAsync(UserId));
        Assert.Null(await service.GetEntityAsync(deck.Id, UserId, includeItems: false));
        var deleted = Assert.Single(await service.ListDeletedAsync(UserId));
        Assert.Equal("Monthly review", deleted.Name);
        Assert.Equal(1, deleted.ProjectCount);
        Assert.Equal(clock.UtcNow.AddDays(ProjectBriefingDeckService.DeletedRetentionDays), deleted.RestorableUntilUtc);
        Assert.Equal(1, await db.Set<ProjectBriefingDeckItem>().CountAsync());
    }

    [Fact]
    public async Task RestoringADeck_BringsItBackWithItsProjects()
    {
        await using var db = CreateDb();
        var clock = new MutableClock();
        var deck = await SeedAsync(db, "Monthly review", clock.UtcNow);
        var service = Service(db, clock);
        await service.DeleteAsync(deck.Id, UserId, Version(deck));

        var result = await service.RestoreAsync(deck.Id, UserId);

        Assert.False(result.Renamed);
        var live = Assert.Single(await service.ListAsync(UserId));
        Assert.Equal("Monthly review", live.Name);
        Assert.Equal(1, live.ProjectCount);
        Assert.Empty(await service.ListDeletedAsync(UserId));
    }

    [Fact]
    public async Task ADeletedName_CanBeReused_AndRestoreThenRenamesTheOldDeck()
    {
        await using var db = CreateDb();
        var clock = new MutableClock();
        var deck = await SeedAsync(db, "Monthly review", clock.UtcNow);
        var service = Service(db, clock);
        await service.DeleteAsync(deck.Id, UserId, Version(deck));

        await service.CreateAsync(UserId, "Monthly review", description: null);
        var result = await service.RestoreAsync(deck.Id, UserId);

        Assert.True(result.Renamed);
        Assert.Equal("Monthly review (restored)", result.Name);
        Assert.Equal(
            new[] { "Monthly review", "Monthly review (restored)" },
            (await service.ListAsync(UserId)).Select(item => item.Name).OrderBy(name => name).ToArray());
    }

    [Fact]
    public async Task AfterTheRetentionWindow_ADeckCannotBeRestored_AndIsRemovedOnTheNextDelete()
    {
        await using var db = CreateDb();
        var clock = new MutableClock();
        var old = await SeedAsync(db, "Old review", clock.UtcNow);
        var other = await SeedAsync(db, "Other review", clock.UtcNow);
        var service = Service(db, clock);
        await service.DeleteAsync(old.Id, UserId, Version(old));

        clock.Advance(TimeSpan.FromDays(ProjectBriefingDeckService.DeletedRetentionDays + 1));

        Assert.Empty(await service.ListDeletedAsync(UserId));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.RestoreAsync(old.Id, UserId));

        await service.DeleteAsync(other.Id, UserId, Version(other));
        Assert.False(await db.Set<ProjectBriefingDeck>().AnyAsync(deck => deck.Id == old.Id));
        Assert.True(await db.Set<ProjectBriefingDeck>().AnyAsync(deck => deck.Id == other.Id));
    }

    [Fact]
    public async Task DuplicatingADeckWithAVeryLongName_StaysWithinTheNameLimit()
    {
        await using var db = CreateDb();
        var clock = new MutableClock();
        var deck = await SeedAsync(db, new string('A', 158), clock.UtcNow);
        var service = Service(db, clock);

        var copyId = await service.DuplicateAsync(deck.Id, UserId);

        var copy = await db.Set<ProjectBriefingDeck>().SingleAsync(item => item.Id == copyId);
        Assert.True(copy.Name.Length <= 160);
        Assert.EndsWith("— Copy", copy.Name);
    }

    private static async Task<ProjectBriefingDeck> SeedAsync(ApplicationDbContext db, string name, DateTimeOffset now)
    {
        if (!await db.Projects.AnyAsync())
        {
            db.Projects.Add(new Project { Id = 10, Name = "Project ten", CreatedByUserId = UserId });
        }

        var deck = new ProjectBriefingDeck
        {
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            OwnerUserId = UserId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = Guid.NewGuid().ToByteArray(),
            Items = new List<ProjectBriefingDeckItem>
            {
                new() { ProjectId = 10, SortOrder = 10, AddedAtUtc = now }
            }
        };
        db.Add(deck);
        await db.SaveChangesAsync();
        return deck;
    }

    private static string Version(ProjectBriefingDeck deck) => Convert.ToBase64String(deck.RowVersion);

    private static ProjectBriefingDeckService Service(ApplicationDbContext db, IClock clock)
        => new(db, new NoSelection(), new NoAudit(), clock, NullLogger<ProjectBriefingDeckService>.Instance);

    private static ApplicationDbContext CreateDb()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"briefing-deck-delete-{Guid.NewGuid()}")
            .Options);
        db.Users.Add(new ApplicationUser { Id = UserId, UserName = "comdt", FullName = "Comdt" });
        db.SaveChanges();
        return db;
    }

    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
    }

    private sealed class NoAudit : IAuditService
    {
        public Task LogAsync(
            string action,
            string? message = null,
            string level = "Info",
            string? userId = null,
            string? userName = null,
            IDictionary<string, string?>? data = null,
            HttpContext? http = null)
            => Task.CompletedTask;
    }

    private sealed class NoSelection : IProjectBriefingSelectionService
    {
        public Task<ProjectBriefingSelectionOptionsVm> GetOptionsAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ProjectBriefingSelectionResult> ResolveAsync(ProjectBriefingSelectionRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ProjectBriefingSearchResultVm>> SearchAsync(string? query, int take = 25, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<int>> ValidateProjectIdsAsync(IReadOnlyCollection<int> projectIds, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<int>>(projectIds.ToArray());
    }
}
