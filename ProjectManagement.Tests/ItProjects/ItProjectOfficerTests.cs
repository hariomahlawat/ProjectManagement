using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Models.ItProjects;
using ProjectManagement.Pages.ItProjects;
using ProjectManagement.Services.ItProjects;
using Xunit;
using Molti = ProjectManagement.Pages.MoltiProjects;

namespace ProjectManagement.Tests.ItProjects;

// SECTION: The assigned Project Officer (docs/ux/molti-sre-projects.md, PR 4)
// Admin and HoD assign, change and unassign the officer of a MOLTI or SRE project. Only an active
// user who holds the Project Officer role can be chosen. The officer shows on the page and the
// board, and the board can be narrowed to the unassigned projects or the viewer's own.
public class ItProjectOfficerTests
{
    private static readonly ItProjectActor Hod = new("hod-1", "hod-1", ItProjectModules.Molti);

    // ---- who can be assigned ---------------------------------------------------------------

    [Fact]
    public async Task AssignableOfficers_AreTheActiveUsersWithTheRole_ByName()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        await SeedPeopleAsync(db);

        var officers = await db.Reads().GetAssignableOfficersAsync();

        Assert.Equal(new[] { "Maj Anil Rao", "Maj Bina Shah" }, officers.Select(officer => officer.Name));
        Assert.Equal(new[] { "po-1", "po-2" }, officers.Select(officer => officer.UserId));
    }

    // ---- the command -----------------------------------------------------------------------

    [Fact]
    public async Task Assign_Change_Unassign_EachWritesItsOwnAuditEntry()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        await SeedPeopleAsync(db);
        var id = await SeedAsync(db, ItProjectModules.Molti, "A case");

        Assert.True((await db.Commands().AssignOfficerAsync(id, "po-1", await db.RowVersionAsync(id), Hod)).Succeeded);
        var assigned = db.Audit.Entries.Last();
        Assert.Equal("ItProjects.OfficerAssigned", assigned.Action);
        Assert.Null(assigned.Data["OfficerBeforeUserId"]);
        Assert.Equal("po-1", assigned.Data["OfficerAfterUserId"]);
        Assert.Equal("Maj Anil Rao", assigned.Data["OfficerAfter"]);
        Assert.Equal(ItProjectModules.Molti, assigned.Data["Module"]);

        Assert.True((await db.Commands().AssignOfficerAsync(id, " po-2 ", await db.RowVersionAsync(id), Hod)).Succeeded);
        var changed = db.Audit.Entries.Last();
        Assert.Equal("ItProjects.OfficerChanged", changed.Action);
        Assert.Equal("Maj Anil Rao", changed.Data["OfficerBefore"]);
        Assert.Equal("Maj Bina Shah", changed.Data["OfficerAfter"]);

        await using (var context = db.NewContext())
        {
            var stored = await context.ItProjects.SingleAsync(project => project.Id == id);
            Assert.Equal("po-2", stored.ProjectOfficerUserId);
            Assert.Equal(ItProjectTestDb.Now.UtcDateTime, stored.ProjectOfficerAssignedAtUtc);
        }

        Assert.True((await db.Commands().AssignOfficerAsync(id, "", await db.RowVersionAsync(id), Hod)).Succeeded);
        var unassigned = db.Audit.Entries.Last();
        Assert.Equal("ItProjects.OfficerUnassigned", unassigned.Action);
        Assert.Equal("po-2", unassigned.Data["OfficerBeforeUserId"]);
        Assert.Null(unassigned.Data["OfficerAfterUserId"]);

        await using var check = db.NewContext();
        var after = await check.ItProjects.SingleAsync(project => project.Id == id);
        Assert.Null(after.ProjectOfficerUserId);
        Assert.Null(after.ProjectOfficerAssignedAtUtc);
    }

    [Fact]
    public async Task Assign_TheSameOfficerAgain_ChangesNothing()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        await SeedPeopleAsync(db);
        var id = await SeedAsync(db, ItProjectModules.Molti, "A case");
        await db.Commands().AssignOfficerAsync(id, "po-1", await db.RowVersionAsync(id), Hod);
        var version = await db.RowVersionAsync(id);
        var entries = db.Audit.Entries.Count;

        // Even with a stale version: nothing is written, so nothing can conflict.
        Assert.True((await db.Commands().AssignOfficerAsync(id, "po-1", new byte[] { 9 }, Hod)).Succeeded);

        Assert.Equal(entries, db.Audit.Entries.Count);
        Assert.Equal(version, await db.RowVersionAsync(id));
    }

    [Theory]
    [InlineData("ito-1")]       // holds another role
    [InlineData("po-disabled")] // the role, but the account is disabled
    [InlineData("po-leaving")]  // the role, but the account is being deleted
    [InlineData("nobody")]      // no such user
    public async Task Assign_RejectsAnyoneWhoIsNotAnActiveProjectOfficer(string userId)
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        await SeedPeopleAsync(db);
        var id = await SeedAsync(db, ItProjectModules.Molti, "A case");

        var result = await db.Commands().AssignOfficerAsync(id, userId, await db.RowVersionAsync(id), Hod);

        Assert.Equal(ItProjectCommandService.NotAnOfficerMessage, result.Errors.Single());
        await using var check = db.NewContext();
        Assert.Null((await check.ItProjects.SingleAsync(project => project.Id == id)).ProjectOfficerUserId);
    }

    [Fact]
    public async Task Assign_IsRefusedOnAnItProject_AndDoesNotReachAnotherModule()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        await SeedPeopleAsync(db);
        var (it, itVersion) = await db.SeedAsync("IT case");
        var molti = await SeedAsync(db, ItProjectModules.Molti, "MOLTI case");

        var onIt = await db.Commands().AssignOfficerAsync(it, "po-1", itVersion, ItProjectTestDb.Actor());
        Assert.Equal(ItProjectCommandService.NoOfficerInModuleMessage, onIt.Errors.Single());

        var throughSre = await db.Commands().AssignOfficerAsync(molti, "po-1", await db.RowVersionAsync(molti), Hod with { Module = ItProjectModules.Sre });
        Assert.Equal("The SRE project could not be found.", throughSre.Errors.Single());

        var stale = await db.Commands().AssignOfficerAsync(molti, "po-1", new byte[] { 9 }, Hod);
        Assert.True(stale.ConcurrencyConflict);

        await using var check = db.NewContext();
        Assert.All(await check.ItProjects.ToListAsync(), project => Assert.Null(project.ProjectOfficerUserId));
    }

    [Fact]
    public async Task Create_CanStartWithAnOfficer_ButNotOnAnItProject()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        await SeedPeopleAsync(db);
        var details = ItProjectTestDb.Details("A case");

        var (created, id) = await db.Commands().CreateAsync(details with { ProjectOfficerUserId = "po-1" }, Hod);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors));
        await using (var context = db.NewContext())
        {
            var stored = await context.ItProjects.SingleAsync(project => project.Id == id);
            Assert.Equal("po-1", stored.ProjectOfficerUserId);
            Assert.NotNull(stored.ProjectOfficerAssignedAtUtc);
        }

        Assert.Equal("po-1", db.Audit.Entries.Last().Data["ProjectOfficerUserId"]);

        var (notAnOfficer, _) = await db.Commands().CreateAsync(details with { ProjectOfficerUserId = "ito-1" }, Hod);
        Assert.Contains(ItProjectCommandService.NotAnOfficerMessage, notAnOfficer.Errors);

        var (onIt, _) = await db.Commands().CreateAsync(details with { ProjectOfficerUserId = "po-1" }, ItProjectTestDb.Actor());
        Assert.Contains(ItProjectCommandService.NoOfficerInModuleMessage, onIt.Errors);
    }

    // ---- board -----------------------------------------------------------------------------

    [Fact]
    public async Task Board_ShowsTheOfficer_AndNarrowsToUnassignedOrMine()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        await SeedPeopleAsync(db);
        var mine = await SeedAsync(db, ItProjectModules.Molti, "Mine", "po-1");
        await SeedAsync(db, ItProjectModules.Molti, "Theirs", "po-2");
        await SeedAsync(db, ItProjectModules.Molti, "Nobody's");
        var dropped = await SeedAsync(db, ItProjectModules.Molti, "Nobody's, dropped");
        await db.Commands().DropAsync(dropped, new DateOnly(2026, 11, 1), "Not needed.", await db.RowVersionAsync(dropped), Hod);

        ItProjectBoardQuery Query(ItProjectOfficerFilter filter) => new(ItProjectBoardScope.All, Module: ItProjectModules.Molti, Officer: filter, ViewerUserId: "po-1");

        var all = await db.Reads().GetBoardAsync(Query(ItProjectOfficerFilter.Any));
        Assert.Equal("Maj Anil Rao", all.Rows.Single(row => row.Id == mine).ProjectOfficerName);
        Assert.Null(all.Rows.Single(row => row.Title == "Nobody's").ProjectOfficerUserId);
        Assert.Equal(3, all.ActiveCount);

        // The switches count live cases only, whatever is being shown.
        Assert.Equal(1, all.UnassignedActiveCount);
        Assert.Equal(1, all.MineActiveCount);

        var unassigned = await db.Reads().GetBoardAsync(Query(ItProjectOfficerFilter.Unassigned));
        Assert.Equal(new[] { "Nobody's", "Nobody's, dropped" }, unassigned.Rows.Select(row => row.Title));
        Assert.Equal(1, unassigned.ActiveCount);
        Assert.Equal(1, unassigned.DroppedCount);
        Assert.Equal(1, unassigned.MineActiveCount);

        var own = await db.Reads().GetBoardAsync(Query(ItProjectOfficerFilter.Mine));
        Assert.Equal(new[] { "Mine" }, own.Rows.Select(row => row.Title));
        Assert.Equal(1, own.ActiveCount);

        // "Mine" without a viewer shows nothing rather than everything.
        var anonymous = await db.Reads().GetBoardAsync(new ItProjectBoardQuery(ItProjectBoardScope.All, Module: ItProjectModules.Molti, Officer: ItProjectOfficerFilter.Mine));
        Assert.Empty(anonymous.Rows);
    }

    [Fact]
    public async Task BoardPage_ReadsTheFilterFromTheAddress_AndItProjectsIgnoreIt()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        await SeedPeopleAsync(db);
        await SeedAsync(db, ItProjectModules.Molti, "Mine", "po-1");
        await SeedAsync(db, ItProjectModules.Molti, "Nobody's");
        await db.SeedAsync("IT case");

        var officer = ItProjectTestDb.User("po-1", RoleNames.ProjectOfficer);
        var molti = new Molti.IndexModel(db.Reads()) { PageContext = ItProjectBoardPageTests.Context(officer), Officer = "mine" };
        await molti.OnGetAsync(CancellationToken.None);
        Assert.Equal(new[] { "Mine" }, molti.ScopedRows.Select(row => row.Title));
        Assert.Equal("mine", molti.OfficerKey);

        var it = new IndexModel(db.Reads()) { PageContext = ItProjectBoardPageTests.Context(officer), Officer = "unassigned" };
        await it.OnGetAsync(CancellationToken.None);
        Assert.Equal(new[] { "IT case" }, it.ScopedRows.Select(row => row.Title));
        Assert.Null(it.OfficerKey);
    }

    // ---- project page ----------------------------------------------------------------------

    [Fact]
    public async Task ProjectPage_AdminAndHodAssign_TheOfficerThenWorksOnIt_AndLosesItWhenUnassigned()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        await SeedPeopleAsync(db);
        var id = await SeedAsync(db, ItProjectModules.Molti, "A case");
        var hod = ItProjectTestDb.User("hod-1", RoleNames.HoD);
        var officer = ItProjectTestDb.User("po-1", RoleNames.ProjectOfficer);

        // Before: the officer only reads; the HoD is offered the list.
        var before = Page(db, officer);
        await before.OnGetAsync(id, CancellationToken.None);
        Assert.False(before.CanWorkOn);
        Assert.Empty(before.OfficerOptions);
        var asHod = Page(db, hod);
        await asHod.OnGetAsync(id, CancellationToken.None);
        Assert.True(asHod.AssignsOfficer);
        Assert.Equal(new[] { "No officer", "Maj Anil Rao", "Maj Bina Shah" }, asHod.OfficerOptions.Select(option => option.Text));
        Assert.True(asHod.OfficerOptions[0].Selected);

        // An officer cannot hand a project to themself.
        var grab = Page(db, officer);
        grab.Officer = new ItProjectDetailsPageModel.OfficerInput { UserId = "po-1", RowVersion = DetailsModel.EncodeRowVersion(await db.RowVersionAsync(id)) };
        Assert.IsType<ForbidResult>(await grab.OnPostOfficerAsync(id, CancellationToken.None));

        var assign = Page(db, hod);
        assign.Officer = new ItProjectDetailsPageModel.OfficerInput { UserId = "po-1", RowVersion = DetailsModel.EncodeRowVersion(await db.RowVersionAsync(id)) };
        Assert.IsType<RedirectToPageResult>(await assign.OnPostOfficerAsync(id, CancellationToken.None));
        Assert.Contains(assign.TempData.Values.OfType<string>(), value => value.Contains("Maj Anil Rao is now the Project Officer", StringComparison.Ordinal));

        var after = Page(db, officer);
        await after.OnGetAsync(id, CancellationToken.None);
        Assert.True(after.CanWorkOn);
        Assert.True(after.CanRemark);
        Assert.Equal("Maj Anil Rao", after.OfficerName);
        Assert.Equal(new DateOnly(2026, 11, 8), after.OfficerAssignedOn);
        Assert.Empty(after.OfficerOptions);

        var unassign = Page(db, hod);
        unassign.Officer = new ItProjectDetailsPageModel.OfficerInput { UserId = null, RowVersion = DetailsModel.EncodeRowVersion(await db.RowVersionAsync(id)) };
        Assert.IsType<RedirectToPageResult>(await unassign.OnPostOfficerAsync(id, CancellationToken.None));

        var gone = Page(db, officer);
        await gone.OnGetAsync(id, CancellationToken.None);
        Assert.False(gone.CanWorkOn);
        Assert.False(gone.CanRemark);
        Assert.Null(gone.OfficerName);
    }

    [Fact]
    public async Task ProjectPage_KeepsAnOfficerWhoLostTheRoleInTheList_AndGivesThemNoRights()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        await SeedPeopleAsync(db);
        var id = await SeedAsync(db, ItProjectModules.Molti, "A case", "po-1");
        await using (var context = db.NewContext())
        {
            context.UserRoles.RemoveRange(context.UserRoles.Where(link => link.UserId == "po-1"));
            await context.SaveChangesAsync();
        }

        var asHod = Page(db, ItProjectTestDb.User("hod-1", RoleNames.HoD));
        await asHod.OnGetAsync(id, CancellationToken.None);
        var current = asHod.OfficerOptions.Single(option => option.Selected);
        Assert.Equal("po-1", current.Value);
        Assert.Equal("Maj Anil Rao (no longer a Project Officer)", current.Text);

        // The claims of a user without the role give no rights, whatever the project row says.
        var former = Page(db, ItProjectTestDb.User("po-1", RoleNames.Ta));
        await former.OnGetAsync(id, CancellationToken.None);
        Assert.False(former.CanWorkOn);
    }

    [Fact]
    public async Task OfficerHandler_IsForbiddenOnAnItProject()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        await SeedPeopleAsync(db);
        var (id, version) = await db.SeedAsync("IT case");

        var page = ItProjectDetailsPageTests.PageFor(db, ItProjectTestDb.User("hod-1", RoleNames.HoD));
        page.Officer = new ItProjectDetailsPageModel.OfficerInput { UserId = "po-1", RowVersion = DetailsModel.EncodeRowVersion(version) };

        Assert.IsType<ForbidResult>(await page.OnPostOfficerAsync(id, CancellationToken.None));
        await page.OnGetAsync(id, CancellationToken.None);
        Assert.False(page.AssignsOfficer);
        Assert.Empty(page.OfficerOptions);
    }

    // ---- create page -----------------------------------------------------------------------

    [Fact]
    public async Task CreatePage_OffersOfficersOnlyWhereTheModuleAssignsOne()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        await SeedPeopleAsync(db);
        var context = ItProjectBoardPageTests.Context(ItProjectTestDb.User("hod-1", RoleNames.HoD));

        var molti = new Molti.CreateModel(db.Commands(), db.Clock, db.Reads()) { PageContext = context, TempData = new TempDataDictionary(context.HttpContext, new NullTempData()) };
        await molti.OnGetAsync(CancellationToken.None);
        Assert.Equal(new[] { "No officer yet", "Maj Anil Rao", "Maj Bina Shah" }, molti.OfficerOptions.Select(option => option.Text));

        molti.Input = new ItProjectCreatePageModel.InputModel { Title = "With an officer", PppReferenceNumber = "M/1", FinancialYearStart = 2026, Description = "A case.", ProjectOfficerUserId = "po-2" };
        Assert.IsType<RedirectToPageResult>(await molti.OnPostAsync(CancellationToken.None));

        var it = new CreateModel(db.Commands(), db.Clock) { PageContext = context, TempData = new TempDataDictionary(context.HttpContext, new NullTempData()) };
        await it.OnGetAsync(CancellationToken.None);
        Assert.Empty(it.OfficerOptions);

        // A posted officer on the IT form is dropped, not an error: the form never offered one.
        it.Input = new ItProjectCreatePageModel.InputModel { Title = "IT case", PppReferenceNumber = "IT PPP 1", FinancialYearStart = 2026, Description = "A case.", ProjectOfficerUserId = "po-2" };
        Assert.IsType<RedirectToPageResult>(await it.OnPostAsync(CancellationToken.None));

        await using var check = db.NewContext();
        Assert.Equal("po-2", (await check.ItProjects.SingleAsync(project => project.Title == "With an officer")).ProjectOfficerUserId);
        Assert.Null((await check.ItProjects.SingleAsync(project => project.Title == "IT case")).ProjectOfficerUserId);
    }

    // ---- helpers ---------------------------------------------------------------------------

    /// <summary>Two active Project Officers, a disabled one, one being deleted, and an ITO.</summary>
    private static async Task SeedPeopleAsync(ItProjectTestDb db)
    {
        await using var context = db.NewContext();
        context.Roles.AddRange(
            new IdentityRole { Id = "role-po", Name = RoleNames.ProjectOfficer, NormalizedName = RoleNames.ProjectOfficer.ToUpperInvariant() },
            new IdentityRole { Id = "role-ito", Name = RoleNames.Ito, NormalizedName = RoleNames.Ito.ToUpperInvariant() });
        void Add(string id, string name, string roleId, bool disabled = false, bool leaving = false)
        {
            context.Users.Add(new ApplicationUser { Id = id, UserName = id, NormalizedUserName = id.ToUpperInvariant(), FullName = name, IsDisabled = disabled, PendingDeletion = leaving });
            context.UserRoles.Add(new IdentityUserRole<string> { UserId = id, RoleId = roleId });
        }

        Add("po-2", "Maj Bina Shah", "role-po");
        Add("po-1", "Maj Anil Rao", "role-po");
        Add("po-disabled", "Maj Disabled", "role-po", disabled: true);
        Add("po-leaving", "Maj Leaving", "role-po", leaving: true);
        Add("ito-1", "Capt ITO", "role-ito");
        await context.SaveChangesAsync();
    }

    private static async Task<int> SeedAsync(ItProjectTestDb db, string module, string title, string? officerUserId = null)
    {
        var (result, id) = await db.Commands().CreateAsync(ItProjectTestDb.Details(title) with { ProjectOfficerUserId = officerUserId }, new ItProjectActor("hod-1", "hod-1", module));
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        return id;
    }

    private static Molti.DetailsModel Page(ItProjectTestDb db, ClaimsPrincipal user)
    {
        var context = ItProjectBoardPageTests.Context(user);
        return new Molti.DetailsModel(db.Reads(), db.Commands(), db.Remarks(), db.Attachments(), db.Clock)
        {
            PageContext = context,
            TempData = new TempDataDictionary(context.HttpContext, new NullTempData())
        };
    }

    private sealed class NullTempData : ITempDataProvider
    {
        public System.Collections.Generic.IDictionary<string, object> LoadTempData(HttpContext context) => new System.Collections.Generic.Dictionary<string, object>();

        public void SaveTempData(HttpContext context, System.Collections.Generic.IDictionary<string, object> values)
        {
        }
    }
}
