using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.ItProjects;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.ItProjects;
using ProjectManagement.Services.Workspace;
using ProjectManagement.ViewModels.Workspace;
using Xunit;
using WorkspaceIndexModel = ProjectManagement.Pages.Workspace.IndexModel;
using static ProjectManagement.Tests.ItProjects.ItProjectStageRulesTests;

namespace ProjectManagement.Tests.ItProjects;

// The ITO's own workspace (10 Nov 2026): an officer holding only the ITO role opens My Workspace on
// their conference review (the live IT projects), and the rail links to the IT projects board.
public class ItoWorkspaceTests
{
    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private static async Task<(int Live, int Delivered)> SeedAsync(ItProjectTestDb db, bool withLiveItProject = true)
    {
        await using (var context = db.NewContext())
        {
            context.Users.AddRange(User("hod-1"), User("ito-1"));
            context.Roles.Add(new IdentityRole { Id = "role-ito", Name = RoleNames.Ito, NormalizedName = RoleNames.Ito.ToUpperInvariant() });
            context.UserRoles.Add(new IdentityUserRole<string> { UserId = "ito-1", RoleId = "role-ito" });
            await context.SaveChangesAsync();
        }

        var live = 0;
        if (withLiveItProject)
        {
            (live, var liveVersion) = await db.SeedAsync("Core switches", "IT PPP 09/2025", 2025);
            Assert.True((await db.Commands().SaveStagesAsync(live, Input(S("TEC", true, D(2025, 11, 18))), liveVersion, ItProjectTestDb.Actor())).Result.Succeeded);
        }

        var (delivered, deliveredVersion) = await db.SeedAsync("Fibre ring", "IT PPP 10/2024", 2024);
        Assert.True((await db.Commands().SaveStagesAsync(delivered, Input(S("DLV", true, D(2025, 6, 30))), deliveredVersion, ItProjectTestDb.Actor())).Result.Succeeded);
        return (live, delivered);
    }

    private static ApplicationUser User(string id) => new()
    {
        Id = id,
        UserName = id,
        NormalizedUserName = id.ToUpperInvariant(),
        FullName = id,
        SecurityStamp = Guid.NewGuid().ToString()
    };

    private static WorkspaceIndexModel Page(ItProjectTestDb db, ApplicationDbContext context, string userId, params string[] roles)
    {
        var users = new UserManager<ApplicationUser>(
            new UserStore<ApplicationUser, IdentityRole, ApplicationDbContext>(context),
            new OptionsWrapper<IdentityOptions>(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            NullLogger<UserManager<ApplicationUser>>.Instance);
        var conference = new OfficerConferenceReadService(context, new NoWorkload(), new WorkflowStageMetadataProvider(), db.Clock);

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        return new WorkspaceIndexModel(null!, null!, conference, users, new DenyAll(), null!, NullLogger<WorkspaceIndexModel>.Instance, new ItoWorkspaceQuery(context, db.Clock))
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) },
                RouteData = new RouteData()
            }
        };
    }

    [Theory]
    [InlineData(null)]
    [InlineData("overview")]
    [InlineData("projects")]
    [InlineData("documents")]
    [InlineData("tasks")]
    public async Task ItoOnly_OpensTheirConferenceReview_NotTheDashboard(string? view)
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        await SeedAsync(db);
        await using var context = db.NewContext();
        var page = Page(db, context, "ito-1", RoleNames.Ito);
        page.View = view;

        var result = await page.OnGetAsync(CancellationToken.None);

        // The desk, register and documents need the Project Officer role; the ITO's work is the review.
        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Workspace/Index", redirect.PageName);
        Assert.Equal("project-officer", redirect.RouteValues!["mode"]);
        Assert.Equal("conference", redirect.RouteValues["view"]);
    }

    [Fact]
    public async Task WithoutAWorkspaceRole_TheDashboardStillOpens()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        await SeedAsync(db);
        await using var context = db.NewContext();
        var page = Page(db, context, "hod-1", RoleNames.Mco);

        var redirect = Assert.IsType<RedirectToPageResult>(await page.OnGetAsync(CancellationToken.None));

        Assert.Equal("/Dashboard/Index", redirect.PageName);
    }

    [Fact]
    public async Task ItoOnly_ReadsTheDirectionHistoryOfALiveItProject()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (live, delivered) = await SeedAsync(db);
        Assert.True((await db.Remarks().AddAsync(live, new ItProjectRemarkInput("Close COB this week.", D(2026, 11, 8), ItProjectRemarkType.Conference), ItProjectTestDb.RemarkActor("hod-1", RoleNames.HoD))).Result.Succeeded);
        await using var context = db.NewContext();

        var ito = Page(db, context, "ito-1", RoleNames.Ito);
        var json = Assert.IsType<JsonResult>(await ito.OnGetDirectionHistoryAsync(ConferenceItemKind.ItProject, live, CancellationToken.None));
        var history = Assert.IsType<ConferenceDirectionHistoryVm>(json.Value);
        Assert.Equal("Close COB this week.", Assert.Single(history.Cycles).Direction!.Body);

        // Only the live IT projects are the ITO's: a delivered one has no history to read.
        var notLive = Assert.IsType<JsonResult>(await ito.OnGetDirectionHistoryAsync(ConferenceItemKind.ItProject, delivered, CancellationToken.None));
        Assert.Equal(StatusCodes.Status404NotFound, notLive.StatusCode);

        // An officer with neither the ITO nor the Project Officer role is refused.
        var other = Page(db, context, "hod-1", RoleNames.Mco);
        Assert.IsType<ForbidResult>(await other.OnGetDirectionHistoryAsync(ConferenceItemKind.ItProject, live, CancellationToken.None));
    }

    [Fact]
    public async Task OwnReview_ShowsOnlyTheItProjects_WhenTheItoHasNoOtherWork()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (live, _) = await SeedAsync(db);
        await using var context = db.NewContext();

        var itoOnly = await new OfficerConferenceReadService(context, new NoWorkload(), new WorkflowStageMetadataProvider(), db.Clock)
            .GetForProjectOfficerAsync("ito-1");
        Assert.Equal(new[] { ConferenceItemKind.ItProject }, itoOnly!.Sections.Select(section => section.Kind));
        Assert.Equal(1, itoOnly.ItProjectCount);
        Assert.Equal(live, Assert.Single(itoOnly.Sections.Single().Items).ItemId);

        // An ITO who also has Project Officer work keeps the project, idea and task sections.
        var withTask = new CommandOfficerWorkloadVm
        {
            UserId = "ito-1",
            OfficerName = "ito-1",
            OtherTaskCount = 1,
            OtherTasks = new[] { new CommandOfficerTaskVm(7, "Draft the SOW", "Assigned", DateTime.UtcNow.AddDays(7), "/ActionTasks/Index?taskId=7") }
        };
        var both = await new OfficerConferenceReadService(context, new NoWorkload(withTask), new WorkflowStageMetadataProvider(), db.Clock)
            .GetForProjectOfficerAsync("ito-1");
        Assert.Equal(
            new[] { ConferenceItemKind.Project, ConferenceItemKind.ProjectIdea, ConferenceItemKind.ActionTask, ConferenceItemKind.ItProject },
            both!.Sections.Select(section => section.Kind));
    }

    [Fact]
    public async Task OwnReview_ShowsTheEmptyItProjectsSection_WhenNoItProjectIsLive()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        await SeedAsync(db, withLiveItProject: false);
        await using var context = db.NewContext();

        // Only a delivered IT project: the ITO still gets the IT projects section, empty, and not
        // the Project Officer's empty project, idea and task sections.
        var review = await new OfficerConferenceReadService(context, new NoWorkload(), new WorkflowStageMetadataProvider(), db.Clock)
            .GetForProjectOfficerAsync("ito-1");

        var section = Assert.Single(review!.Sections);
        Assert.Equal(ConferenceItemKind.ItProject, section.Kind);
        Assert.Empty(section.Items);
        Assert.Equal(0, review.ItProjectCount);
    }

    [Fact]
    public async Task RailCount_IsTheLiveItProjects()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        await SeedAsync(db);
        await using var context = db.NewContext();

        // Two IT projects, one delivered: the rail shows the one still live.
        Assert.Equal(1, await new ItoWorkspaceQuery(context, db.Clock).CountLiveItProjectsAsync());
    }

    [Theory]
    [InlineData(false, true, false, "conference", true, false)]   // ITO only
    [InlineData(true, false, false, "overview", true, false)]     // Project Officer only
    [InlineData(true, true, false, "overview", true, false)]      // both: the desk stays the landing page
    [InlineData(false, true, true, "conference", true, true)]     // ITO with a command role can switch lenses
    [InlineData(false, false, true, "conference", false, false)]  // command only: no personal workspace
    public void Rail_GivesTheItoAPersonalWorkspace(
        bool projectOfficer, bool ito, bool command, string landing, bool personal, bool canSwitch)
    {
        var rail = new CommandWorkspaceRailVm
        {
            HasProjectOfficerAccess = projectOfficer,
            HasItoAccess = ito,
            HasCommandAccess = command
        };

        Assert.Equal(landing, rail.PersonalLandingView);
        Assert.Equal(personal, rail.HasPersonalAccess);
        Assert.Equal(canSwitch, rail.CanSwitchWorkspace);
    }

    private sealed class DenyAll : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(AuthorizationResult.Failed());
    }

    private sealed class NoWorkload(params CommandOfficerWorkloadVm[] officers) : IOfficerWorkloadReadService
    {
        public Task<IReadOnlyList<CommandOfficerWorkloadVm>> GetAllAsync(string requestingUserId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CommandOfficerWorkloadVm>>(officers);

        public Task<IReadOnlyList<CommandOfficerWorkloadVm>> GetAllWithItProjectsAsync(string requestingUserId, CancellationToken cancellationToken = default)
            => GetAllAsync(requestingUserId, cancellationToken);

        public Task<CommandOfficerWorkloadVm?> GetOfficerAsync(string officerUserId, CancellationToken cancellationToken = default)
            => Task.FromResult(officers.SingleOrDefault(officer => officer.UserId == officerUserId));

        public Task<int> CountActiveOfficersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }
}
