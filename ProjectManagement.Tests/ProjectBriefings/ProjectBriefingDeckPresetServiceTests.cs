using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services;
using ProjectManagement.Services.ProjectBriefings;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

// A preset that switches the SDD profile on must not leave the deck in a state that blocks
// later deck-tab saves (rename, theme…) the user makes without touching the profile.
public sealed class ProjectBriefingDeckPresetServiceTests
{
    private const string UserId = "user-1";

    [Fact]
    public async Task TheRetiredVisitPreset_CannotBeCreatedFrom()
    {
        // A template is a slide design; the visit preset was only settings.
        await using var db = CreateDb();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).CreateAsync(
            UserId, "Visit deck", description: null, preset: ProjectBriefingDeckPreset.VisitOrExternalAudience));
        Assert.Empty(db.Set<ProjectBriefingDeck>());
    }

    [Fact]
    public async Task ADeckAlreadyStuckByAnOlderPreset_CanStillBeRenamed()
    {
        await using var db = CreateDb();
        var stuck = ProjectBriefingInstitutionalProfileOptions.Default with { IncludeSlide = true };
        var deck = await SeedAsync(
            db,
            ProjectBriefingDeckConfigurationCodec.WithInstitutionalProfileOptions(null, stuck));

        await Service(db).UpdateSettingsAsync(deck.Id, UserId, RenameCommand(deck, "Renamed deck"));

        Assert.Equal("Renamed deck", deck.Name);
    }

    [Fact]
    public async Task ChangingTheProfileToAnInvalidOne_IsStillRefused()
    {
        await using var db = CreateDb();
        var deck = await SeedAsync(db, configurationJson: null);
        // Partnerships ticked with no partner entries.
        var invalid = ProjectBriefingInstitutionalProfileOptions.Default with
        {
            IncludeSlide = true,
            Modules = ProjectBriefingInstitutionalProfileOptions.AllModules
        };
        var command = RenameCommand(deck, "Same name", invalid);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(db).UpdateSettingsAsync(deck.Id, UserId, command));

        Assert.Contains("MoU or partner", error.Message);
    }

    [Fact]
    public void PreparingAProfileWithOnlyPartnerships_KeepsTheHistoryTimeline()
    {
        var onlyPartnerships = ProjectBriefingInstitutionalProfileOptions.Default with
        {
            IncludeHistory = false,
            Modules = new[] { ProjectBriefingInstitutionalProfileModule.Partnerships }
        };

        var prepared = ProjectBriefingDeckService.PrepareProfileForPreset(onlyPartnerships);

        Assert.True(prepared.IncludeSlide);
        Assert.Empty(prepared.Modules);
        Assert.True(prepared.IncludeHistory);
    }

    [Fact]
    public async Task CreatingWithATemplate_StartsTheDeckFromIt()
    {
        await using var db = CreateDb();

        var deckId = await Service(db).CreateAsync(
            UserId, "Sheets deck", description: null, preset: ProjectBriefingDeckPreset.ProjectUpdateSheets);

        var deck = await db.Set<ProjectBriefingDeck>().SingleAsync(item => item.Id == deckId);
        Assert.Equal(ProjectBriefingLayout.ProjectUpdateSheet, deck.Layout);
        Assert.Equal(ProjectBriefingDeckPreset.ProjectUpdateSheets, Configuration(deck).Preset);
    }

    // Review fixes PR 8: the template goes on before the deck's one save, so creating a deck records
    // one change (created, from the template) and never leaves a deck with default settings behind.
    [Fact]
    public async Task CreatingWithATemplate_IsOneSave_WithOneAuditEntry()
    {
        await using var db = CreateDb();
        var audit = new RecordingAudit();
        var service = new ProjectBriefingDeckService(db, new NoSelection(), audit, new FixedClock(), NullLogger<ProjectBriefingDeckService>.Instance, ProjectManagement.Tests.ProjectBriefings.FixedDeckRoles.Command);

        var deckId = await service.CreateAsync(
            UserId, "Monthly", description: null, preset: ProjectBriefingDeckPreset.ProjectUpdateSheets);

        var deck = await db.Set<ProjectBriefingDeck>().SingleAsync(item => item.Id == deckId);
        Assert.Equal(ProjectBriefingLayout.ProjectUpdateSheet, deck.Layout);
        var entry = Assert.Single(audit.Actions);
        Assert.Equal("ProjectBriefing.DeckCreated", entry);
    }

    [Fact]
    public async Task CreatingWithoutATemplate_IsSetUpByHand()
    {
        await using var db = CreateDb();

        var deckId = await Service(db).CreateAsync(UserId, "Hand deck", description: null);

        var deck = await db.Set<ProjectBriefingDeck>().SingleAsync(item => item.Id == deckId);
        Assert.Null(Configuration(deck).Preset);
        Assert.Equal(ProjectBriefingLayout.StandardBriefing, deck.Layout);
    }

    private static ProjectBriefingDeckSettingsCommand RenameCommand(
        ProjectBriefingDeck deck,
        string name,
        ProjectBriefingInstitutionalProfileOptions? profile = null)
    {
        // Mirrors the deck-tab save: everything except the edited fields comes from the stored deck.
        var configuration = Configuration(deck);
        return new ProjectBriefingDeckSettingsCommand
        {
            Name = name,
            Layout = deck.Layout,
            PresentationMode = deck.PresentationMode,
            CostMode = deck.CostMode,
            NarrativeMode = deck.NarrativeMode,
            ProjectBriefLayout = configuration.StandardSlideOptions.ProjectBriefLayout,
            ShowPresentStage = configuration.StandardSlideOptions.ShowPresentStage,
            ShowPresentStatus = configuration.StandardSlideOptions.ShowPresentStatus,
            PresentationTheme = deck.PresentationTheme,
            ClosingSlideType = configuration.ClosingSlideType,
            InstitutionalProfileOptions = profile ?? configuration.InstitutionalProfileOptions,
            RoleCharterOptions = configuration.RoleCharterOptions,
            FfcGlobalFootprintOptions = configuration.FfcGlobalFootprintOptions,
            AdditionalSlideOrder = configuration.AdditionalSlideOrder,
            UpdateSheetRows = configuration.UpdateSheetOptions.Rows,
            HideEmptyUpdateSheetValues = configuration.UpdateSheetOptions.HideEmptyValues,
            RowVersion = Version(deck)
        };
    }

    private static ProjectBriefingDeckConfiguration Configuration(ProjectBriefingDeck deck)
        => ProjectBriefingDeckConfigurationCodec.Read(deck.SelectionRulesJson);

    private static string Version(ProjectBriefingDeck deck) => Convert.ToBase64String(deck.RowVersion);

    private static async Task<ProjectBriefingDeck> SeedAsync(ApplicationDbContext db, string? configurationJson)
    {
        var now = DateTimeOffset.UtcNow;
        var deck = new ProjectBriefingDeck
        {
            Name = "Preset deck",
            NormalizedName = "PRESET DECK",
            OwnerUserId = UserId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = Guid.NewGuid().ToByteArray(),
            SelectionRulesJson = configurationJson
        };
        db.Add(deck);
        await db.SaveChangesAsync();
        return deck;
    }

    private static ProjectBriefingDeckService Service(ApplicationDbContext db)
        => new(db, new NoSelection(), new NoAudit(), new FixedClock(), NullLogger<ProjectBriefingDeckService>.Instance, ProjectManagement.Tests.ProjectBriefings.FixedDeckRoles.Command);

    private static ApplicationDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"briefing-deck-preset-{Guid.NewGuid()}")
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

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
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
            => throw new NotSupportedException();
    }
}
