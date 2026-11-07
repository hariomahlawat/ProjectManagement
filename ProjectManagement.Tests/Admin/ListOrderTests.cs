using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData;
using ProjectManagement.Services.Admin.MasterData.Integrity;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Tests.DocRepo;
using ProjectManagement.Tests.Fakes;
using Xunit;
using DocumentCategoriesPage = ProjectManagement.Areas.DocumentRepository.Pages.Admin.DocumentCategories.IndexModel;
using OfficeCategoriesPage = ProjectManagement.Areas.DocumentRepository.Pages.Admin.OfficeCategories.IndexModel;

namespace ProjectManagement.Tests.Admin;

/// <summary>
/// A cold review found every project type at display order 1 and every repository type at
/// 100, so the up/down arrows looked broken. Lists must stay numbered 1, 2, 3… and the
/// renumber repair must keep the order people already see.
/// </summary>
public sealed class ListOrderTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private DbContextOptions<ApplicationDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options;
        await using var db = new ApplicationDbContext(_options);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task CreatingAProjectType_PutsItAtTheEnd_WhateverPositionTheFormSent()
    {
        await SeedProjectTypesAsync(("Alpha", 0), ("Bravo", 1), ("Charlie", 2));

        await using var db = new ApplicationDbContext(_options);
        var result = await CreateCommands(db).CreateProjectTypeAsync(new FlatLookupCreateCommand("Delta", 0));

        Assert.True(result.Succeeded, result.UserMessage);
        Assert.Equal(new[] { ("Alpha", 0), ("Bravo", 1), ("Charlie", 2), ("Delta", 3) }, await ProjectTypeOrderAsync());
    }

    [Fact]
    public async Task MovingInACollapsedList_RenumbersItAndSwapsWithTheNeighbour()
    {
        // Rows added before the "append at the end" rule all sit at 0 ("1" on screen).
        await SeedProjectTypesAsync(("Alpha", 0), ("Bravo", 0), ("Charlie", 0), ("Delta", 0));
        var charlie = await ProjectTypeIdAsync("Charlie");

        await using var db = new ApplicationDbContext(_options);
        var result = await CreateCommands(db).MoveProjectTypeAsync(charlie, -1);

        Assert.True(result.Succeeded, result.UserMessage);
        Assert.Equal(new[] { ("Alpha", 0), ("Charlie", 1), ("Bravo", 2), ("Delta", 3) }, await ProjectTypeOrderAsync());
    }

    [Fact]
    public async Task MovingDown_SwapsOnlyWithTheNextRow_AndClosesGaps()
    {
        await SeedProjectTypesAsync(("Alpha", 0), ("Bravo", 4), ("Charlie", 9), ("Delta", 12));
        var bravo = await ProjectTypeIdAsync("Bravo");

        await using var db = new ApplicationDbContext(_options);
        var result = await CreateCommands(db).MoveProjectTypeAsync(bravo, 1);

        Assert.True(result.Succeeded, result.UserMessage);
        Assert.Equal(new[] { ("Alpha", 0), ("Charlie", 1), ("Bravo", 2), ("Delta", 3) }, await ProjectTypeOrderAsync());
    }

    [Fact]
    public async Task DataChecks_FlagsRepositoryListsWithRepeatedPositions()
    {
        await using var db = CreateInMemoryDb();
        db.DocumentCategories.AddRange(
            new DocumentCategory { Id = 1, Name = "Letter", SortOrder = 100 },
            new DocumentCategory { Id = 2, Name = "Minutes", SortOrder = 100 });
        db.OfficeCategories.AddRange(
            new OfficeCategory { Id = 1, Name = "North", SortOrder = 1 },
            new OfficeCategory { Id = 2, Name = "South", SortOrder = 2 });
        await db.SaveChangesAsync();

        var snapshot = await CreateIntegrity(db).InspectAsync();

        var documents = snapshot.Checks.Single(check => check.Key == "document-category-order");
        Assert.Equal(MasterDataIntegrityStatus.Warning, documents.Status);
        Assert.True(documents.CanRepair);
        Assert.Equal(2, documents.Findings.Count);
        Assert.Equal(MasterDataIntegrityStatus.Healthy,
            snapshot.Checks.Single(check => check.Key == "office-category-order").Status);
    }

    [Fact]
    public async Task RenumberingARepositoryList_KeepsTheVisibleOrder_AndIsAudited()
    {
        await using var db = CreateInMemoryDb();
        // On screen: Memo (5), then Alpha and Zulu tied at 100, broken by name.
        db.DocumentCategories.AddRange(
            new DocumentCategory { Id = 1, Name = "Zulu", SortOrder = 100 },
            new DocumentCategory { Id = 2, Name = "Alpha", SortOrder = 100 },
            new DocumentCategory { Id = 3, Name = "Memo", SortOrder = 5 });
        await db.SaveChangesAsync();
        var audit = new RecordingAudit();
        var integrity = CreateIntegrity(db, audit);

        var result = await integrity.NormaliseOrderAsync("document-category-order");

        Assert.True(result.Succeeded, result.UserMessage);
        var rows = await db.DocumentCategories.AsNoTracking().OrderBy(item => item.SortOrder).ToListAsync();
        Assert.Equal(new[] { ("Memo", 1), ("Alpha", 2), ("Zulu", 3) }, rows.Select(item => (item.Name, item.SortOrder)));
        var entry = Assert.Single(audit.Entries);
        Assert.Equal("MasterData.IntegrityOrderNormalised", entry.Action);
        Assert.Equal("document-category-order", entry.EntityId);

        var check = await integrity.InspectOrderAsync("document-category-order");
        Assert.NotNull(check);
        Assert.Empty(check!.Findings);
    }

    [Fact]
    public async Task RenumberingTheOfficeList_StartsAtOne()
    {
        await using var db = CreateInMemoryDb();
        db.OfficeCategories.AddRange(
            new OfficeCategory { Id = 1, Name = "South", SortOrder = 100 },
            new OfficeCategory { Id = 2, Name = "North", SortOrder = 100 });
        await db.SaveChangesAsync();

        var result = await CreateIntegrity(db).NormaliseOrderAsync("office-category-order");

        Assert.True(result.Succeeded, result.UserMessage);
        var rows = await db.OfficeCategories.AsNoTracking().OrderBy(item => item.SortOrder).ToListAsync();
        Assert.Equal(new[] { ("North", 1), ("South", 2) }, rows.Select(item => (item.Name, item.SortOrder)));
    }

    [Fact]
    public async Task ListPageNotice_ShowsTheProblem_ButOnlyDataChecksPeopleCanRenumber()
    {
        await using var db = CreateInMemoryDb();
        db.ProjectTypes.AddRange(
            new ProjectType { Id = 1, Name = "Alpha", SortOrder = 0 },
            new ProjectType { Id = 2, Name = "Bravo", SortOrder = 0 });
        await db.SaveChangesAsync();
        var audit = new RecordingAudit();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "lists-editor") }, "test"));

        var denied = new ListOrderNoticeService(CreateIntegrity(db, audit), new FixedAuthorization(false));
        var notice = await denied.GetAsync(user, "project-type-order");
        var refused = await denied.RenumberAsync(user, "project-type-order");

        Assert.True(notice.HasProblem);
        Assert.False(notice.CanRenumber);
        Assert.False(refused.Succeeded);
        Assert.Empty(audit.Entries);
        Assert.All(await db.ProjectTypes.AsNoTracking().ToListAsync(), item => Assert.Equal(0, item.SortOrder));

        var allowed = new ListOrderNoticeService(CreateIntegrity(db, audit), new FixedAuthorization(true));
        Assert.True((await allowed.GetAsync(user, "project-type-order")).CanRenumber);
        Assert.True((await allowed.RenumberAsync(user, "project-type-order")).Succeeded);
        Assert.False((await allowed.GetAsync(user, "project-type-order")).HasProblem);
        Assert.Single(audit.Entries);
    }

    [Fact]
    public async Task AddingARepositoryType_WithNoPosition_PutsItAtTheEnd()
    {
        await using var db = CreateInMemoryDb();
        db.DocumentCategories.AddRange(
            new DocumentCategory { Id = 1, Name = "Letter", SortOrder = 1 },
            new DocumentCategory { Id = 2, Name = "Minutes", SortOrder = 2 });
        await db.SaveChangesAsync();
        var page = new DocumentCategoriesPage(new DocRepoListService(db, new DocRepoAuditService(db), DocRepoTestSupport.Authorization(), Microsoft.Extensions.Options.Options.Create(new DocRepoOptions())), DeniedNotices(db))
        {
            Input = new DocumentCategoriesPage.CreateInput { Name = "Policy" }
        };
        DocRepoTestSupport.ConfigurePage(page, DocRepoTestSupport.User("admin-user", ProjectManagement.Configuration.RoleNames.Admin));

        await page.OnPostCreateAsync(CancellationToken.None);

        Assert.Equal(3, (await db.DocumentCategories.SingleAsync(item => item.Name == "Policy")).SortOrder);
    }

    [Fact]
    public async Task AddingTheFirstOffice_WithNoPosition_StartsAtOne()
    {
        await using var db = CreateInMemoryDb();
        var page = new OfficeCategoriesPage(new DocRepoListService(db, new DocRepoAuditService(db), DocRepoTestSupport.Authorization(), Microsoft.Extensions.Options.Options.Create(new DocRepoOptions())), DeniedNotices(db))
        {
            Input = new OfficeCategoriesPage.CreateInput { Name = "North" }
        };
        DocRepoTestSupport.ConfigurePage(page, DocRepoTestSupport.User("admin-user", ProjectManagement.Configuration.RoleNames.Admin));

        await page.OnPostCreateAsync(CancellationToken.None);

        Assert.Equal(1, (await db.OfficeCategories.SingleAsync()).SortOrder);
    }

    private async Task SeedProjectTypesAsync(params (string Name, int SortOrder)[] rows)
    {
        await using var db = new ApplicationDbContext(_options);
        db.ProjectTypes.AddRange(rows.Select(row => new ProjectType { Name = row.Name, SortOrder = row.SortOrder, IsActive = true }));
        await db.SaveChangesAsync();
    }

    private async Task<int> ProjectTypeIdAsync(string name)
    {
        await using var db = new ApplicationDbContext(_options);
        return await db.ProjectTypes.Where(item => item.Name == name).Select(item => item.Id).SingleAsync();
    }

    private async Task<(string, int)[]> ProjectTypeOrderAsync()
    {
        await using var db = new ApplicationDbContext(_options);
        var rows = await db.ProjectTypes.AsNoTracking().OrderBy(item => item.SortOrder).ToListAsync();
        return rows.Select(item => (item.Name, item.SortOrder)).ToArray();
    }

    private static AdminMasterDataCommandService CreateCommands(ApplicationDbContext db)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "admin") }, "test"))
        };

        return new AdminMasterDataCommandService(
            db,
            new AdminHierarchyValidationService(db),
            new RecordingAudit(),
            new FixedAuthorization(true),
            new HttpContextAccessor { HttpContext = http },
            FakeClock.AtUtc(new DateTimeOffset(2026, 11, 6, 6, 0, 0, TimeSpan.Zero)));
    }

    private static ApplicationDbContext CreateInMemoryDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static AdminMasterDataIntegrityService CreateIntegrity(ApplicationDbContext db, RecordingAudit? audit = null) =>
        new(db, audit ?? new RecordingAudit(), new FixedTime());

    private static IListOrderNoticeService DeniedNotices(ApplicationDbContext db) =>
        new ListOrderNoticeService(CreateIntegrity(db), new FixedAuthorization(false));

    private sealed class RecordingAudit : IAdminAuditService
    {
        public List<AdminAuditEntry> Entries { get; } = new();

        public Task RecordAsync(AdminAuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedAuthorization(bool allow) : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(allow ? AuthorizationResult.Success() : AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) =>
            Task.FromResult(allow ? AuthorizationResult.Success() : AuthorizationResult.Failed());
    }

    private sealed class FixedTime : IAdminTimeService
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 11, 6, 6, 0, 0, TimeSpan.Zero);
        public DateOnly TodayIst => DateOnly.FromDateTime(UtcNow.DateTime);
        public DateTimeOffset StartOfIstDayUtc(DateOnly date) => throw new NotSupportedException();
        public DateTimeOffset EndExclusiveOfIstDayUtc(DateOnly date) => throw new NotSupportedException();
        public DateTime ToIst(DateTime utc) => utc;
        public DateTimeOffset ToIst(DateTimeOffset utc) => utc;
        public string FormatIst(DateTime? utc, string fallback = "—") => utc?.ToString("O") ?? fallback;
        public string FormatIst(DateTimeOffset? utc, string fallback = "—") => utc?.ToString("O") ?? fallback;
    }
}
