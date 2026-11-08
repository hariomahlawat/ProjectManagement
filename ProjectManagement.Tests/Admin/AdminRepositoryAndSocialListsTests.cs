using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using ProjectManagement.Areas.Admin.Pages.Shared;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData.ReferenceLists;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Services.Navigation.ModuleNav;
using ProjectManagement.Tests.DocRepo;
using Xunit;
using DocumentTypesPage = ProjectManagement.Areas.DocumentRepository.Pages.Admin.DocumentCategories.IndexModel;
using OfficesPage = ProjectManagement.Areas.DocumentRepository.Pages.Admin.OfficeCategories.IndexModel;
using EventTypesPage = ProjectManagement.Areas.ProjectOfficeReports.Pages.Admin.SocialMediaTypes.IndexModel;
using PlatformsPage = ProjectManagement.Areas.ProjectOfficeReports.Pages.Admin.SocialMediaTypes.Platforms.IndexModel;

namespace ProjectManagement.Tests.Admin;

/// <summary>
/// PR 12 (docs/ux/admin-review-3-fixes.md, owner decisions 1 and 2): the Document Repository's
/// document types and offices and the social media event types and platforms use the shared
/// list page inside Administration, and the one usage-and-delete rule.
/// </summary>
public sealed class AdminRepositoryAndSocialListsTests
{
    // ---- Document Repository lists ----

    [Theory]
    [InlineData(ReferenceListKeys.DocumentTypes, false)]
    [InlineData(ReferenceListKeys.DocumentTypes, true)]
    [InlineData(ReferenceListKeys.DocumentOffices, false)]
    public async Task RepositoryValue_UsedByADocument_InTheLibraryOrTheTrash_CannotBeDeleted(string key, bool onlyInTrash)
    {
        await using var db = CreateDb();
        DocRepoTestSupport.SeedLookups(db);
        DocRepoTestSupport.SeedDocument(db, "Filed under it", isDeleted: onlyInTrash);
        await db.SaveChangesAsync();
        var lists = Lists(db, out var audit);

        var page = await lists.GetPageAsync(key, new ReferenceListRequest(null, null, 1, 25));
        var row = Assert.Single(page.Rows);
        Assert.Equal("1 document", row.UsedBy.Single(item => item.Count > 0).Text);
        Assert.False(row.CanDelete);
        Assert.Equal(new ReferenceListTiles(1, 1, 0), page.Tiles);

        var result = await lists.DeleteAsync(key, 1, Array.Empty<byte>());

        Assert.Equal("EntityInUse", result.ErrorCode);
        Assert.Contains("Deactivate it instead", result.UserMessage);
        Assert.Single(await db.Documents.AsNoTracking().ToListAsync());
        Assert.Empty(audit.Entries);
    }

    [Theory]
    [InlineData(ReferenceListKeys.DocumentTypes)]
    [InlineData(ReferenceListKeys.DocumentOffices)]
    public async Task RepositoryValue_WhereImportedPdfsAreFiled_CountsAsInUse(string key)
    {
        await using var db = CreateDb();
        DocRepoTestSupport.SeedLookups(db);
        await db.SaveChangesAsync();
        // No document uses it yet, but ingestion is set to file new PDFs under it.
        var lists = Lists(db, out _, new DocRepoOptions { IngestionOfficeCategoryId = 1, IngestionDocumentCategoryId = 1 });

        var row = Assert.Single((await lists.GetPageAsync(key, new ReferenceListRequest(null, ReferenceListStatuses.InUse, 1, 25))).Rows);
        Assert.Equal("1 setting for imported PDFs", row.UsedBy.Single(item => item.Count > 0).Text);

        var result = await lists.DeleteAsync(key, 1, Array.Empty<byte>());
        Assert.Equal("EntityInUse", result.ErrorCode);
        Assert.True(key == ReferenceListKeys.DocumentTypes ? await db.DocumentCategories.AnyAsync() : await db.OfficeCategories.AnyAsync());
    }

    [Fact]
    public async Task UnusedRepositoryValue_IsDeleted_InTheRepositoryAudit_AndTheTypedOrderStays()
    {
        await using var db = CreateDb();
        DocRepoTestSupport.SeedLookups(db);
        db.DocumentCategories.AddRange(
            new DocumentCategory { Id = 2, Name = "Unused", SortOrder = 20 },
            new DocumentCategory { Id = 3, Name = "Later", SortOrder = 30 });
        await db.SaveChangesAsync();
        var lists = Lists(db, out var adminAudit);

        var page = await lists.GetPageAsync(ReferenceListKeys.DocumentTypes, new ReferenceListRequest(null, null, 1, 25));
        Assert.Equal(new[] { 100, 20, 30 }.OrderBy(order => order), page.Rows.Select(row => row.ShownOrder));
        Assert.True(page.Rows.Single(row => row.Id == "2").CanDelete);

        var result = await lists.DeleteAsync(ReferenceListKeys.DocumentTypes, 2, Array.Empty<byte>());

        Assert.True(result.Succeeded, result.UserMessage);
        Assert.False(await db.DocumentCategories.AnyAsync(item => item.Id == 2));
        // People type the order; a delete leaves the rest as typed.
        Assert.Equal(30, (await db.DocumentCategories.AsNoTracking().SingleAsync(item => item.Id == 3)).SortOrder);
        var entry = Assert.Single(await db.DocRepoAudits.AsNoTracking().ToListAsync());
        Assert.Equal(DocRepoAuditEventTypes.CategoryChanged, entry.EventType);
        Assert.Contains("Deleted", entry.DetailsJson);
        Assert.Equal("admin-user", entry.ActorUserId);
        Assert.Empty(adminAudit.Entries);
    }

    // Codex review on #3019: the search also reads Detail, so the repository projections bind it
    // (DocRepoListPostgresIntegrationTests runs the same search on PostgreSQL).
    [Fact]
    public async Task RepositoryList_CanBeSearched_WithAndWithoutARowVersion()
    {
        await using var plain = CreateDb();
        DocRepoTestSupport.SeedLookups(plain);
        plain.DocumentCategories.Add(new DocumentCategory { Id = 2, Name = "Minutes" });
        await plain.SaveChangesAsync();
        await using var versioned = CreateVersionedDb();
        DocRepoTestSupport.SeedLookups(versioned);
        versioned.DocumentCategories.Add(new DocumentCategory { Id = 2, Name = "Minutes" });
        await versioned.SaveChangesAsync();

        foreach (var db in new ApplicationDbContext[] { plain, versioned })
        {
            var searched = await Lists(db, out _).GetPageAsync(ReferenceListKeys.DocumentTypes, new ReferenceListRequest("minu", null, 1, 25));
            Assert.Equal("Minutes", Assert.Single(searched.Rows).Name);
            Assert.Equal(1, searched.FilteredCount);
        }

    }

    [Fact]
    public async Task RepositoryDelete_FromAnOutOfDatePage_IsRefused()
    {
        await using var db = CreateVersionedDb();
        DocRepoTestSupport.SeedLookups(db);
        db.Entry(db.DocumentCategories.Local.Single()).Property(DocRepoListService.VersionProperty).CurrentValue = 7u;
        await db.SaveChangesAsync();
        var lists = Lists(db, out _);

        var row = Assert.Single((await lists.GetPageAsync(ReferenceListKeys.DocumentTypes, new ReferenceListRequest(null, null, 1, 25))).Rows);
        Assert.Equal(7u, BitConverter.ToUInt32(Convert.FromBase64String(row.RowVersion)));

        var missing = await lists.DeleteAsync(ReferenceListKeys.DocumentTypes, 1, Array.Empty<byte>());
        var stale = await lists.DeleteAsync(ReferenceListKeys.DocumentTypes, 1, BitConverter.GetBytes(6u));
        Assert.Equal("ConcurrencyTokenMissing", missing.ErrorCode);
        Assert.Equal("ConcurrencyConflict", stale.ErrorCode);
        Assert.True(await db.DocumentCategories.AnyAsync());

        var current = await lists.DeleteAsync(ReferenceListKeys.DocumentTypes, 1, Convert.FromBase64String(row.RowVersion));
        Assert.True(current.Succeeded, current.UserMessage);
    }

    [Fact]
    public async Task RepositoryDelete_NeedsTheRepositoryListsPolicy()
    {
        await using var db = CreateDb();
        DocRepoTestSupport.SeedLookups(db);
        await db.SaveChangesAsync();

        var result = await Lists(db, out _, user: DocRepoTestSupport.Manager).DeleteAsync(ReferenceListKeys.DocumentTypes, 1, Array.Empty<byte>());

        Assert.Equal("Forbidden", result.ErrorCode);
        Assert.True(await db.DocumentCategories.AnyAsync());
    }

    // ---- Social media lists (keyed by Guid) ----

    [Fact]
    public async Task SocialMediaEventTypes_ListAToZ_CountEvents_AndSearchTheDescription()
    {
        await using var db = CreateDb();
        var launch = SocialMediaTestData.CreateEventType(name: "Launch", description: "New product");
        var award = SocialMediaTestData.CreateEventType(name: "Award", description: "Prizes and honours");
        var visit = SocialMediaTestData.CreateEventType(name: "Visit", isActive: false);
        var platform = SocialMediaTestData.CreatePlatform();
        db.SocialMediaEventTypes.AddRange(launch, award, visit);
        db.SocialMediaPlatforms.Add(platform);
        db.SocialMediaEvents.AddRange(
            SocialMediaTestData.CreateEvent(launch.Id, platform.Id),
            SocialMediaTestData.CreateEvent(launch.Id, platform.Id));
        await db.SaveChangesAsync();
        var lists = Lists(db, out _);

        var page = await lists.GetPageAsync(ReferenceListKeys.SocialMediaEventTypes, new ReferenceListRequest(null, null, 1, 25));
        Assert.Equal(new[] { "Award", "Launch", "Visit" }, page.Rows.Select(row => row.Name));
        Assert.Equal(new ReferenceListTiles(3, 1, 1), page.Tiles);
        var row = page.Rows.Single(item => item.Name == "Launch");
        Assert.Equal(launch.Id.ToString(), row.Id);
        Assert.Equal("2 events", row.UsedBy.Single().Text);
        Assert.Equal("New product", row.Detail);
        Assert.Equal(new[] { 2 }, await lists.GetUsageAsync(ReferenceListKeys.SocialMediaEventTypes, launch.Id).ContinueWith(task => task.Result.Select(item => item.Count).ToArray()));

        var searched = await lists.GetPageAsync(ReferenceListKeys.SocialMediaEventTypes, new ReferenceListRequest("honours", null, 1, 25));
        Assert.Equal("Award", Assert.Single(searched.Rows).Name);
        Assert.False(ReferenceListCatalog.SocialMediaEventTypes.HasOrder);
    }

    [Theory]
    [InlineData(ReferenceListKeys.SocialMediaEventTypes, "ProjectOfficeReports.SocialMediaEventTypeDeleted")]
    [InlineData(ReferenceListKeys.SocialMediaPlatforms, "ProjectOfficeReports.SocialMediaPlatformDeleted")]
    public async Task SocialMediaValue_InUse_IsRefused_AndAnUnusedOne_IsDeletedAndAudited(string key, string auditAction)
    {
        await using var db = CreateDb();
        var usedType = SocialMediaTestData.CreateEventType(name: "Launch");
        var spareType = SocialMediaTestData.CreateEventType(name: "Spare type");
        var usedPlatform = SocialMediaTestData.CreatePlatform(name: "Instagram");
        var sparePlatform = SocialMediaTestData.CreatePlatform(name: "Spare platform");
        db.SocialMediaEventTypes.AddRange(usedType, spareType);
        db.SocialMediaPlatforms.AddRange(usedPlatform, sparePlatform);
        db.SocialMediaEvents.Add(SocialMediaTestData.CreateEvent(usedType.Id, usedPlatform.Id));
        await db.SaveChangesAsync();
        var lists = Lists(db, out var audit);
        var isTypes = key == ReferenceListKeys.SocialMediaEventTypes;
        var (used, spare) = isTypes ? (usedType.Id, spareType.Id) : (usedPlatform.Id, sparePlatform.Id);
        var (usedVersion, spareVersion) = isTypes ? (usedType.RowVersion, spareType.RowVersion) : (usedPlatform.RowVersion, sparePlatform.RowVersion);

        var refused = await lists.DeleteAsync(key, used, usedVersion);
        Assert.Equal("EntityInUse", refused.ErrorCode);
        Assert.Contains("1 event", refused.UserMessage);

        var deleted = await lists.DeleteAsync(key, spare, spareVersion);
        Assert.True(deleted.Succeeded, deleted.UserMessage);
        Assert.False(isTypes
            ? await db.SocialMediaEventTypes.AnyAsync(item => item.Id == spare)
            : await db.SocialMediaPlatforms.AnyAsync(item => item.Id == spare));
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(auditAction, entry.Action);
        Assert.Equal(spare.ToString(), entry.EntityId);

        // A number is not a Guid: nothing is found, nothing is deleted.
        var wrongKey = await lists.DeleteAsync(key, 5, new byte[] { 1 });
        Assert.Equal("NotFound", wrongKey.ErrorCode);
    }

    [Fact]
    public async Task SocialMediaDelete_NeedsTheSocialMediaListsPolicy_AndTheRowVersion()
    {
        await using var db = CreateDb();
        var platform = SocialMediaTestData.CreatePlatform();
        db.SocialMediaPlatforms.Add(platform);
        await db.SaveChangesAsync();

        var authorization = new PolicyAuthorization(AdminPolicies.MasterDataManage);
        var denied = await Lists(db, out _, authorization: authorization).DeleteAsync(ReferenceListKeys.SocialMediaPlatforms, platform.Id, platform.RowVersion);
        var missing = await Lists(db, out _).DeleteAsync(ReferenceListKeys.SocialMediaPlatforms, platform.Id, Array.Empty<byte>());

        Assert.Equal("Forbidden", denied.ErrorCode);
        Assert.Contains(AdminPolicies.SocialMediaListsManage, authorization.Asked);
        Assert.Equal("ConcurrencyTokenMissing", missing.ErrorCode);
        Assert.True(await db.SocialMediaPlatforms.AnyAsync());
    }

    // ---- The pages ----

    public static IEnumerable<object[]> ListPages() => new[]
    {
        new object[] { "Areas/DocumentRepository/Pages/Admin/DocumentCategories/Index.cshtml", typeof(DocumentTypesPage), Policies.Documents.ManageCategories, AdminNavigationKeys.DocumentTypes, "DocumentRepository", "/Admin/DocumentCategories/Index" },
        new object[] { "Areas/DocumentRepository/Pages/Admin/OfficeCategories/Index.cshtml", typeof(OfficesPage), Policies.Documents.ManageCategories, AdminNavigationKeys.DocumentOffices, "DocumentRepository", "/Admin/OfficeCategories/Index" },
        new object[] { "Areas/ProjectOfficeReports/Pages/Admin/SocialMediaTypes/Index.cshtml", typeof(EventTypesPage), AdminPolicies.SocialMediaListsManage, AdminNavigationKeys.SocialMediaTypes, "ProjectOfficeReports", "/Admin/SocialMediaTypes/Index" },
        new object[] { "Areas/ProjectOfficeReports/Pages/Admin/SocialMediaTypes/Platforms/Index.cshtml", typeof(PlatformsPage), AdminPolicies.SocialMediaListsManage, AdminNavigationKeys.SocialMediaPlatforms, "ProjectOfficeReports", "/Admin/SocialMediaTypes/Platforms/Index" }
    };

    [Theory]
    [MemberData(nameof(ListPages))]
    public void EachPage_RendersTheSharedListPage_InTheAdminShell_AndKeepsItsPolicy(string view, Type pageModel, string policy, string navigationKey, string area, string page)
    {
        var text = Read(view);
        if (area == "DocumentRepository")
        {
            Assert.Contains("<partial name=\"../_DocRepoListPage\" model=\"Model\" />", text);
            text = Read("Areas/DocumentRepository/Pages/Admin/_DocRepoListPage.cshtml");
        }

        Assert.Contains("<partial name=\"~/Areas/Admin/Pages/Shared/_AdminPageHeader.cshtml\" model=\"Model.Header\" />", text);
        Assert.Contains("<partial name=\"~/Areas/Admin/Pages/Shared/_AdminReferenceList.cshtml\" model=\"Model.ListModel\" />", text);
        Assert.DoesNotContain("library-head", text);
        Assert.DoesNotContain("pm-card", text);
        Assert.DoesNotContain("UseFullWidth", text);

        var attribute = Assert.Single(pageModel.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false).Cast<AuthorizeAttribute>());
        Assert.Equal(policy, attribute.Policy);

        // Inside Administration: in scope for the shell, the Reference lists eyebrow, and Lists
        // used in forms lit in the sidebar.
        var entry = AdminNavigationCatalog.GetEntry(navigationKey);
        Assert.True(entry.UsesAdminShell);
        Assert.Equal(page, entry.Item.Page);
        Assert.Equal(policy, entry.Item.AuthorizationPolicy);
        Assert.Same(entry, AdminNavigationCatalog.FindActiveEntry(area, page, new QueryCollection()));
        Assert.True(AdminNavigationCatalog.IsInScope(area, page, null, null, new QueryCollection()));
        Assert.Equal(AdminNavigationGroups.ReferenceLists, AdminNavigationCatalog.EyebrowFor(area, page, null, "Old eyebrow"));
        Assert.Equal(AdminNavigationKeys.MasterDataCentre, entry.SidebarParent);
    }

    [Theory]
    [InlineData("/Admin/SocialMediaTypes/New", AdminNavigationKeys.SocialMediaTypes)]
    [InlineData("/Admin/SocialMediaTypes/Edit", AdminNavigationKeys.SocialMediaTypes)]
    [InlineData("/Admin/SocialMediaTypes/Platforms/New", AdminNavigationKeys.SocialMediaPlatforms)]
    [InlineData("/Admin/SocialMediaTypes/Platforms/Edit", AdminNavigationKeys.SocialMediaPlatforms)]
    public void SocialMediaAddAndEditPages_AreInTheAdminShellToo(string page, string navigationKey)
    {
        Assert.True(AdminNavigationCatalog.IsInScope("ProjectOfficeReports", page, null, null, new QueryCollection()));
        Assert.Equal(navigationKey, AdminNavigationCatalog.FindActiveEntry("ProjectOfficeReports", page, new QueryCollection())!.Key);

        var text = Read($"Areas/ProjectOfficeReports/Pages{page}.cshtml");
        // The shared list form writes the admin page header (docs/ux/admin-review-4-fixes.md, PR 5).
        Assert.Contains("~/Areas/Admin/Pages/Shared/_AdminListForm.cshtml", text);
        Assert.Contains("_AdminPageHeader.cshtml", Read("Areas/Admin/Pages/Shared/_AdminListForm.cshtml"));
        Assert.DoesNotContain("pm-card", text);
        if (page.EndsWith("/Edit", StringComparison.Ordinal))
        {
            Assert.StartsWith("@page \"{id:guid}\"", text);
            Assert.Contains("asp-for=\"RowVersion\"", text);
        }
    }

    [Fact]
    public void OldHandlersAndFormFields_StayInPlace()
    {
        // The repository pages keep Create, Update, Toggle, Delete and RenumberOrder, and still
        // read the old page's fields (active, version).
        var repository = typeof(ProjectManagement.Areas.DocumentRepository.Pages.Admin.DocRepoListPageModel);
        foreach (var handler in new[] { "OnPostCreateAsync", "OnPostUpdateAsync", "OnPostToggleAsync", "OnPostDeleteAsync", "OnPostRenumberOrderAsync" })
        {
            Assert.NotNull(repository.GetMethod(handler));
        }

        Assert.Equal("active", BindName(repository, "LegacyActive"));
        Assert.Equal("version", BindName(repository, "LegacyVersion"));

        // The social media pages keep Toggle and Delete (and the event types page its platform
        // handlers), and still read the old Toggle field (enable).
        foreach (var page in new[] { typeof(EventTypesPage), typeof(PlatformsPage) })
        {
            Assert.True(typeof(AdminReferenceListPageModel<Guid>).IsAssignableFrom(page));
            Assert.NotNull(page.GetMethod("OnPostToggleAsync"));
            Assert.NotNull(page.GetMethod("OnPostDeleteAsync"));
            Assert.Equal("enable", BindName(page, "LegacyEnable"));
        }

        Assert.NotNull(typeof(EventTypesPage).GetMethod("OnPostTogglePlatformAsync"));
        Assert.NotNull(typeof(EventTypesPage).GetMethod("OnPostDeletePlatformAsync"));
        Assert.True(typeof(AdminReferenceListPageModel).IsAssignableFrom(typeof(DocumentTypesPage)));

        // One table: the repository's own list table is gone.
        Assert.False(File.Exists(Path.Combine(FindRepositoryRoot(), "Areas/DocumentRepository/Pages/Admin/_DocRepoListTable.cshtml")));
    }

    [Fact]
    public async Task SocialMediaPage_OldToggleField_SetsTheStateAskedFor()
    {
        await using var db = CreateDb();
        var platform = SocialMediaTestData.CreatePlatform(isActive: true);
        db.SocialMediaPlatforms.Add(platform);
        await db.SaveChangesAsync();
        var page = new PlatformsPage(
            Lists(db, out _),
            new NoNotices(),
            new ProjectManagement.Areas.ProjectOfficeReports.Application.SocialMediaPlatformService(db, new FixedClock(), new ProjectManagement.Tests.Fakes.RecordingAudit()),
            new Microsoft.AspNetCore.Identity.UserManager<ProjectManagement.Models.ApplicationUser>(
                new Microsoft.AspNetCore.Identity.EntityFrameworkCore.UserStore<ProjectManagement.Models.ApplicationUser, Microsoft.AspNetCore.Identity.IdentityRole, ApplicationDbContext>(db),
                new Microsoft.Extensions.Options.OptionsWrapper<Microsoft.AspNetCore.Identity.IdentityOptions>(new Microsoft.AspNetCore.Identity.IdentityOptions()),
                new Microsoft.AspNetCore.Identity.PasswordHasher<ProjectManagement.Models.ApplicationUser>(),
                Array.Empty<Microsoft.AspNetCore.Identity.IUserValidator<ProjectManagement.Models.ApplicationUser>>(),
                Array.Empty<Microsoft.AspNetCore.Identity.IPasswordValidator<ProjectManagement.Models.ApplicationUser>>(),
                new Microsoft.AspNetCore.Identity.UpperInvariantLookupNormalizer(),
                new Microsoft.AspNetCore.Identity.IdentityErrorDescriber(),
                null!,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<Microsoft.AspNetCore.Identity.UserManager<ProjectManagement.Models.ApplicationUser>>.Instance))
        {
            LegacyEnable = true
        };
        DocRepoTestSupport.ConfigurePage(page, DocRepoTestSupport.User("admin-user", RoleNames.Admin));

        // The platform is already active, so asking for active changes nothing; it never flips.
        await page.OnPostToggleAsync(platform.Id, null, Convert.ToBase64String(platform.RowVersion), CancellationToken.None);

        Assert.True((await db.SocialMediaPlatforms.AsNoTracking().SingleAsync()).IsActive);
        Assert.Contains("already active", (string?)page.TempData[FlashMessageKeys.AdminMasterDataSuccess]);
    }

    // ---- Helpers ----

    private static string? BindName(Type type, string property) =>
        type.GetProperty(property)?.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.BindPropertyAttribute), inherit: true)
            .Cast<Microsoft.AspNetCore.Mvc.BindPropertyAttribute>().SingleOrDefault()?.Name;

    private static ApplicationDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static VersionedDb CreateVersionedDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static ReferenceListService Lists(
        ApplicationDbContext db,
        out CapturingAdminAudit audit,
        DocRepoOptions? options = null,
        ClaimsPrincipal? user = null,
        IAuthorizationService? authorization = null)
    {
        audit = new CapturingAdminAudit();
        var services = new ServiceCollection()
            .AddSingleton<IDocRepoAuditService>(new DocRepoAuditService(db))
            .AddSingleton(Microsoft.Extensions.Options.Options.Create(options ?? new DocRepoOptions()))
            .BuildServiceProvider();
        var http = new DefaultHttpContext
        {
            User = user ?? DocRepoTestSupport.User("admin-user", RoleNames.Admin),
            RequestServices = services
        };

        // The real repository policies; every other policy allowed unless a stub is given.
        return new ReferenceListService(db, audit, authorization ?? new RepositoryOrAllow(), new HttpContextAccessor { HttpContext = http }, services);
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProjectManagement.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    /// <summary>The production model maps the repository lists' version to PostgreSQL's xmin; this adds it for the in-memory tests.</summary>
    private sealed class VersionedDb(DbContextOptions<ApplicationDbContext> options) : ApplicationDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<DocumentCategory>().Property<uint>(DocRepoListService.VersionProperty).IsConcurrencyToken();
            builder.Entity<OfficeCategory>().Property<uint>(DocRepoListService.VersionProperty).IsConcurrencyToken();
        }
    }

    private sealed class CapturingAdminAudit : IAdminAuditService
    {
        public List<AdminAuditEntry> Entries { get; } = new();

        public Task RecordAsync(AdminAuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class RepositoryOrAllow : IAuthorizationService
    {
        private readonly IAuthorizationService _repository = DocRepoTestSupport.Authorization();

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(AuthorizationResult.Success());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) =>
            policyName.StartsWith("DocRepo.", StringComparison.Ordinal) || policyName == Policies.Documents.ManageCategories
                ? _repository.AuthorizeAsync(user, resource, policyName)
                : Task.FromResult(AuthorizationResult.Success());
    }

    private sealed class PolicyAuthorization(string only) : IAuthorizationService
    {
        public List<string> Asked { get; } = new();

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(AuthorizationResult.Success());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
        {
            Asked.Add(policyName);
            return Task.FromResult(policyName == only ? AuthorizationResult.Success() : AuthorizationResult.Failed());
        }
    }

    private sealed class NoNotices : ProjectManagement.Services.Admin.MasterData.Integrity.IListOrderNoticeService
    {
        public Task<ProjectManagement.Services.Admin.MasterData.Integrity.ListOrderNotice> GetAsync(ClaimsPrincipal user, string checkKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProjectManagement.Services.Admin.MasterData.Integrity.ListOrderNotice.None(checkKey));

        public Task<AdminOperationResult> RenumberAsync(ClaimsPrincipal user, string checkKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedClock : ProjectManagement.Services.IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 11, 8, 6, 0, 0, TimeSpan.Zero);
    }
}
