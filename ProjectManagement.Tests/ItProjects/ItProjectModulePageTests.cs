using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Models.ItProjects;
using ProjectManagement.Pages.ItProjects;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.ItProjects;
using ProjectManagement.Services.Usage;
using Xunit;
using Molti = ProjectManagement.Pages.MoltiProjects;
using Sre = ProjectManagement.Pages.SreProjects;

namespace ProjectManagement.Tests.ItProjects;

// SECTION: MOLTI projects and SRE projects on screen (docs/ux/molti-sre-projects.md, PR 3)
// Each module has its own pages, policies, menu entry and access rows; the page models and the
// markup are shared, and a module's page never shows or reaches another module's project, nor
// says "IT".
public class ItProjectModulePageTests
{
    // ---- page classes and policies ---------------------------------------------------------

    [Theory]
    [InlineData(typeof(IndexModel), Policies.ItProjects.View, ItProjectModules.It)]
    [InlineData(typeof(CreateModel), Policies.ItProjects.Manage, ItProjectModules.It)]
    [InlineData(typeof(DetailsModel), Policies.ItProjects.View, ItProjectModules.It)]
    [InlineData(typeof(Molti.IndexModel), Policies.MoltiProjects.View, ItProjectModules.Molti)]
    [InlineData(typeof(Molti.CreateModel), Policies.MoltiProjects.Manage, ItProjectModules.Molti)]
    [InlineData(typeof(Molti.DetailsModel), Policies.MoltiProjects.View, ItProjectModules.Molti)]
    [InlineData(typeof(Sre.IndexModel), Policies.SreProjects.View, ItProjectModules.Sre)]
    [InlineData(typeof(Sre.CreateModel), Policies.SreProjects.Manage, ItProjectModules.Sre)]
    [InlineData(typeof(Sre.DetailsModel), Policies.SreProjects.View, ItProjectModules.Sre)]
    public async Task EachPage_CarriesOnlyItsOwnModulesPolicyAndCode(Type page, string policy, string module)
    {
        // Inherited attributes count: a policy on a shared base would apply to every module.
        Assert.Equal(policy, Assert.Single(page.GetCustomAttributes<AuthorizeAttribute>(inherit: true)).Policy);

        await using var db = await ItProjectTestDb.CreateAsync();
        var instance = page.Name switch
        {
            nameof(IndexModel) => Activator.CreateInstance(page, db.Reads()),
            nameof(CreateModel) => page == typeof(CreateModel)
                ? Activator.CreateInstance(page, db.Commands(), db.Clock)
                : Activator.CreateInstance(page, db.Commands(), db.Clock, db.Reads()),
            _ => Activator.CreateInstance(page, db.Reads(), db.Commands(), db.Remarks(), db.Attachments(), db.Clock)
        };
        Assert.Equal(module, (string)page.GetProperty("ModuleCode")!.GetValue(instance)!);
    }

    [Fact]
    public void SharedPageModels_CarryNoPolicyOfTheirOwn()
    {
        foreach (var shared in new[] { typeof(ItProjectBoardPageModel), typeof(ItProjectCreatePageModel), typeof(ItProjectDetailsPageModel) })
        {
            Assert.True(shared.IsAbstract);
            Assert.Empty(shared.GetCustomAttributes<AuthorizeAttribute>(inherit: true));
        }
    }

    // ---- board -----------------------------------------------------------------------------

    [Fact]
    public async Task Board_ShowsItsOwnModule_AndOnlyAdminAndHodMayAdd()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        await db.SeedAsync("IT case");
        await SeedAsync(db, ItProjectModules.Molti, "MOLTI case");
        await SeedAsync(db, ItProjectModules.Sre, "SRE case");

        var molti = new Molti.IndexModel(db.Reads()) { PageContext = ItProjectBoardPageTests.Context(ItProjectTestDb.User("hod-1", RoleNames.HoD)) };
        await molti.OnGetAsync(CancellationToken.None);
        Assert.Equal(new[] { "MOLTI case" }, molti.ScopedRows.Select(row => row.Title));
        Assert.True(molti.CanManage);
        Assert.Equal("MOLTI projects", molti.Module.Name);

        // The ITO runs IT projects, not the other modules.
        var sreAsIto = new Sre.IndexModel(db.Reads()) { PageContext = ItProjectBoardPageTests.Context(ItProjectTestDb.User("ito-1", RoleNames.Ito)) };
        await sreAsIto.OnGetAsync(CancellationToken.None);
        Assert.Equal(new[] { "SRE case" }, sreAsIto.ScopedRows.Select(row => row.Title));
        Assert.False(sreAsIto.CanManage);

        var it = new IndexModel(db.Reads()) { PageContext = ItProjectBoardPageTests.Context(ItProjectTestDb.User("ito-1", RoleNames.Ito)) };
        await it.OnGetAsync(CancellationToken.None);
        Assert.Equal(new[] { "IT case" }, it.ScopedRows.Select(row => row.Title));
        Assert.True(it.CanManage);
    }

    // ---- create ----------------------------------------------------------------------------

    [Fact]
    public async Task Create_SavesIntoThePagesModule_AndOpensThatModulesProjectPage()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var context = ItProjectBoardPageTests.Context(ItProjectTestDb.User("hod-1", RoleNames.HoD));
        var page = new Sre.CreateModel(db.Commands(), db.Clock, db.Reads())
        {
            PageContext = context,
            TempData = new TempDataDictionary(context.HttpContext, new NullTempData()),
            Input = { Title = "SRE case", PppReferenceNumber = "SRE/04", FinancialYearStart = 2026, Description = "A case of this module." }
        };

        var result = Assert.IsType<RedirectToPageResult>(await page.OnPostAsync(CancellationToken.None));

        // Relative, so it lands on /SreProjects/{id}, not on the IT page.
        Assert.Equal("./Details", result.PageName);
        await using var check = db.NewContext();
        var stored = await check.ItProjects.SingleAsync();
        Assert.Equal(ItProjectModules.Sre, stored.Module);
        Assert.Equal((int)result.RouteValues!["id"]!, stored.Id);
        Assert.Contains(page.TempData.Values.OfType<string>(), value => value.Contains("SRE project", StringComparison.Ordinal));
        Assert.DoesNotContain(page.TempData.Values.OfType<string>(), value => value.Contains("IT project", StringComparison.Ordinal));
    }

    // ---- project page ----------------------------------------------------------------------

    [Fact]
    public async Task ProjectPage_FindsAProjectOnlyUnderItsOwnModule()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (it, _) = await db.SeedAsync("IT case");
        var molti = await SeedAsync(db, ItProjectModules.Molti, "MOLTI case");
        var hod = ItProjectTestDb.User("hod-1", RoleNames.HoD);

        Assert.IsType<PageResult>(await MoltiPage(db, hod).OnGetAsync(molti, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await MoltiPage(db, hod).OnGetAsync(it, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await ItProjectDetailsPageTests.PageFor(db, hod).OnGetAsync(molti, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await SrePage(db, hod).OnGetAsync(molti, CancellationToken.None));

        // A write through the wrong module's page does not reach the project either.
        var wrong = ItProjectDetailsPageTests.PageFor(db, hod);
        wrong.RowVersion = DetailsModel.EncodeRowVersion(await db.RowVersionAsync(molti));
        Assert.IsType<NotFoundResult>(await wrong.OnPostDeleteAsync(molti, CancellationToken.None));
        await using var check = db.NewContext();
        Assert.False((await check.ItProjects.SingleAsync(project => project.Id == molti)).IsDeleted);
    }

    [Fact]
    public async Task ProjectPage_SpeaksInTheModulesWords()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var molti = await SeedAsync(db, ItProjectModules.Molti, "MOLTI case");
        var page = MoltiPage(db, ItProjectTestDb.User("hod-1", RoleNames.HoD));

        await page.OnGetAsync(molti, CancellationToken.None);

        Assert.Equal("MOLTI ARPPP / IPA", page.StageRows[0].Label);
        Assert.Equal("MOLTI ARPPP / IPA", page.Journey[0].Label);
        Assert.Equal("MOLTI ARPPP / IPA", page.Status.CurrentStageLabel);
        Assert.DoesNotContain(page.StageRows, row => row.Label.Contains("IT ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AssignedOfficer_WorksOnTheirProjectButDoesNotDeleteIt()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var own = await SeedAsync(db, ItProjectModules.Molti, "Own case");
        var other = await SeedAsync(db, ItProjectModules.Molti, "Another officer's case");
        await using (var context = db.NewContext())
        {
            (await context.ItProjects.SingleAsync(project => project.Id == own)).ProjectOfficerUserId = "po-1";
            (await context.ItProjects.SingleAsync(project => project.Id == other)).ProjectOfficerUserId = "po-2";
            await context.SaveChangesAsync();
        }

        var officer = ItProjectTestDb.User("po-1", RoleNames.ProjectOfficer);

        var page = MoltiPage(db, officer);
        await page.OnGetAsync(own, CancellationToken.None);
        Assert.True(page.CanWorkOn);
        Assert.False(page.CanManage);
        Assert.True(page.CanRemark);
        Assert.Equal(RoleNames.ProjectOfficer, page.RemarkRole);

        // Works on it: marks it dropped.
        var drop = MoltiPage(db, officer);
        drop.Drop = new ItProjectDetailsPageModel.DropInput { DroppedOn = new DateOnly(2026, 11, 1), Reason = "Requirement withdrawn.", RowVersion = DetailsModel.EncodeRowVersion(await db.RowVersionAsync(own)) };
        Assert.IsType<RedirectToPageResult>(await drop.OnPostDropAsync(own, CancellationToken.None));

        // Does not delete it: that stays with Admin and HoD.
        var delete = MoltiPage(db, officer);
        delete.RowVersion = DetailsModel.EncodeRowVersion(await db.RowVersionAsync(own));
        Assert.IsType<ForbidResult>(await delete.OnPostDeleteAsync(own, CancellationToken.None));

        // Another officer's project is read only.
        var elsewhere = MoltiPage(db, officer);
        await elsewhere.OnGetAsync(other, CancellationToken.None);
        Assert.False(elsewhere.CanWorkOn);
        Assert.False(elsewhere.CanRemark);
        var blocked = MoltiPage(db, officer);
        blocked.Drop = new ItProjectDetailsPageModel.DropInput { DroppedOn = new DateOnly(2026, 11, 1), Reason = "No.", RowVersion = DetailsModel.EncodeRowVersion(await db.RowVersionAsync(other)) };
        Assert.IsType<ForbidResult>(await blocked.OnPostDropAsync(other, CancellationToken.None));

        await using var check = db.NewContext();
        Assert.NotNull((await check.ItProjects.SingleAsync(project => project.Id == own)).DroppedOn);
        Assert.False((await check.ItProjects.SingleAsync(project => project.Id == own)).IsDeleted);
        Assert.Null((await check.ItProjects.SingleAsync(project => project.Id == other)).DroppedOn);
    }

    // ---- wording ---------------------------------------------------------------------------

    [Fact]
    public void Reword_PutsTheModulesNamesWhereTheItOnesStood()
    {
        Assert.Equal("MOLTI ARPPP / IPA", ItProjectStages.LabelOf(ItProjectStages.Ppp, ItProjectModules.Molti));
        Assert.Equal("SRE ARPPP / IPA", ItProjectStages.LabelOf(ItProjectStages.Ppp, ItProjectModules.Sre));
        Assert.Equal("IT ARPPP / IPA", ItProjectStages.LabelOf(ItProjectStages.Ppp, ItProjectModules.It));
        Assert.Equal("Delivery and ATP", ItProjectStages.LabelOf(ItProjectStages.Delivery, ItProjectModules.Molti));

        Assert.Equal("The SRE project could not be found.", ItProjectModules.Reword(ItProjectCommandService.NotFoundMessage, ItProjectModules.Sre));
        Assert.Equal("Give the MOLTI ARPPP reference number.", ItProjectModules.Reword("Give the IT ARPPP reference number.", ItProjectModules.Molti));
        Assert.Equal(ItProjectCommandService.NotFoundMessage, ItProjectModules.Reword(ItProjectCommandService.NotFoundMessage, ItProjectModules.It));
        Assert.Equal(ItProjectCommandService.NotFoundMessage, ItProjectModules.Reword(ItProjectCommandService.NotFoundMessage, "OTHER"));

        // Role names are not module names: "ITO" stays as written.
        Assert.Equal("The ITO was told.", ItProjectModules.Reword("The ITO was told.", ItProjectModules.Molti));
    }

    [Theory]
    [InlineData(ItProjectModules.Molti)]
    [InlineData(ItProjectModules.Sre)]
    public async Task ServiceMessages_NeverSayItInAnotherModule(string module)
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var actor = new ItProjectActor("hod-1", "hod-1", module);
        var id = await SeedAsync(db, module, "A case");
        var version = await db.RowVersionAsync(id);
        var list = ItProjectModules.Get(module).ListName;

        var (blank, _) = await db.Commands().CreateAsync(new ItProjectDetailsInput("Title", " ", 2026, "Text"), actor);
        Assert.Contains($"Give the {list} reference number.", blank.Errors);

        var notDropped = await db.Commands().ReinstateAsync(id, version, actor);
        var stale = await db.Commands().DeleteAsync(id, new byte[] { 1 }, actor);
        var costOnBid = await db.Commands().SaveStagesAsync(
            id,
            ItProjectStages.All.Select(stage => new ItProjectStageInput(stage.Code, false, null, stage.IsOptional, stage.Code == ItProjectStages.Bid ? 5m : null)).ToList(),
            version,
            actor);
        var (noRole, _) = await db.Remarks().AddAsync(id, new ItProjectRemarkInput("A remark.", new DateOnly(2026, 11, 8)), new ItProjectRemarkActor("u", "u", null, false, Module: module));

        var messages = blank.Errors.Concat(notDropped.Errors).Concat(stale.Errors).Concat(costOnBid.Result.Errors).Concat(noRole.Errors).ToList();
        Assert.True(messages.Count >= 5);
        Assert.All(messages, message => Assert.DoesNotMatch(@"\bIT\b|\bITO\b", message));
        Assert.Contains(messages, message => message.Contains($"{list} / IPA", StringComparison.Ordinal));
        Assert.Contains(messages, message => message.Contains("assigned officer", StringComparison.Ordinal));
        Assert.True(stale.ConcurrencyConflict);
    }

    // ---- views -----------------------------------------------------------------------------

    [Theory]
    [InlineData("_ModuleBoard.cshtml")]
    [InlineData("_ModuleCreate.cshtml")]
    [InlineData("_ModuleProject.cshtml")]
    public void SharedViews_NameNoModule(string partial)
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "Pages", "ItProjects", partial));
        var withoutComments = Regex.Replace(Regex.Replace(source, @"@\*.*?\*@", string.Empty, RegexOptions.Singleline), @"^\s*//.*$", string.Empty, RegexOptions.Multiline);

        // The words come from the module ("@(m.ListName)"), and links are relative, so the same
        // markup serves the three modules.
        Assert.DoesNotMatch(@"\bIT\b|\bITO\b|\bMOLTI\b|\bSRE\b", withoutComments);
        Assert.DoesNotMatch(@"asp-page=""/(It|Molti|Sre)Projects", withoutComments);
        Assert.DoesNotContain("@page", withoutComments, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ItProjects")]
    [InlineData("MoltiProjects")]
    [InlineData("SreProjects")]
    public void EachModule_HasItsOwnThreePagesOnTheSharedViews(string folder)
    {
        var root = Path.Combine(RepositoryRoot(), "Pages", folder);
        foreach (var (page, partial, directive) in new[]
                 {
                     ("Index", "_ModuleBoard", "@page"),
                     ("Create", "_ModuleCreate", "@page"),
                     ("Details", "_ModuleProject", $"@page \"/{folder}/{{id:int}}\"")
                 })
        {
            var source = File.ReadAllText(Path.Combine(root, page + ".cshtml"));
            Assert.Equal(directive, source.Split('\n')[0].Trim());
            Assert.Contains($"@model ProjectManagement.Pages.{folder}.{page}Model", source, StringComparison.Ordinal);
            Assert.Contains($"<partial name=\"~/Pages/ItProjects/{partial}.cshtml\" model=\"Model\" />", source, StringComparison.Ordinal);
        }
    }

    // ---- menu, usage, who can do what ------------------------------------------------------

    [Theory]
    [InlineData("/MoltiProjects/Index", "molti-projects")]
    [InlineData("/MoltiProjects/12", "molti-projects")]
    [InlineData("/SreProjects/Create", "sre-projects")]
    [InlineData("/ItProjects/12", "it-projects")]
    public void Usage_CountsEachModuleOnItsOwn(string path, string expected)
        => Assert.Equal(expected, new ErpUsageModuleCatalog().ResolvePath(new PathString(path)));

    [Theory]
    [InlineData("molti-projects", "MOLTI projects")]
    [InlineData("sre-projects", "SRE projects")]
    public void RoleAccessCatalogue_ListsTheModuleForTheRightRoles(string key, string title)
    {
        var catalog = new AdminRoleAccessCatalog();
        var all = new[] { RoleNames.Admin, RoleNames.HoD, RoleNames.Ito, RoleNames.Mco, RoleNames.Comdt, RoleNames.ProjectOfficer };

        var group = catalog.BuildForRoles(all).Single(candidate => candidate.Key == key);
        Assert.Equal(title, group.Title);
        Assert.Equal(new[] { $"{key}-view", $"{key}-manage", $"{key}-remark", $"{key}-assigned" }, group.Items.Select(item => item.Key));
        Assert.Contains($"{key}-assigned", catalog.ForRoles(new[] { RoleNames.ProjectOfficer }, all).Select(item => item.Key));
        Assert.DoesNotContain($"{key}-assigned", catalog.ForRoles(new[] { RoleNames.HoD }, all).Select(item => item.Key));
        Assert.All(group.Items, item => Assert.DoesNotMatch(@"\bIT\b", item.Title + " " + item.Description));

        string[] KeysFor(string role) => catalog.ForRoles(new[] { role }, all).Select(item => item.Key).ToArray();
        Assert.Contains($"{key}-manage", KeysFor(RoleNames.HoD));
        Assert.Contains($"{key}-manage", KeysFor(RoleNames.Admin));
        Assert.Contains($"{key}-remark", KeysFor(RoleNames.Comdt));
        Assert.Contains($"{key}-remark", KeysFor(RoleNames.Mco));
        Assert.DoesNotContain($"{key}-remark", KeysFor(RoleNames.Admin));

        // The ITO keeps IT projects only; everyone can view.
        var ito = KeysFor(RoleNames.Ito);
        Assert.Contains($"{key}-view", ito);
        Assert.DoesNotContain($"{key}-manage", ito);
        Assert.DoesNotContain($"{key}-remark", ito);
        Assert.Contains("it-projects-manage", ito);
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static async Task<int> SeedAsync(ItProjectTestDb db, string module, string title)
    {
        var (result, id) = await db.Commands().CreateAsync(ItProjectTestDb.Details(title), new ItProjectActor("hod-1", "hod-1", module));
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        return id;
    }

    private static Molti.DetailsModel MoltiPage(ItProjectTestDb db, ClaimsPrincipal user)
    {
        var context = ItProjectBoardPageTests.Context(user);
        return new Molti.DetailsModel(db.Reads(), db.Commands(), db.Remarks(), db.Attachments(), db.Clock)
        {
            PageContext = context,
            TempData = new TempDataDictionary(context.HttpContext, new NullTempData())
        };
    }

    private static Sre.DetailsModel SrePage(ItProjectTestDb db, ClaimsPrincipal user)
    {
        var context = ItProjectBoardPageTests.Context(user);
        return new Sre.DetailsModel(db.Reads(), db.Commands(), db.Remarks(), db.Attachments(), db.Clock)
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

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProjectManagement.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
