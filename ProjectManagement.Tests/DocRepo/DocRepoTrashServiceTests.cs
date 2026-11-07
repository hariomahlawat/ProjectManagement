using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.DocumentRepository.Pages.Admin.Trash;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Models;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Tests.Fakes;
using Xunit;
using static ProjectManagement.Tests.DocRepo.DocRepoTestSupport;

namespace ProjectManagement.Tests.DocRepo;

public sealed class DocRepoTrashServiceTests
{
    private const string AdminId = "admin-user";
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);
    private static ClaimsPrincipal Admin => User(AdminId, RoleNames.Admin);

    // SECTION: Restore
    [Fact]
    public async Task RestoreAsync_PutsAnApprovedDeletionBackForReadersAndSearch()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        // As RepositoryDocumentDeleteApprovalService leaves it.
        var document = SeedDocument(db, "Supply order", isActive: false, isDeleted: true);
        document.DeletedByUserId = "approver";
        document.DeleteReason = "Duplicate";
        await db.SaveChangesAsync();

        var outcome = await CreateService(db).RestoreAsync(Admin, document.Id);

        Assert.Equal(DocRepoRestoreOutcome.Restored, outcome);
        var saved = await db.Documents.AsNoTracking().SingleAsync(d => d.Id == document.Id);
        Assert.False(saved.IsDeleted);
        Assert.True(saved.IsActive);
        Assert.Null(saved.DeletedAtUtc);
        Assert.Null(saved.DeletedByUserId);
        Assert.Null(saved.DeleteReason);
        Assert.Equal(AdminId, saved.UpdatedByUserId);
        Assert.Equal(Now.UtcDateTime, saved.UpdatedAtUtc);

        // A signed-in user with no repository role can read it again.
        var access = Access();
        var canViewInactive = await access.CanViewInactiveAsync(Reader);
        Assert.False(canViewInactive);
        Assert.Contains(document.Id, await access.Readable(db.Documents.AsNoTracking(), canViewInactive).Select(d => d.Id).ToListAsync());

        var audit = await db.DocRepoAudits.AsNoTracking().SingleAsync();
        Assert.Equal("RestoredFromTrash", audit.EventType);
        Assert.Equal(document.Id, audit.DocumentId);
        Assert.Equal(AdminId, audit.ActorUserId);
    }

    [Fact]
    public async Task RestoreAsync_NeedsThePurgePolicy()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        var document = SeedDocument(db, isActive: false, isDeleted: true);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        Assert.Equal(DocRepoRestoreOutcome.Forbidden, await service.RestoreAsync(Reader, document.Id));
        Assert.Equal(DocRepoRestoreOutcome.Forbidden, await service.RestoreAsync(Manager, document.Id));

        var saved = await db.Documents.AsNoTracking().SingleAsync();
        Assert.True(saved.IsDeleted);
        Assert.False(saved.IsActive);
        Assert.Empty(await db.DocRepoAudits.AsNoTracking().ToListAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListAsync(Manager, new DocRepoTrashQuery()));
    }

    [Fact]
    public async Task RestoreAsync_LeavesDocumentsOutsideTheTrashAlone()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        // Restored before this fix: still switched off. Reactivating these is an owner decision.
        var restoredEarlier = SeedDocument(db, isActive: false, isDeleted: false);
        await db.SaveChangesAsync();

        Assert.Equal(DocRepoRestoreOutcome.NotFound, await CreateService(db).RestoreAsync(Admin, restoredEarlier.Id));
        Assert.Equal(DocRepoRestoreOutcome.NotFound, await CreateService(db).RestoreAsync(Admin, Guid.NewGuid()));

        Assert.False((await db.Documents.AsNoTracking().SingleAsync()).IsActive);
        Assert.Empty(await db.DocRepoAudits.AsNoTracking().ToListAsync());
    }

    // SECTION: List
    [Fact]
    public async Task ListAsync_PagesTheTrashNewestFirst()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        db.Users.Add(new ApplicationUser { Id = "approver", UserName = "approver", FullName = "Col A Approver" });
        var trashed = Enumerable.Range(0, 30).Select(i =>
        {
            var document = SeedDocument(db, $"Trashed {i:00}", isActive: false, isDeleted: true);
            document.DeletedAtUtc = Now.UtcDateTime.AddHours(-i);
            document.DeletedByUserId = "approver";
            return document;
        }).ToList();
        SeedDocument(db, "Live");
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var first = await service.ListAsync(Admin, new DocRepoTrashQuery());
        Assert.Equal(25, first.PageSize);
        Assert.Equal(25, first.Items.Count);
        Assert.Equal(30, first.TrashCount);
        Assert.Equal(30, first.MatchCount);
        Assert.Equal(300, first.TrashBytes);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal((1, 25), (first.FirstItem, first.LastItem));
        Assert.Equal(trashed.Take(25).Select(d => d.Id), first.Items.Select(d => d.Id));
        Assert.Equal("Letter", first.Items[0].Category);
        Assert.Equal("CPDS North", first.Items[0].Office);
        Assert.Equal("Col A Approver", first.UserNames["approver"]);

        var second = await service.ListAsync(Admin, new DocRepoTrashQuery(Page: 2));
        Assert.Equal(trashed.Skip(25).Select(d => d.Id), second.Items.Select(d => d.Id));
        Assert.Equal((26, 30), (second.FirstItem, second.LastItem));

        // Past the end: the last page. Unknown sizes fall back to the default.
        var beyond = await service.ListAsync(Admin, new DocRepoTrashQuery(Page: 9, PageSize: 7));
        Assert.Equal((2, 25), (beyond.Page, beyond.PageSize));

        var tens = await service.ListAsync(Admin, new DocRepoTrashQuery(Page: 3, PageSize: 10));
        Assert.Equal(trashed.Skip(20).Take(10).Select(d => d.Id), tens.Items.Select(d => d.Id));
        Assert.Equal(3, tens.TotalPages);
    }

    [Fact]
    public async Task ListAsync_EmptyTrashIsOnePageOfNothing()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        SeedDocument(db, "Live");
        await db.SaveChangesAsync();

        var page = await CreateService(db).ListAsync(Admin, new DocRepoTrashQuery(Page: 4));

        Assert.Empty(page.Items);
        Assert.Equal((1, 1, 0, 0), (page.Page, page.TotalPages, page.FirstItem, page.LastItem));
    }

    // SECTION: Page
    [Fact]
    public async Task TrashPage_RestoreReturnsToTheSamePage()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        var document = SeedDocument(db, isActive: false, isDeleted: true);
        await db.SaveChangesAsync();
        var page = new IndexModel(CreateService(db), CreatePurgeService(db)) { Query = "order", PageNumber = 3, PageSize = 50 };
        ConfigurePage(page, Admin);

        var result = Assert.IsType<RedirectToPageResult>(await page.OnPostRestoreAsync(document.Id, CancellationToken.None));

        var values = new RouteValueDictionary(result.RouteValues);
        Assert.Equal("order", values["q"]);
        Assert.Equal(3, values["pageNumber"]);
        Assert.Equal(50, values["pageSize"]);
        Assert.StartsWith("Document restored.", page.StatusMessage);
        Assert.True((await db.Documents.AsNoTracking().SingleAsync()).IsActive);
    }

    [Fact]
    public async Task TrashPage_ShowsTheRequestedPage()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        for (var i = 0; i < 12; i++)
        {
            SeedDocument(db, $"Trashed {i}", isActive: false, isDeleted: true);
        }
        await db.SaveChangesAsync();
        var page = new IndexModel(CreateService(db), CreatePurgeService(db)) { PageNumber = 2, PageSize = 10 };
        ConfigurePage(page, Admin);

        await page.OnGetAsync(CancellationToken.None);

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(12, page.TotalCount);
        Assert.Equal(2, page.PageNumber);
        Assert.Equal(2, page.Result.TotalPages);
    }

    private static DocRepoTrashService CreateService(ApplicationDbContext db)
        => new(db, new DocRepoAuditService(db), Authorization(), FakeClock.AtUtc(Now));

    private static DocRepoTrashPurgeService CreatePurgeService(ApplicationDbContext db)
        => new(db, new RecordingDocStorage(), new DocRepoAuditService(db), FakeClock.AtUtc(Now), new StaticOptions());

    private sealed class StaticOptions : IOptionsMonitor<DocRepoOptions>
    {
        public DocRepoOptions CurrentValue { get; } = new() { TrashRetentionDays = 30 };

        public DocRepoOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<DocRepoOptions, string?> listener) => null;
    }
}
