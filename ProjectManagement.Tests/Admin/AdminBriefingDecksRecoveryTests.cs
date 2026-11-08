using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Areas.Admin.Pages.BriefingDecks;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.Recovery;
using ProjectManagement.Services.Navigation.ModuleNav;
using ProjectManagement.Services.ProjectBriefings;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests.Admin;

// Restore deleted items › Briefing decks (docs/ux/admin-review-3-fixes.md, PR 15): deleted decks
// are a kind in Restore deleted items, restored through the briefing-deck service, under the
// Restore deleted items policy.
public sealed class AdminBriefingDecksRecoveryTests
{
    private const string OwnerId = "comdt-1";
    private const string AdminId = "admin-1";
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Listing_ShowsDecksThatCanStillBeRestored_WithTheirOwner_AndLeavesOutDecksPastTheWindow()
    {
        await using var db = CreateDb();
        await SeedDeckAsync(db, "Monthly review", deletedAt: Now.AddDays(-2));
        await SeedDeckAsync(db, "Last month's review", deletedAt: Now.AddDays(-(ProjectBriefingDeckService.DeletedRetentionDays + 1)));
        await SeedDeckAsync(db, "Live deck", deletedAt: null);
        var page = CreatePage(db, out _);

        var result = await page.OnGetAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        var deck = Assert.Single(page.Decks);
        Assert.Equal("Monthly review", deck.Name);
        Assert.Equal("Comdt Sharma", deck.OwnerDisplay);
        Assert.Equal(1, deck.ProjectCount);
        Assert.Equal(Now.AddDays(-2).AddDays(ProjectBriefingDeckService.DeletedRetentionDays), deck.RestorableUntilUtc);
        Assert.Contains($"{ProjectBriefingDeckService.DeletedRetentionDays} days", page.Header.Description);
    }

    [Fact]
    public async Task TheKindsCount_MatchesTheList_AndHomeAddsItIn()
    {
        await using var db = CreateDb();
        await SeedDeckAsync(db, "Monthly review", deletedAt: Now.AddDays(-2));
        await SeedDeckAsync(db, "Weekly review", deletedAt: Now.AddDays(-(ProjectBriefingDeckService.DeletedRetentionDays - 1)));
        await SeedDeckAsync(db, "Gone for good", deletedAt: Now.AddDays(-(ProjectBriefingDeckService.DeletedRetentionDays + 1)));
        await SeedDeckAsync(db, "Live deck", deletedAt: null);

        var tabs = await AdminRestoreTabsTests.CreateService(
                db,
                AdminNavigationCatalog.Entries.Select(entry => entry.Key).ToArray(),
                FakeClock.AtUtc(Now))
            .GetAsync(new ClaimsPrincipal(new ClaimsIdentity()));

        var tab = Assert.Single(tabs, item => item.Key == AdminRestoreTabKeys.BriefingDecks);
        Assert.Equal("Briefing decks", tab.Label);
        Assert.Equal(2, tab.Count);
        Assert.False(tab.OpensAnotherPage);
        Assert.Equal(AdminNavigationKeys.DeletedBriefingDecks, tab.NavigationKey);

        var page = CreatePage(db, out _);
        await page.OnGetAsync(CancellationToken.None);
        Assert.Equal(tab.Count, page.Decks.Count);
    }

    [Fact]
    public void ThePageAndItsRestore_UseTheRestoreDeletedItemsPolicy_WhichOnlyAdministratorsHold()
    {
        var attribute = typeof(DeletedModel).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(attribute);
        Assert.Equal(AdminPolicies.RecoveryManage, attribute!.Policy);
        // The Restore handler is on the same page model, so the page's policy covers it; no
        // handler opts out with [AllowAnonymous].
        Assert.Null(typeof(DeletedModel).GetMethod(nameof(DeletedModel.OnPostRestoreAsync))!
            .GetCustomAttribute<AllowAnonymousAttribute>());

        var entry = AdminNavigationCatalog.GetEntry(AdminNavigationKeys.DeletedBriefingDecks);
        Assert.Equal(AdminPolicies.RecoveryManage, AdminNavigationCatalog.Get(AdminNavigationKeys.DeletedBriefingDecks).AuthorizationPolicy);
        Assert.Equal(AdminNavigationKeys.RecoveryCentre, entry.SidebarParent);
    }

    [Theory]
    [InlineData(RoleNames.Admin, true)]
    [InlineData(RoleNames.HoD, false)]
    [InlineData(RoleNames.Comdt, false)]
    [InlineData(RoleNames.ProjectOfficer, false)]
    public async Task OnlyAdministrators_PassTheRestoreDeletedItemsPolicy(string role, bool allowed)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(AdminCapabilityCatalog.RegisterPolicies);
        await using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "someone"), new Claim(ClaimTypes.Role, role) },
            "Test"));

        var result = await authorization.AuthorizeAsync(user, null, AdminPolicies.RecoveryManage);

        Assert.Equal(allowed, result.Succeeded);
    }

    [Fact]
    public async Task Restoring_AnotherPersonsDeck_BringsItBack_AndWritesTheServicesAuditEntry()
    {
        await using var db = CreateDb();
        var deck = await SeedDeckAsync(db, "Monthly review", deletedAt: Now.AddDays(-3));
        var page = CreatePage(db, out var audit);

        var result = await page.OnPostRestoreAsync(deck.Id, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        var restored = await db.Set<ProjectBriefingDeck>().AsNoTracking().SingleAsync(item => item.Id == deck.Id);
        Assert.Null(restored.DeletedAtUtc);
        Assert.Equal(OwnerId, restored.OwnerUserId);
        Assert.Equal("“Monthly review” is back in Briefing Studio.", page.TempData[FlashMessageKeys.AdminRecoverySuccess]);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("ProjectBriefing.DeckRestored", entry.Action);
        Assert.Equal(AdminId, entry.UserId);
        Assert.Equal(deck.Id.ToString(), entry.Data!["DeckId"]);
    }

    [Fact]
    public async Task Restoring_ADeckPastTheWindow_RestoresNothing_AndSaysWhy()
    {
        await using var db = CreateDb();
        var deck = await SeedDeckAsync(db, "Old review", deletedAt: Now.AddDays(-(ProjectBriefingDeckService.DeletedRetentionDays + 1)));
        var page = CreatePage(db, out var audit);

        await page.OnPostRestoreAsync(deck.Id, CancellationToken.None);

        Assert.NotNull(await db.Set<ProjectBriefingDeck>().AsNoTracking().Where(item => item.Id == deck.Id).Select(item => item.DeletedAtUtc).SingleAsync());
        Assert.Empty(audit.Entries);
        Assert.Contains("no longer in the list", (string)page.TempData[FlashMessageKeys.AdminRecoveryError]!);
    }

    [Fact]
    public async Task Restoring_KeepsTheServicesNameRule()
    {
        await using var db = CreateDb();
        var deck = await SeedDeckAsync(db, "Monthly review", deletedAt: Now.AddDays(-1));
        await SeedDeckAsync(db, "Monthly review", deletedAt: null);
        var page = CreatePage(db, out _);

        await page.OnPostRestoreAsync(deck.Id, CancellationToken.None);

        var restored = await db.Set<ProjectBriefingDeck>().AsNoTracking().SingleAsync(item => item.Id == deck.Id);
        Assert.Equal("Monthly review (restored)", restored.Name);
        Assert.Contains("new name", (string)page.TempData[FlashMessageKeys.AdminRecoverySuccess]!);
    }

    private static DeletedModel CreatePage(ApplicationDbContext db, out RecordingAudit audit)
    {
        audit = new RecordingAudit();
        var clock = FakeClock.AtUtc(Now);
        var service = new ProjectBriefingDeckService(db, new NoSelection(), audit, clock, NullLogger<ProjectBriefingDeckService>.Instance);
        var page = new DeletedModel(service, new AdminTimeService(clock));
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, AdminId), new Claim(ClaimTypes.Role, RoleNames.Admin) },
                "Test"))
        };
        page.PageContext = new PageContext(new ActionContext(httpContext, new RouteData(), new PageActionDescriptor()));
        page.TempData = new TempDataDictionary(httpContext, new DictionaryTempDataProvider());
        return page;
    }

    private static async Task<ProjectBriefingDeck> SeedDeckAsync(ApplicationDbContext db, string name, DateTimeOffset? deletedAt)
    {
        if (!await db.Projects.AnyAsync())
        {
            db.Projects.Add(new Project { Id = 10, Name = "Project ten", CreatedByUserId = OwnerId });
        }

        var deck = new ProjectBriefingDeck
        {
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            OwnerUserId = OwnerId,
            CreatedAtUtc = Now.AddDays(-60),
            UpdatedAtUtc = Now.AddDays(-60),
            DeletedAtUtc = deletedAt,
            DeletedByUserId = deletedAt is null ? null : OwnerId,
            RowVersion = Guid.NewGuid().ToByteArray(),
            Items = new List<ProjectBriefingDeckItem>
            {
                new() { ProjectId = 10, SortOrder = 10, AddedAtUtc = Now.AddDays(-60) }
            }
        };
        db.Add(deck);
        await db.SaveChangesAsync();
        return deck;
    }

    private static ApplicationDbContext CreateDb()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"admin-deck-recovery-{Guid.NewGuid()}")
            .Options);
        db.Users.AddRange(
            new ApplicationUser { Id = OwnerId, UserName = "comdt", FullName = "Comdt Sharma" },
            new ApplicationUser { Id = AdminId, UserName = "admin", FullName = "Admin Rao" });
        db.SaveChanges();
        return db;
    }

    private sealed record AuditEntry(string Action, string? UserId, IDictionary<string, string?>? Data);

    private sealed class RecordingAudit : IAuditService
    {
        public List<AuditEntry> Entries { get; } = new();

        public Task LogAsync(
            string action,
            string? message = null,
            string level = "Info",
            string? userId = null,
            string? userName = null,
            IDictionary<string, string?>? data = null,
            HttpContext? http = null)
        {
            Entries.Add(new AuditEntry(action, userId, data));
            return Task.CompletedTask;
        }
    }

    private sealed class DictionaryTempDataProvider : ITempDataProvider
    {
        private Dictionary<string, object> _values = new();

        public IDictionary<string, object> LoadTempData(HttpContext context) => _values;

        public void SaveTempData(HttpContext context, IDictionary<string, object> values) =>
            _values = new Dictionary<string, object>(values);
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
