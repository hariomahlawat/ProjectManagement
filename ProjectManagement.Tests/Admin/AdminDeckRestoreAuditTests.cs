using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Areas.Admin.Pages.BriefingDecks;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.ProjectBriefings;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests.Admin;

/// <summary>
/// A briefing deck restore and its audit entry commit together (docs/ux/admin-review-4-fixes.md,
/// PR 2). The audit used to be written after the commit and its failure was swallowed, so a deck
/// could come back with no record of who restored it; and when two people restored the same deck
/// at once, the second was told it "could not be restored" although it was back. The rollback
/// tests run on SQLite, which has real transactions (the in-memory provider has none).
/// </summary>
public sealed class AdminDeckRestoreAuditTests : IAsyncLifetime
{
    private const string OwnerId = "comdt-1";
    private const string AdminId = "admin-1";
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 6, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private DbContextOptions<ApplicationDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options;
        await using var db = new ApplicationDbContext(_options);
        await db.Database.EnsureCreatedAsync();
        await SeedUsersAndProjectAsync(db);
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task AdminRestore_WritesItsAuditEntryInTheSameCommit()
    {
        var deckId = await SeedDeletedDeckAsync(_options);
        await using var db = new ApplicationDbContext(_options);
        var page = CreatePage(db, new AuditService(db, new HttpContextAccessor(), FakeClock.AtUtc(Now)));

        await page.OnPostRestoreAsync(deckId, CancellationToken.None);

        await using var verify = new ApplicationDbContext(_options);
        Assert.Null((await verify.Set<ProjectBriefingDeck>().SingleAsync(deck => deck.Id == deckId)).DeletedAtUtc);
        var entry = await verify.AuditLogs.SingleAsync(log => log.Action == "ProjectBriefing.DeckRestored");
        Assert.Equal(AdminId, entry.UserId);
        Assert.Equal("“Monthly review” is back in Briefing Studio.", page.TempData[FlashMessageKeys.AdminRecoverySuccess]);
    }

    [Fact]
    public async Task AdminRestore_WhenTheAuditCannotBeWritten_RollsTheRestoreBack_AndSaysNothingChanged()
    {
        var deckId = await SeedDeletedDeckAsync(_options);
        await using var db = new ApplicationDbContext(_options);
        var page = CreatePage(db, new FailingAudit());

        var result = await page.OnPostRestoreAsync(deckId, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(
            "The deck could not be restored, and nothing was changed. Reload the page and try again.",
            page.TempData[FlashMessageKeys.AdminRecoveryError]);
        Assert.False(page.TempData.ContainsKey(FlashMessageKeys.AdminRecoverySuccess));

        await using var verify = new ApplicationDbContext(_options);
        Assert.NotNull((await verify.Set<ProjectBriefingDeck>().SingleAsync(deck => deck.Id == deckId)).DeletedAtUtc);
        Assert.Equal(0, await verify.AuditLogs.CountAsync());

        // Still in the list, so the administrator can try again.
        var listing = CreatePage(verify, new RecordingAudit());
        await listing.OnGetAsync(CancellationToken.None);
        Assert.Single(listing.Decks);
    }

    [Fact]
    public async Task StudioRestore_UsesTheSameRule_AFailedAuditLeavesTheDeckDeleted()
    {
        // The owner's own restore in Briefing Studio goes through the same service method, so it
        // follows the same rule: no restore without its audit entry.
        var deckId = await SeedDeletedDeckAsync(_options);
        await using var db = new ApplicationDbContext(_options);

        await Assert.ThrowsAsync<DbUpdateException>(() => CreateService(db, new FailingAudit()).RestoreAsync(deckId, OwnerId));

        await using var verify = new ApplicationDbContext(_options);
        Assert.NotNull((await verify.Set<ProjectBriefingDeck>().SingleAsync(deck => deck.Id == deckId)).DeletedAtUtc);
    }

    [Fact]
    public async Task TwoAdminsRestoringTheSameDeckAtOnce_TheSecondIsToldItIsAlreadyRestored()
    {
        // The in-memory provider checks the deck's row version, which is all this race needs.
        var databaseName = $"deck-restore-race-{Guid.NewGuid()}";
        var plain = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(databaseName).Options;
        await using (var seed = new ApplicationDbContext(plain))
        {
            await SeedUsersAndProjectAsync(seed);
        }

        var deckId = await SeedDeletedDeckAsync(plain);
        var firstAudit = new RecordingAudit();
        var secondAudit = new RecordingAudit();
        DeletedModel? firstPage = null;

        // The second admin has loaded the deck; just before their save, the first admin's whole
        // restore runs and commits.
        var interleave = new BeforeFirstSave(async () =>
        {
            await using var firstDb = new ApplicationDbContext(plain);
            firstPage = CreatePage(firstDb, firstAudit);
            await firstPage.OnPostRestoreAsync(deckId, CancellationToken.None);
        });
        await using var secondDb = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddInterceptors(interleave)
            .Options);
        var secondPage = CreatePage(secondDb, secondAudit);

        await secondPage.OnPostRestoreAsync(deckId, CancellationToken.None);

        Assert.Equal("“Monthly review” is back in Briefing Studio.", firstPage!.TempData[FlashMessageKeys.AdminRecoverySuccess]);
        Assert.Equal(
            "Someone else restored this deck a moment ago. It is already back in Briefing Studio.",
            secondPage.TempData[FlashMessageKeys.AdminRecoverySuccess]);
        Assert.False(secondPage.TempData.ContainsKey(FlashMessageKeys.AdminRecoveryError));

        await using var verify = new ApplicationDbContext(plain);
        Assert.Null((await verify.Set<ProjectBriefingDeck>().SingleAsync(deck => deck.Id == deckId)).DeletedAtUtc);
        Assert.Single(firstAudit.Entries);
        Assert.Empty(secondAudit.Entries);
    }

    private static DeletedModel CreatePage(ApplicationDbContext db, IAuditService audit)
    {
        var page = new DeletedModel(CreateService(db, audit), new AdminTimeService(FakeClock.AtUtc(Now)));
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

    private static ProjectBriefingDeckService CreateService(ApplicationDbContext db, IAuditService audit) =>
        new(db, new NoSelection(), audit, FakeClock.AtUtc(Now), NullLogger<ProjectBriefingDeckService>.Instance);

    private static async Task SeedUsersAndProjectAsync(ApplicationDbContext db)
    {
        db.Users.AddRange(
            new ApplicationUser { Id = OwnerId, UserName = "comdt", NormalizedUserName = "COMDT", FullName = "Comdt Sharma" },
            new ApplicationUser { Id = AdminId, UserName = "admin", NormalizedUserName = "ADMIN", FullName = "Admin Rao" });
        db.Projects.Add(new Project { Id = 10, Name = "Project ten", CreatedByUserId = OwnerId, RowVersion = new byte[] { 1 } });
        await db.SaveChangesAsync();
    }

    private static async Task<long> SeedDeletedDeckAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var db = new ApplicationDbContext(options);
        var deck = new ProjectBriefingDeck
        {
            Name = "Monthly review",
            NormalizedName = "MONTHLY REVIEW",
            OwnerUserId = OwnerId,
            CreatedAtUtc = Now.AddDays(-60),
            UpdatedAtUtc = Now.AddDays(-60),
            DeletedAtUtc = Now.AddDays(-2),
            DeletedByUserId = OwnerId,
            RowVersion = Guid.NewGuid().ToByteArray(),
            Items = new List<ProjectBriefingDeckItem>
            {
                new() { ProjectId = 10, SortOrder = 10, AddedAtUtc = Now.AddDays(-60) }
            }
        };
        db.Add(deck);
        await db.SaveChangesAsync();
        return deck.Id;
    }

    /// <summary>Runs <paramref name="action"/> once, just before this context's first save.</summary>
    private sealed class BeforeFirstSave(Func<Task> action) : SaveChangesInterceptor
    {
        private bool _ran;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!_ran)
            {
                _ran = true;
                await action();
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    /// <summary>Fails the audit write the way the real audit service does when its insert fails.</summary>
    private sealed class FailingAudit : IAuditService
    {
        public Task LogAsync(
            string action,
            string? message = null,
            string level = "Info",
            string? userId = null,
            string? userName = null,
            IDictionary<string, string?>? data = null,
            HttpContext? http = null)
            => throw new DbUpdateException("Audit store unavailable.");
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
