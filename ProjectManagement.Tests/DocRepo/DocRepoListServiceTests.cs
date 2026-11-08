using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Services.Admin.MasterData.Integrity;
using ProjectManagement.Services.DocRepo;
using Xunit;
using static ProjectManagement.Tests.DocRepo.DocRepoTestSupport;
using DocumentTypesPage = ProjectManagement.Areas.DocumentRepository.Pages.Admin.DocumentCategories.IndexModel;
using OfficesPage = ProjectManagement.Areas.DocumentRepository.Pages.Admin.OfficeCategories.IndexModel;

namespace ProjectManagement.Tests.DocRepo;

/// <summary>
/// Second admin review, PR 6 (docs/ux/admin-review-2-fixes.md, M6): the repository's document
/// types and offices go through one service. Switching on/off takes the state wanted and the
/// row's version, so a double submit or two administrators cannot undo each other; a value in
/// use cannot be deleted.
/// </summary>
public sealed class DocRepoListServiceTests
{
    [Theory]
    [InlineData(DocRepoListKind.DocumentType)]
    [InlineData(DocRepoListKind.Office)]
    public async Task Deactivating_Twice_LeavesItDeactivated(DocRepoListKind kind)
    {
        await using var db = CreateDb();
        SeedLookups(db);
        await db.SaveChangesAsync();
        var lists = Service(db);

        var first = await lists.SetActiveAsync(kind, Admin, 1, isActive: false, version: null);
        var second = await lists.SetActiveAsync(kind, Admin, 1, isActive: false, version: null);

        Assert.True(first.Succeeded, first.Message);
        Assert.True(second.Succeeded, second.Message);
        Assert.Contains("already deactivated", second.Message);
        Assert.False(await IsActiveAsync(db, kind, 1));
        Assert.Single(await db.DocRepoAudits.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Toggle_WithoutTheStateWanted_IsRefused_AndChangesNothing()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        await db.SaveChangesAsync();

        var result = await Service(db).SetActiveAsync(DocRepoListKind.DocumentType, Admin, 1, isActive: null, version: null);

        Assert.False(result.Succeeded);
        Assert.Equal("TargetStateMissing", result.ErrorCode);
        Assert.True(await IsActiveAsync(db, DocRepoListKind.DocumentType, 1));
    }

    [Fact]
    public async Task Toggle_FromAStalePage_DoesNotFlip()
    {
        await using var db = CreateVersionedDb();
        SeedLookups(db);
        db.Entry(db.DocumentCategories.Local.Single()).Property(DocRepoListService.VersionProperty).CurrentValue = 7u;
        await db.SaveChangesAsync();

        // The page was opened at version 6; someone has changed the row since.
        var result = await Service(db).SetActiveAsync(DocRepoListKind.DocumentType, Admin, 1, isActive: false, version: 6u);

        Assert.False(result.Succeeded);
        Assert.Equal("ConcurrencyConflict", result.ErrorCode);
        Assert.True(await IsActiveAsync(db, DocRepoListKind.DocumentType, 1));
        Assert.Empty(await db.DocRepoAudits.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Toggle_WithTheCurrentVersion_Saves()
    {
        await using var db = CreateVersionedDb();
        SeedLookups(db);
        db.Entry(db.OfficeCategories.Local.Single()).Property(DocRepoListService.VersionProperty).CurrentValue = 7u;
        await db.SaveChangesAsync();

        var result = await Service(db).SetActiveAsync(DocRepoListKind.Office, Admin, 1, isActive: false, version: 7u);

        Assert.True(result.Succeeded, result.Message);
        Assert.False(await IsActiveAsync(db, DocRepoListKind.Office, 1));
    }

    [Fact]
    public async Task Rename_FromAStalePage_IsRefused()
    {
        await using var db = CreateVersionedDb();
        SeedLookups(db);
        db.Entry(db.DocumentCategories.Local.Single()).Property(DocRepoListService.VersionProperty).CurrentValue = 7u;
        await db.SaveChangesAsync();

        var stale = await Service(db).UpdateAsync(DocRepoListKind.DocumentType, Admin, 1, "Letters", 1, version: 6u);
        var missing = await Service(db).UpdateAsync(DocRepoListKind.DocumentType, Admin, 1, "Letters", 1, version: null);

        Assert.Equal("ConcurrencyConflict", stale.ErrorCode);
        Assert.Equal("ConcurrencyTokenMissing", missing.ErrorCode);
        Assert.Equal("Letter", (await db.DocumentCategories.AsNoTracking().SingleAsync()).Name);
    }

    [Fact]
    public async Task Create_RefusesTheSameNameInAnotherCase_AndChangesNeedThePolicy()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        await db.SaveChangesAsync();
        var lists = Service(db);

        var duplicate = await lists.CreateAsync(DocRepoListKind.DocumentType, Admin, "  LETTER ", null);
        var denied = await lists.SetActiveAsync(DocRepoListKind.DocumentType, Reader, 1, isActive: false, version: null);
        var added = await lists.CreateAsync(DocRepoListKind.DocumentType, Admin, "Minutes", null);

        Assert.Equal("DuplicateName", duplicate.ErrorCode);
        Assert.Equal("Forbidden", denied.ErrorCode);
        Assert.True(added.Succeeded, added.Message);
        Assert.Equal(new[] { "Letter", "Minutes" }, await db.DocumentCategories.AsNoTracking().OrderBy(item => item.Id).Select(item => item.Name).ToArrayAsync());
        Assert.True(await IsActiveAsync(db, DocRepoListKind.DocumentType, 1));
    }

    [Fact]
    public void BothPages_GoThroughTheService_NotTheDatabase()
    {
        foreach (var page in new[] { typeof(DocumentTypesPage), typeof(OfficesPage) })
        {
            var parameters = page.GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType).ToArray();
            Assert.Contains(typeof(IDocRepoListService), parameters);
            Assert.DoesNotContain(typeof(ApplicationDbContext), parameters);
            Assert.Equal(typeof(ProjectManagement.Areas.DocumentRepository.Pages.Admin.DocRepoListPageModel), page.BaseType);
        }
    }

    [Fact]
    public async Task TogglePage_PostsTheStateWanted_ToTheService()
    {
        await using var db = CreateDb();
        var lists = new RecordingLists();
        var page = new OfficesPage(lists, ReferenceLists(db), new NoNotices());
        ConfigurePage(page, Admin);

        // The shared list posts isActive and rowVersion (xmin as four bytes)...
        var result = await page.OnPostToggleAsync(4, true, Convert.ToBase64String(BitConverter.GetBytes(12u)), CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal((DocRepoListKind.Office, 4, (bool?)true, (uint?)12u), lists.LastToggle);
        Assert.Equal("Done.", page.TempData[ProjectManagement.Services.Admin.FlashMessageKeys.AdminMasterDataSuccess]);

        // ...and a form from the old page (active, version) still works (PR 12).
        page.LegacyActive = false;
        page.LegacyVersion = 13u;
        await page.OnPostToggleAsync(5, null, null, CancellationToken.None);
        Assert.Equal((DocRepoListKind.Office, 5, (bool?)false, (uint?)13u), lists.LastToggle);
    }

    // SECTION: Helpers
    /// <summary>Only administrators manage the repository lists (Policies.Documents.CategoryManagerRoles).</summary>
    private static System.Security.Claims.ClaimsPrincipal Admin => User("admin-user", ProjectManagement.Configuration.RoleNames.Admin);

    private static DocRepoListService Service(ApplicationDbContext db) =>
        new(db, new DocRepoAuditService(db), Authorization());

    private static ProjectManagement.Services.Admin.MasterData.ReferenceLists.ReferenceListService ReferenceLists(ApplicationDbContext db) =>
        new(db, new NoAdminAudit(), Authorization(), new Microsoft.AspNetCore.Http.HttpContextAccessor());

    private sealed class NoAdminAudit : ProjectManagement.Services.Admin.IAdminAuditService
    {
        public Task RecordAsync(ProjectManagement.Services.Admin.AdminAuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static async Task<bool> IsActiveAsync(ApplicationDbContext db, DocRepoListKind kind, int id) =>
        kind == DocRepoListKind.DocumentType
            ? (await db.DocumentCategories.AsNoTracking().SingleAsync(item => item.Id == id)).IsActive
            : (await db.OfficeCategories.AsNoTracking().SingleAsync(item => item.Id == id)).IsActive;

    private static VersionedDb CreateVersionedDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    /// <summary>
    /// The production model maps the row version to PostgreSQL's xmin, which only exists there.
    /// This context adds the same shadow property as a plain concurrency token so the in-memory
    /// tests can exercise the version check.
    /// </summary>
    private sealed class VersionedDb(DbContextOptions<ApplicationDbContext> options) : ApplicationDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<DocumentCategory>().Property<uint>(DocRepoListService.VersionProperty).IsConcurrencyToken();
            builder.Entity<OfficeCategory>().Property<uint>(DocRepoListService.VersionProperty).IsConcurrencyToken();
        }
    }

    private sealed class RecordingLists : IDocRepoListService
    {
        public (DocRepoListKind, int, bool?, uint?) LastToggle { get; private set; }

        public Task<DocRepoListResult> CreateAsync(DocRepoListKind kind, System.Security.Claims.ClaimsPrincipal user, string? name, int? sortOrder, CancellationToken cancellationToken = default) =>
            Task.FromResult(DocRepoListResult.Success("Done."));

        public Task<DocRepoListResult> UpdateAsync(DocRepoListKind kind, System.Security.Claims.ClaimsPrincipal user, int id, string? name, int sortOrder, uint? version, CancellationToken cancellationToken = default) =>
            Task.FromResult(DocRepoListResult.Success("Done."));

        public Task<DocRepoListResult> SetActiveAsync(DocRepoListKind kind, System.Security.Claims.ClaimsPrincipal user, int id, bool? isActive, uint? version, CancellationToken cancellationToken = default)
        {
            LastToggle = (kind, id, isActive, version);
            return Task.FromResult(DocRepoListResult.Success("Done."));
        }
    }

    private sealed class NoNotices : IListOrderNoticeService
    {
        public Task<ListOrderNotice> GetAsync(System.Security.Claims.ClaimsPrincipal user, string checkKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(ListOrderNotice.None(checkKey));

        public Task<ProjectManagement.Services.Admin.AdminOperationResult> RenumberAsync(System.Security.Claims.ClaimsPrincipal user, string checkKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
