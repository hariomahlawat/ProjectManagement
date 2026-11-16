using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Pages.Workspace.BriefingDecks;
using ProjectManagement.Services;
using ProjectManagement.Services.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings.Presentation;
using ProjectManagement.Services.Workspace;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

// The Briefing Studio's page handlers per role (docs/ux/briefing-deck-review-fixes.md, PR 7).
// ProjectBriefingDeckAccessTests covers the rule in the service; these post to the handlers the
// page uses, so a refusal must come back as a message, never a server error (500).
// A Project Officer owns their deck; a command user (Comdt/HoD) may only download or duplicate
// it; a command deck does not exist for a Project Officer.
public sealed class ProjectBriefingDeckPageHandlerTests
{
    private const string Officer = "po-1";
    private const string Comdt = "comdt-1";
    private const string Hod = "hod-1";
    private const int ProjectId = 10;

    private static readonly FixedDeckRoles Roles = new(user => user is Comdt or Hod);

    // ---- UpdateBlocks -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateBlocks_TheOwnerSaves()
    {
        await using var db = CreateDb();
        var deck = await SeedAsync(db, Officer, ProjectBriefingDeckScope.Personal);
        var page = CreatePage(db, Officer);

        var result = await page.OnPostUpdateBlocksAsync(BlocksInput(deck), CancellationToken.None);

        Assert.Equal(200, StatusOf(result));
        Assert.False((await ReloadAsync(db, deck)).IncludeCoverSlide);
    }

    [Fact]
    public async Task UpdateBlocks_ACommandUserIsRefusedWithAMessage()
    {
        await using var db = CreateDb();
        var deck = await SeedAsync(db, Officer, ProjectBriefingDeckScope.Personal);
        var page = CreatePage(db, Comdt);

        var result = await page.OnPostUpdateBlocksAsync(BlocksInput(deck), CancellationToken.None);

        Assert.Equal(400, StatusOf(result));
        Assert.Equal(ProjectBriefingDeckService.ViewOnlyMessage, MessageOf(result));
        Assert.True((await ReloadAsync(db, deck)).IncludeCoverSlide);
    }

    // ---- SaveSettings -------------------------------------------------------------------------

    [Fact]
    public async Task SaveSettings_TheOwnerSaves()
    {
        await using var db = CreateDb();
        var deck = await SeedAsync(db, Officer, ProjectBriefingDeckScope.Personal);
        var page = CreatePage(db, Officer, ajax: true);

        var result = await page.OnPostSaveSettingsAsync(SettingsInput(deck, "Renamed by owner"), CancellationToken.None);

        Assert.True(StatusOf(result) == 200, MessageOf(result));
        Assert.Equal("Renamed by owner", (await ReloadAsync(db, deck)).Name);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaveSettings_ACommandUserIsRefusedWithAMessage(bool ajax)
    {
        await using var db = CreateDb();
        var deck = await SeedAsync(db, Officer, ProjectBriefingDeckScope.Personal);
        var page = CreatePage(db, Hod, ajax);

        var result = await page.OnPostSaveSettingsAsync(SettingsInput(deck, "Renamed by HoD"), CancellationToken.None);

        // The view-only message, not "not found" (review 2 fixes, Low 1).
        if (ajax)
        {
            Assert.Equal(400, StatusOf(result));
            Assert.Equal(ProjectBriefingDeckService.ViewOnlyMessage, MessageOf(result));
        }
        else
        {
            Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal(ProjectBriefingDeckService.ViewOnlyMessage, page.ErrorMessage);
        }

        Assert.Equal("Visit deck", (await ReloadAsync(db, deck)).Name);
    }

    // ---- Generate -----------------------------------------------------------------------------

    [Fact]
    public async Task Generate_TheOwnerGetsThePowerPointAndItIsRecorded()
    {
        // Recording the generation uses ExecuteUpdate, which the in-memory provider cannot run.
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateSqliteDbAsync(connection);
        var deck = await SeedAsync(db, Officer, ProjectBriefingDeckScope.Personal);
        var page = CreatePage(db, Officer, ajax: true);

        var result = await page.OnPostGenerateAsync(deck.Id, CancellationToken.None);

        Assert.True(result is FileContentResult, MessageOf(result));
        Assert.NotNull((await ReloadAsync(db, deck)).LastGeneratedAtUtc);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Generate_ACommandUserIsRefusedWithAMessage(bool ajax)
    {
        await using var db = CreateDb();
        var deck = await SeedAsync(db, Officer, ProjectBriefingDeckScope.Personal);
        var page = CreatePage(db, Comdt, ajax);

        var result = await page.OnPostGenerateAsync(deck.Id, CancellationToken.None);

        if (ajax)
        {
            Assert.Equal(400, StatusOf(result));
            Assert.Equal(ProjectBriefingDeckService.ViewOnlyMessage, MessageOf(result));
        }
        else
        {
            Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal(ProjectBriefingDeckService.ViewOnlyMessage, page.ErrorMessage);
        }

        Assert.Null((await ReloadAsync(db, deck)).LastGeneratedAtUtc);
    }

    // ---- Photo options ------------------------------------------------------------------------

    [Fact]
    public async Task PhotoOptions_TheOwnerGetsThePhotos()
    {
        await using var db = CreateDb();
        var deck = await SeedAsync(db, Officer, ProjectBriefingDeckScope.Personal);
        var page = CreatePage(db, Officer);

        var result = await page.OnGetPhotoOptionsAsync(deck.Id, ProjectId, CancellationToken.None);

        Assert.Equal(200, StatusOf(result));
    }

    [Fact]
    public async Task PhotoOptions_ACommandUserIsRefusedWithAMessage()
    {
        await using var db = CreateDb();
        var deck = await SeedAsync(db, Officer, ProjectBriefingDeckScope.Personal);
        var page = CreatePage(db, Hod);

        var result = await page.OnGetPhotoOptionsAsync(deck.Id, ProjectId, CancellationToken.None);

        Assert.Equal(404, StatusOf(result));
        Assert.Equal(ProjectBriefingDeckService.NotFoundMessage, MessageOf(result));
    }

    // ---- Delete -------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_TheOwnerMovesTheDeckToRecentlyDeleted()
    {
        await using var db = CreateDb();
        var deck = await SeedAsync(db, Officer, ProjectBriefingDeckScope.Personal);
        var page = CreatePage(db, Officer);

        var result = await page.OnPostDeleteAsync(deck.Id, Version(deck), CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.NotNull((await ReloadAsync(db, deck)).DeletedAtUtc);
    }

    [Fact]
    public async Task Delete_ACommandUserIsRefusedWithAMessage()
    {
        await using var db = CreateDb();
        var deck = await SeedAsync(db, Officer, ProjectBriefingDeckScope.Personal);
        var page = CreatePage(db, Comdt);

        var result = await page.OnPostDeleteAsync(deck.Id, Version(deck), CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(ProjectBriefingDeckService.ViewOnlyMessage, page.ErrorMessage);
        Assert.Null((await ReloadAsync(db, deck)).DeletedAtUtc);
    }

    // ---- What a command user may still do -----------------------------------------------------

    [Fact]
    public async Task ACommandUser_CanStillDownloadAProjectOfficersDeck()
    {
        await using var db = CreateDb();
        var deck = await SeedAsync(db, Officer, ProjectBriefingDeckScope.Personal);
        var page = CreatePage(db, Comdt);

        var result = await page.OnPostDownloadAsync(deck.Id, CancellationToken.None);

        Assert.IsType<FileContentResult>(result);
        // A download is not a generation: the officer's deck is untouched.
        Assert.Null((await ReloadAsync(db, deck)).LastGeneratedAtUtc);
    }

    [Fact]
    public async Task ACommandUser_CanStillDuplicateAProjectOfficersDeck()
    {
        await using var db = CreateDb();
        var deck = await SeedAsync(db, Officer, ProjectBriefingDeckScope.Personal);
        var page = CreatePage(db, Hod);

        var result = await page.OnPostDuplicateAsync(deck.Id, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        var copyId = Assert.IsType<long>(redirect.RouteValues!["deckId"]);
        Assert.NotEqual(deck.Id, copyId);
        var copy = await db.Set<ProjectBriefingDeck>().SingleAsync(item => item.Id == copyId);
        Assert.Equal(Hod, copy.OwnerUserId);
        Assert.Equal(ProjectBriefingDeckScope.Command, copy.Scope);
    }

    // ---- A command deck, seen by a Project Officer --------------------------------------------

    // Review 2 fixes, PR 4: a content editor saved in place answers with the version its own save
    // produced, and leaves no message behind for the next page load.
    [Fact]
    public async Task AContentEditorSavedInPlace_AnswersWithItsOwnVersion()
    {
        await using var db = CreateDb();
        var deck = await SeedAsync(db, Officer, ProjectBriefingDeckScope.Personal);
        var page = CreatePage(db, Officer, ajax: true);

        var result = await page.OnPostSaveFfcGlobalFootprintAsync(new IndexModel.SaveFfcGlobalFootprintInput
        {
            DeckId = deck.Id,
            RowVersion = Version(deck),
            FfcGlobalFootprintTitle = "Friendly Foreign Countries",
            MaximumCountryRows = 8
        }, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        Assert.Equal(200, json.StatusCode ?? 200);
        var saved = await ReloadAsync(db, deck);
        Assert.Equal(Version(saved), json.Value!.GetType().GetProperty("rowVersion")!.GetValue(json.Value) as string);
        Assert.Null(page.StatusMessage);
        Assert.Null(page.ErrorMessage);
        Assert.False(page.ReopenFfcGlobalFootprint);
    }

    [Fact]
    public async Task ACommandDeck_ReadsAsNotFoundForAProjectOfficer()
    {
        await using var db = CreateDb();
        var deck = await SeedAsync(db, Hod, ProjectBriefingDeckScope.Command);

        var blocks = await CreatePage(db, Officer).OnPostUpdateBlocksAsync(BlocksInput(deck), CancellationToken.None);
        Assert.Equal(400, StatusOf(blocks));
        Assert.Equal(ProjectBriefingDeckService.NotFoundMessage, MessageOf(blocks));

        var settings = await CreatePage(db, Officer, ajax: true).OnPostSaveSettingsAsync(SettingsInput(deck, "Taken"), CancellationToken.None);
        Assert.Equal(400, StatusOf(settings));

        var generate = await CreatePage(db, Officer, ajax: true).OnPostGenerateAsync(deck.Id, CancellationToken.None);
        Assert.Equal(400, StatusOf(generate));
        Assert.Equal(ProjectBriefingDeckService.NotFoundMessage, MessageOf(generate));

        var photos = await CreatePage(db, Officer).OnGetPhotoOptionsAsync(deck.Id, ProjectId, CancellationToken.None);
        Assert.Equal(404, StatusOf(photos));

        var deletePage = CreatePage(db, Officer);
        Assert.IsType<RedirectToPageResult>(await deletePage.OnPostDeleteAsync(deck.Id, Version(deck), CancellationToken.None));
        Assert.Equal(ProjectBriefingDeckService.NotFoundMessage, deletePage.ErrorMessage);

        var downloadPage = CreatePage(db, Officer);
        Assert.IsType<RedirectToPageResult>(await downloadPage.OnPostDownloadAsync(deck.Id, CancellationToken.None));
        Assert.Equal(ProjectBriefingDeckService.NotFoundMessage, downloadPage.ErrorMessage);

        var duplicatePage = CreatePage(db, Officer);
        Assert.IsType<RedirectToPageResult>(await duplicatePage.OnPostDuplicateAsync(deck.Id, CancellationToken.None));
        Assert.Equal(ProjectBriefingDeckService.NotFoundMessage, duplicatePage.ErrorMessage);

        var unchanged = await ReloadAsync(db, deck);
        Assert.Equal("Visit deck", unchanged.Name);
        Assert.Null(unchanged.DeletedAtUtc);
        Assert.Null(unchanged.LastGeneratedAtUtc);
        Assert.Equal(1, await db.Set<ProjectBriefingDeck>().CountAsync());
    }

    // ---- Every other change, as a command user on a Project Officer's deck (review 2 fixes, PR 7)

    [Theory]
    [InlineData("AddSelection")]
    [InlineData("UpdateMembership")]
    [InlineData("Reorder")]
    [InlineData("UpdateDescription")]
    [InlineData("UpdateItemPresentation")]
    [InlineData("UpdateSpeakerNotes")]
    [InlineData("UpdateItemPhoto")]
    [InlineData("SaveInstitutionalProfile")]
    [InlineData("SaveRoleCharter")]
    [InlineData("SaveFfcGlobalFootprint")]
    public async Task EveryChange_ACommandUserIsRefusedWithTheViewOnlyMessage(string handler)
    {
        await using var db = CreateDb();
        var deck = await SeedAsync(db, Officer, ProjectBriefingDeckScope.Personal);
        var before = Version(deck);
        var page = CreatePage(db, Hod, ajax: true);

        var result = handler switch
        {
            // The selection stub throws if it is asked to resolve: the deck is checked first.
            "AddSelection" => await page.OnPostAddSelectionAsync(new IndexModel.AddSelectionInput
            {
                DeckId = deck.Id, Kind = ProjectBriefingSelectionKind.IndividualProjects, ProjectIds = new List<int> { ProjectId }, RowVersion = before
            }, CancellationToken.None),
            "UpdateMembership" => await page.OnPostUpdateMembershipAsync(new IndexModel.UpdateMembershipInput
            {
                DeckId = deck.Id, RemoveProjectIds = new List<int> { ProjectId }, RowVersion = before
            }, CancellationToken.None),
            "Reorder" => await page.OnPostReorderAsync(new IndexModel.ReorderInput
            {
                DeckId = deck.Id, ProjectIds = new List<int> { ProjectId }, RowVersion = before
            }, CancellationToken.None),
            "UpdateDescription" => await page.OnPostUpdateDescriptionAsync(new IndexModel.UpdateDescriptionInput
            {
                DeckId = deck.Id, ProjectId = ProjectId, Value = "Changed by HoD", RowVersion = before
            }, CancellationToken.None),
            "UpdateItemPresentation" => await page.OnPostUpdateItemPresentationAsync(new IndexModel.UpdateItemPresentationInput
            {
                DeckId = deck.Id, ProjectId = ProjectId, Presentation = ProjectBriefingItemPresentation.LeftOut, RowVersion = before
            }, CancellationToken.None),
            "UpdateSpeakerNotes" => await page.OnPostUpdateSpeakerNotesAsync(new IndexModel.UpdateSpeakerNotesInput
            {
                DeckId = deck.Id, ProjectId = ProjectId, Notes = "Changed by HoD", RowVersion = before
            }, CancellationToken.None),
            "UpdateItemPhoto" => await page.OnPostUpdateItemPhotoAsync(new IndexModel.UpdateItemPhotoInput
            {
                DeckId = deck.Id, ProjectId = ProjectId, PhotoId = null, RowVersion = before
            }, CancellationToken.None),
            "SaveInstitutionalProfile" => await page.OnPostSaveInstitutionalProfileAsync(new IndexModel.SaveInstitutionalProfileInput
            {
                DeckId = deck.Id, RowVersion = before, InstitutionalProfileTitle = "Changed by HoD"
            }, CancellationToken.None),
            "SaveRoleCharter" => await page.OnPostSaveRoleCharterAsync(new IndexModel.SaveRoleCharterInput
            {
                DeckId = deck.Id, RowVersion = before, RoleCharterTitle = "Changed by HoD"
            }, CancellationToken.None),
            "SaveFfcGlobalFootprint" => await page.OnPostSaveFfcGlobalFootprintAsync(new IndexModel.SaveFfcGlobalFootprintInput
            {
                DeckId = deck.Id, RowVersion = before, FfcGlobalFootprintTitle = "Changed by HoD"
            }, CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(handler))
        };

        Assert.Equal(ProjectBriefingDeckService.ViewOnlyMessage, MessageOf(result) ?? page.ErrorMessage);
        var unchanged = await ReloadAsync(db, deck);
        Assert.Equal(before, Version(unchanged));
        Assert.Single(await db.Set<ProjectBriefingDeckItem>().Where(item => item.DeckId == deck.Id).ToListAsync());
    }

    // Review 4 fixes, Low 2: an entry without a lead phrase (tab + text) keeps a "|" in its text.
    [Fact]
    public async Task ARoleCharterEntryWithoutALeadPhrase_KeepsItsWholeText()
    {
        await using var db = CreateDb();
        var deck = await SeedAsync(db, Officer, ProjectBriefingDeckScope.Personal);
        var page = CreatePage(db, Officer, ajax: true);

        var result = await page.OnPostSaveRoleCharterAsync(new IndexModel.SaveRoleCharterInput
        {
            DeckId = deck.Id,
            RowVersion = Version(deck),
            RoleCharterTitle = "Role & Charter",
            RoleCharterLayout = ProjectBriefingRoleCharterLayout.CharterOnly,
            UseSharedRoleCharterContent = false,
            CharterItemLines = "\tLiaison | coordination\nTrials\tField trials of new equipment"
        }, CancellationToken.None);

        Assert.Equal(200, StatusOf(result) ?? 200);
        var stored = ProjectBriefingDeckConfigurationCodec.Read((await ReloadAsync(db, deck)).SelectionRulesJson).RoleCharterOptions;
        Assert.Equal(
            new[] { ("", "Liaison | coordination"), ("Trials", "Field trials of new equipment") },
            stored.CharterItems.Select(item => (item.LeadPhrase, item.Text)).ToArray());
    }

    // ---- Set-up -------------------------------------------------------------------------------

    private static IndexModel CreatePage(ApplicationDbContext db, string userId, bool ajax = false)
    {
        var clock = new FixedClock();
        var deckService = new ProjectBriefingDeckService(
            db, new NoSelection(), new NoAudit(), clock, NullLogger<ProjectBriefingDeckService>.Instance, Roles);
        var dataService = new StubDataService(deckService);
        var exportService = new ProjectBriefingPowerPointExportService(
            dataService,
            deckService,
            new StubComposer(),
            new NoPhotos(),
            NullLogger<ProjectBriefingPowerPointExportService>.Instance);

        // The workspace services only feed the page's rail on GET; no handler here touches them.
        var page = new IndexModel(
            deckService,
            new NoSelection(),
            dataService,
            exportService,
            (CommandWorkspaceService)RuntimeHelpers.GetUninitializedObject(typeof(CommandWorkspaceService)),
            (ProjectOfficerWorkspaceService)RuntimeHelpers.GetUninitializedObject(typeof(ProjectOfficerWorkspaceService)),
            CreateUserManager(db),
            NullLogger<IndexModel>.Instance);

        var role = userId == Officer ? RoleNames.ProjectOfficer : userId == Hod ? RoleNames.HoD : RoleNames.Comdt;
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, userId), new Claim(ClaimTypes.Role, role) },
                "Test"))
        };
        if (ajax)
        {
            httpContext.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        }

        var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor());
        page.PageContext = new PageContext(actionContext);
        page.TempData = new TempDataDictionary(httpContext, new DictionaryTempDataProvider());
        page.Url = new StubUrlHelper(actionContext);
        return page;
    }

    private static IndexModel.UpdateBlocksInput BlocksInput(ProjectBriefingDeck deck) => new()
    {
        DeckId = deck.Id,
        RowVersion = Version(deck),
        Blocks = new List<IndexModel.UpdateBlockInput>
        {
            new() { Type = ProjectBriefingDeckBlockType.Cover, Enabled = false }
        }
    };

    private static IndexModel.SaveDeckSettingsInput SettingsInput(ProjectBriefingDeck deck, string name) => new()
    {
        DeckId = deck.Id,
        Name = name,
        Layout = deck.Layout,
        CostMode = deck.CostMode,
        NarrativeMode = deck.NarrativeMode,
        PresentationTheme = deck.PresentationTheme,
        UpdateSheetRows = ProjectBriefingUpdateSheetOptions.Default.Rows.ToList(),
        RowVersion = Version(deck)
    };

    private static int? StatusOf(IActionResult result) => result switch
    {
        JsonResult json => json.StatusCode ?? 200,
        ObjectResult value => value.StatusCode,
        _ => null
    };

    private static string? MessageOf(IActionResult result)
    {
        var value = result switch
        {
            JsonResult json => json.Value,
            ObjectResult objectResult => objectResult.Value,
            _ => null
        };
        return value?.GetType().GetProperty("message")?.GetValue(value) as string;
    }

    private static async Task<ProjectBriefingDeck> ReloadAsync(ApplicationDbContext db, ProjectBriefingDeck deck)
    {
        db.ChangeTracker.Clear();
        return await db.Set<ProjectBriefingDeck>().IgnoreQueryFilters().SingleAsync(item => item.Id == deck.Id);
    }

    private static async Task<ProjectBriefingDeck> SeedAsync(ApplicationDbContext db, string owner, ProjectBriefingDeckScope scope)
    {
        db.Projects.Add(new Project { Id = ProjectId, Name = "Project ten", CreatedByUserId = owner });
        var now = new FixedClock().UtcNow;
        var deck = new ProjectBriefingDeck
        {
            Name = "Visit deck",
            NormalizedName = "VISIT DECK",
            OwnerUserId = owner,
            Scope = scope,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = Guid.NewGuid().ToByteArray(),
            Items = new List<ProjectBriefingDeckItem>
            {
                new() { ProjectId = ProjectId, SortOrder = 10, AddedAtUtc = now }
            }
        };
        db.Add(deck);
        await db.SaveChangesAsync();
        return deck;
    }

    private static string Version(ProjectBriefingDeck deck) => Convert.ToBase64String(deck.RowVersion);

    private static ApplicationDbContext CreateDb()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"briefing-deck-handlers-{Guid.NewGuid()}")
            .Options);
        foreach (var id in new[] { Officer, Comdt, Hod })
        {
            db.Users.Add(new ApplicationUser { Id = id, UserName = id, FullName = id });
        }

        db.SaveChanges();
        return db;
    }

    private static async Task<ApplicationDbContext> CreateSqliteDbAsync(SqliteConnection connection)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options);
        await db.Database.EnsureCreatedAsync();
        foreach (var id in new[] { Officer, Comdt, Hod })
        {
            db.Users.Add(new ApplicationUser { Id = id, UserName = id, FullName = id });
        }

        await db.SaveChangesAsync();
        return db;
    }

    private static UserManager<ApplicationUser> CreateUserManager(ApplicationDbContext db) =>
        new(
            new UserStore<ApplicationUser, IdentityRole, ApplicationDbContext>(db),
            new OptionsWrapper<IdentityOptions>(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            NullLogger<UserManager<ApplicationUser>>.Instance);

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

    // The real data service needs the whole reporting stack. This one answers what the handlers
    // read and, like the real one, refuses the presentation data to anyone who may not see the
    // deck, using the deck service's access rule.
    private sealed class StubDataService : IProjectBriefingDataService
    {
        private readonly IProjectBriefingDeckService _decks;

        public StubDataService(IProjectBriefingDeckService decks)
        {
            _decks = decks;
        }

        public Task<ProjectBriefingDeckVm?> GetDeckAsync(long deckId, string requestingUserId, CancellationToken cancellationToken = default, bool includeFfcFootprintSummary = false)
            => Task.FromResult<ProjectBriefingDeckVm?>(null);

        public async Task<ProjectBriefingPresentationData> BuildPresentationDataAsync(long deckId, string requestingUserId, CancellationToken cancellationToken = default)
        {
            if (await _decks.GetAccessAsync(deckId, requestingUserId, cancellationToken) == ProjectBriefingDeckAccessLevel.None)
            {
                throw new KeyNotFoundException(ProjectBriefingDeckService.NotFoundMessage);
            }

            return new ProjectBriefingPresentationData
            {
                DeckId = deckId,
                DeckName = "Visit deck",
                PresentationMode = ProjectBriefingPresentationMode.ExecutiveTable,
                GeneratedAtUtc = new FixedClock().UtcNow
            };
        }

        public Task<IReadOnlyList<ProjectBriefingPhotoOptionVm>?> GetPhotoOptionsAsync(long deckId, int projectId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ProjectBriefingPhotoOptionVm>?>(new[]
            {
                new ProjectBriefingPhotoOptionVm { PhotoId = 1, IsDefault = true, ThumbnailUrl = "/photo/1" }
            });

        public Task<IReadOnlyDictionary<int, ProjectBriefingCurrentProjectFacts>> GetCurrentSlideFactsAsync(
            IReadOnlyCollection<int> projectIds,
            CancellationToken cancellationToken = default,
            IReadOnlyCollection<int>? milestoneProjectIds = null,
            IReadOnlyCollection<int>? bandProjectIds = null)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ProjectBriefingDeckSummaryVm>> WithDataMovementAsync(IReadOnlyList<ProjectBriefingDeckSummaryVm> decks, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubComposer : IProjectBriefingSlideComposer
    {
        public (byte[] Content, int SlideCount) Compose(ProjectBriefingPresentationData data) => (new byte[] { 1, 2, 3 }, 1);

        public ProjectBriefingComposition ComposeWithOutline(ProjectBriefingPresentationData data)
            => new(new byte[] { 1, 2, 3 }, new[]
            {
                new ProjectBriefingSlideOutlineItem(1, default, null, Array.Empty<int>())
            });
    }

    private sealed class NoPhotos : IProjectBriefingPhotoLoader
    {
        public Task<IReadOnlyDictionary<int, ProjectBriefingPhotoProbe>> ProbeAsync(IReadOnlyCollection<ProjectBriefingPhotoReference> references, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<int, ProjectBriefingPhotoProbe>>(new Dictionary<int, ProjectBriefingPhotoProbe>());

        public Task<ProjectBriefingPhotoContent?> LoadAsync(int projectId, int photoId, CancellationToken cancellationToken = default)
            => Task.FromResult<ProjectBriefingPhotoContent?>(null);
    }

    private sealed class DictionaryTempDataProvider : ITempDataProvider
    {
        private Dictionary<string, object> _values = new();

        public IDictionary<string, object> LoadTempData(HttpContext context) => _values;

        public void SaveTempData(HttpContext context, IDictionary<string, object> values) =>
            _values = new Dictionary<string, object>(values);
    }

    // Delete's Undo toast builds a Restore link; the address itself is not under test.
    private sealed class StubUrlHelper : IUrlHelper
    {
        public StubUrlHelper(ActionContext actionContext)
        {
            ActionContext = actionContext;
        }

        public ActionContext ActionContext { get; }

        public string? Action(UrlActionContext actionContext) => "/stub";

        public string? Content(string? contentPath) => contentPath;

        public bool IsLocalUrl(string? url) => true;

        public string? Link(string? routeName, object? values) => "/stub";

        public string? RouteUrl(UrlRouteContext routeContext) => "/Workspace/BriefingDecks?handler=Restore";
    }
}
