using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Pages.Projects.Procurement;
using ProjectManagement.Services.Projects;
using ProjectManagement.Tests.Fakes;
using ProjectManagement.ViewModels;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProcurementEditAuthorizationTests
{
    private const int ProjectId = 23;
    private static readonly DateOnly SupplyOrderDate = new(2026, 6, 20);

    [Theory]
    [InlineData("Project Officer", "assigned-po", true)]
    [InlineData("Project Officer", "other-po", false)]
    [InlineData("HoD", "any-hod", true)]
    [InlineData("Admin", "any-admin", true)]
    public async Task OnPostAsync_RequiresAssignmentForProjectOfficers(string role, string userId, bool allowed)
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbAsync(connection);

        var result = await CreatePage(db, role, userId).OnPostAsync(ProjectId, CancellationToken.None);

        var fact = await db.ProjectSupplyOrderFacts.SingleOrDefaultAsync(x => x.ProjectId == ProjectId);
        if (allowed)
        {
            Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal(SupplyOrderDate, fact?.SupplyOrderDate);
        }
        else
        {
            Assert.IsType<ForbidResult>(result);
            Assert.Null(fact);
        }
    }

    [Fact]
    public async Task OnPostAsync_UnassignedProjectOfficerCannotClearBackfillFlag()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbAsync(connection, requiresBackfill: true);

        var result = await CreatePage(db, "Project Officer", "other-po").OnPostAsync(ProjectId, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
        var stage = await db.ProjectStages.AsNoTracking().SingleAsync(s => s.ProjectId == ProjectId);
        Assert.True(stage.RequiresBackfill);
    }

    [Fact]
    public async Task OnPostAsync_MissingProjectIsNotFound()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbAsync(connection);

        var page = CreatePage(db, "Admin", "any-admin");
        page.Input.ProjectId = 999;

        var result = await page.OnPostAsync(999, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    // SECTION: Facts-only backfill exception (Phase D J-1)
    // From D-2 an assigned PO-only user cannot publish Actual dates, but may still save
    // procurement facts. That can clear a facts-only backfill flag on a stage whose dates
    // are present, and must never resolve a missing completion date.
    [Fact]
    public async Task OnPostAsync_AssignedProjectOfficerClearsFactsOnlyBackfill_WhenTheDateIsPresent()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbAsync(connection, requiresBackfill: true);

        var result = await CreatePage(db, "Project Officer", "assigned-po").OnPostAsync(ProjectId, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        var stage = await db.ProjectStages.AsNoTracking().SingleAsync(s => s.ProjectId == ProjectId);
        Assert.False(stage.RequiresBackfill);
        Assert.Equal(new DateOnly(2026, 6, 1), stage.CompletedOn);
    }

    [Fact]
    public async Task OnPostAsync_AssignedProjectOfficerCannotResolveAMissingCompletionDate_ThroughFacts()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbAsync(connection, requiresBackfill: true, missingCompletionDate: true);

        var result = await CreatePage(db, "Project Officer", "assigned-po").OnPostAsync(ProjectId, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(SupplyOrderDate, (await db.ProjectSupplyOrderFacts.SingleAsync(x => x.ProjectId == ProjectId)).SupplyOrderDate);
        var stage = await db.ProjectStages.AsNoTracking().SingleAsync(s => s.ProjectId == ProjectId);
        Assert.True(stage.RequiresBackfill);
        Assert.Null(stage.CompletedOn);
        Assert.Empty(await db.StageChangeLogs.ToListAsync());
    }

    // SECTION: Return to the briefing desk
    [Fact]
    public async Task OnPostAsync_FromTheBriefingDesk_ReturnsToTheDeskWithAToast()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbAsync(connection);
        var page = CreatePage(db, "Project Officer", "assigned-po");
        page.ReturnUrl = "/Workspace?mode=project-officer&view=overview&focus=desk-project-23#desk-project-23";

        var result = await page.OnPostAsync(ProjectId, CancellationToken.None);

        var redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal(page.ReturnUrl, redirect.Url);
        Assert.Equal("Procurement details updated.", page.TempData["ToastSuccess"]);
        Assert.False(page.TempData.ContainsKey("Flash"));
    }

    [Fact]
    public async Task OnPostAsync_WithAReturnUrlOutsideTheWorkspace_KeepsTheProjectOverviewRedirect()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbAsync(connection);
        var page = CreatePage(db, "Project Officer", "assigned-po");
        page.ReturnUrl = "https://example.test/Workspace";

        var result = await page.OnPostAsync(ProjectId, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Projects/Overview", redirect.PageName);
        Assert.Equal("Procurement details updated.", page.TempData["Flash"]);
    }

    [Theory]
    [InlineData("/Workspace", "/Workspace")]
    [InlineData("/Workspace?mode=project-officer#desk-project-1", "/Workspace?mode=project-officer#desk-project-1")]
    [InlineData("/workspace?mode=project-officer", "/workspace?mode=project-officer")]
    [InlineData("//evil.example/Workspace", null)]
    [InlineData(@"/\evil.example", null)]
    [InlineData("https://evil.example/Workspace?x", null)]
    [InlineData("/Projects/Overview/1", null)]
    [InlineData("/WorkspaceEvil", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void DeskReturnUrl_AcceptsOnlyTheLocalWorkspace(string? candidate, string? expected)
        => Assert.Equal(expected, EditModel.DeskReturnUrl(candidate));

    private static async Task<ApplicationDbContext> CreateDbAsync(
        SqliteConnection connection,
        bool requiresBackfill = false,
        bool missingCompletionDate = false)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        // Project and fact user references are foreign keys under SQLite.
        foreach (var id in new[] { "seed", "assigned-po", "other-po", "any-hod", "any-admin" })
        {
            db.Users.Add(new ApplicationUser { Id = id, UserName = id });
        }

        db.Projects.Add(new Project
        {
            Id = ProjectId,
            Name = "Procurement authorization project",
            CreatedByUserId = "seed",
            LeadPoUserId = "assigned-po"
        });
        // Completed with a date, so only the facts-driven backfill flag is at stake.
        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = ProjectId,
            StageCode = StageCodes.SO,
            Status = StageStatus.Completed,
            CompletedOn = missingCompletionDate ? null : new DateOnly(2026, 6, 1),
            RequiresBackfill = requiresBackfill
        });
        await db.SaveChangesAsync();
        return db;
    }

    private static EditModel CreatePage(ApplicationDbContext db, string role, string userId)
    {
        var userManager = new UserManager<ApplicationUser>(
            new UserStore<ApplicationUser>(db),
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            NullLogger<UserManager<ApplicationUser>>.Instance);

        var facts = new ProjectFactsService(db, FakeClock.AtUtc(DateTimeOffset.UtcNow), new RecordingAudit());
        var page = new EditModel(facts, userManager, db, NullLogger<EditModel>.Instance)
        {
            Input = new ProcurementEditInput
            {
                ProjectId = ProjectId,
                SupplyOrderDate = SupplyOrderDate
            }
        };

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Role, role)
            }, "Test"))
        };
        page.PageContext = new PageContext(new ActionContext(httpContext, new RouteData(), new ActionDescriptor()));
        page.TempData = new TempDataDictionary(httpContext, new NullTempDataProvider());
        return page;
    }

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
