using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Workspace;
using Xunit;
using WorkspaceIndexModel = ProjectManagement.Pages.Workspace.IndexModel;
using static ProjectManagement.Tests.ItProjects.ItProjectStageRulesTests;

namespace ProjectManagement.Tests.ItProjects;

// IT projects on the Officer workload view: the ITO runs every live IT project (they have no
// assigned Project Officer), the same rule as the conference review (ItProjectConferenceTests).
public class ItProjectWorkloadTests
{
    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private static async Task<(int Live, int Delivered)> SeedAsync(
        ItProjectTestDb db,
        bool withIto = true,
        bool itoIsAlsoProjectOfficer = false,
        bool itoDisabled = false,
        bool withLiveItProject = true)
    {
        await using (var context = db.NewContext())
        {
            var ito = User("ito-1", "Rao", "Maj");
            ito.IsDisabled = itoDisabled;
            context.Users.AddRange(User("hod-1", "Sharma", "Col"), ito, User("po-1", "Iyer", "Lt Col"));
            context.Roles.AddRange(
                new IdentityRole { Id = "role-po", Name = RoleNames.ProjectOfficer, NormalizedName = RoleNames.ProjectOfficer.ToUpperInvariant() },
                new IdentityRole { Id = "role-ito", Name = RoleNames.Ito, NormalizedName = RoleNames.Ito.ToUpperInvariant() });
            context.UserRoles.Add(new IdentityUserRole<string> { UserId = "po-1", RoleId = "role-po" });
            if (withIto)
            {
                context.UserRoles.Add(new IdentityUserRole<string> { UserId = "ito-1", RoleId = "role-ito" });
            }

            if (itoIsAlsoProjectOfficer)
            {
                context.UserRoles.Add(new IdentityUserRole<string> { UserId = "ito-1", RoleId = "role-po" });
            }

            // Iyer leads one procurement project; with the PO role the ITO leads one too.
            context.Projects.Add(Project("Simulator upgrade", "po-1"));
            if (itoIsAlsoProjectOfficer)
            {
                context.Projects.Add(Project("Range telemetry", "ito-1"));
            }

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

    private static ApplicationUser User(string id, string name, string rank) => new()
    {
        Id = id,
        UserName = id,
        NormalizedUserName = id.ToUpperInvariant(),
        FullName = name,
        Rank = rank,
        SecurityStamp = Guid.NewGuid().ToString()
    };

    private static Project Project(string name, string leadPoUserId) => new()
    {
        Name = name,
        CreatedByUserId = "seed",
        LeadPoUserId = leadPoUserId,
        WorkflowVersion = ProcurementWorkflow.VersionV2,
        LifecycleStatus = ProjectLifecycleStatus.Active
    };

    private static (OfficerWorkloadReadService Service, ApplicationDbContext Context, UserManager<ApplicationUser> Users) Workload(ItProjectTestDb db)
    {
        var context = db.NewContext();
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
        return (new OfficerWorkloadReadService(context, users, new WorkflowStageMetadataProvider(), db.Clock), context, users);
    }

    [Fact]
    public async Task Ito_GetsACardWithTheLiveItProjects_EvenWithoutOtherWork()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (live, delivered) = await SeedAsync(db);
        var (service, context, users) = Workload(db);
        await using var _ = context;
        using var __ = users;

        var cards = await service.GetAllWithItProjectsAsync("hod-1");

        // Default order is by rank: Lt Col before Maj.
        Assert.Equal(new[] { "po-1", "ito-1" }, cards.Select(card => card.UserId));
        var ito = cards.Single(card => card.UserId == "ito-1");
        Assert.Equal("Rao", ito.OfficerName);
        Assert.Equal("Maj", ito.Rank);
        Assert.Equal(0, ito.ProjectCount);
        Assert.Equal(1, ito.ItProjectCount);
        var item = Assert.Single(ito.ItProjects);
        Assert.Equal(live, item.ItProjectId);                 // the delivered one is not live
        Assert.Equal("Core switches", item.Title);
        Assert.Equal("At COB", item.State);
        Assert.Equal("IT PPP 09/2025", item.Reference);
        Assert.Equal($"/ItProjects/{live}", item.OpenUrl);
        Assert.DoesNotContain(delivered, ito.ItProjects.Select(row => row.ItProjectId));

        // A Project Officer without the ITO role gets no IT projects.
        var po = cards.Single(card => card.UserId == "po-1");
        Assert.Equal(0, po.ItProjectCount);
        Assert.Empty(po.ItProjects);
        Assert.Equal(1, po.ProjectCount);
    }

    [Fact]
    public async Task GetAllAsync_KeepsItsScope_ForTheOtherConsumers()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        await SeedAsync(db);
        var (service, context, users) = Workload(db);
        await using var _ = context;
        using var __ = users;

        // The command desk, conference commands and conference review read GetAllAsync, unchanged.
        var cards = await service.GetAllAsync("hod-1");
        Assert.Equal(new[] { "po-1" }, cards.Select(card => card.UserId));
        Assert.All(cards, card => Assert.Empty(card.ItProjects));
    }

    [Fact]
    public async Task ItoWhoIsAlsoAProjectOfficer_KeepsTheirProjects_AndGetsTheItProjects()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (live, _) = await SeedAsync(db, itoIsAlsoProjectOfficer: true);
        var (service, context, users) = Workload(db);
        await using var _ = context;
        using var __ = users;

        var ito = (await service.GetAllWithItProjectsAsync("hod-1")).Single(card => card.UserId == "ito-1");

        Assert.Equal("Range telemetry", Assert.Single(ito.Projects).Name);
        Assert.Equal(live, Assert.Single(ito.ItProjects).ItProjectId);
    }

    [Theory]
    [InlineData(false, false, true)]  // no ITO
    [InlineData(true, true, true)]    // the ITO's account is disabled
    [InlineData(true, false, false)]  // no live IT project (only a delivered one)
    public async Task NoActiveIto_OrNoLiveItProject_ChangesNothing(bool withIto, bool itoDisabled, bool withLiveItProject)
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        await SeedAsync(db, withIto: withIto, itoDisabled: itoDisabled, withLiveItProject: withLiveItProject);
        var (service, context, users) = Workload(db);
        await using var _ = context;
        using var __ = users;

        var cards = await service.GetAllWithItProjectsAsync("hod-1");

        Assert.Equal(new[] { "po-1" }, cards.Select(card => card.UserId));
        Assert.Empty(cards.Single().ItProjects);
        Assert.Equal(1, await service.CountActiveOfficersAsync());
    }

    [Fact]
    public async Task OfficerCount_IncludesTheIto_WhenAnItProjectIsLive()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        await SeedAsync(db);
        var (service, context, users) = Workload(db);
        await using var _ = context;
        using var __ = users;

        Assert.Equal(2, await service.CountActiveOfficersAsync());
    }

    [Fact]
    public async Task SavingTheOfficerOrder_KeepsTheItoCardsPosition()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        await SeedAsync(db);
        var (_, context, users) = Workload(db);
        await using var _ = context;
        using var __ = users;

        var page = new WorkspaceIndexModel(null!, null!, null!, users, null!, null!, NullLogger<WorkspaceIndexModel>.Instance)
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, "hod-1"), new Claim(ClaimTypes.Role, RoleNames.HoD) },
                        "test"))
                },
                RouteData = new RouteData()
            }
        };

        // hod-1 holds neither role, so it is not a card and is dropped.
        await page.OnPostSaveOfficerOrderAsync(
            new WorkspaceIndexModel.SaveOfficerOrderRequest { OfficerUserIds = { "ito-1", "hod-1", "po-1" } },
            CancellationToken.None);

        await using var check = db.NewContext();
        var saved = (await check.Users.FindAsync("hod-1"))!.ComdtOfficerWorkloadOrderJson;
        Assert.Equal(new[] { "ito-1", "po-1" }, JsonSerializer.Deserialize<string[]>(saved!));
    }

    [Fact]
    public async Task SavedOrder_AppliesToTheItoCard()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        await SeedAsync(db);
        await using (var context = db.NewContext())
        {
            var hod = await context.Users.FindAsync("hod-1");
            hod!.ComdtOfficerWorkloadOrderJson = JsonSerializer.Serialize(new[] { "ito-1", "po-1" });
            await context.SaveChangesAsync();
        }

        var (service, readContext, users) = Workload(db);
        await using var _ = readContext;
        using var __ = users;

        var cards = await service.GetAllWithItProjectsAsync("hod-1");
        Assert.Equal(new[] { "ito-1", "po-1" }, cards.Select(card => card.UserId));
    }
}
