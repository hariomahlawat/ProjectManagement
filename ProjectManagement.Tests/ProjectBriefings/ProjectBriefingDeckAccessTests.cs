using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services;
using ProjectManagement.Services.ProjectBriefings;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

// Project Officer decks (docs/ux/briefing-decks.md, "Plan: Blocks, Project Officer decks and focus
// view", PR 2): command decks stay shared by Comdt and HoD; a Project Officer's deck is theirs to
// change, and command users may only look at it or duplicate it. Each path below is a server
// check, not a hidden button.
public sealed class ProjectBriefingDeckAccessTests
{
    private const string Hod = "hod-1";
    private const string Comdt = "comdt-1";
    private const string Officer = "po-1";
    private const string OtherOfficer = "po-2";

    private static readonly FixedDeckRoles Roles = new(user => user is Hod or Comdt);

    [Theory]
    [InlineData(ProjectBriefingDeckScope.Command, Hod, Hod, true, ProjectBriefingDeckAccessLevel.Edit)]
    [InlineData(ProjectBriefingDeckScope.Command, Hod, Comdt, true, ProjectBriefingDeckAccessLevel.Edit)]
    [InlineData(ProjectBriefingDeckScope.Command, Hod, Officer, false, ProjectBriefingDeckAccessLevel.None)]
    [InlineData(ProjectBriefingDeckScope.Personal, Officer, Officer, false, ProjectBriefingDeckAccessLevel.Edit)]
    [InlineData(ProjectBriefingDeckScope.Personal, Officer, OtherOfficer, false, ProjectBriefingDeckAccessLevel.None)]
    [InlineData(ProjectBriefingDeckScope.Personal, Officer, Hod, true, ProjectBriefingDeckAccessLevel.View)]
    [InlineData(ProjectBriefingDeckScope.Personal, Hod, Hod, true, ProjectBriefingDeckAccessLevel.Edit)]
    public void TheRule_DecidesEachDeck(
        ProjectBriefingDeckScope scope,
        string owner,
        string user,
        bool isCommand,
        ProjectBriefingDeckAccessLevel expected)
        => Assert.Equal(expected, ProjectBriefingDeckAccessRules.Evaluate(scope, owner, user, isCommand));

    [Fact]
    public void ThePolicy_LetsProjectOfficersIn()
    {
        Assert.Contains(RoleNames.ProjectOfficer, Policies.ProjectBriefingDecks.ManageAllowedRoles);
        Assert.Contains(RoleNames.Comdt, Policies.ProjectBriefingDecks.ManageAllowedRoles);
        Assert.Contains(RoleNames.HoD, Policies.ProjectBriefingDecks.ManageAllowedRoles);
    }

    [Fact]
    public async Task NewDecks_AreCommandForCommandUsersAndPersonalForProjectOfficers()
    {
        await using var db = CreateDb();
        var service = Service(db);

        var commandId = await service.CreateAsync(Hod, "Monthly review", null);
        var personalId = await service.CreateAsync(Officer, "My visit deck", null);

        Assert.Equal(ProjectBriefingDeckScope.Command, (await db.Set<ProjectBriefingDeck>().SingleAsync(deck => deck.Id == commandId)).Scope);
        Assert.Equal(ProjectBriefingDeckScope.Personal, (await db.Set<ProjectBriefingDeck>().SingleAsync(deck => deck.Id == personalId)).Scope);
    }

    [Fact]
    public async Task AProjectOfficer_SeesAndChangesOnlyTheirOwnDecks()
    {
        await using var db = CreateDb();
        var command = await SeedAsync(db, "Monthly review", Hod, ProjectBriefingDeckScope.Command);
        var mine = await SeedAsync(db, "My deck", Officer, ProjectBriefingDeckScope.Personal);
        var someoneElses = await SeedAsync(db, "Their deck", OtherOfficer, ProjectBriefingDeckScope.Personal);
        var service = Service(db);

        var listed = await service.ListAsync(Officer);
        Assert.Equal(new[] { mine.Id }, listed.Select(deck => deck.Id));
        Assert.True(listed.Single().CanEdit);

        Assert.NotNull(await service.GetEntityAsync(mine.Id, Officer, includeItems: false));
        Assert.Null(await service.GetEntityAsync(command.Id, Officer, includeItems: false));
        Assert.Null(await service.GetEntityAsync(someoneElses.Id, Officer, includeItems: false));
        Assert.Equal(ProjectBriefingDeckAccessLevel.None, await service.GetAccessAsync(command.Id, Officer));

        // Not even told the deck exists.
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.DeleteAsync(command.Id, Officer, Version(command)));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.UpdateItemSpeakerNotesAsync(someoneElses.Id, 10, Officer, "notes", Version(someoneElses)));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.DuplicateAsync(command.Id, Officer));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.MarkGeneratedAsync(command.Id, Officer, 5));

        await service.UpdateItemSpeakerNotesAsync(mine.Id, 10, Officer, "Say this", Version(mine));
        Assert.Equal("Say this", (await db.Set<ProjectBriefingDeckItem>().SingleAsync(item => item.DeckId == mine.Id)).SpeakerNotes);
    }

    [Fact]
    public async Task ACommandUser_SeesAProjectOfficersDeckButCannotChangeIt()
    {
        await using var db = CreateDb();
        var command = await SeedAsync(db, "Monthly review", Hod, ProjectBriefingDeckScope.Command);
        var officers = await SeedAsync(db, "Visit deck", Officer, ProjectBriefingDeckScope.Personal);
        var service = Service(db);

        var listed = await service.ListAsync(Comdt);
        Assert.Equal(2, listed.Count);
        Assert.True(listed.Single(deck => deck.Id == command.Id).CanEdit);
        var view = listed.Single(deck => deck.Id == officers.Id);
        Assert.False(view.CanEdit);
        Assert.Equal(ProjectBriefingDeckScope.Personal, view.Scope);

        Assert.Equal(ProjectBriefingDeckAccessLevel.View, await service.GetAccessAsync(officers.Id, Comdt));
        Assert.Null(await service.GetEntityAsync(officers.Id, Comdt, includeItems: false));
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UpdateItemSpeakerNotesAsync(officers.Id, 10, Comdt, "notes", Version(officers)));
        Assert.Equal(ProjectBriefingDeckService.ViewOnlyMessage, refused.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(officers.Id, Comdt, Version(officers)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.MarkGeneratedAsync(officers.Id, Comdt, 5));
        Assert.Null((await db.Set<ProjectBriefingDeck>().SingleAsync(deck => deck.Id == officers.Id)).LastGeneratedAtUtc);
    }

    [Fact]
    public async Task DuplicatingAProjectOfficersDeck_GivesTheCommandUserACommandDeck()
    {
        await using var db = CreateDb();
        var officers = await SeedAsync(db, "Visit deck", Officer, ProjectBriefingDeckScope.Personal);
        var service = Service(db);

        var copyId = await service.DuplicateAsync(officers.Id, Hod);

        var copy = await db.Set<ProjectBriefingDeck>().Include(deck => deck.Items).SingleAsync(deck => deck.Id == copyId);
        Assert.Equal(ProjectBriefingDeckScope.Command, copy.Scope);
        Assert.Equal(Hod, copy.OwnerUserId);
        Assert.Single(copy.Items);
        Assert.Equal(ProjectBriefingDeckAccessLevel.None, await service.GetAccessAsync(copyId, Officer));
    }

    [Fact]
    public async Task DeckNames_AreUniqueWithinCommandAndWithinEachOfficersOwnDecks()
    {
        await using var db = CreateDb();
        await SeedAsync(db, "Monthly review", Hod, ProjectBriefingDeckScope.Command);
        var service = Service(db);

        // A Project Officer cannot see the command deck, so its name does not block theirs.
        await service.CreateAsync(Officer, "Monthly review", null);
        await service.CreateAsync(OtherOfficer, "Monthly review", null);

        var again = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(Officer, "monthly review", null));
        Assert.Equal("You already have a deck with this name.", again.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(Comdt, "Monthly review", null));
    }

    [Fact]
    public async Task RecentlyDeleted_ListsAndRestoresOnlyDecksThePersonMayChange()
    {
        await using var db = CreateDb();
        var command = await SeedAsync(db, "Monthly review", Hod, ProjectBriefingDeckScope.Command);
        var officers = await SeedAsync(db, "Visit deck", Officer, ProjectBriefingDeckScope.Personal);
        var service = Service(db);
        await service.DeleteAsync(command.Id, Hod, Version(command));
        await service.DeleteAsync(officers.Id, Officer, Version(officers));

        Assert.Equal(new[] { officers.Id }, (await service.ListDeletedAsync(Officer)).Select(deck => deck.Id));
        Assert.Equal(new[] { command.Id }, (await service.ListDeletedAsync(Comdt)).Select(deck => deck.Id));

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.RestoreAsync(command.Id, Officer));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.RestoreAsync(officers.Id, Comdt));
        var restored = await service.RestoreAsync(officers.Id, Officer);
        Assert.Equal(officers.Id, restored.DeckId);
    }

    [Fact]
    public async Task RestoreDeletedItems_LetsAnAdministratorRecoverEveryDeck()
    {
        await using var db = CreateDb();
        db.Users.Add(new ApplicationUser { Id = "admin-1", UserName = "admin-1", FullName = "Admin" });
        await db.SaveChangesAsync();
        var command = await SeedAsync(db, "Monthly review", Hod, ProjectBriefingDeckScope.Command);
        var officers = await SeedAsync(db, "Visit deck", Officer, ProjectBriefingDeckScope.Personal);
        var service = Service(db);
        await service.DeleteAsync(command.Id, Hod, Version(command));
        await service.DeleteAsync(officers.Id, Officer, Version(officers));

        // Not command and not the owner: Briefing Studio offers nothing, the Admin page everything.
        Assert.Empty(await service.ListDeletedAsync("admin-1"));
        var listed = await service.ListDeletedAsync("admin-1", asRecoveryAdministrator: true);
        Assert.Equal(new[] { command.Id, officers.Id }.OrderBy(id => id), listed.Select(deck => deck.Id).OrderBy(id => id));

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.RestoreAsync(command.Id, "admin-1"));
        Assert.Equal(command.Id, (await service.RestoreAsync(command.Id, "admin-1", asRecoveryAdministrator: true)).DeckId);
        Assert.Equal(officers.Id, (await service.RestoreAsync(officers.Id, "admin-1", asRecoveryAdministrator: true)).DeckId);
        Assert.Equal(ProjectBriefingDeckScope.Personal, (await db.Set<ProjectBriefingDeck>().SingleAsync(deck => deck.Id == officers.Id)).Scope);
    }

    [Fact]
    public async Task TheRoleResolver_ReadsComdtAndHodFromIdentityRoles()
    {
        await using var db = CreateDb();
        db.Roles.AddRange(
            new IdentityRole(RoleNames.HoD) { Id = "role-hod" },
            new IdentityRole(RoleNames.ProjectOfficer) { Id = "role-po" });
        db.UserRoles.AddRange(
            new IdentityUserRole<string> { UserId = Hod, RoleId = "role-hod" },
            new IdentityUserRole<string> { UserId = Officer, RoleId = "role-po" });
        await db.SaveChangesAsync();
        var resolver = new ProjectBriefingDeckRoleResolver(db);

        Assert.True(await resolver.IsCommandAsync(Hod));
        Assert.False(await resolver.IsCommandAsync(Officer));
        Assert.False(await resolver.IsCommandAsync("nobody"));
    }

    private static async Task<ProjectBriefingDeck> SeedAsync(
        ApplicationDbContext db,
        string name,
        string owner,
        ProjectBriefingDeckScope scope)
    {
        if (!await db.Projects.AnyAsync())
        {
            db.Projects.Add(new Project { Id = 10, Name = "Project ten", CreatedByUserId = Hod });
        }

        var now = new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);
        var deck = new ProjectBriefingDeck
        {
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            OwnerUserId = owner,
            Scope = scope,
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

    private static ProjectBriefingDeckService Service(ApplicationDbContext db)
        => new(db, new NoSelection(), new NoAudit(), new FixedClock(), NullLogger<ProjectBriefingDeckService>.Instance, Roles);

    private static ApplicationDbContext CreateDb()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"briefing-deck-access-{Guid.NewGuid()}")
            .Options);
        foreach (var id in new[] { Hod, Comdt, Officer, OtherOfficer })
        {
            db.Users.Add(new ApplicationUser { Id = id, UserName = id, FullName = id });
        }

        db.SaveChanges();
        return db;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);
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
