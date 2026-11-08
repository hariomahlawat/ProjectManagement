using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.Admin.Pages.Shared;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Activities;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData.ReferenceLists;
using Xunit;

namespace ProjectManagement.Tests.Admin;

/// <summary>
/// PR 11 (docs/ux/admin-review-3-fixes.md, owner decision 1): project and technical categories,
/// activity types and the ARPP values on the shared list page, each with its own usage count in
/// the one Delete rule. On SQLite so transactions, foreign keys and row versions are real.
/// </summary>
public sealed class AdminReferenceListMoreListsTests : IAsyncLifetime
{
    private const string Admin = "admin";
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private DbContextOptions<ApplicationDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _connection.CreateFunction("btrim", (string? value) => value?.Trim());
        _options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options;
        await using var db = new ApplicationDbContext(_options);
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new ApplicationUser { Id = Admin, UserName = Admin, NormalizedUserName = "ADMIN" });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    // ---- Project categories: a tree ----

    [Fact]
    public async Task ProjectCategories_ListParentsBeforeChildren_IndentedAndNamed()
    {
        var ids = await SeedProjectCategoryTreeAsync();
        await using var db = new ApplicationDbContext(_options);
        var service = CreateService(db, new CapturingAdminAudit());

        var page = await service.GetPageAsync(ReferenceListKeys.ProjectCategories, new ReferenceListRequest(null, null, 1, 25));
        Assert.Equal(new[] { "Defence", "Radar", "Sonar", "Training" }, page.Rows.Select(row => row.Name));
        Assert.Equal(new[] { 0, 1, 1, 0 }, page.Rows.Select(row => row.Depth));
        Assert.Equal("Defence", page.Rows.Single(row => row.Name == "Sonar").ParentPath);

        // The order is kept per parent: Radar can only move down, Sonar only up.
        var radar = page.Rows.Single(row => row.Name == "Radar");
        Assert.False(radar.CanMoveUp);
        Assert.True(radar.CanMoveDown);
        Assert.False(page.Rows.Single(row => row.Name == "Sonar").CanMoveDown);
        Assert.Equal(1, radar.ShownOrder);

        // A parent counts its sub-categories as use; Training is used by a project.
        var defence = page.Rows.Single(row => row.Name == "Defence");
        Assert.Contains("2 sub-categories", defence.UsedBy.Select(item => item.Text));
        Assert.False(defence.CanDelete);
        Assert.Equal("1 project", page.Rows.Single(row => row.Name == "Training").UsedBy.Single(item => item.Count > 0).Text);
        Assert.Equal(new ReferenceListTiles(All: 4, InUse: 2, Inactive: 0), page.Tiles);

        // With a search the rows are not indented, and each child still names its parent.
        var searched = await service.GetPageAsync(ReferenceListKeys.ProjectCategories, new ReferenceListRequest("son", null, 1, 25));
        var sonar = Assert.Single(searched.Rows);
        Assert.Equal(0, sonar.Depth);
        Assert.Equal("Defence", sonar.ParentPath);

        // Searching a parent's name finds its children too.
        var byParent = await service.GetPageAsync(ReferenceListKeys.ProjectCategories, new ReferenceListRequest("defence", null, 1, 25));
        Assert.Equal(new[] { "Defence", "Radar", "Sonar" }, byParent.Rows.Select(row => row.Name));

        var inUse = await service.GetPageAsync(ReferenceListKeys.ProjectCategories, new ReferenceListRequest(null, "inuse", 1, 25));
        Assert.Equal(new[] { "Defence", "Training" }, inUse.Rows.Select(row => row.Name));
        _ = ids;
    }

    [Fact]
    public async Task ProjectCategories_DeleteRefusesAParentAndAUsedItem_AndClosesOnlyTheSiblingGap()
    {
        var ids = await SeedProjectCategoryTreeAsync();
        var audit = new CapturingAdminAudit();

        await using (var db = new ApplicationDbContext(_options))
        {
            var service = CreateService(db, audit);
            var parent = await db.ProjectCategories.AsNoTracking().SingleAsync(item => item.Id == ids.Defence);
            var refused = await service.DeleteAsync(ReferenceListKeys.ProjectCategories, ids.Defence, parent.RowVersion);
            Assert.Equal("EntityInUse", refused.ErrorCode);
            Assert.Contains("used by 2 sub-categories", refused.UserMessage);

            var training = await db.ProjectCategories.AsNoTracking().SingleAsync(item => item.Id == ids.Training);
            Assert.Equal("EntityInUse", (await service.DeleteAsync(ReferenceListKeys.ProjectCategories, ids.Training, training.RowVersion)).ErrorCode);
        }

        await using (var db = new ApplicationDbContext(_options))
        {
            var radar = await db.ProjectCategories.AsNoTracking().SingleAsync(item => item.Id == ids.Radar);
            var deleted = await CreateService(db, audit).DeleteAsync(ReferenceListKeys.ProjectCategories, ids.Radar, radar.RowVersion);
            Assert.True(deleted.Succeeded, deleted.UserMessage);
        }

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("MasterData.ProjectCategoryDeleted", entry.Action);

        await using var check = new ApplicationDbContext(_options);
        // Sonar closes up under Defence; the top level is left as it was.
        Assert.Equal(0, await check.ProjectCategories.Where(item => item.Id == ids.Sonar).Select(item => item.SortOrder).SingleAsync());
        Assert.Equal(1, await check.ProjectCategories.Where(item => item.Id == ids.Training).Select(item => item.SortOrder).SingleAsync());
    }

    [Theory]
    [InlineData("legacy")]
    [InlineData("original")]
    [InlineData("proposed")]
    public async Task ProjectCategories_LegacyImportsAndChangeRequestsCountAsUse(string how)
    {
        var ids = await SeedProjectCategoryTreeAsync();
        await using (var seed = new ApplicationDbContext(_options))
        {
            if (how == "legacy")
            {
                var technical = new TechnicalCategory { Name = "Optics", CreatedByUserId = Admin, IsActive = true };
                seed.TechnicalCategories.Add(technical);
                await seed.SaveChangesAsync();
                seed.ProjectLegacyImports.Add(new ProjectLegacyImport { ProjectCategoryId = ids.Sonar, TechnicalCategoryId = technical.Id, ImportedByUserId = Admin });
            }
            else
            {
                var project = await seed.Projects.FirstAsync();
                seed.ProjectMetaChangeRequests.Add(new ProjectMetaChangeRequest
                {
                    ProjectId = project.Id,
                    ChangeType = "Meta",
                    Payload = how == "proposed" ? $"{{\"name\":\"Trainer\",\"categoryId\":{ids.Sonar}}}" : "{\"name\":\"Trainer\"}",
                    OriginalName = "Trainer",
                    OriginalCategoryId = how == "original" ? ids.Sonar : null,
                    DecisionStatus = "Pending"
                });
            }

            await seed.SaveChangesAsync();
        }

        await using var db = new ApplicationDbContext(_options);
        var service = CreateService(db, new CapturingAdminAudit());
        var sonar = await service.GetItemAsync(ReferenceListKeys.ProjectCategories, ids.Sonar);
        Assert.False(sonar!.CanDelete);
        Assert.Equal(how == "legacy" ? "1 legacy import" : "1 change request", sonar.UsedBy.Single(item => item.Count > 0).Text);

        var stored = await db.ProjectCategories.AsNoTracking().SingleAsync(item => item.Id == ids.Sonar);
        Assert.Equal("EntityInUse", (await service.DeleteAsync(ReferenceListKeys.ProjectCategories, ids.Sonar, stored.RowVersion)).ErrorCode);
        Assert.True(await db.ProjectCategories.AnyAsync(item => item.Id == ids.Sonar));
    }

    // ---- Technical categories ----

    [Theory]
    [InlineData("column")]
    [InlineData("original")]
    [InlineData("proposed")]
    [InlineData("project")]
    public async Task TechnicalCategories_ProjectsAndChangeRequestsCountAsUse(string how)
    {
        int categoryId;
        await using (var seed = new ApplicationDbContext(_options))
        {
            var category = new TechnicalCategory { Name = "Sensors", CreatedByUserId = Admin, IsActive = true };
            var spare = new TechnicalCategory { Name = "Spare", CreatedByUserId = Admin, IsActive = true, SortOrder = 1 };
            seed.TechnicalCategories.AddRange(category, spare);
            await seed.SaveChangesAsync();
            categoryId = category.Id;
            var project = new Project { Name = "Radar", CreatedByUserId = Admin, CreatedAt = DateTime.UtcNow, TechnicalCategoryId = how == "project" ? category.Id : null };
            seed.Projects.Add(project);
            await seed.SaveChangesAsync();
            if (how != "project")
            {
                seed.ProjectMetaChangeRequests.Add(new ProjectMetaChangeRequest
                {
                    ProjectId = project.Id,
                    ChangeType = "Meta",
                    Payload = how == "proposed" ? $"{{\"name\":\"Radar\",\"technicalCategoryId\":{category.Id}}}" : "{\"name\":\"Radar\"}",
                    OriginalName = "Radar",
                    OriginalTechnicalCategoryId = how == "original" ? category.Id : null,
                    TechnicalCategoryId = how == "column" ? category.Id : null,
                    DecisionStatus = "Rejected"
                });
                await seed.SaveChangesAsync();
            }
        }

        await using var db = new ApplicationDbContext(_options);
        var service = CreateService(db, new CapturingAdminAudit());
        var page = await service.GetPageAsync(ReferenceListKeys.TechnicalCategories, new ReferenceListRequest(null, "inuse", 1, 25));
        var row = Assert.Single(page.Rows);
        Assert.Equal(how == "project" ? "1 project" : "1 change request", row.UsedBy.Single(item => item.Count > 0).Text);

        var stored = await db.TechnicalCategories.AsNoTracking().SingleAsync(item => item.Id == categoryId);
        Assert.Equal("EntityInUse", (await service.DeleteAsync(ReferenceListKeys.TechnicalCategories, categoryId, stored.RowVersion)).ErrorCode);
    }

    // ---- Activity types: no stored order ----

    [Fact]
    public async Task ActivityTypes_AreListedAToZ_CountEveryActivity_AndDeleteOnlyUnusedOnes()
    {
        int usedId, unusedId;
        await using (var seed = new ApplicationDbContext(_options))
        {
            seed.ActivityTypes.RemoveRange(seed.ActivityTypes); // the seeded types
            var visit = new ActivityType { Name = "Visit", Description = "Visits by guests", CreatedByUserId = Admin };
            var briefing = new ActivityType { Name = "Briefing", CreatedByUserId = Admin };
            var drill = new ActivityType { Name = "Drill", CreatedByUserId = Admin, IsActive = false };
            seed.ActivityTypes.AddRange(visit, briefing, drill);
            await seed.SaveChangesAsync();
            (usedId, unusedId) = (visit.Id, briefing.Id);
            // A deleted activity still points at its type, so it counts too.
            seed.Activities.Add(new Activity { Title = "Old visit", ActivityTypeId = visit.Id, CreatedByUserId = Admin, IsDeleted = true });
            await seed.SaveChangesAsync();
        }

        var audit = new CapturingAdminAudit();
        await using (var db = new ApplicationDbContext(_options))
        {
            var service = CreateService(db, audit);
            var page = await service.GetPageAsync(ReferenceListKeys.ActivityTypes, new ReferenceListRequest(null, null, 1, 25));
            Assert.Equal(new[] { "Briefing", "Drill", "Visit" }, page.Rows.Select(row => row.Name));
            Assert.Equal(new ReferenceListTiles(3, 1, 1), page.Tiles);
            var visit = page.Rows.Single(row => row.Name == "Visit");
            Assert.Equal("1 activity", visit.UsedBy.Single().Text);
            Assert.Equal("Visits by guests", visit.Detail);
            Assert.False(ReferenceListCatalog.ActivityTypes.HasOrder);

            // Search reads the description too.
            var searched = await service.GetPageAsync(ReferenceListKeys.ActivityTypes, new ReferenceListRequest("guests", null, 1, 25));
            Assert.Equal("Visit", Assert.Single(searched.Rows).Name);

            var used = await db.ActivityTypes.AsNoTracking().SingleAsync(item => item.Id == usedId);
            var refused = await service.DeleteAsync(ReferenceListKeys.ActivityTypes, usedId, used.RowVersion);
            Assert.Equal("EntityInUse", refused.ErrorCode);
            Assert.Contains("used by 1 activity", refused.UserMessage);

            var unused = await db.ActivityTypes.AsNoTracking().SingleAsync(item => item.Id == unusedId);
            var deleted = await service.DeleteAsync(ReferenceListKeys.ActivityTypes, unusedId, unused.RowVersion);
            Assert.True(deleted.Succeeded, deleted.UserMessage);
        }

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("MasterData.ActivityTypeDeleted", entry.Action);
        Assert.Equal("Activity type deleted", new AuditActionPresentationCatalog().Describe(entry.Action).Label);
        await using var check = new ApplicationDbContext(_options);
        Assert.False(await check.ActivityTypes.AnyAsync(item => item.Id == unusedId));
    }

    [Fact]
    public async Task ActivityTypes_DeleteWithoutTheActivityTypesPermission_IsRefused()
    {
        int id;
        await using (var seed = new ApplicationDbContext(_options))
        {
            var type = new ActivityType { Name = "Spare", CreatedByUserId = Admin };
            seed.ActivityTypes.Add(type);
            await seed.SaveChangesAsync();
            id = type.Id;
        }

        await using var db = new ApplicationDbContext(_options);
        var authorization = new PolicyAuthorization(AdminPolicies.MasterDataManage);
        var stored = await db.ActivityTypes.AsNoTracking().SingleAsync(item => item.Id == id);
        var result = await CreateService(db, new CapturingAdminAudit(), authorization).DeleteAsync(ReferenceListKeys.ActivityTypes, id, stored.RowVersion);

        Assert.Equal("Forbidden", result.ErrorCode);
        Assert.Equal(AdminPolicies.ActivityTypesManage, Assert.Single(authorization.Asked));
        Assert.True(await db.ActivityTypes.AnyAsync(item => item.Id == id));
    }

    // ---- ARPP values: an entry's link would be emptied, so the count is the only guard ----

    [Fact]
    public async Task ArppValues_AnEntryKeepsItsValue_AndAnUnusedValueIsDeletedWithoutRenumbering()
    {
        int usedId, unusedId, laterId, scheduleId;
        await using (var seed = new ApplicationDbContext(_options))
        {
            var now = DateTimeOffset.UtcNow;
            var used = new ArppCfaOption { Name = "CFA One", NormalizedName = "CFA ONE", SortOrder = 10, CreatedAtUtc = now, UpdatedAtUtc = now, CreatedByUserId = Admin, UpdatedByUserId = Admin };
            var unused = new ArppCfaOption { Name = "CFA Two", NormalizedName = "CFA TWO", SortOrder = 20, CreatedAtUtc = now, UpdatedAtUtc = now, CreatedByUserId = Admin, UpdatedByUserId = Admin };
            var later = new ArppCfaOption { Name = "CFA Three", NormalizedName = "CFA THREE", SortOrder = 30, CreatedAtUtc = now, UpdatedAtUtc = now, CreatedByUserId = Admin, UpdatedByUserId = Admin };
            var schedule = new ArppDfpdsSchedule { Code = "Sch 4", NormalizedCode = "SCH 4", Description = "Works", SortOrder = 1, CreatedAtUtc = now, UpdatedAtUtc = now, CreatedByUserId = Admin, UpdatedByUserId = Admin };
            var issue = new ArppIssue { Name = "ARPP 2026-27", FinancialYearStart = 2026, Kind = ArppIssueKind.Original, IssueSequence = 0, IssueDate = new DateOnly(2026, 4, 1), CreatedAtUtc = now, UpdatedAtUtc = now, CreatedByUserId = Admin, UpdatedByUserId = Admin };
            seed.AddRange(used, unused, later, schedule, issue);
            await seed.SaveChangesAsync();
            seed.ArppEntries.Add(new ArppEntry { ArppIssueId = issue.Id, ProjectReference = "P-1", Category = ArppCategory.New, SerialNumber = "1", Fund = "Capital", CfaOptionId = used.Id, Cfa = used.Name, DfpdsScheduleId = schedule.Id, DfpdsSchedule = schedule.Code, CreatedAtUtc = now, UpdatedAtUtc = now, CreatedByUserId = Admin, UpdatedByUserId = Admin });
            await seed.SaveChangesAsync();
            (usedId, unusedId, laterId, scheduleId) = (used.Id, unused.Id, later.Id, schedule.Id);
        }

        var audit = new CapturingAdminAudit();
        await using (var db = new ApplicationDbContext(_options))
        {
            var service = CreateService(db, audit);
            var page = await service.GetPageAsync(ReferenceListKeys.ArppCfa, new ReferenceListRequest(null, null, 1, 25));
            Assert.Equal(new[] { "CFA One", "CFA Two", "CFA Three" }, page.Rows.Select(row => row.Name));
            Assert.Equal(new[] { 10, 20, 30 }, page.Rows.Select(row => row.ShownOrder));
            Assert.Equal("1 ARPP entry", page.Rows[0].UsedBy.Single().Text);
            Assert.False(page.Rows[0].CanDelete);

            var schedules = await service.GetPageAsync(ReferenceListKeys.ArppDfpdsSchedules, new ReferenceListRequest(null, null, 1, 25));
            var schedule = Assert.Single(schedules.Rows);
            Assert.Equal(("Sch 4", "Works"), (schedule.Name, schedule.Detail));
            Assert.False(schedule.CanDelete);

            var used = await db.ArppCfaOptions.AsNoTracking().SingleAsync(item => item.Id == usedId);
            Assert.Equal("EntityInUse", (await service.DeleteAsync(ReferenceListKeys.ArppCfa, usedId, used.RowVersion)).ErrorCode);
            var unused = await db.ArppCfaOptions.AsNoTracking().SingleAsync(item => item.Id == unusedId);
            Assert.True((await service.DeleteAsync(ReferenceListKeys.ArppCfa, unusedId, unused.RowVersion)).Succeeded);
        }

        Assert.Equal("MasterData.ArppReferenceDeleted", Assert.Single(audit.Entries).Action);
        await using var check = new ApplicationDbContext(_options);
        Assert.Equal(usedId, await check.ArppEntries.Select(entry => entry.CfaOptionId).SingleAsync());
        Assert.Equal(30, await check.ArppCfaOptions.Where(item => item.Id == laterId).Select(item => item.SortOrder).SingleAsync());
        Assert.True(await check.ArppDfpdsSchedules.AnyAsync(item => item.Id == scheduleId));
    }

    // ---- The pages ----

    public static IEnumerable<object[]> ListPages() => new[]
    {
        new object[] { "Areas/Admin/Pages/Categories/Index.cshtml", typeof(ProjectManagement.Areas.Admin.Pages.Categories.IndexModel), AdminPolicies.MasterDataManage },
        new object[] { "Areas/Admin/Pages/TechnicalCategories/Index.cshtml", typeof(ProjectManagement.Areas.Admin.Pages.TechnicalCategories.IndexModel), AdminPolicies.MasterDataManage },
        new object[] { "Areas/Admin/Pages/ActivityTypes/Index.cshtml", typeof(ProjectManagement.Areas.Admin.Pages.ActivityTypes.IndexModel), AdminPolicies.ActivityTypesManage },
        new object[] { "Areas/Admin/Pages/MasterData/ArppReferences/Index.cshtml", typeof(ProjectManagement.Areas.Admin.Pages.MasterData.ArppReferences.IndexModel), AdminPolicies.MasterDataManage }
    };

    [Theory]
    [MemberData(nameof(ListPages))]
    public void EachPage_RendersTheSharedListPage_AndKeepsItsPolicy(string view, Type pageModel, string policy)
    {
        var text = Read(view);
        Assert.Contains("<partial name=\"~/Areas/Admin/Pages/Shared/_AdminReferenceList.cshtml\" model=\"Model.ListModel\" />", text);
        Assert.True(typeof(AdminReferenceListPageModel).IsAssignableFrom(pageModel));

        var attribute = Assert.Single(pageModel.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false).Cast<AuthorizeAttribute>());
        Assert.Equal(policy, attribute.Policy);
    }

    [Fact]
    public void OldHandlersAndRoutes_StayInPlace()
    {
        // Deactivate in place keeps the Toggle handler on categories and activity types; the
        // ARPP page keeps Save and SetActive; the category Delete pages keep their routes.
        Assert.NotNull(typeof(AdminReferenceListPageModel).GetMethod("OnPostToggleAsync"));
        var arpp = typeof(ProjectManagement.Areas.Admin.Pages.MasterData.ArppReferences.IndexModel);
        Assert.NotNull(arpp.GetMethod("OnPostSaveAsync"));
        Assert.NotNull(arpp.GetMethod("OnPostSetActiveAsync"));
        Assert.StartsWith("@page \"{id:int}\"", Read("Areas/Admin/Pages/Categories/Delete.cshtml"));
        Assert.StartsWith("@page \"{id:int}\"", Read("Areas/Admin/Pages/TechnicalCategories/Delete.cshtml"));

        // Inside a sentence ("Search …") acronyms keep their capitals.
        Assert.Equal("project categories", ReferenceListCatalog.ProjectCategories.PluralInSentence);
        Assert.Equal("CFA values", ReferenceListCatalog.ArppCfa.PluralInSentence);
        Assert.Equal("DFPDS schedules", ReferenceListCatalog.ArppDfpdsSchedules.PluralInSentence);
        Assert.Equal("funds", ReferenceListCatalog.ArppFunds.PluralInSentence);

        // The tree and the ARPP dialog come from the shared partial, not a second table.
        Assert.False(File.Exists(Path.Combine(FindRepositoryRoot(), "Areas/Admin/Pages/Shared/_AdminCategoryDirectory.cshtml")));
        Assert.False(File.Exists(Path.Combine(FindRepositoryRoot(), "Areas/Admin/Pages/MasterData/ArppReferences/_ReferencePanel.cshtml")));
        var partial = Read("Areas/Admin/Pages/Shared/_AdminReferenceList.cshtml");
        Assert.Contains("Add sub-category", partial);
        Assert.DoesNotContain("data-reference-edit", partial);
        Assert.Contains("--reference-depth", partial);
    }

    private async Task<(int Defence, int Radar, int Sonar, int Training)> SeedProjectCategoryTreeAsync()
    {
        await using var db = new ApplicationDbContext(_options);
        var defence = new ProjectCategory { Name = "Defence", CreatedByUserId = Admin, IsActive = true, SortOrder = 0 };
        var training = new ProjectCategory { Name = "Training", CreatedByUserId = Admin, IsActive = true, SortOrder = 1 };
        db.ProjectCategories.AddRange(defence, training);
        await db.SaveChangesAsync();
        var radar = new ProjectCategory { Name = "Radar", CreatedByUserId = Admin, IsActive = true, SortOrder = 0, ParentId = defence.Id };
        var sonar = new ProjectCategory { Name = "Sonar", CreatedByUserId = Admin, IsActive = true, SortOrder = 1, ParentId = defence.Id };
        db.ProjectCategories.AddRange(radar, sonar);
        db.Projects.Add(new Project { Name = "Simulator", CreatedByUserId = Admin, CreatedAt = DateTime.UtcNow, CategoryId = training.Id });
        await db.SaveChangesAsync();
        return (defence.Id, radar.Id, sonar.Id, training.Id);
    }

    private static ReferenceListService CreateService(ApplicationDbContext db, IAdminAuditService audit, IAuthorizationService? authorization = null)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, Admin) }, "test"))
        };

        return new ReferenceListService(db, audit, authorization ?? new PolicyAuthorization(null), new HttpContextAccessor { HttpContext = http });
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

    /// <summary>Allows every policy, or only the one given, and records what was asked.</summary>
    private sealed class PolicyAuthorization : IAuthorizationService
    {
        private readonly string? _only;

        public PolicyAuthorization(string? only) => _only = only;

        public List<string> Asked { get; } = new();

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(AuthorizationResult.Success());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
        {
            Asked.Add(policyName);
            return Task.FromResult(_only is null || _only == policyName ? AuthorizationResult.Success() : AuthorizationResult.Failed());
        }
    }
}
