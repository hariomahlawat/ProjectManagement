using System;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.DocumentRepository.Pages.Admin.RestoredHidden;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Services.Navigation.ModuleNav;
using ProjectManagement.Tests.Fakes;
using Xunit;
using static ProjectManagement.Tests.DocRepo.DocRepoTestSupport;

namespace ProjectManagement.Tests.DocRepo;

/// <summary>
/// Admin fix 16 (docs/ux/admin-review-3-fixes.md): documents restored from the trash before
/// second-review PR 2 kept IsActive = false. They are listed for review, never changed
/// automatically, and reactivated one at a time or all together.
/// </summary>
public sealed class DocRepoRestoredHiddenServiceTests
{
    private const string AdminId = "admin-user";
    private const string RestorerId = "restorer";
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RestoredAt = new(2026, 10, 20, 6, 30, 0, TimeSpan.Zero);
    private static ClaimsPrincipal Admin => User(AdminId, RoleNames.Admin);

    // SECTION: Which documents are listed
    [Fact]
    public async Task ListAsync_ShowsOnlyRestoredDocumentsThatAreStillHidden()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        db.Users.Add(new ApplicationUser { Id = RestorerId, UserName = "restorer", FullName = "Asha Verma" });

        // Restored before PR 2: out of the trash but still inactive.
        var hidden = SeedDocument(db, "Supply order", isActive: false);
        Audit(db, hidden, "SoftDeleted", RestoredAt.AddDays(-3));
        Audit(db, hidden, DocRepoRestoredHiddenService.RestoredEvent, RestoredAt, RestorerId);

        // Restored since PR 2: visible already.
        var visible = SeedDocument(db, "Visible letter", isActive: true);
        Audit(db, visible, DocRepoRestoredHiddenService.RestoredEvent, RestoredAt);

        // Made inactive on purpose and never deleted or restored.
        var deliberate = SeedDocument(db, "Deliberately hidden", isActive: false);
        Audit(db, deliberate, DocRepoRestoredHiddenService.DeactivatedEvent, RestoredAt);

        // Restored, then hidden again on purpose.
        var hiddenAgain = SeedDocument(db, "Hidden again", isActive: false);
        Audit(db, hiddenAgain, DocRepoRestoredHiddenService.RestoredEvent, RestoredAt);
        Audit(db, hiddenAgain, DocRepoRestoredHiddenService.DeactivatedEvent, RestoredAt.AddDays(1));

        // Restored, then deleted again: it is in the trash, not on this list.
        var trashedAgain = SeedDocument(db, "Trashed again", isActive: false, isDeleted: true);
        Audit(db, trashedAgain, DocRepoRestoredHiddenService.RestoredEvent, RestoredAt);
        await db.SaveChangesAsync();

        var rows = (await CreateService(db).ListAsync(Admin)).Items;

        var row = Assert.Single(rows);
        Assert.Equal(hidden.Id, row.Id);
        Assert.Equal("Supply order", row.Subject);
        Assert.Equal("CPDS North", row.Office);
        Assert.Equal("Letter", row.Category);
        Assert.Equal(RestoredAt, row.RestoredAtUtc);
        Assert.Equal("Asha Verma", row.RestoredBy);
        Assert.Null(row.DeactivatedBeforeAtUtc);
        Assert.Equal(1, await CreateService(db).CountAsync());
    }

    [Fact]
    public async Task ListAsync_MarksADocumentThatWasMadeInactiveBeforeItWasDeleted()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        var wasHidden = SeedDocument(db, "Hidden before deletion", isActive: false);
        Audit(db, wasHidden, DocRepoRestoredHiddenService.DeactivatedEvent, RestoredAt.AddDays(-10), "hod-user");
        Audit(db, wasHidden, "SoftDeleted", RestoredAt.AddDays(-5));
        Audit(db, wasHidden, DocRepoRestoredHiddenService.RestoredEvent, RestoredAt);

        // Deactivated once but switched back on before it was deleted: not marked.
        var switchedBack = SeedDocument(db, "Switched back on", isActive: false);
        Audit(db, switchedBack, DocRepoRestoredHiddenService.DeactivatedEvent, RestoredAt.AddDays(-10));
        Audit(db, switchedBack, DocRepoRestoredHiddenService.ActivatedEvent, RestoredAt.AddDays(-9));
        Audit(db, switchedBack, DocRepoRestoredHiddenService.RestoredEvent, RestoredAt.AddMinutes(-1));
        await db.SaveChangesAsync();

        var rows = (await CreateService(db).ListAsync(Admin)).Items;

        Assert.Equal(2, rows.Count);
        var marked = rows.Single(r => r.Id == wasHidden.Id);
        Assert.Equal(RestoredAt.AddDays(-10), marked.DeactivatedBeforeAtUtc);
        Assert.Equal("hod-user", marked.DeactivatedBeforeBy); // No account by that id: the id is shown.
        Assert.Null(rows.Single(r => r.Id == switchedBack.Id).DeactivatedBeforeAtUtc);
        // Newest restore first.
        Assert.Equal(wasHidden.Id, rows[0].Id);
    }

    // SECTION: Reactivate
    [Fact]
    public async Task ReactivateAsync_ShowsTheDocumentAgainAndRecordsWhoDidIt()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        var hidden = SeedDocument(db, "Supply order", isActive: false);
        Audit(db, hidden, DocRepoRestoredHiddenService.RestoredEvent, RestoredAt, RestorerId);
        await db.SaveChangesAsync();
        var before = await db.DocRepoAudits.CountAsync();

        var outcome = await CreateService(db).ReactivateAsync(Admin, hidden.Id);

        Assert.Equal(DocRepoReactivateOutcome.Reactivated, outcome);
        var saved = await db.Documents.AsNoTracking().SingleAsync(d => d.Id == hidden.Id);
        Assert.True(saved.IsActive);
        Assert.False(saved.IsDeleted);
        Assert.Equal(AdminId, saved.UpdatedByUserId);
        Assert.Equal(Now.UtcDateTime, saved.UpdatedAtUtc);

        // Readers can see it again; search reindexes through the Documents trigger on save.
        var access = Access();
        Assert.Contains(hidden.Id, await access.Readable(db.Documents.AsNoTracking(), canViewInactive: false).Select(d => d.Id).ToListAsync());

        Assert.Equal(before + 1, await db.DocRepoAudits.CountAsync());
        var audit = await db.DocRepoAudits.AsNoTracking().OrderByDescending(a => a.Id).FirstAsync();
        Assert.Equal(DocRepoRestoredHiddenService.ActivatedEvent, audit.EventType);
        Assert.Equal(hidden.Id, audit.DocumentId);
        Assert.Equal(AdminId, audit.ActorUserId);
        using var details = JsonDocument.Parse(audit.DetailsJson);
        Assert.Equal(DocRepoRestoredHiddenService.AuditSource, details.RootElement.GetProperty("source").GetString());

        // Off the list now, and a repeat changes nothing.
        Assert.Empty((await CreateService(db).ListAsync(Admin)).Items);
        Assert.Equal(DocRepoReactivateOutcome.NotListed, await CreateService(db).ReactivateAsync(Admin, hidden.Id));
        Assert.Equal(before + 1, await db.DocRepoAudits.CountAsync());
    }

    [Fact]
    public async Task ReactivateAsync_LeavesADeliberatelyInactiveDocumentAlone()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        var deliberate = SeedDocument(db, "Deliberately hidden", isActive: false);
        Audit(db, deliberate, DocRepoRestoredHiddenService.DeactivatedEvent, RestoredAt);
        var hiddenAgain = SeedDocument(db, "Hidden again", isActive: false);
        Audit(db, hiddenAgain, DocRepoRestoredHiddenService.RestoredEvent, RestoredAt);
        Audit(db, hiddenAgain, DocRepoRestoredHiddenService.DeactivatedEvent, RestoredAt.AddDays(1));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        Assert.Equal(DocRepoReactivateOutcome.NotListed, await service.ReactivateAsync(Admin, deliberate.Id));
        Assert.Equal(DocRepoReactivateOutcome.NotListed, await service.ReactivateAsync(Admin, hiddenAgain.Id));
        Assert.Equal(DocRepoReactivateOutcome.NotListed, await service.ReactivateAsync(Admin, Guid.NewGuid()));
        Assert.False(await db.Documents.AsNoTracking().AnyAsync(d => d.IsActive));
        Assert.DoesNotContain(await db.DocRepoAudits.AsNoTracking().ToListAsync(), a => a.EventType == DocRepoRestoredHiddenService.ActivatedEvent);
    }

    [Fact]
    public async Task ReactivateAsync_RechecksTheList_SoADeactivationAfterThePageOpenedWins()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        var hidden = SeedDocument(db, "Supply order", isActive: false);
        Audit(db, hidden, DocRepoRestoredHiddenService.RestoredEvent, RestoredAt);
        await db.SaveChangesAsync();
        Assert.Single((await CreateService(db).ListAsync(Admin)).Items);

        // After the page opened, another administrator deactivated it on purpose.
        Audit(db, hidden, DocRepoRestoredHiddenService.DeactivatedEvent, Now.AddMinutes(-1), "other-admin");
        await db.SaveChangesAsync();

        Assert.Equal(DocRepoReactivateOutcome.NotListed, await CreateService(db).ReactivateAsync(Admin, hidden.Id));
        Assert.False((await db.Documents.AsNoTracking().SingleAsync()).IsActive);
        // The row lock that makes this hold under concurrency is covered on PostgreSQL by
        // DocRepoRestoredHiddenPostgresIntegrationTests.
    }

    [Fact]
    public async Task ReactivateManyAsync_ChangesOnlyListedDocumentsAndSavesThemTogether()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        var first = SeedDocument(db, "First", isActive: false);
        Audit(db, first, DocRepoRestoredHiddenService.RestoredEvent, RestoredAt);
        var second = SeedDocument(db, "Second", isActive: false);
        Audit(db, second, DocRepoRestoredHiddenService.RestoredEvent, RestoredAt);
        var deliberate = SeedDocument(db, "Deliberately hidden", isActive: false);
        Audit(db, deliberate, DocRepoRestoredHiddenService.DeactivatedEvent, RestoredAt);
        await db.SaveChangesAsync();

        var result = await CreateService(db).ReactivateManyAsync(Admin, new[] { first.Id, second.Id, deliberate.Id, first.Id });

        Assert.Equal(new DocRepoReactivateManyResult(2, 1), result);
        var active = await db.Documents.AsNoTracking().Where(d => d.IsActive).Select(d => d.Id).ToListAsync();
        Assert.Equal(new[] { first.Id, second.Id }.OrderBy(id => id), active.OrderBy(id => id));
        Assert.Equal(2, await db.DocRepoAudits.CountAsync(a => a.EventType == DocRepoRestoredHiddenService.ActivatedEvent));
    }

    [Fact]
    public async Task ALongList_IsShownAndReactivatedInBoundedBatches()
    {
        // "Reactivate all shown" posts one value per row plus the antiforgery token; ASP.NET
        // Core refuses a form with more than 1,024 values by default.
        Assert.True(DocRepoRestoredHiddenService.MaxShown + 1 < 1024);

        await using var db = CreateDb();
        SeedLookups(db);
        var total = DocRepoRestoredHiddenService.MaxShown + 5;
        var documents = Enumerable.Range(0, total)
            .Select(i =>
            {
                var document = SeedDocument(db, $"Document {i:000}", isActive: false);
                Audit(db, document, DocRepoRestoredHiddenService.RestoredEvent, RestoredAt.AddMinutes(i));
                return document;
            })
            .ToList();
        await db.SaveChangesAsync();

        var list = await CreateService(db).ListAsync(Admin);

        Assert.Equal(total, list.TotalCount);
        Assert.Equal(DocRepoRestoredHiddenService.MaxShown, list.Items.Count);
        Assert.True(list.IsCapped);
        // The most recently restored are shown first; the oldest five wait.
        Assert.Equal(documents[^1].Id, list.Items[0].Id);
        Assert.DoesNotContain(list.Items, row => documents.Take(5).Any(d => d.Id == row.Id));

        // A request for more than the bound changes at most the bound.
        var result = await CreateService(db).ReactivateManyAsync(Admin, documents.Select(d => d.Id).ToList());
        Assert.Equal(DocRepoRestoredHiddenService.MaxShown, result.Reactivated);
        var next = await CreateService(db).ListAsync(Admin);
        Assert.Equal(5, next.TotalCount);
        Assert.False(next.IsCapped);
    }

    // SECTION: Policy
    [Fact]
    public async Task WithoutTheRepositoryTrashPolicy_NothingIsListedOrChanged()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        var hidden = SeedDocument(db, "Supply order", isActive: false);
        Audit(db, hidden, DocRepoRestoredHiddenService.RestoredEvent, RestoredAt);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        // A HoD may approve deletions and see inactive documents, but not manage the trash.
        foreach (var user in new[] { Manager, Reader })
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListAsync(user));
            Assert.Equal(DocRepoReactivateOutcome.Forbidden, await service.ReactivateAsync(user, hidden.Id));
            Assert.True((await service.ReactivateManyAsync(user, new[] { hidden.Id })).Forbidden);
            Assert.False(await service.CanManageAsync(user));
        }

        Assert.False((await db.Documents.AsNoTracking().SingleAsync()).IsActive);
        Assert.DoesNotContain(await db.DocRepoAudits.AsNoTracking().ToListAsync(), a => a.EventType == DocRepoRestoredHiddenService.ActivatedEvent);
    }

    [Fact]
    public async Task Page_UsesTheRepositoryTrashPolicyAndRefusesAnyoneElse()
    {
        var attribute = typeof(IndexModel).GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal(Policies.Documents.Purge, attribute!.Policy);
        Assert.Equal(
            typeof(ProjectManagement.Areas.DocumentRepository.Pages.Admin.Trash.IndexModel).GetCustomAttribute<AuthorizeAttribute>()!.Policy,
            attribute.Policy);

        await using var db = CreateDb();
        SeedLookups(db);
        var hidden = SeedDocument(db, "Supply order", isActive: false);
        Audit(db, hidden, DocRepoRestoredHiddenService.RestoredEvent, RestoredAt);
        await db.SaveChangesAsync();

        var page = new IndexModel(CreateService(db), new ProjectManagement.Services.Admin.AdminTimeService(new ProjectManagement.Services.SystemClock()));
        ConfigurePage(page, Manager);
        Assert.IsType<ForbidResult>(await page.OnPostReactivateAsync(hidden.Id, CancellationToken.None));
        Assert.IsType<ForbidResult>(await page.OnPostReactivateAllAsync(new[] { hidden.Id }, CancellationToken.None));

        var adminPage = new IndexModel(CreateService(db), new ProjectManagement.Services.Admin.AdminTimeService(new ProjectManagement.Services.SystemClock()));
        ConfigurePage(adminPage, Admin);
        Assert.IsType<RedirectToPageResult>(await adminPage.OnPostReactivateAllAsync(new[] { hidden.Id }, CancellationToken.None));
        Assert.Equal("1 document shown again.", adminPage.TempData[FlashMessageKeys.AdminRecoverySuccess]);
        Assert.True((await db.Documents.AsNoTracking().SingleAsync()).IsActive);
    }

    // SECTION: Administration
    [Fact]
    public void Catalogue_ListsItUnderRestoreDeletedItems_InTheAdminLayout()
    {
        var entry = AdminNavigationCatalog.GetEntry(AdminNavigationKeys.RestoredHiddenDocuments);

        Assert.Equal("Restored but still hidden", entry.Item.Text);
        Assert.Equal(AdminNavigationGroups.RestoreArchive, entry.Group);
        Assert.Equal(AdminNavigationKeys.RecoveryCentre, entry.SidebarParent);
        Assert.Equal(Policies.Documents.Purge, entry.Item.AuthorizationPolicy);
        Assert.True(entry.UsesAdminShell);
        Assert.Same(entry, AdminNavigationCatalog.FindActiveEntry("DocumentRepository", "/Admin/RestoredHidden/Index", new QueryCollection()));
        Assert.True(AdminNavigationCatalog.IsInScope("DocumentRepository", "/Admin/RestoredHidden/Index", null, null, new QueryCollection()));
        // Next to the Repository trash.
        var trash = AdminNavigationCatalog.GetEntry(AdminNavigationKeys.RepositoryTrash);
        var ordered = AdminNavigationCatalog.Entries.Where(e => e.Group == AdminNavigationGroups.RestoreArchive).OrderBy(e => e.Order).ToList();
        Assert.Equal(ordered.IndexOf(trash) + 1, ordered.IndexOf(entry));
    }

    // SECTION: Helpers
    private static DocRepoRestoredHiddenService CreateService(ApplicationDbContext db)
        => new(db, new DocRepoAuditService(db), Authorization(), FakeClock.AtUtc(Now));

    private static void Audit(ApplicationDbContext db, Document document, string eventType, DateTimeOffset at, string actor = AdminId)
        => db.DocRepoAudits.Add(new DocRepoAudit
        {
            DocumentId = document.Id,
            EventType = eventType,
            ActorUserId = actor,
            OccurredAtUtc = at,
            DetailsJson = "{}"
        });
}
