using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Models.ItProjects;
using ProjectManagement.Services.ItProjects;
using Xunit;

namespace ProjectManagement.Tests.ItProjects;

// SECTION: Modules on the IT projects engine (docs/ux/molti-sre-projects.md)
// A project belongs to one module. Every read and write is scoped by it, so a project id of
// another module is "not found", and the officer assigned to a project has rights on it alone.
public class ItProjectModuleTests
{
    private static ItProjectActor Actor(string module, string userId = "hod-1") => new(userId, userId, module);

    private static async Task<int> SeedAsync(ItProjectTestDb db, string module, string title)
    {
        var (result, id) = await db.Commands().CreateAsync(ItProjectTestDb.Details(title), Actor(module));
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        return id;
    }

    // ---- catalogue -------------------------------------------------------------------------

    [Fact]
    public void Catalogue_HasThreeModules_NamedAsTheOwnerWroteThem()
    {
        Assert.Equal(new[] { "IT", "MOLTI", "SRE" }, ItProjectModules.All.Select(module => module.Code));
        Assert.Equal(new[] { "IT projects", "MOLTI projects", "SRE projects" }, ItProjectModules.All.Select(module => module.Name));
        Assert.Equal(new[] { "/ItProjects", "/MoltiProjects", "/SreProjects" }, ItProjectModules.All.Select(module => module.RoutePrefix));
        Assert.False(ItProjectModules.HasAssignedOfficer(ItProjectModules.It));
        Assert.True(ItProjectModules.HasAssignedOfficer(ItProjectModules.Molti));
        Assert.True(ItProjectModules.HasAssignedOfficer(ItProjectModules.Sre));
        Assert.All(ItProjectModules.All, module => Assert.True(module.Code.Length <= ItProjectModules.CodeMaxLength));
        Assert.False(ItProjectModules.IsKnown("it"));
        Assert.False(ItProjectModules.IsKnown(null));
    }

    // ---- create ----------------------------------------------------------------------------

    [Fact]
    public async Task Create_StoresTheActorsModule_AndAnExistingCallIsAnItProject()
    {
        await using var db = await ItProjectTestDb.CreateAsync();

        var (it, _) = await db.SeedAsync();
        var molti = await SeedAsync(db, ItProjectModules.Molti, "MOLTI case");

        await using var context = db.NewContext();
        Assert.Equal(ItProjectModules.It, (await context.ItProjects.SingleAsync(project => project.Id == it)).Module);
        var stored = await context.ItProjects.SingleAsync(project => project.Id == molti);
        Assert.Equal(ItProjectModules.Molti, stored.Module);
        Assert.Null(stored.ProjectOfficerUserId);
        Assert.Equal(ItProjectModules.Molti, db.Audit.Entries.Last().Data["Module"]);
    }

    [Fact]
    public async Task Create_RejectsAnUnknownModule()
    {
        await using var db = await ItProjectTestDb.CreateAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.Commands().CreateAsync(ItProjectTestDb.Details(), Actor("OTHER")));
    }

    // ---- reads -----------------------------------------------------------------------------

    [Fact]
    public async Task Board_ShowsOnlyItsOwnModule()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        await db.SeedAsync("IT case");
        await SeedAsync(db, ItProjectModules.Molti, "MOLTI case");
        await SeedAsync(db, ItProjectModules.Sre, "SRE case one");
        await SeedAsync(db, ItProjectModules.Sre, "SRE case two");

        var it = await db.Reads().GetBoardAsync(new ItProjectBoardQuery(ItProjectBoardScope.All));
        var molti = await db.Reads().GetBoardAsync(new ItProjectBoardQuery(ItProjectBoardScope.All, Module: ItProjectModules.Molti));
        var sre = await db.Reads().GetBoardAsync(new ItProjectBoardQuery(ItProjectBoardScope.All, Module: ItProjectModules.Sre));

        Assert.Equal(new[] { "IT case" }, it.Rows.Select(row => row.Title));
        Assert.Equal(1, it.ActiveCount);
        Assert.Equal(new[] { "MOLTI case" }, molti.Rows.Select(row => row.Title));
        Assert.Equal(new[] { "SRE case one", "SRE case two" }, sre.Rows.Select(row => row.Title));
        Assert.Equal(2, sre.ActiveCount);
    }

    [Fact]
    public async Task Get_FindsAProjectOnlyThroughItsOwnModule()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (it, _) = await db.SeedAsync();
        var molti = await SeedAsync(db, ItProjectModules.Molti, "MOLTI case");

        Assert.NotNull(await db.Reads().GetAsync(it));
        Assert.Null(await db.Reads().GetAsync(molti));
        Assert.NotNull(await db.Reads().GetAsync(molti, ItProjectModules.Molti));
        Assert.Null(await db.Reads().GetAsync(molti, ItProjectModules.Sre));
        Assert.Null(await db.Reads().GetAsync(it, ItProjectModules.Molti));
    }

    [Fact]
    public async Task LiveItProjects_LeavesOutTheOtherModules()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        await db.SeedAsync("IT case");
        await SeedAsync(db, ItProjectModules.Molti, "MOLTI case");
        await SeedAsync(db, ItProjectModules.Sre, "SRE case");

        await using var context = db.NewContext();
        var live = await LiveItProjects.LoadAsync(context, ItProjectTestDb.Now, CancellationToken.None);

        Assert.Equal(new[] { "IT case" }, live.Select(row => row.Title));
    }

    // ---- writes ----------------------------------------------------------------------------

    [Fact]
    public async Task Commands_DoNotReachAProjectOfAnotherModule()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var molti = await SeedAsync(db, ItProjectModules.Molti, "MOLTI case");
        var version = await db.RowVersionAsync(molti);
        var it = ItProjectTestDb.Actor();
        var sre = Actor(ItProjectModules.Sre);
        var stages = ItProjectStages.All
            .Select(stage => new ItProjectStageInput(stage.Code, IsCompleted: stage.Code == ItProjectStages.Ppp, null, stage.IsOptional, null))
            .ToList();

        foreach (var actor in new[] { it, sre })
        {
            // "Not found", in the words of the module the caller is in.
            var notFound = ItProjectModules.Reword(ItProjectCommandService.NotFoundMessage, actor.Module);
            Assert.Equal(notFound, (await db.Commands().UpdateDetailsAsync(molti, ItProjectTestDb.Details("Renamed"), version, actor)).Errors.Single());
            Assert.Equal(notFound, (await db.Commands().SaveStagesAsync(molti, stages, version, actor)).Result.Errors.Single());
            Assert.Equal(notFound, (await db.Commands().DropAsync(molti, new DateOnly(2026, 11, 1), "Not needed", version, actor)).Errors.Single());
            Assert.Equal(notFound, (await db.Commands().ReinstateAsync(molti, version, actor)).Errors.Single());
            Assert.Equal(notFound, (await db.Commands().SaveYearAsync(molti, 2025, new ItProjectYearInput(null, "X/1"), version, actor)).Errors.Single());
            Assert.Equal(notFound, (await db.Commands().DeleteAsync(molti, version, actor)).Errors.Single());
        }

        await using var context = db.NewContext();
        var stored = await context.ItProjects.Include(project => project.Stages).SingleAsync(project => project.Id == molti);
        Assert.Equal("MOLTI case", stored.Title);
        Assert.False(stored.IsDeleted);
        Assert.Null(stored.DroppedOn);
        Assert.DoesNotContain(stored.Stages, stage => stage.IsCompleted);

        // Through its own module the same project is reached.
        var own = await db.Commands().SaveStagesAsync(molti, stages, version, Actor(ItProjectModules.Molti));
        Assert.True(own.Result.Succeeded, string.Join("; ", own.Result.Errors));
    }

    [Fact]
    public async Task Remarks_AreScopedByModule()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var molti = await SeedAsync(db, ItProjectModules.Molti, "MOLTI case");
        var input = new ItProjectRemarkInput("Chase the vendor for the revised quote.", new DateOnly(2026, 11, 8));
        var hodInIt = ItProjectTestDb.RemarkActor("hod-1", RoleNames.HoD);
        var hodInMolti = hodInIt with { Module = ItProjectModules.Molti };

        var (wrongModule, _) = await db.Remarks().AddAsync(molti, input, hodInIt);
        Assert.Equal(ItProjectCommandService.NotFoundMessage, wrongModule.Errors.Single());

        var (added, remarkId) = await db.Remarks().AddAsync(molti, input, hodInMolti);
        Assert.True(added.Succeeded, string.Join("; ", added.Errors));

        Assert.Empty(await db.Remarks().ListAsync(molti));
        Assert.Empty(await db.Remarks().ListAsync(molti, ItProjectModules.Sre));
        var listed = Assert.Single(await db.Remarks().ListAsync(molti, ItProjectModules.Molti));

        Assert.Equal(ItProjectRemarkService.NotFoundMessage, (await db.Remarks().EditAsync(molti, remarkId, input with { Body = "Changed." }, listed.RowVersion, hodInIt)).Errors.Single());
        Assert.Equal(ItProjectRemarkService.NotFoundMessage, (await db.Remarks().DeleteAsync(molti, remarkId, listed.RowVersion, hodInIt)).Errors.Single());
        Assert.True((await db.Remarks().EditAsync(molti, remarkId, input with { Body = "Changed." }, listed.RowVersion, hodInMolti)).Succeeded);
    }

    [Fact]
    public async Task Attachments_AreScopedByModule()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var sre = await SeedAsync(db, ItProjectModules.Sre, "SRE case");
        var file = ItProjectAttachmentServiceTests.FormFile("note.pdf", ItProjectAttachmentServiceTests.PdfType, ItProjectAttachmentServiceTests.PdfBytes);

        var (wrongModule, _) = await db.Attachments().UploadAsync(sre, file, ItProjectTestDb.Actor());
        Assert.Equal(ItProjectCommandService.NotFoundMessage, wrongModule.Errors.Single());
        Assert.Empty(db.Storage.Saved);

        var (uploaded, attachmentId) = await db.Attachments().UploadAsync(sre, file, Actor(ItProjectModules.Sre));
        Assert.True(uploaded.Succeeded, string.Join("; ", uploaded.Errors));

        Assert.Empty(await db.Attachments().ListAsync(sre));
        Assert.Single(await db.Attachments().ListAsync(sre, ItProjectModules.Sre));
        Assert.Null(await db.Attachments().OpenAsync(sre, attachmentId));
        Assert.Null(await db.Attachments().OpenAsync(sre, attachmentId, ItProjectModules.Molti));
        await using (var download = (await db.Attachments().OpenAsync(sre, attachmentId, ItProjectModules.Sre))!.Content)
        {
            Assert.True(download.Length > 0);
        }

        Assert.False((await db.Attachments().DeleteAsync(sre, attachmentId, ItProjectTestDb.Actor())).Succeeded);
        Assert.True((await db.Attachments().DeleteAsync(sre, attachmentId, Actor(ItProjectModules.Sre))).Succeeded);
    }

    // ---- access ----------------------------------------------------------------------------

    [Theory]
    [InlineData(ItProjectModules.Molti)]
    [InlineData(ItProjectModules.Sre)]
    public void ManageAndRemarkRoles_OfAModuleWithAnAssignedOfficer(string module)
    {
        var project = new ItProject { Module = module };

        foreach (var role in new[] { RoleNames.Admin, RoleNames.HoD })
        {
            Assert.True(ItProjectAccess.CanManage(ItProjectTestDb.User("u", role), project));
            Assert.True(ItProjectAccess.CanManage(ItProjectTestDb.User("u", role), module));
            Assert.True(ItProjectAccess.CanWorkOn(ItProjectTestDb.User("u", role), project));
        }

        // The ITO runs IT projects only.
        foreach (var role in new[] { RoleNames.Ito, RoleNames.Mco, RoleNames.Comdt, RoleNames.ProjectOfficer, RoleNames.ItCellClerk })
        {
            var user = ItProjectTestDb.User("u", role);
            Assert.True(ItProjectAccess.CanView(user, project));
            Assert.False(ItProjectAccess.CanManage(user, project));
            Assert.False(ItProjectAccess.CanManage(user, module));
            Assert.False(ItProjectAccess.CanWorkOn(user, project));
        }

        Assert.Equal(RoleNames.Comdt, ItProjectAccess.RemarkRoleOf(ItProjectTestDb.User("u", RoleNames.Comdt, RoleNames.HoD), project));
        Assert.Equal(RoleNames.HoD, ItProjectAccess.RemarkRoleOf(ItProjectTestDb.User("u", RoleNames.HoD), project));
        Assert.Equal(RoleNames.Mco, ItProjectAccess.RemarkRoleOf(ItProjectTestDb.User("u", RoleNames.Mco), project));
        Assert.False(ItProjectAccess.CanRemark(ItProjectTestDb.User("u", RoleNames.Ito), project));
        Assert.False(ItProjectAccess.CanRemark(ItProjectTestDb.User("u", RoleNames.Admin), project));
        Assert.False(ItProjectAccess.CanRemark(ItProjectTestDb.User("u", RoleNames.ProjectOfficer), project));
    }

    [Theory]
    [InlineData(ItProjectModules.Molti)]
    [InlineData(ItProjectModules.Sre)]
    public void AssignedOfficer_WorksOnAndRemarksOnTheirOwnProjectOnly(string module)
    {
        var own = new ItProject { Module = module, ProjectOfficerUserId = "po-1" };
        var someoneElses = new ItProject { Module = module, ProjectOfficerUserId = "po-2" };
        var unassigned = new ItProject { Module = module };
        var officer = ItProjectTestDb.User("po-1", RoleNames.ProjectOfficer);

        Assert.True(ItProjectAccess.IsAssignedOfficer(officer, own));
        Assert.True(ItProjectAccess.CanWorkOn(officer, own));
        Assert.True(ItProjectAccess.CanRemark(officer, own));
        Assert.Equal(RoleNames.ProjectOfficer, ItProjectAccess.RemarkRoleOf(officer, own));

        // Creating, deleting and choosing the officer stay with Admin and HoD.
        Assert.False(ItProjectAccess.CanManage(officer, own));

        foreach (var project in new[] { someoneElses, unassigned })
        {
            Assert.True(ItProjectAccess.CanView(officer, project));
            Assert.False(ItProjectAccess.IsAssignedOfficer(officer, project));
            Assert.False(ItProjectAccess.CanWorkOn(officer, project));
            Assert.False(ItProjectAccess.CanRemark(officer, project));
        }
    }

    [Fact]
    public void AssignedOfficer_LosesTheRightsWithTheRole_AndOnADeletedProject()
    {
        var project = new ItProject { Module = ItProjectModules.Molti, ProjectOfficerUserId = "po-1" };

        var noLongerAnOfficer = ItProjectTestDb.User("po-1", RoleNames.Ta);
        Assert.False(ItProjectAccess.CanWorkOn(noLongerAnOfficer, project));
        Assert.False(ItProjectAccess.CanRemark(noLongerAnOfficer, project));

        project.IsDeleted = true;
        var officer = ItProjectTestDb.User("po-1", RoleNames.ProjectOfficer);
        Assert.False(ItProjectAccess.CanWorkOn(officer, project));
        Assert.False(ItProjectAccess.CanRemark(officer, project));
    }

    [Fact]
    public void ItProjects_NeverHaveAnAssignedOfficer()
    {
        // A stray officer id on an IT project gives no rights: the ITO runs IT projects.
        var project = new ItProject { Module = ItProjectModules.It, ProjectOfficerUserId = "po-1" };
        var officer = ItProjectTestDb.User("po-1", RoleNames.ProjectOfficer);

        Assert.False(ItProjectAccess.IsAssignedOfficer(officer, project));
        Assert.False(ItProjectAccess.CanWorkOn(officer, project));
        Assert.False(ItProjectAccess.CanRemark(officer, project));
        Assert.True(ItProjectAccess.CanWorkOn(ItProjectTestDb.User("ito-1", RoleNames.Ito), project));
    }

    [Fact]
    public void UnknownModule_GivesNoRights()
    {
        var project = new ItProject { Module = "OTHER", ProjectOfficerUserId = "po-1" };

        Assert.False(ItProjectAccess.CanManage(ItProjectTestDb.User("u", RoleNames.Admin), project));
        Assert.False(ItProjectAccess.CanWorkOn(ItProjectTestDb.User("po-1", RoleNames.ProjectOfficer), project));
        Assert.False(ItProjectAccess.CanRemark(ItProjectTestDb.User("u", RoleNames.Comdt), project));
    }
}
