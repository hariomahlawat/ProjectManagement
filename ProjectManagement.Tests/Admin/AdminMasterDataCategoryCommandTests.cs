using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData;
using ProjectManagement.Services.Admin.MasterData.ReferenceLists;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests.Admin;

/// <summary>
/// Category delete and activate/deactivate, on SQLite so the transactions, foreign keys and
/// row versions are real.
/// </summary>
public sealed class AdminMasterDataCategoryCommandTests : IAsyncLifetime
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

    // The delete check looked only at projects and child categories. A category used by a
    // project change request or a legacy import passed it, the page said "no dependencies
    // were detected", and the database then refused the delete with an error page.
    [Fact]
    public async Task DeleteTechnicalCategory_IsRefusedWhileAChangeRequestUsesIt()
    {
        var categoryId = await SeedTechnicalCategoryAsync("Sensors");
        await using (var seed = new ApplicationDbContext(_options))
        {
            var project = new Project { Name = "Radar", CreatedByUserId = Admin, CreatedAt = DateTime.UtcNow };
            seed.Projects.Add(project);
            await seed.SaveChangesAsync();
            seed.ProjectMetaChangeRequests.Add(new ProjectMetaChangeRequest
            {
                ProjectId = project.Id,
                ChangeType = "Meta",
                Payload = "{}",
                OriginalName = "Radar",
                TechnicalCategoryId = categoryId,
                DecisionStatus = "Rejected"
            });
            await seed.SaveChangesAsync();
        }

        await using var db = new ApplicationDbContext(_options);
        var category = await db.TechnicalCategories.AsNoTracking().SingleAsync(item => item.Id == categoryId);
        var result = await Lists(db).DeleteAsync(ReferenceListKeys.TechnicalCategories, categoryId, category.RowVersion);

        Assert.False(result.Succeeded);
        Assert.Equal("EntityInUse", result.ErrorCode);
        Assert.True(await db.TechnicalCategories.AnyAsync(item => item.Id == categoryId));

        var item = await Lists(db).GetItemAsync(ReferenceListKeys.TechnicalCategories, categoryId);
        Assert.Contains("1 change request", item!.UsedBy.Select(usage => usage.Text));
        Assert.False(item.CanDelete);
    }

    [Fact]
    public async Task DeleteProjectCategory_IsRefusedWhileALegacyImportUsesIt()
    {
        var technicalId = await SeedTechnicalCategoryAsync("Optics");
        int projectCategoryId;
        await using (var seed = new ApplicationDbContext(_options))
        {
            var category = new ProjectCategory { Name = "Simulators", CreatedByUserId = Admin, IsActive = true };
            seed.ProjectCategories.Add(category);
            await seed.SaveChangesAsync();
            projectCategoryId = category.Id;
            seed.ProjectLegacyImports.Add(new ProjectLegacyImport
            {
                ProjectCategoryId = projectCategoryId,
                TechnicalCategoryId = technicalId,
                ImportedByUserId = Admin
            });
            await seed.SaveChangesAsync();
        }

        await using var db = new ApplicationDbContext(_options);
        var row = await db.ProjectCategories.AsNoTracking().SingleAsync(item => item.Id == projectCategoryId);
        var result = await Lists(db).DeleteAsync(ReferenceListKeys.ProjectCategories, projectCategoryId, row.RowVersion);

        Assert.False(result.Succeeded);
        Assert.Equal("EntityInUse", result.ErrorCode);
        Assert.True(await db.ProjectCategories.AnyAsync(item => item.Id == projectCategoryId));
    }

    [Fact]
    public async Task DeleteTechnicalCategory_SucceedsWhenNothingUsesIt()
    {
        var categoryId = await SeedTechnicalCategoryAsync("Unused");

        await using var db = new ApplicationDbContext(_options);
        var category = await db.TechnicalCategories.AsNoTracking().SingleAsync(item => item.Id == categoryId);
        var result = await Lists(db).DeleteAsync(ReferenceListKeys.TechnicalCategories, categoryId, category.RowVersion);

        Assert.True(result.Succeeded, result.UserMessage);
        Assert.False(await db.TechnicalCategories.AnyAsync(item => item.Id == categoryId));
    }

    // The old toggle flipped whatever was stored, so a resubmitted "Activate" deactivated
    // an item that was already active.
    [Fact]
    public async Task SetActive_LeavesAnItemAlreadyInTheRequestedStateAlone()
    {
        var categoryId = await SeedTechnicalCategoryAsync("Already active");

        await using var db = new ApplicationDbContext(_options);
        var category = await db.TechnicalCategories.AsNoTracking().SingleAsync(item => item.Id == categoryId);
        var result = await CreateService(db).SetTechnicalCategoryActiveAsync(categoryId, isActive: true, category.RowVersion);

        Assert.True(result.Succeeded);
        Assert.Contains("already active", result.UserMessage);
        Assert.True((await db.TechnicalCategories.AsNoTracking().SingleAsync(item => item.Id == categoryId)).IsActive);
    }

    [Fact]
    public async Task SetActive_RefusesAStaleRowVersion()
    {
        var categoryId = await SeedTechnicalCategoryAsync("Stale");
        byte[] shownVersion;
        await using (var read = new ApplicationDbContext(_options))
        {
            shownVersion = (await read.TechnicalCategories.AsNoTracking().SingleAsync(item => item.Id == categoryId)).RowVersion;
        }

        // Another administrator renames it after the page was shown.
        await using (var other = new ApplicationDbContext(_options))
        {
            var category = await other.TechnicalCategories.SingleAsync(item => item.Id == categoryId);
            category.Name = "Stale (renamed)";
            await other.SaveChangesAsync();
        }

        await using var db = new ApplicationDbContext(_options);
        var result = await CreateService(db).SetTechnicalCategoryActiveAsync(categoryId, isActive: false, shownVersion);

        Assert.False(result.Succeeded);
        await using var verify = new ApplicationDbContext(_options);
        Assert.True((await verify.TechnicalCategories.SingleAsync(item => item.Id == categoryId)).IsActive);
    }

    [Fact]
    public async Task SetActive_AppliesTheRequestedState()
    {
        var categoryId = await SeedTechnicalCategoryAsync("Retire me");

        await using var db = new ApplicationDbContext(_options);
        var category = await db.TechnicalCategories.AsNoTracking().SingleAsync(item => item.Id == categoryId);
        var result = await CreateService(db).SetTechnicalCategoryActiveAsync(categoryId, isActive: false, category.RowVersion);

        Assert.True(result.Succeeded, result.UserMessage);
        await using var verify = new ApplicationDbContext(_options);
        Assert.False((await verify.TechnicalCategories.SingleAsync(item => item.Id == categoryId)).IsActive);
    }

    // A project type, sponsoring unit or line directorate used by any project could never be
    // deactivated, although the page and Help said referenced values should be deactivated.
    [Fact]
    public async Task SetProjectTypeActive_RetiresATypeThatProjectsStillUse()
    {
        int typeId;
        await using (var seed = new ApplicationDbContext(_options))
        {
            var type = new ProjectType { Name = "Retiring type", IsActive = true };
            seed.ProjectTypes.Add(type);
            await seed.SaveChangesAsync();
            typeId = type.Id;
            seed.Projects.Add(new Project { Name = "Uses it", CreatedByUserId = Admin, CreatedAt = DateTime.UtcNow, ProjectTypeId = typeId });
            await seed.SaveChangesAsync();
        }

        await using var db = new ApplicationDbContext(_options);
        var row = await db.ProjectTypes.AsNoTracking().SingleAsync(item => item.Id == typeId);
        var result = await CreateService(db).SetProjectTypeActiveAsync(typeId, isActive: false, row.RowVersion);

        Assert.True(result.Succeeded, result.UserMessage);
        await using var verify = new ApplicationDbContext(_options);
        Assert.False((await verify.ProjectTypes.SingleAsync(item => item.Id == typeId)).IsActive);
        Assert.Equal(typeId, (await verify.Projects.SingleAsync()).ProjectTypeId);
    }

    // Deleting a category, or moving it under another parent, left a gap in its old sibling
    // group (0, 2, 3) that the directory showed and the integrity check reported.
    [Fact]
    public async Task DeleteAndMove_CloseTheGapInTheOldSiblingGroup()
    {
        int firstId, secondId, thirdId, fourthId, otherParentId;
        await using (var seed = new ApplicationDbContext(_options))
        {
            var categories = new[] { "First", "Second", "Third", "Fourth", "Other parent" }
                .Select((name, index) => new TechnicalCategory { Name = name, CreatedByUserId = Admin, IsActive = true, SortOrder = index })
                .ToArray();
            seed.TechnicalCategories.AddRange(categories);
            await seed.SaveChangesAsync();
            (firstId, secondId, thirdId, fourthId, otherParentId) =
                (categories[0].Id, categories[1].Id, categories[2].Id, categories[3].Id, categories[4].Id);
        }

        await using (var db = new ApplicationDbContext(_options))
        {
            var second = await db.TechnicalCategories.AsNoTracking().SingleAsync(item => item.Id == secondId);
            Assert.True((await Lists(db).DeleteAsync(ReferenceListKeys.TechnicalCategories, secondId, second.RowVersion)).Succeeded);
        }

        await using (var db = new ApplicationDbContext(_options))
        {
            var third = await db.TechnicalCategories.AsNoTracking().SingleAsync(item => item.Id == thirdId);
            var moved = await CreateService(db).UpdateTechnicalCategoryAsync(
                new CategoryUpdateCommand(thirdId, third.Name, otherParentId, true, third.RowVersion));
            Assert.True(moved.Succeeded, moved.UserMessage);
        }

        await using var verify = new ApplicationDbContext(_options);
        var roots = await verify.TechnicalCategories.AsNoTracking()
            .Where(item => item.ParentId == null)
            .OrderBy(item => item.SortOrder)
            .Select(item => new { item.Id, item.SortOrder })
            .ToListAsync();
        Assert.Equal(new[] { firstId, fourthId, otherParentId }, roots.Select(item => item.Id));
        Assert.Equal(new[] { 0, 1, 2 }, roots.Select(item => item.SortOrder));
    }

    [Fact]
    public async Task Overview_DoesNotCountHolidaysAndCelebrationsAsInUse()
    {
        await using (var seed = new ApplicationDbContext(_options))
        {
            seed.Celebrations.Add(new ProjectManagement.Models.Celebration
            {
                Id = Guid.NewGuid(),
                Name = "Founding day",
                EventType = ProjectManagement.Models.CelebrationType.Anniversary,
                Day = 1,
                Month = 2,
                CreatedById = Admin,
                CreatedUtc = DateTimeOffset.UtcNow,
                UpdatedUtc = DateTimeOffset.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        await using var db = new ApplicationDbContext(_options);
        var overview = await new MasterDataAdministrationQueryService(db, new AuditActionPresentationCatalog())
            .GetOverviewAsync(2026);

        var celebrations = overview.Domains.Single(domain => domain.Key == "celebrations");
        Assert.False(celebrations.TracksUsage);
        Assert.Equal(1, celebrations.Total);
        Assert.Equal(0, celebrations.InUse);
        Assert.Equal(0, overview.RecordsInUse);
    }

    private async Task<int> SeedTechnicalCategoryAsync(string name)
    {
        await using var db = new ApplicationDbContext(_options);
        var category = new TechnicalCategory { Name = name, CreatedByUserId = Admin, IsActive = true };
        db.TechnicalCategories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    // Since PR 11 categories are deleted through the shared list page's one rule.
    private static ReferenceListService Lists(ApplicationDbContext db)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, Admin) }, "test"))
        };

        return new ReferenceListService(db, new NoAdminAudit(), new AllowAllAuthorization(), new HttpContextAccessor { HttpContext = http });
    }

    private static AdminMasterDataCommandService CreateService(ApplicationDbContext db)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, Admin) }, "test"))
        };

        return new AdminMasterDataCommandService(
            db,
            new AdminHierarchyValidationService(db),
            new NoAdminAudit(),
            new AllowAllAuthorization(),
            new HttpContextAccessor { HttpContext = http },
            FakeClock.AtUtc(new DateTimeOffset(2026, 11, 6, 6, 0, 0, TimeSpan.Zero)));
    }

    private sealed class NoAdminAudit : IAdminAuditService
    {
        public Task RecordAsync(AdminAuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class AllowAllAuthorization : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(AuthorizationResult.Success());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) =>
            Task.FromResult(AuthorizationResult.Success());
    }
}
