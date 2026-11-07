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
    public async Task List_CarriesTheRowVersion_WhereTheDatabaseHasOne()
    {
        await using var versioned = CreateVersionedDb();
        SeedLookups(versioned);
        versioned.Entry(versioned.DocumentCategories.Local.Single()).Property(DocRepoListService.VersionProperty).CurrentValue = 9u;
        await versioned.SaveChangesAsync();
        await using var plain = CreateDb();
        SeedLookups(plain);
        await plain.SaveChangesAsync();

        Assert.Equal(9u, (await Service(versioned).ListAsync(DocRepoListKind.DocumentType)).Single().Version);
        Assert.Null((await Service(plain).ListAsync(DocRepoListKind.DocumentType)).Single().Version);
    }

    [Theory]
    [InlineData(DocRepoListKind.DocumentType, false)]
    [InlineData(DocRepoListKind.Office, false)]
    [InlineData(DocRepoListKind.DocumentType, true)]
    public async Task Delete_RefusesAValueInUse_EvenByTrashedDocuments(DocRepoListKind kind, bool onlyInTrash)
    {
        await using var db = CreateDb();
        SeedLookups(db);
        SeedDocument(db, "Filed under it", isDeleted: onlyInTrash);
        await db.SaveChangesAsync();
        var lists = Service(db);

        var result = await lists.DeleteAsync(kind, Admin, 1, version: null);

        Assert.False(result.Succeeded);
        Assert.Equal("InUse", result.ErrorCode);
        Assert.Contains("Deactivate it instead", result.Message);
        Assert.Single(await db.Documents.AsNoTracking().ToListAsync());
        var row = Assert.Single(await lists.ListAsync(kind));
        Assert.False(row.CanDelete);
    }

    [Theory]
    [InlineData(DocRepoListKind.DocumentType)]
    [InlineData(DocRepoListKind.Office)]
    public async Task Delete_RefusesTheValueAutomaticImportsAreFiledUnder(DocRepoListKind kind)
    {
        await using var db = CreateDb();
        SeedLookups(db);
        await db.SaveChangesAsync();
        // No document uses it yet, but ingestion is configured to file new PDFs under it.
        var lists = Service(db, new DocRepoOptions { IngestionOfficeCategoryId = 1, IngestionDocumentCategoryId = 1 });

        var result = await lists.DeleteAsync(kind, Admin, 1, version: null);

        Assert.Equal("UsedByIngestion", result.ErrorCode);
        Assert.True(kind == DocRepoListKind.DocumentType ? await db.DocumentCategories.AnyAsync() : await db.OfficeCategories.AnyAsync());
        var row = Assert.Single(await lists.ListAsync(kind));
        Assert.True(row.UsedByIngestion);
        Assert.False(row.CanDelete);
    }

    [Fact]
    public async Task Delete_RemovesAnUnusedValue_AndAuditsIt()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        db.DocumentCategories.Add(new DocumentCategory { Id = 2, Name = "Unused" });
        await db.SaveChangesAsync();
        var lists = Service(db);
        Assert.True((await lists.ListAsync(DocRepoListKind.DocumentType)).Single(item => item.Id == 2).CanDelete);

        var result = await lists.DeleteAsync(DocRepoListKind.DocumentType, Admin, 2, version: null);

        Assert.True(result.Succeeded, result.Message);
        Assert.False(await db.DocumentCategories.AnyAsync(item => item.Id == 2));
        var audit = Assert.Single(await db.DocRepoAudits.AsNoTracking().ToListAsync());
        Assert.Equal(DocRepoAuditEventTypes.CategoryChanged, audit.EventType);
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
    public async Task List_CountsLibraryAndTrashSeparately()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        SeedDocument(db, "Live one");
        SeedDocument(db, "Live two");
        SeedDocument(db, "Trashed", isDeleted: true);
        await db.SaveChangesAsync();

        var office = Assert.Single(await Service(db).ListAsync(DocRepoListKind.Office));

        Assert.Equal(2, office.DocumentCount);
        Assert.Equal(1, office.TrashedDocumentCount);
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
        var lists = new RecordingLists();
        var page = new OfficesPage(lists, new NoNotices());
        ConfigurePage(page, Admin);

        var result = await page.OnPostToggleAsync(4, active: true, version: 12u, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal((DocRepoListKind.Office, 4, (bool?)true, (uint?)12u), lists.LastToggle);
        Assert.Equal("Done.", page.TempData["ToastMessage"]);
    }

    // SECTION: Helpers
    /// <summary>Only administrators manage the repository lists (Policies.Documents.CategoryManagerRoles).</summary>
    private static System.Security.Claims.ClaimsPrincipal Admin => User("admin-user", ProjectManagement.Configuration.RoleNames.Admin);

    private static DocRepoListService Service(ApplicationDbContext db, DocRepoOptions? options = null) =>
        new(db, new DocRepoAuditService(db), Authorization(), Microsoft.Extensions.Options.Options.Create(options ?? new DocRepoOptions()));

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

        public Task<System.Collections.Generic.IReadOnlyList<DocRepoListItem>> ListAsync(DocRepoListKind kind, CancellationToken cancellationToken = default) =>
            Task.FromResult<System.Collections.Generic.IReadOnlyList<DocRepoListItem>>(Array.Empty<DocRepoListItem>());

        public Task<DocRepoListResult> CreateAsync(DocRepoListKind kind, System.Security.Claims.ClaimsPrincipal user, string? name, int? sortOrder, CancellationToken cancellationToken = default) =>
            Task.FromResult(DocRepoListResult.Success("Done."));

        public Task<DocRepoListResult> UpdateAsync(DocRepoListKind kind, System.Security.Claims.ClaimsPrincipal user, int id, string? name, int sortOrder, uint? version, CancellationToken cancellationToken = default) =>
            Task.FromResult(DocRepoListResult.Success("Done."));

        public Task<DocRepoListResult> SetActiveAsync(DocRepoListKind kind, System.Security.Claims.ClaimsPrincipal user, int id, bool? isActive, uint? version, CancellationToken cancellationToken = default)
        {
            LastToggle = (kind, id, isActive, version);
            return Task.FromResult(DocRepoListResult.Success("Done."));
        }

        public Task<DocRepoListResult> DeleteAsync(DocRepoListKind kind, System.Security.Claims.ClaimsPrincipal user, int id, uint? version, CancellationToken cancellationToken = default) =>
            Task.FromResult(DocRepoListResult.Success("Done."));
    }

    private sealed class NoNotices : IListOrderNoticeService
    {
        public Task<ListOrderNotice> GetAsync(System.Security.Claims.ClaimsPrincipal user, string checkKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(ListOrderNotice.None(checkKey));

        public Task<ProjectManagement.Services.Admin.AdminOperationResult> RenumberAsync(System.Security.Claims.ClaimsPrincipal user, string checkKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
