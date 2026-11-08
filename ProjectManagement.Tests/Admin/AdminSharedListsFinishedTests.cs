using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ProjectManagement.Areas.Admin.Pages.Lookups;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData;
using ProjectManagement.Services.Admin.MasterData.Integrity;
using ProjectManagement.Services.Admin.MasterData.ReferenceLists;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests.Admin;

/// <summary>
/// The shared lists, finished (docs/ux/admin-review-4-fixes.md, PR 4): change requests read once
/// per page (C3), the three lookups switched off and on in place with one Add and Edit form (C5).
/// On SQLite so row versions and transactions are real.
/// </summary>
public sealed class AdminSharedListsFinishedTests : IAsyncLifetime
{
    private const string Admin = "admin";
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly ChangeRequestReads _reads = new();
    private DbContextOptions<ApplicationDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_reads)
            .Options;
        await using var db = new ApplicationDbContext(_options);
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new ApplicationUser { Id = Admin, UserName = Admin, NormalizedUserName = "ADMIN" });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    // C3: the tiles, the In use filter and the Used by column all count change requests. Where
    // the database cannot read the payload they are loaded once for the page, not three times.
    [Fact]
    public async Task ChangeRequests_AreReadOncePerPage_WhereTheDatabaseCannotReadThePayload()
    {
        await using (var seed = new ApplicationDbContext(_options))
        {
            var type = new ProjectType { Name = "Prototype", SortOrder = 0 };
            var project = new Project { Name = "Radar", CreatedByUserId = Admin, CreatedAt = DateTime.UtcNow };
            seed.AddRange(type, project, new ProjectType { Name = "Study", SortOrder = 1 });
            await seed.SaveChangesAsync();
            seed.ProjectMetaChangeRequests.Add(Request(project.Id, $"{{\"name\":\"Radar\",\"projectTypeId\":{type.Id}}}"));
            await seed.SaveChangesAsync();
        }

        await using var db = new ApplicationDbContext(_options);
        _reads.Count = 0;
        var page = await CreateService(db).GetPageAsync(ReferenceListKeys.ProjectTypes, new ReferenceListRequest(null, "inuse", 1, 25));

        var row = Assert.Single(page.Rows);
        Assert.Equal("Prototype", row.Name);
        Assert.Equal(new[] { "0 projects", "1 change request" }, row.UsedBy.Select(item => item.Text));
        Assert.Equal(1, page.Tiles.InUse);
        Assert.Equal(1, _reads.Count);
    }

    public static IEnumerable<object[]> Lookups() => new[]
    {
        new object[] { "ProjectTypes" },
        new object[] { "SponsoringUnits" },
        new object[] { "LineDirectorates" }
    };

    // C5: the three lookups switch off and on in place, with the state asked for and the version
    // the page showed, like the other lists.
    [Theory]
    [MemberData(nameof(Lookups))]
    public async Task Lookups_SwitchOffAndOnInPlace(string folder)
    {
        var id = await SeedAsync(folder, "Alpha");

        await using var db = new ApplicationDbContext(_options);
        var page = IndexPage(folder, db);
        var result = await page.OnPostToggleAsync(id, false, await VersionAsync(db, folder, id), CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Null(page.TempData[FlashMessageKeys.AdminMasterDataError]);
        Assert.False(await IsActiveAsync(folder, id));
        Assert.NotNull(page.TempData[FlashMessageKeys.AdminMasterDataSuccess]);

        // An out-of-date version is refused and changes nothing.
        page = IndexPage(folder, new ApplicationDbContext(_options));
        await page.OnPostToggleAsync(id, true, Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }), CancellationToken.None);
        Assert.False(await IsActiveAsync(folder, id));
        Assert.NotNull(page.TempData[FlashMessageKeys.AdminMasterDataError]);

        // A form without a state is refused rather than flipped.
        await using var again = new ApplicationDbContext(_options);
        page = IndexPage(folder, again);
        await page.OnPostToggleAsync(id, null, await VersionAsync(again, folder, id), CancellationToken.None);
        Assert.False(await IsActiveAsync(folder, id));

        await using var on = new ApplicationDbContext(_options);
        page = IndexPage(folder, on);
        await page.OnPostToggleAsync(id, true, await VersionAsync(on, folder, id), CancellationToken.None);
        Assert.True(await IsActiveAsync(folder, id));
    }

    [Theory]
    [MemberData(nameof(Lookups))]
    public async Task OldDeactivatePage_RedirectsToTheList_AndAFormLeftOpenStillWorks(string folder)
    {
        var id = await SeedAsync(folder, "Alpha");
        await using var db = new ApplicationDbContext(_options);

        var opened = new LookupDeactivateModel(CreateCommands(db)) { Id = id };
        Configure(opened, $"/Lookups/{folder}/Deactivate");
        var redirect = Assert.IsType<RedirectToPageResult>(opened.OnGet());
        Assert.Equal("./Index", redirect.PageName);

        var posted = new LookupDeactivateModel(CreateCommands(db))
        {
            Id = id,
            Restore = false,
            RowVersion = Convert.FromBase64String(await VersionAsync(db, folder, id))
        };
        Configure(posted, $"/Lookups/{folder}/Deactivate");
        redirect = Assert.IsType<RedirectToPageResult>(await posted.OnPostAsync(CancellationToken.None));
        Assert.Equal("./Index", redirect.PageName);
        Assert.False(await IsActiveAsync(folder, id));
    }

    [Theory]
    [MemberData(nameof(Lookups))]
    public async Task OneAddAndEditForm_ServesEachLookup(string folder)
    {
        await using (var db = new ApplicationDbContext(_options))
        {
            var add = new LookupItemModel(CreateCommands(db), CreateService(db))
            {
                Input = new LookupItemModel.InputModel { Name = "Alpha" }
            };
            Configure(add, $"/Lookups/{folder}/Create");
            Assert.IsType<RedirectToPageResult>(await add.OnPostAsync(null, CancellationToken.None));
            Assert.Equal($"Add {LookupLists.FromPagePath($"/Lookups/{folder}/Create")!.Label}", add.Heading);
        }

        var id = await IdAsync(folder, "Alpha");
        await using (var db = new ApplicationDbContext(_options))
        {
            var edit = new LookupItemModel(CreateCommands(db), CreateService(db));
            Configure(edit, $"/Lookups/{folder}/Edit");
            Assert.IsType<PageResult>(await edit.OnGetAsync(id, CancellationToken.None));
            Assert.True(edit.IsEdit);
            Assert.Equal("Alpha", edit.Input.Name);
            Assert.Equal("Edit Alpha", edit.Heading);

            edit.Input.Name = "Bravo";
            Assert.IsType<RedirectToPageResult>(await edit.OnPostAsync(id, CancellationToken.None));
        }

        Assert.Equal(id, await IdAsync(folder, "Bravo"));

        // A duplicate name is refused on the form.
        await using (var db = new ApplicationDbContext(_options))
        {
            var add = new LookupItemModel(CreateCommands(db), CreateService(db))
            {
                Input = new LookupItemModel.InputModel { Name = "bravo" }
            };
            Configure(add, $"/Lookups/{folder}/Create");
            Assert.IsType<PageResult>(await add.OnPostAsync(null, CancellationToken.None));
            Assert.True(add.ModelState.ContainsKey("Input.Name"));
        }

        // Editing an item that is gone is a 404.
        await using (var db = new ApplicationDbContext(_options))
        {
            var edit = new LookupItemModel(CreateCommands(db), CreateService(db));
            Configure(edit, $"/Lookups/{folder}/Edit");
            Assert.IsType<NotFoundResult>(await edit.OnGetAsync(99999, CancellationToken.None));
        }
    }

    [Theory]
    [MemberData(nameof(Lookups))]
    public void LookupPages_KeepTheirFilesAndRoutes_ButShareOneModel(string folder)
    {
        var root = FindRepositoryRoot();
        var create = File.ReadAllText(Path.Combine(root, $"Areas/Admin/Pages/Lookups/{folder}/Create.cshtml"));
        var edit = File.ReadAllText(Path.Combine(root, $"Areas/Admin/Pages/Lookups/{folder}/Edit.cshtml"));
        var deactivate = File.ReadAllText(Path.Combine(root, $"Areas/Admin/Pages/Lookups/{folder}/Deactivate.cshtml"));

        Assert.StartsWith("@page\n", create.ReplaceLineEndings("\n"));
        Assert.StartsWith("@page \"{id:int}\"\n", edit.ReplaceLineEndings("\n"));
        Assert.Contains("@model ProjectManagement.Areas.Admin.Pages.Lookups.LookupItemModel", create);
        Assert.Contains("@model ProjectManagement.Areas.Admin.Pages.Lookups.LookupItemModel", edit);
        Assert.Contains("@model ProjectManagement.Areas.Admin.Pages.Lookups.LookupDeactivateModel", deactivate);
        foreach (var page in new[] { "Create", "Edit", "Deactivate" })
        {
            Assert.False(File.Exists(Path.Combine(root, $"Areas/Admin/Pages/Lookups/{folder}/{page}.cshtml.cs")));
        }

        // The shared models read no database directly and keep the list policy.
        var source = File.ReadAllText(Path.Combine(root, "Areas/Admin/Pages/Lookups/LookupPages.cs"));
        Assert.DoesNotContain("ApplicationDbContext", source);
        foreach (var model in new[] { typeof(LookupItemModel), typeof(LookupDeactivateModel) })
        {
            var policy = Assert.Single(model.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false).Cast<AuthorizeAttribute>());
            Assert.Equal(AdminPolicies.MasterDataManage, policy.Policy);
        }
    }

    [Fact]
    public void PagePath_PicksTheList()
    {
        Assert.Same(LookupLists.ProjectTypes, LookupLists.FromPagePath("/Lookups/ProjectTypes/Edit"));
        Assert.Same(LookupLists.SponsoringUnits, LookupLists.FromPagePath("/Lookups/SponsoringUnits/Create"));
        Assert.Same(LookupLists.LineDirectorates, LookupLists.FromPagePath("/Lookups/LineDirectorates/Deactivate"));
        Assert.Null(LookupLists.FromPagePath("/Categories/Edit"));
        Assert.Null(LookupLists.FromPagePath(null));
    }

    private static ProjectMetaChangeRequest Request(int projectId, string payload) => new()
    {
        ProjectId = projectId,
        ChangeType = "Meta",
        Payload = payload,
        OriginalName = "Radar",
        DecisionStatus = "Pending"
    };

    private async Task<int> SeedAsync(string folder, string name)
    {
        await using var db = new ApplicationDbContext(_options);
        switch (folder)
        {
            case "ProjectTypes":
                db.ProjectTypes.Add(new ProjectType { Name = name, SortOrder = 0 });
                break;
            case "SponsoringUnits":
                db.SponsoringUnits.Add(new SponsoringUnit { Name = name, SortOrder = 0, IsActive = true });
                break;
            default:
                db.LineDirectorates.Add(new LineDirectorate { Name = name, SortOrder = 0, IsActive = true });
                break;
        }

        await db.SaveChangesAsync();
        return await IdAsync(folder, name);
    }

    private async Task<int> IdAsync(string folder, string name)
    {
        await using var db = new ApplicationDbContext(_options);
        return folder switch
        {
            "ProjectTypes" => await db.ProjectTypes.Where(item => item.Name == name).Select(item => item.Id).SingleAsync(),
            "SponsoringUnits" => await db.SponsoringUnits.Where(item => item.Name == name).Select(item => item.Id).SingleAsync(),
            _ => await db.LineDirectorates.Where(item => item.Name == name).Select(item => item.Id).SingleAsync()
        };
    }

    private async Task<bool> IsActiveAsync(string folder, int id)
    {
        await using var db = new ApplicationDbContext(_options);
        return folder switch
        {
            "ProjectTypes" => await db.ProjectTypes.Where(item => item.Id == id).Select(item => item.IsActive).SingleAsync(),
            "SponsoringUnits" => await db.SponsoringUnits.Where(item => item.Id == id).Select(item => item.IsActive).SingleAsync(),
            _ => await db.LineDirectorates.Where(item => item.Id == id).Select(item => item.IsActive).SingleAsync()
        };
    }

    private static async Task<string> VersionAsync(ApplicationDbContext db, string folder, int id)
    {
        var key = LookupLists.FromPagePath($"/Lookups/{folder}/Index")!.Definition.Key;
        var row = await CreateService(db).GetItemAsync(key, id);
        return row!.RowVersion;
    }

    private static ProjectManagement.Areas.Admin.Pages.Shared.AdminReferenceListPageModel IndexPage(string folder, ApplicationDbContext db)
    {
        var commands = CreateCommands(db);
        var lists = CreateService(db);
        var notices = new ListOrderNoticeService(new AdminMasterDataIntegrityService(db, new CapturingAdminAudit(), new AdminTimeService(FakeClock.AtUtc(DateTimeOffset.UtcNow))), new FixedAuthorization(true));
        ProjectManagement.Areas.Admin.Pages.Shared.AdminReferenceListPageModel page = folder switch
        {
            "ProjectTypes" => new ProjectManagement.Areas.Admin.Pages.Lookups.ProjectTypes.IndexModel(lists, commands, notices),
            "SponsoringUnits" => new ProjectManagement.Areas.Admin.Pages.Lookups.SponsoringUnits.IndexModel(lists, commands, notices),
            _ => new ProjectManagement.Areas.Admin.Pages.Lookups.LineDirectorates.IndexModel(lists, commands, notices)
        };
        Configure(page, $"/Lookups/{folder}/Index");
        return page;
    }

    private static void Configure(PageModel page, string viewEnginePath)
    {
        var httpContext = new DefaultHttpContext { User = AdminUser() };
        var descriptor = new CompiledPageActionDescriptor { ViewEnginePath = viewEnginePath, RelativePath = $"/Areas/Admin/Pages{viewEnginePath}.cshtml" };
        var actionContext = new ActionContext(httpContext, new RouteData(), descriptor);
        page.PageContext = new PageContext(actionContext)
        {
            ActionDescriptor = descriptor,
            ViewData = new ViewDataDictionary(new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(), new ModelStateDictionary())
        };
        page.TempData = new TempDataDictionary(httpContext, new DictionaryTempDataProvider());
    }

    private static ClaimsPrincipal AdminUser() =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, Admin) }, "test"));

    private static ReferenceListService CreateService(ApplicationDbContext db) =>
        new(db, new CapturingAdminAudit(), new FixedAuthorization(true), new PlainHttpContextAccessor { HttpContext = new DefaultHttpContext { User = AdminUser() } });

    private static AdminMasterDataCommandService CreateCommands(ApplicationDbContext db) =>
        new(
            db,
            new AdminHierarchyValidationService(db),
            new CapturingAdminAudit(),
            new FixedAuthorization(true),
            new PlainHttpContextAccessor { HttpContext = new DefaultHttpContext { User = AdminUser() } },
            FakeClock.AtUtc(new DateTimeOffset(2026, 11, 8, 6, 0, 0, TimeSpan.Zero)));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProjectManagement.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    /// <summary>Counts the queries that read the change requests table.</summary>
    private sealed class ChangeRequestReads : DbCommandInterceptor
    {
        public int Count;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("ProjectMetaChangeRequests", StringComparison.Ordinal)
                && command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref Count);
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    // HttpContextAccessor keeps one slot per async flow, and setting a second one clears the
    // first; the page, the list service and the commands each get their own here.
    private sealed class PlainHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class DictionaryTempDataProvider : ITempDataProvider
    {
        private Dictionary<string, object> _data = new();

        public IDictionary<string, object> LoadTempData(HttpContext context) => _data;

        public void SaveTempData(HttpContext context, IDictionary<string, object> values) => _data = new Dictionary<string, object>(values);
    }

    private sealed class CapturingAdminAudit : IAdminAuditService
    {
        public Task RecordAsync(AdminAuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedAuthorization : IAuthorizationService
    {
        private readonly bool _allow;

        public FixedAuthorization(bool allow) => _allow = allow;

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(_allow ? AuthorizationResult.Success() : AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) =>
            Task.FromResult(_allow ? AuthorizationResult.Success() : AuthorizationResult.Failed());
    }
}
