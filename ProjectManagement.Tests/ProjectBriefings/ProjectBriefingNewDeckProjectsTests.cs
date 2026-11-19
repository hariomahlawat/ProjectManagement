using System;
using System.Collections.Generic;
using System.Linq;
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

// A new deck made with its projects (docs/ux/briefing-decks.md, "Plan: The Studio is optional",
// PR 4): ticked in the New deck dialog, or the list of another deck the person may see. The deck
// and its projects are one save, so a refused list leaves no deck behind.
public sealed class ProjectBriefingNewDeckProjectsTests
{
    private const string Hod = "hod-1";
    private const string Officer = "po-1";
    private const string OtherOfficer = "po-2";

    [Fact]
    public async Task TickedProjects_AreSavedWithTheDeck_InTheOrderTicked_LeavingOutThoseThatCannotBeAdded()
    {
        await using var db = CreateDb();
        await SeedProjectsAsync(db);
        var audit = new RecordingAudit();

        var deckId = await Service(db, audit).CreateAsync(
            Hod, "December review", null, projects: new ProjectBriefingNewDeckProjects(new[] { 3, 1, 4, 3, 99 }));

        var deck = await db.Set<ProjectBriefingDeck>().Include(saved => saved.Items).SingleAsync(saved => saved.Id == deckId);
        // 4 is archived and 99 does not exist; the repeated 3 counts once.
        Assert.Equal(new[] { 3, 1 }, deck.Items.OrderBy(item => item.SortOrder).Select(item => item.ProjectId));
        Assert.Equal(new[] { 10, 20 }, deck.Items.OrderBy(item => item.SortOrder).Select(item => item.SortOrder));
        Assert.Equal(new[] { "ProjectBriefing.DeckCreated", "ProjectBriefing.ProjectsAdded" }, audit.Actions);
    }

    [Fact]
    public async Task SameAsADeck_CopiesItsProjectsInItsOrder_NotItsNotesOrPhotographs()
    {
        await using var db = CreateDb();
        await SeedProjectsAsync(db);
        var source = await SeedDeckAsync(db, Officer, ProjectBriefingDeckScope.Personal, (2, 10), (1, 20), (3, 30));
        source.Items.First(item => item.ProjectId == 2).SpeakerNotes = "Say this first.";
        await db.SaveChangesAsync();

        // A command user may see an officer's deck, so may copy its list.
        var deckId = await Service(db).CreateAsync(
            Hod, "Copy", null, projects: new ProjectBriefingNewDeckProjects(Array.Empty<int>(), source.Id));

        var deck = await db.Set<ProjectBriefingDeck>().Include(saved => saved.Items).SingleAsync(saved => saved.Id == deckId);
        Assert.Equal(new[] { 2, 1, 3 }, deck.Items.OrderBy(item => item.SortOrder).Select(item => item.ProjectId));
        Assert.All(deck.Items, item => Assert.Null(item.SpeakerNotes));
        Assert.Equal(ProjectBriefingDeckScope.Command, deck.Scope);
    }

    [Fact]
    public async Task SameAsADeckThePersonCannotSee_IsNotFound_AndNoDeckIsMade()
    {
        await using var db = CreateDb();
        await SeedProjectsAsync(db);
        var hidden = await SeedDeckAsync(db, OtherOfficer, ProjectBriefingDeckScope.Personal, (1, 10));

        await Assert.ThrowsAsync<KeyNotFoundException>(() => Service(db).CreateAsync(
            Officer, "Mine", null, projects: new ProjectBriefingNewDeckProjects(Array.Empty<int>(), hidden.Id)));

        Assert.Single(db.Set<ProjectBriefingDeck>());
    }

    [Fact]
    public async Task OnlyProjectsThatCannotBeAdded_AreRefused_AndNoDeckIsMade()
    {
        await using var db = CreateDb();
        await SeedProjectsAsync(db);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).CreateAsync(
            Hod, "Archived only", null, projects: new ProjectBriefingNewDeckProjects(new[] { 4 })));

        Assert.Contains("archived, deleted or cancelled", error.Message);
        Assert.Empty(db.Set<ProjectBriefingDeck>());
    }

    [Fact]
    public async Task MoreThanADeckHolds_IsRefused_AndNoDeckIsMade()
    {
        await using var db = CreateDb();
        db.Projects.AddRange(Enumerable.Range(1000, ProjectBriefingDeckService.MaximumProjectsPerDeck + 1)
            .Select(id => new Project { Id = id, Name = $"Bulk {id}", CreatedByUserId = Hod }));
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).CreateAsync(
            Hod, "Too many", null,
            projects: new ProjectBriefingNewDeckProjects(Enumerable.Range(1000, ProjectBriefingDeckService.MaximumProjectsPerDeck + 1).ToArray())));

        Assert.Contains($"at most {ProjectBriefingDeckService.MaximumProjectsPerDeck} projects", error.Message);
        Assert.Empty(db.Set<ProjectBriefingDeck>());
    }

    [Fact]
    public async Task WithoutProjects_TheDeckIsMadeEmpty_AsBefore()
    {
        await using var db = CreateDb();
        var audit = new RecordingAudit();

        var deckId = await Service(db, audit).CreateAsync(Hod, "Empty", null);

        Assert.Empty(db.Set<ProjectBriefingDeckItem>().Where(item => item.DeckId == deckId));
        Assert.Equal(new[] { "ProjectBriefing.DeckCreated" }, audit.Actions);
    }

    // A new deck started from a slide style (Plan: The Studio is optional, PR 5) is that style, read
    // back the way the Deck tab and the library read it.
    [Theory]
    [InlineData(ProjectBriefingProjectSlideDesign.Capability)]
    [InlineData(ProjectBriefingProjectSlideDesign.KeyPoints)]
    [InlineData(ProjectBriefingProjectSlideDesign.Brief)]
    [InlineData(ProjectBriefingProjectSlideDesign.FactSheet)]
    [InlineData(ProjectBriefingProjectSlideDesign.KeyPointsOnly)]
    public async Task ASlideStyle_StartsTheDeckAsThatStyle(ProjectBriefingProjectSlideDesign style)
    {
        await using var db = CreateDb();

        var deckId = await Service(db).CreateAsync(Hod, $"Deck {style}", null, style: style);

        var deck = await db.Set<ProjectBriefingDeck>().SingleAsync(saved => saved.Id == deckId);
        var options = ProjectBriefingDeckConfigurationCodec.Read(deck.SelectionRulesJson).StandardSlideOptions;
        Assert.Equal(style, ProjectBriefingProjectSlideDesigns.Of(deck.Layout, deck.NarrativeMode, options));
    }

    // The older choice is kept for decks that have it, but a new deck cannot start from it.
    [Fact]
    public async Task CapabilityAndBrief_IsNotOfferedForANewDeck()
    {
        await using var db = CreateDb();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).CreateAsync(
            Hod, "Old style", null, style: ProjectBriefingProjectSlideDesign.CapabilityAndBrief));
        Assert.Empty(db.Set<ProjectBriefingDeck>());
    }

    private static async Task SeedProjectsAsync(ApplicationDbContext db)
    {
        db.Projects.AddRange(
            new Project { Id = 1, Name = "Driving simulator", CreatedByUserId = Hod },
            new Project { Id = 2, Name = "Gunnery trainer", CreatedByUserId = Hod },
            new Project { Id = 3, Name = "Parachute simulator", CreatedByUserId = Hod },
            new Project { Id = 4, Name = "Archived trainer", CreatedByUserId = Hod, IsArchived = true });
        await db.SaveChangesAsync();
    }

    private static async Task<ProjectBriefingDeck> SeedDeckAsync(
        ApplicationDbContext db,
        string owner,
        ProjectBriefingDeckScope scope,
        params (int ProjectId, int SortOrder)[] items)
    {
        var now = new FixedClock().UtcNow;
        var deck = new ProjectBriefingDeck
        {
            Name = $"Source {owner}",
            NormalizedName = $"SOURCE {owner}".ToUpperInvariant(),
            OwnerUserId = owner,
            Scope = scope,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = Guid.NewGuid().ToByteArray(),
            Items = items.Select(item => new ProjectBriefingDeckItem
            {
                ProjectId = item.ProjectId,
                SortOrder = item.SortOrder,
                AddedAtUtc = now
            }).ToList()
        };
        db.Add(deck);
        await db.SaveChangesAsync();
        return deck;
    }

    private static ProjectBriefingDeckService Service(ApplicationDbContext db, IAuditService? audit = null)
        => new(
            db,
            new ProjectBriefingSelectionService(db, new FixedClock()),
            audit ?? new RecordingAudit(),
            new FixedClock(),
            NullLogger<ProjectBriefingDeckService>.Instance,
            new FixedDeckRoles(user => user == Hod));

    private static ApplicationDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"briefing-new-deck-projects-{Guid.NewGuid()}")
            .Options);

    private sealed class RecordingAudit : IAuditService
    {
        public List<string> Actions { get; } = new();

        public Task LogAsync(
            string action,
            string? message = null,
            string level = "Info",
            string? userId = null,
            string? userName = null,
            IDictionary<string, string?>? data = null,
            HttpContext? http = null)
        {
            Actions.Add(action);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);
    }
}
