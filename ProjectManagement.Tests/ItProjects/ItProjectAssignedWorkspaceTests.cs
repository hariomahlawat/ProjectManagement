using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
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
using static ProjectManagement.Tests.ItProjects.ItProjectStageRulesTests;

namespace ProjectManagement.Tests.ItProjects;

// SECTION: MOLTI and SRE projects in the workspace (docs/ux/molti-sre-projects.md, PR 5)
// A live MOLTI or SRE project sits under the Project Officer assigned to it: on their Officer
// workload card, in their conference review (a section per module) and on their workspace rail.
// An unassigned project sits under nobody. IT projects stay under the ITO.
public class ItProjectAssignedWorkspaceTests
{
    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private sealed record Seeded(int It, int MoltiOne, int MoltiTwo, int Sre, int Unassigned, int Delivered, int OtherOfficers);

    /// <summary>
    /// po-1 holds two MOLTI projects and one SRE project (and a delivered MOLTI one); po-2 holds
    /// one MOLTI project; one MOLTI project has no officer; ito-1 runs the one IT project.
    /// </summary>
    private static async Task<Seeded> SeedAsync(ItProjectTestDb db)
    {
        await using (var context = db.NewContext())
        {
            context.Roles.AddRange(
                Role("role-hod", RoleNames.HoD),
                Role("role-ito", RoleNames.Ito),
                Role("role-po", RoleNames.ProjectOfficer));
            void Add(string id, string name, string rank, string roleId)
            {
                context.Users.Add(new ApplicationUser { Id = id, UserName = id, NormalizedUserName = id.ToUpperInvariant(), FullName = name, Rank = rank, SecurityStamp = Guid.NewGuid().ToString() });
                context.UserRoles.Add(new IdentityUserRole<string> { UserId = id, RoleId = roleId });
            }

            Add("hod-1", "Sharma", "Col", "role-hod");
            Add("ito-1", "Rao", "Maj", "role-ito");
            Add("po-1", "Iyer", "Lt Col", "role-po");
            Add("po-2", "Khan", "Maj", "role-po");
            await context.SaveChangesAsync();
        }

        var (it, itVersion) = await db.SeedAsync("Core switches", "IT PPP 09/2026", 2026);
        Assert.True((await db.Commands().SaveStagesAsync(it, Input(S("AON", true, D(2026, 10, 20))), itVersion, ItProjectTestDb.Actor())).Result.Succeeded);

        var moltiOne = await CreateAsync(db, ItProjectModules.Molti, "Range spares", "po-1");
        Assert.True((await db.Commands().SaveStagesAsync(moltiOne, Input(S("BID", true, D(2026, 10, 30))), await db.RowVersionAsync(moltiOne), Actor(ItProjectModules.Molti))).Result.Succeeded);
        var moltiTwo = await CreateAsync(db, ItProjectModules.Molti, "Test bench", "po-1");
        var sre = await CreateAsync(db, ItProjectModules.Sre, "Shelter repair", "po-1");
        var unassigned = await CreateAsync(db, ItProjectModules.Molti, "Nobody's case", null);
        var delivered = await CreateAsync(db, ItProjectModules.Molti, "Delivered case", "po-1");
        Assert.True((await db.Commands().SaveStagesAsync(delivered, Input(S("DLV", true, D(2026, 9, 30))), await db.RowVersionAsync(delivered), Actor(ItProjectModules.Molti))).Result.Succeeded);
        var others = await CreateAsync(db, ItProjectModules.Molti, "Khan's case", "po-2");
        return new Seeded(it, moltiOne, moltiTwo, sre, unassigned, delivered, others);
    }

    private static IdentityRole Role(string id, string name) => new() { Id = id, Name = name, NormalizedName = name.ToUpperInvariant() };

    private static ItProjectActor Actor(string module) => new("hod-1", "hod-1", module);

    private static async Task<int> CreateAsync(ItProjectTestDb db, string module, string title, string? officer)
    {
        var (result, id) = await db.Commands().CreateAsync(ItProjectTestDb.Details(title, $"{module}/{title[..3]}", 2026) with { ProjectOfficerUserId = officer }, Actor(module));
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        return id;
    }

    // ---- the shared feed -------------------------------------------------------------------

    [Fact]
    public async Task AssignedFeed_HoldsTheLiveProjectsOfActiveOfficers_ByModule()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var seeded = await SeedAsync(db);
        await using var context = db.NewContext();

        var rows = await LiveItProjects.LoadAssignedAsync(context, ItProjectTestDb.Now, CancellationToken.None);

        // MOLTI before SRE; in a module, board order (stage, then title). No IT project, no
        // unassigned one, no delivered one.
        Assert.Equal(new[] { seeded.OtherOfficers, seeded.MoltiTwo, seeded.MoltiOne, seeded.Sre }, rows.Select(row => row.Id));
        Assert.All(rows, row => Assert.NotNull(row.ProjectOfficerUserId));
        Assert.Equal($"/MoltiProjects/{seeded.MoltiOne}", LiveItProjects.OpenUrl(rows.Single(row => row.Id == seeded.MoltiOne)));
        Assert.Equal($"/SreProjects/{seeded.Sre}", LiveItProjects.OpenUrl(rows.Single(row => row.Id == seeded.Sre)));

        // The IT feed is unchanged.
        Assert.Equal(new[] { seeded.It }, (await LiveItProjects.LoadAsync(context, ItProjectTestDb.Now, CancellationToken.None)).Select(row => row.Id));

        // An officer who lost the role has no rights, so the projects are not their workload.
        context.UserRoles.RemoveRange(context.UserRoles.Where(link => link.UserId == "po-1"));
        await context.SaveChangesAsync();
        Assert.Equal(new[] { seeded.OtherOfficers }, (await LiveItProjects.LoadAssignedAsync(context, ItProjectTestDb.Now, CancellationToken.None)).Select(row => row.Id));
    }

    // ---- Officer workload ------------------------------------------------------------------

    [Fact]
    public async Task WorkloadCard_ListsEachProjectUnderItsAssignedOfficer_AModuleAtATime()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var seeded = await SeedAsync(db);
        var (service, context, users) = Workload(db);
        await using var _ = context;
        using var __ = users;

        var cards = await service.GetAllWithItProjectsAsync("hod-1");

        var officer = cards.Single(card => card.UserId == "po-1");
        Assert.Equal(3, officer.ItProjectCount);
        Assert.Equal(new[] { "MOLTI projects", "MOLTI projects", "SRE projects" }, officer.ItProjects.Select(item => item.ModuleName));
        var first = officer.ItProjects.Single(item => item.ItProjectId == seeded.MoltiOne);
        Assert.Equal("At TEC", first.State);
        Assert.Equal($"/MoltiProjects/{seeded.MoltiOne}", first.OpenUrl);
        Assert.Equal("bi-clipboard-check", first.ModuleIcon);
        Assert.Equal("Not started", officer.ItProjects.Single(item => item.ItProjectId == seeded.MoltiTwo).State);
        Assert.Equal($"/SreProjects/{seeded.Sre}", officer.ItProjects.Single(item => item.ItProjectId == seeded.Sre).OpenUrl);

        Assert.Equal(new[] { seeded.OtherOfficers }, cards.Single(card => card.UserId == "po-2").ItProjects.Select(item => item.ItProjectId));

        // The ITO still carries the IT projects, and only those.
        var ito = cards.Single(card => card.UserId == "ito-1");
        Assert.Equal(new[] { seeded.It }, ito.ItProjects.Select(item => item.ItProjectId));
        Assert.Equal("IT projects", ito.ItProjects.Single().ModuleName);

        // Nobody carries the unassigned or the delivered project.
        var listed = cards.SelectMany(card => card.ItProjects).Select(item => item.ItProjectId).ToList();
        Assert.DoesNotContain(seeded.Unassigned, listed);
        Assert.DoesNotContain(seeded.Delivered, listed);

        // The plain workload (command desk, digest) keeps its scope.
        Assert.All(await service.GetAllAsync("hod-1"), card => Assert.Empty(card.ItProjects));
    }

    // ---- conference review -----------------------------------------------------------------

    [Fact]
    public async Task ConferenceReview_HasASectionPerModuleUnderTheAssignedOfficer()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var seeded = await SeedAsync(db);
        var service = Conference(db,
            new CommandOfficerWorkloadVm { UserId = "po-1", OfficerName = "Iyer", Rank = "Lt Col" },
            new CommandOfficerWorkloadVm { UserId = "po-2", OfficerName = "Khan", Rank = "Maj" });

        var options = await service.GetOfficerOptionsAsync("hod-1");
        var option = options.Single(candidate => candidate.UserId == "po-1");
        Assert.Equal(3, option.ItProjectCount);
        Assert.Equal(new[] { "2 MOLTI projects", "1 SRE project" }, option.ModuleProjectCounts.Select(count => $"{count.Count} {count.Label}"));
        Assert.Equal(new[] { "1 IT project" }, options.Single(candidate => candidate.UserId == "ito-1").ModuleProjectCounts.Select(count => $"{count.Count} {count.Label}"));

        var conference = (await service.GetAsync("hod-1", "po-1"))!;

        // A Project Officer keeps the project, idea and task sections; the module sections follow.
        Assert.Equal(
            new[] { "Projects", "Ideas", "Other tasks", "MOLTI projects", "SRE projects" },
            conference.Sections.Select(section => section.Title));
        var molti = conference.Sections.Single(section => section.ModuleCode == ItProjectModules.Molti);
        Assert.Equal(ConferenceItemKind.ItProject, molti.Kind);
        Assert.Equal("MOLTI project", molti.ItemLabel);
        Assert.Equal(new[] { seeded.MoltiTwo, seeded.MoltiOne }, molti.Items.Select(item => item.ItemId));
        var item = molti.Items.Single(candidate => candidate.ItemId == seeded.MoltiOne);
        Assert.Equal("At TEC", item.CurrentStateName);
        Assert.Equal($"/MoltiProjects/{seeded.MoltiOne}", item.OpenUrl);
        Assert.Equal("MOLTI project", item.ItemLabel);
        Assert.Equal(new[] { seeded.Sre }, conference.Sections.Single(section => section.ModuleCode == ItProjectModules.Sre).Items.Select(row => row.ItemId));
        Assert.Equal(new[] { "2 MOLTI projects", "1 SRE project" }, conference.ModuleProjectCounts.Select(count => $"{count.Count} {count.Label}"));

        // The other officer sees only their own; the ITO only the IT projects.
        var other = (await service.GetAsync("hod-1", "po-2"))!;
        Assert.Equal(new[] { seeded.OtherOfficers }, other.Sections.Where(section => section.Kind == ConferenceItemKind.ItProject).SelectMany(section => section.Items).Select(row => row.ItemId));
        var ito = (await service.GetAsync("hod-1", "ito-1"))!;
        Assert.Equal(new[] { "IT projects" }, ito.Sections.Select(section => section.Title));
        Assert.Equal(new[] { seeded.It }, ito.Sections.Single().Items.Select(row => row.ItemId));

        // The unassigned project sits under nobody.
        foreach (var officerId in new[] { "po-1", "po-2", "ito-1" })
        {
            var review = (await service.GetAsync("hod-1", officerId))!;
            Assert.DoesNotContain(seeded.Unassigned, review.Sections.SelectMany(section => section.Items).Select(row => row.ItemId));
        }
    }

    [Fact]
    public async Task ConferenceReview_ShowsDirectionsAndTheOfficersReply_InTheModulesWords()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var seeded = await SeedAsync(db);
        var remarks = db.Remarks();
        var hod = ItProjectTestDb.RemarkActor("hod-1", RoleNames.HoD) with { Module = ItProjectModules.Molti };
        var officer = new ItProjectRemarkActor("po-1", "po-1", RoleNames.ProjectOfficer, IsAdmin: false, Module: ItProjectModules.Molti);

        // Issued while the case is at its first stage: the snapshot is in the module's words.
        Assert.True((await remarks.AddAsync(seeded.MoltiTwo, new ItProjectRemarkInput("Get the IPA moving.", D(2026, 11, 2), ItProjectRemarkType.Conference), hod)).Result.Succeeded);
        var service = Conference(db, new CommandOfficerWorkloadVm { UserId = "po-1", OfficerName = "Iyer" });

        var waiting = Item(await service.GetAsync("hod-1", "po-1"), seeded.MoltiTwo);
        Assert.Equal("Get the IPA moving.", waiting.LatestDirection!.Body);
        Assert.Equal("MOLTI ARPPP / IPA", waiting.LatestDirection.SnapshotValue);
        Assert.Equal("Progress update awaited. The officer has not written a remark since the direction was issued.", waiting.EmptyProgressText);

        db.Clock.Set(ItProjectTestDb.Now.AddHours(1));
        Assert.True((await remarks.AddAsync(seeded.MoltiTwo, new ItProjectRemarkInput("File put up on Monday.", D(2026, 11, 8)), officer)).Result.Succeeded);

        var replied = Item(await service.GetAsync("hod-1", "po-1"), seeded.MoltiTwo);
        Assert.Equal("File put up on Monday.", Assert.Single(replied.ProgressEntries).Body);

        var history = await service.GetDirectionHistoryAsync("hod-1", "po-1", ConferenceItemKind.ItProject, seeded.MoltiTwo);
        Assert.Equal("File put up on Monday.", Assert.Single(Assert.Single(history!.Cycles).ProgressEntries).Body);

        // Not readable through an officer who does not hold the project.
        Assert.Null(await service.GetDirectionHistoryAsync("hod-1", "po-2", ConferenceItemKind.ItProject, seeded.MoltiTwo));
        Assert.Null(await service.GetDirectionHistoryAsync("hod-1", "ito-1", ConferenceItemKind.ItProject, seeded.MoltiTwo));
    }

    [Fact]
    public async Task OfficersOwnReview_AddsTheirModuleSections_AndKeepsTheRest()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var seeded = await SeedAsync(db);
        var service = Conference(db, new CommandOfficerWorkloadVm { UserId = "po-1", OfficerName = "Iyer" });

        var own = (await service.GetForProjectOfficerAsync("po-1"))!;

        // No other work, but a Project Officer still gets the full review (not an ITO-style one).
        Assert.Equal(
            new[] { "Projects", "Ideas", "Other tasks", "MOLTI projects", "SRE projects" },
            own.Sections.Select(section => section.Title));
        Assert.Equal(3, own.ItProjectCount);
        Assert.NotNull(await service.GetDirectionHistoryForProjectOfficerAsync("po-1", ConferenceItemKind.ItProject, seeded.Sre));
        Assert.Null(await service.GetDirectionHistoryForProjectOfficerAsync("po-1", ConferenceItemKind.ItProject, seeded.OtherOfficers));
        Assert.Null(await service.GetDirectionHistoryForProjectOfficerAsync("po-1", ConferenceItemKind.ItProject, seeded.It));

        // The ITO's own review is as before: only the IT projects.
        var ito = (await service.GetForProjectOfficerAsync("ito-1"))!;
        Assert.Equal(new[] { "IT projects" }, ito.Sections.Select(section => section.Title));
    }

    // ---- the workspace rail ----------------------------------------------------------------

    [Fact]
    public async Task Rail_LinksEachModuleTheOfficerHasLiveProjectsIn()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        await SeedAsync(db);
        var query = new ItoWorkspaceQuery(db.NewContext(), db.Clock);

        var links = await query.GetAssignedModuleLinksAsync("po-1");
        Assert.Equal(new[] { "MOLTI projects", "SRE projects" }, links.Select(link => link.Name));
        Assert.Equal(new[] { "/MoltiProjects/Index", "/SreProjects/Index" }, links.Select(link => link.Page));
        Assert.Equal(new[] { 2, 1 }, links.Select(link => link.Count));

        Assert.Equal(new[] { "MOLTI projects" }, (await query.GetAssignedModuleLinksAsync("po-2")).Select(link => link.Name));
        Assert.Empty(await query.GetAssignedModuleLinksAsync("ito-1"));
        Assert.Empty(await query.GetAssignedModuleLinksAsync("hod-1"));
        Assert.Empty(await query.GetAssignedModuleLinksAsync(null));

        // The ITO's count is of IT projects only.
        Assert.Equal(1, await query.CountLiveItProjectsAsync());
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static OfficerConferenceItemVm Item(OfficerConferenceVm? conference, int id)
        => conference!.Sections.SelectMany(section => section.Items).Single(item => item.Kind == ConferenceItemKind.ItProject && item.ItemId == id);

    private static OfficerConferenceReadService Conference(ItProjectTestDb db, params CommandOfficerWorkloadVm[] officers)
        => new(db.NewContext(), new FixedWorkload(officers), new WorkflowStageMetadataProvider(), db.Clock);

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

    private sealed class FixedWorkload(IReadOnlyList<CommandOfficerWorkloadVm> officers) : IOfficerWorkloadReadService
    {
        public Task<IReadOnlyList<CommandOfficerWorkloadVm>> GetAllAsync(string requestingUserId, CancellationToken cancellationToken = default)
            => Task.FromResult(officers);

        public Task<IReadOnlyList<CommandOfficerWorkloadVm>> GetAllWithItProjectsAsync(string requestingUserId, CancellationToken cancellationToken = default)
            => GetAllAsync(requestingUserId, cancellationToken);

        public Task<CommandOfficerWorkloadVm?> GetOfficerAsync(string officerUserId, CancellationToken cancellationToken = default)
            => Task.FromResult(officers.SingleOrDefault(officer => officer.UserId == officerUserId));

        public Task<int> CountActiveOfficersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(officers.Count);
    }
}
