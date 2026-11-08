using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.Admin.Pages.Shared;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData.ReferenceLists;
using Xunit;

namespace ProjectManagement.Tests.Admin;

/// <summary>
/// The shared list page for reference lists (docs/ux/admin-review-3-fixes.md, owner decision 1,
/// PR 10): one rule for use, tiles and Delete. On SQLite so transactions, foreign keys and row
/// versions are real.
/// </summary>
public sealed class AdminReferenceListTests : IAsyncLifetime
{
    private const string Admin = "admin";
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private DbContextOptions<ApplicationDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options;
        await using var db = new ApplicationDbContext(_options);
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new ApplicationUser { Id = Admin, UserName = Admin, NormalizedUserName = "ADMIN" });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task Delete_IsRefusedWhileAProjectUsesTheItem()
    {
        var (usedId, _, _) = await SeedProjectTypesAsync();

        await using var db = new ApplicationDbContext(_options);
        var audit = new CapturingAdminAudit();
        var row = await db.ProjectTypes.AsNoTracking().SingleAsync(item => item.Id == usedId);
        var result = await CreateService(db, audit).DeleteAsync(ReferenceListKeys.ProjectTypes, usedId, row.RowVersion);

        Assert.False(result.Succeeded);
        Assert.Equal("EntityInUse", result.ErrorCode);
        Assert.Contains("used by 2 projects", result.UserMessage);
        Assert.Contains("Deactivate it instead", result.UserMessage);
        Assert.True(await db.ProjectTypes.AnyAsync(item => item.Id == usedId));
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task Delete_RemovesAnUnusedItem_AuditsIt_AndClosesTheOrderGap()
    {
        var (_, unusedId, inactiveId) = await SeedProjectTypesAsync();

        await using (var db = new ApplicationDbContext(_options))
        {
            var audit = new CapturingAdminAudit();
            var row = await db.ProjectTypes.AsNoTracking().SingleAsync(item => item.Id == unusedId);
            var result = await CreateService(db, audit).DeleteAsync(ReferenceListKeys.ProjectTypes, unusedId, row.RowVersion);

            Assert.True(result.Succeeded, result.UserMessage);
            Assert.Equal("Deleted 'Study'.", result.UserMessage);
            var entry = Assert.Single(audit.Entries);
            Assert.Equal("MasterData.ProjectTypeDeleted", entry.Action);
            Assert.Equal("ProjectType", entry.EntityType);
            Assert.Equal(unusedId.ToString(), entry.EntityId);
            Assert.Equal("Project type deleted", new AuditActionPresentationCatalog().Describe(entry.Action).Label);
        }

        await using var check = new ApplicationDbContext(_options);
        Assert.False(await check.ProjectTypes.AnyAsync(item => item.Id == unusedId));
        var orders = await check.ProjectTypes.OrderBy(item => item.SortOrder).Select(item => item.SortOrder).ToListAsync();
        Assert.Equal(new[] { 0, 1 }, orders);
        Assert.Equal(1, await check.ProjectTypes.Where(item => item.Id == inactiveId).Select(item => item.SortOrder).SingleAsync());
    }

    [Fact]
    public async Task Delete_WithAnOldVersion_IsRefused()
    {
        var (_, unusedId, _) = await SeedProjectTypesAsync();
        await using var db = new ApplicationDbContext(_options);

        var result = await CreateService(db, new CapturingAdminAudit())
            .DeleteAsync(ReferenceListKeys.ProjectTypes, unusedId, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        Assert.False(result.Succeeded);
        Assert.Equal("ConcurrencyConflict", result.ErrorCode);
        Assert.True(await db.ProjectTypes.AnyAsync(item => item.Id == unusedId));
    }

    [Fact]
    public async Task Delete_WithoutPermission_IsRefused()
    {
        var (_, unusedId, _) = await SeedProjectTypesAsync();
        await using var db = new ApplicationDbContext(_options);
        var row = await db.ProjectTypes.AsNoTracking().SingleAsync(item => item.Id == unusedId);

        var result = await CreateService(db, new CapturingAdminAudit(), allow: false)
            .DeleteAsync(ReferenceListKeys.ProjectTypes, unusedId, row.RowVersion);

        Assert.False(result.Succeeded);
        Assert.Equal("Forbidden", result.ErrorCode);
        Assert.True(await db.ProjectTypes.AnyAsync(item => item.Id == unusedId));
    }

    [Fact]
    public async Task Tiles_CountAllInUseAndInactive_AndEachTileFiltersTheTable()
    {
        await SeedProjectTypesAsync();
        await using var db = new ApplicationDbContext(_options);
        var service = CreateService(db, new CapturingAdminAudit());

        var all = await service.GetPageAsync(ReferenceListKeys.ProjectTypes, new ReferenceListRequest(null, null, 1, 25));
        Assert.Equal(new ReferenceListTiles(All: 3, InUse: 2, Inactive: 1), all.Tiles);
        Assert.Equal(ReferenceListStatuses.All, all.Status);
        Assert.Equal(new[] { "Build", "Retired", "Study" }, all.Rows.Select(row => row.Name));

        var build = all.Rows.Single(row => row.Name == "Build");
        Assert.Equal(2, build.UsageCount);
        Assert.Equal("2 projects", Assert.Single(build.UsedBy).Text);
        Assert.False(build.CanDelete);
        Assert.False(build.CanMoveUp);
        Assert.True(build.CanMoveDown);
        Assert.True(all.Rows.Single(row => row.Name == "Study").CanDelete);

        var inUse = await service.GetPageAsync(ReferenceListKeys.ProjectTypes, new ReferenceListRequest(null, "inuse", 1, 25));
        Assert.Equal(new[] { "Build", "Retired" }, inUse.Rows.Select(row => row.Name));
        Assert.Equal(1, inUse.Rows.Single(row => row.Name == "Retired").UsageCount);

        var inactive = await service.GetPageAsync(ReferenceListKeys.ProjectTypes, new ReferenceListRequest(null, "inactive", 1, 25));
        Assert.Equal(new[] { "Retired" }, inactive.Rows.Select(row => row.Name));

        var searched = await service.GetPageAsync(ReferenceListKeys.ProjectTypes, new ReferenceListRequest("stu", "all", 1, 25));
        Assert.Equal(new[] { "Study" }, searched.Rows.Select(row => row.Name));
    }

    [Fact]
    public async Task SponsoringUnitsAndLineDirectorates_CountTheirOwnProjectLinks()
    {
        await using (var seed = new ApplicationDbContext(_options))
        {
            var unit = new SponsoringUnit { Name = "Army HQ", SortOrder = 0 };
            var spare = new SponsoringUnit { Name = "Navy HQ", SortOrder = 1 };
            var line = new LineDirectorate { Name = "Signals", SortOrder = 0 };
            seed.AddRange(unit, spare, line);
            await seed.SaveChangesAsync();
            seed.Projects.Add(new Project { Name = "Link", CreatedByUserId = Admin, CreatedAt = DateTime.UtcNow, SponsoringUnitId = unit.Id, SponsoringLineDirectorateId = line.Id });
            await seed.SaveChangesAsync();
        }

        await using var db = new ApplicationDbContext(_options);
        var service = CreateService(db, new CapturingAdminAudit());
        var units = await service.GetPageAsync(ReferenceListKeys.SponsoringUnits, new ReferenceListRequest(null, null, 1, 25));
        var lines = await service.GetPageAsync(ReferenceListKeys.LineDirectorates, new ReferenceListRequest(null, null, 1, 25));

        Assert.Equal(new ReferenceListTiles(2, 1, 0), units.Tiles);
        Assert.Equal(new ReferenceListTiles(1, 1, 0), lines.Tiles);
        Assert.Equal("1 project", Assert.Single(units.Rows.Single(row => row.Name == "Army HQ").UsedBy).Text);

        var lineRow = await db.LineDirectorates.AsNoTracking().SingleAsync();
        var refused = await service.DeleteAsync(ReferenceListKeys.LineDirectorates, lineRow.Id, lineRow.RowVersion);
        Assert.Equal("EntityInUse", refused.ErrorCode);
    }

    public static IEnumerable<object[]> ListPages() => new[]
    {
        new object[] { "ProjectTypes", typeof(ProjectManagement.Areas.Admin.Pages.Lookups.ProjectTypes.IndexModel) },
        new object[] { "SponsoringUnits", typeof(ProjectManagement.Areas.Admin.Pages.Lookups.SponsoringUnits.IndexModel) },
        new object[] { "LineDirectorates", typeof(ProjectManagement.Areas.Admin.Pages.Lookups.LineDirectorates.IndexModel) }
    };

    [Theory]
    [MemberData(nameof(ListPages))]
    public void ThreeListPages_RenderTheSharedListPage(string folder, Type pageModel)
    {
        var view = Read($"Areas/Admin/Pages/Lookups/{folder}/Index.cshtml");
        Assert.Contains("<partial name=\"~/Areas/Admin/Pages/Shared/_AdminReferenceList.cshtml\" model=\"Model.ListModel\" />", view);
        Assert.True(typeof(AdminReferenceListPageModel).IsAssignableFrom(pageModel));

        // The policy stays on each page.
        var policy = Assert.Single(pageModel.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false).Cast<AuthorizeAttribute>());
        Assert.Equal(ProjectManagement.Configuration.AdminPolicies.MasterDataManage, policy.Policy);
    }

    [Fact]
    public void SharedListPage_HasTheTilesAndTheOneTable()
    {
        var partial = Read("Areas/Admin/Pages/Shared/_AdminReferenceList.cshtml");

        // Tiles: All, In use, Inactive.
        var labels = Regex.Matches(partial, "Label = \"([^\"]+)\"").Select(match => match.Groups[1].Value).ToArray();
        Assert.Equal(new[] { "All", "In use", "Inactive" }, labels);

        // One table: Name, [extra columns], Order, Used by, Status, Actions.
        var headers = Regex.Matches(partial, "<th scope=\"col\"[^>]*>([^<@]+)</th>").Select(match => match.Groups[1].Value).ToArray();
        Assert.Equal(new[] { "Name", "Order", "Used by", "Status", "Actions" }, headers);

        // A card on a phone (PR 8), Delete only for unused items, behind the shared confirm.
        Assert.Contains("<table class=\"admin-data-table admin-data-table--cards", partial);
        Assert.Contains("<td class=\"admin-data-table__actions\">", partial);
        foreach (var label in new[] { "Order", "Used by", "Status" })
        {
            Assert.Contains($"data-label=\"{label}\"", partial);
        }

        Assert.Contains("@if (row.CanDelete)", partial);
        Assert.Contains("data-confirm-tone=\"danger\"", partial);
    }

    [Fact]
    public void OldFlatListPartial_IsGone()
    {
        Assert.False(File.Exists(Path.Combine(FindRepositoryRoot(), "Areas/Admin/Pages/Shared/_AdminFlatLookupDirectory.cshtml")));
    }

    private async Task<(int UsedId, int UnusedId, int InactiveId)> SeedProjectTypesAsync()
    {
        await using var db = new ApplicationDbContext(_options);
        var build = new ProjectType { Name = "Build", SortOrder = 0 };
        var study = new ProjectType { Name = "Study", SortOrder = 2 };
        var retired = new ProjectType { Name = "Retired", SortOrder = 1, IsActive = false };
        db.ProjectTypes.AddRange(build, study, retired);
        await db.SaveChangesAsync();
        db.Projects.AddRange(
            new Project { Name = "One", CreatedByUserId = Admin, CreatedAt = DateTime.UtcNow, ProjectTypeId = build.Id },
            new Project { Name = "Two", CreatedByUserId = Admin, CreatedAt = DateTime.UtcNow, ProjectTypeId = build.Id },
            new Project { Name = "Old", CreatedByUserId = Admin, CreatedAt = DateTime.UtcNow, ProjectTypeId = retired.Id });
        await db.SaveChangesAsync();
        return (build.Id, study.Id, retired.Id);
    }

    private static ReferenceListService CreateService(ApplicationDbContext db, IAdminAuditService audit, bool allow = true)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, Admin) }, "test"))
        };

        return new ReferenceListService(db, audit, new FixedAuthorization(allow), new HttpContextAccessor { HttpContext = http });
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

    private sealed class CapturingAdminAudit : IAdminAuditService
    {
        public List<AdminAuditEntry> Entries { get; } = new();

        public Task RecordAsync(AdminAuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
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
