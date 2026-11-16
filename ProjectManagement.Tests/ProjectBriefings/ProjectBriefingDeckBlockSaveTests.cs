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

// Saving the Deck tab's block list (PR 4 of the blocks plan): switches set the deck's settings,
// the overview order is stored, and the deck reads back in that order.
public sealed class ProjectBriefingDeckBlockSaveTests
{
    private const string UserId = "hod-1";

    [Fact]
    public async Task SwitchesSetTheDeckSettings_AndTheOverviewOrderIsKept()
    {
        await using var db = CreateDb();
        var deck = await SeedAsync(db);
        var service = Service(db);

        await service.UpdateBlocksAsync(deck.Id, UserId, new[]
        {
            Block(ProjectBriefingDeckBlockType.Cover, false),
            Block(ProjectBriefingDeckBlockType.StatusTable, true),
            Block(ProjectBriefingDeckBlockType.TechnicalCategorySummary, true),
            Block(ProjectBriefingDeckBlockType.StageSummary, false),
            Block(ProjectBriefingDeckBlockType.PortfolioSummary, true),
            Block(ProjectBriefingDeckBlockType.ProjectCategorySummary, false),
            Block(ProjectBriefingDeckBlockType.ProjectSlides, false)
        }, Version(deck));

        var saved = await db.Set<ProjectBriefingDeck>().SingleAsync();
        Assert.False(saved.IncludeCoverSlide);
        Assert.False(saved.IncludeStageSummary);
        Assert.True(saved.IncludeTechnicalCategorySummary);
        Assert.Equal(ProjectBriefingPresentationMode.ExecutiveTable, saved.PresentationMode);

        var configuration = ProjectBriefingDeckConfigurationCodec.Read(saved.SelectionRulesJson);
        Assert.Equal(
            new[]
            {
                ProjectBriefingDeckBlockType.StatusTable,
                ProjectBriefingDeckBlockType.TechnicalCategorySummary,
                ProjectBriefingDeckBlockType.StageSummary,
                ProjectBriefingDeckBlockType.PortfolioSummary,
                ProjectBriefingDeckBlockType.ProjectCategorySummary
            },
            configuration.OverviewOrder);

        var overview = ProjectBriefingDeckBlocks.ForDeck(
                saved.Layout, saved.PresentationMode, saved.IncludeCoverSlide, saved.IncludePortfolioSummarySlide,
                saved.IncludeStageSummary, saved.IncludeProjectCategorySummary, saved.IncludeTechnicalCategorySummary,
                configuration.AdditionalSlideOrder, _ => false, configuration.OverviewOrder)
            .Where(block => ProjectBriefingDeckBlocks.ZoneOf(block.Type) == ProjectBriefingDeckZone.Overview)
            .Select(block => block.Type)
            .ToArray();
        Assert.Equal(configuration.OverviewOrder, overview);
    }

    [Fact]
    public async Task ALaterSettingsSave_KeepsTheCoverAndPortfolioTheBlockListSwitchedOff()
    {
        await using var db = CreateDb();
        var deck = await SeedAsync(db);
        var service = Service(db);
        await service.UpdateBlocksAsync(deck.Id, UserId, new[]
        {
            Block(ProjectBriefingDeckBlockType.Cover, false),
            Block(ProjectBriefingDeckBlockType.PortfolioSummary, false)
        }, Version(deck));
        deck = await db.Set<ProjectBriefingDeck>().SingleAsync();
        var configuration = ProjectBriefingDeckConfigurationCodec.Read(deck.SelectionRulesJson);

        // What the page sends after a theme change: the deck's own values for what the blocks own.
        await service.UpdateSettingsAsync(deck.Id, UserId, new ProjectBriefingDeckSettingsCommand
        {
            Name = deck.Name,
            Layout = deck.Layout,
            PresentationMode = deck.PresentationMode,
            CostMode = deck.CostMode,
            NarrativeMode = deck.NarrativeMode,
            ProjectBriefLayout = configuration.StandardSlideOptions.ProjectBriefLayout,
            ShowPresentStage = configuration.StandardSlideOptions.ShowPresentStage,
            ShowPresentStatus = configuration.StandardSlideOptions.ShowPresentStatus,
            PresentationTheme = ProjectBriefingPresentationTheme.GraphiteDark,
            ClosingSlideType = configuration.ClosingSlideType,
            InstitutionalProfileOptions = configuration.InstitutionalProfileOptions,
            RoleCharterOptions = configuration.RoleCharterOptions,
            FfcGlobalFootprintOptions = configuration.FfcGlobalFootprintOptions,
            AdditionalSlideOrder = configuration.AdditionalSlideOrder,
            IncludeCoverSlide = deck.IncludeCoverSlide,
            IncludePortfolioSummarySlide = deck.IncludePortfolioSummarySlide,
            IncludeStageSummary = deck.IncludeStageSummary,
            UpdateSheetRows = configuration.UpdateSheetOptions.Rows,
            HideEmptyUpdateSheetValues = configuration.UpdateSheetOptions.HideEmptyValues,
            RowVersion = Version(deck)
        });

        var saved = await db.Set<ProjectBriefingDeck>().SingleAsync();
        Assert.Equal(ProjectBriefingPresentationTheme.GraphiteDark, saved.PresentationTheme);
        Assert.False(saved.IncludeCoverSlide);
        Assert.False(saved.IncludePortfolioSummarySlide);
    }

    [Fact]
    public async Task SavingAnInstitutionSlidesContent_KeepsWhetherTheBlockListHasItOn()
    {
        await using var db = CreateDb();
        var deck = await SeedAsync(db);
        var service = Service(db);
        await service.UpdateBlocksAsync(deck.Id, UserId, new[] { Block(ProjectBriefingDeckBlockType.RoleAndCharter, false) }, Version(deck));
        deck = await db.Set<ProjectBriefingDeck>().SingleAsync();

        // The editor still posts its own (hidden) inclusion flag; it no longer decides.
        await service.UpdateRoleCharterAsync(
            deck.Id,
            UserId,
            ProjectBriefingRoleCharterOptions.Default with { IncludeSlide = true, Title = "Our role" },
            Version(deck));

        var configuration = ProjectBriefingDeckConfigurationCodec.Read((await db.Set<ProjectBriefingDeck>().SingleAsync()).SelectionRulesJson);
        Assert.False(configuration.RoleCharterOptions.IncludeSlide);
        Assert.Equal("Our role", configuration.RoleCharterOptions.Title);
    }

    [Fact]
    public async Task ADeckNeedsTheStatusTableOrTheProjectSlides()
    {
        await using var db = CreateDb();
        var deck = await SeedAsync(db);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).UpdateBlocksAsync(deck.Id, UserId, new[]
        {
            Block(ProjectBriefingDeckBlockType.StatusTable, false),
            Block(ProjectBriefingDeckBlockType.ProjectSlides, false)
        }, Version(deck)));
        Assert.Contains("status table or the project slides", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstitutionSlides_SwitchOnAndTakeTheListsOrder()
    {
        await using var db = CreateDb();
        var deck = await SeedAsync(db);
        var service = Service(db);

        await service.UpdateBlocksAsync(deck.Id, UserId, new[]
        {
            Block(ProjectBriefingDeckBlockType.RoleAndCharter, true),
            Block(ProjectBriefingDeckBlockType.FfcGlobalFootprint, true)
        }, Version(deck));
        deck = await db.Set<ProjectBriefingDeck>().SingleAsync();
        var configuration = ProjectBriefingDeckConfigurationCodec.Read(deck.SelectionRulesJson);
        Assert.True(configuration.RoleCharterOptions.IncludeSlide);
        Assert.True(configuration.FfcGlobalFootprintOptions.IncludeSlide);
        Assert.Contains(ProjectBriefingAdditionalSlideType.RoleAndCharter, configuration.AdditionalSlideOrder);

        // Role & Charter moved above the SDD profile in the opening.
        await service.UpdateBlocksAsync(deck.Id, UserId, new[]
        {
            Block(ProjectBriefingDeckBlockType.Cover, true),
            Block(ProjectBriefingDeckBlockType.RoleAndCharter, true),
            Block(ProjectBriefingDeckBlockType.InstitutionalProfile, false)
        }, Version(deck));
        configuration = ProjectBriefingDeckConfigurationCodec.Read((await db.Set<ProjectBriefingDeck>().SingleAsync()).SelectionRulesJson);
        var opening = configuration.AdditionalSlideOrder
            .Where(type => type != ProjectBriefingAdditionalSlideType.FfcGlobalFootprint)
            .ToArray();
        Assert.Equal(ProjectBriefingAdditionalSlideType.RoleAndCharter, opening[0]);
    }

    [Fact]
    public async Task AnUnknownOrRepeatedBlock_IsRefused()
    {
        await using var db = CreateDb();
        var deck = await SeedAsync(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).UpdateBlocksAsync(deck.Id, UserId, new[]
        {
            Block(ProjectBriefingDeckBlockType.Cover, true),
            Block(ProjectBriefingDeckBlockType.Cover, false)
        }, Version(deck)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).UpdateBlocksAsync(deck.Id, UserId, new[]
        {
            Block((ProjectBriefingDeckBlockType)99, true)
        }, Version(deck)));
    }

    private static ProjectBriefingDeckBlock Block(ProjectBriefingDeckBlockType type, bool enabled) => new(type, enabled);

    private static async Task<ProjectBriefingDeck> SeedAsync(ApplicationDbContext db)
    {
        var now = new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);
        var deck = new ProjectBriefingDeck
        {
            Name = "Monthly review",
            NormalizedName = "MONTHLY REVIEW",
            OwnerUserId = UserId,
            Layout = ProjectBriefingLayout.StandardBriefing,
            PresentationMode = ProjectBriefingPresentationMode.Combined,
            IncludeCoverSlide = true,
            IncludePortfolioSummarySlide = true,
            IncludeStageSummary = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = Guid.NewGuid().ToByteArray()
        };
        db.Add(deck);
        await db.SaveChangesAsync();
        return deck;
    }

    private static string Version(ProjectBriefingDeck deck) => Convert.ToBase64String(deck.RowVersion);

    private static ProjectBriefingDeckService Service(ApplicationDbContext db)
        => new(db, new NoSelection(), new NoAudit(), new FixedClock(), NullLogger<ProjectBriefingDeckService>.Instance, FixedDeckRoles.Command);

    private static ApplicationDbContext CreateDb()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"briefing-deck-blocks-{Guid.NewGuid()}")
            .Options);
        db.Users.Add(new ApplicationUser { Id = UserId, UserName = UserId, FullName = "HoD" });
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
