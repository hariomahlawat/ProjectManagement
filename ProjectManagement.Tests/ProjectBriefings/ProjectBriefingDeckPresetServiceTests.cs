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
    public async Task TheRetiredVisitPreset_CannotBeAppliedOrCreatedFrom()
    {
        // A template is a slide design; the visit preset was only settings.
        await using var db = CreateDb();
        var deck = await SeedAsync(db, configurationJson: null);
        var service = Service(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyPresetAsync(
            deck.Id, UserId, ProjectBriefingDeckPreset.VisitOrExternalAudience, Version(deck)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            UserId, "Visit deck", description: null, preset: ProjectBriefingDeckPreset.VisitOrExternalAudience));
        Assert.Null(Configuration(deck).Preset);
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
        var invalid = ProjectBriefingInstitutionalProfileOptions.Default with { IncludeSlide = true };
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

    [Fact]
    public async Task CreatingWithoutATemplate_IsSetUpByHand()
    {
        await using var db = CreateDb();

        var deckId = await Service(db).CreateAsync(UserId, "Hand deck", description: null);

        var deck = await db.Set<ProjectBriefingDeck>().SingleAsync(item => item.Id == deckId);
        Assert.Null(Configuration(deck).Preset);
        Assert.Equal(ProjectBriefingLayout.StandardBriefing, deck.Layout);
    }

    [Fact]
    public async Task UndoingATemplate_PutsBackTheSettingsItReplaced()
    {
        await using var db = CreateDb();
        var deck = await SeedAsync(db, configurationJson: null);
        deck.CostMode = ProjectBriefingCostMode.None;
        deck.PresentationTheme = ProjectBriefingPresentationTheme.GraphiteDark;
        await db.SaveChangesAsync();
        var service = Service(db);
        var before = Configuration(deck);

        var applied = await service.ApplyPresetAsync(
            deck.Id, UserId, ProjectBriefingDeckPreset.ProjectUpdateSheets, Version(deck));
        Assert.Equal(ProjectBriefingLayout.ProjectUpdateSheet, deck.Layout);
        Assert.Equal(Version(deck), applied.Undo.AppliedRowVersion);

        await service.UndoPresetAsync(UserId, applied.Undo);

        var after = Configuration(deck);
        Assert.Equal(ProjectBriefingCostMode.None, deck.CostMode);
        Assert.Equal(ProjectBriefingPresentationTheme.GraphiteDark, deck.PresentationTheme);
        Assert.Equal(ProjectBriefingLayout.StandardBriefing, deck.Layout);
        Assert.Null(after.Preset);
        Assert.False(after.InstitutionalProfileOptions.IncludeSlide);
        Assert.Equal(before.InstitutionalProfileOptions.Modules, after.InstitutionalProfileOptions.Modules);
        Assert.Equal(before.AdditionalSlideOrder, after.AdditionalSlideOrder);
        Assert.Equal(before.ClosingSlideType, after.ClosingSlideType);
        Assert.Equal(before.UpdateSheetOptions.Rows, after.UpdateSheetOptions.Rows);
    }

    [Fact]
    public async Task UndoingATemplate_IsRefusedOnceTheDeckChangedAgain()
    {
        await using var db = CreateDb();
        var deck = await SeedAsync(db, configurationJson: null);
        var service = Service(db);
        var applied = await service.ApplyPresetAsync(
            deck.Id, UserId, ProjectBriefingDeckPreset.ProjectUpdateSheets, Version(deck));

        await service.UpdateSettingsAsync(deck.Id, UserId, RenameCommand(deck, "Changed since"));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            service.UndoPresetAsync(UserId, applied.Undo));
        Assert.Equal(ProjectBriefingLayout.ProjectUpdateSheet, deck.Layout);
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
        => new(db, new NoSelection(), new NoAudit(), new FixedClock(), NullLogger<ProjectBriefingDeckService>.Instance);

    private static ApplicationDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"briefing-deck-preset-{Guid.NewGuid()}")
            .Options);

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
