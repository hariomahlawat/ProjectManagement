using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Models.ItProjects;
using ProjectManagement.Pages.ItProjects;
using ProjectManagement.Services.ItProjects;
using Xunit;
using static ProjectManagement.Tests.ItProjects.ItProjectStageRulesTests;

namespace ProjectManagement.Tests.ItProjects;

public class ItProjectDetailsPageTests
{
    [Fact]
    public void Page_CarriesTheViewPolicy()
        => Assert.Equal(Policies.ItProjects.View, Assert.Single(typeof(DetailsModel).GetCustomAttributes<AuthorizeAttribute>(inherit: false)).Policy);

    [Fact]
    public async Task Get_LoadsTheProjectItsStepperAndTheInputs()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, rowVersion) = await db.SeedAsync();
        var save = await db.Commands().SaveStagesAsync(id, Input(S("BID", true, new DateOnly(2025, 9, 29)), S("SO", cost: 46.8m)), rowVersion, ItProjectTestDb.Actor("ito-1"));
        Assert.True(save.Result.Succeeded);

        var page = Page(db, ItProjectTestDb.User("viewer", RoleNames.ProjectOfficer));
        page.Open = DetailsModel.OpenStages;
        var result = await page.OnGetAsync(id, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.False(page.CanManage);
        Assert.Null(page.Open); // viewers get no drawer
        Assert.Equal(ItProjectStatusKind.InProgress, page.Status.Kind);
        Assert.Equal("TEC", page.Status.CurrentStageCode);
        Assert.Equal(9, page.StageRows.Count);
        Assert.Equal(new[] { "PPP", "AON", "BID" }, page.StageRows.Where(row => row.IsCompleted).Select(row => row.Code));
        Assert.True(page.StageRows.Single(row => row.Code == "TEC").IsCurrent);
        Assert.True(page.StageRows.Single(row => row.Code == "BM").IsSkipped);
        Assert.Equal(46.8m, page.StageRows.Single(row => row.Code == "SO").CostLakhs);
        Assert.Equal("Core switches, Admin block", page.Details.Title);
        Assert.Equal(9, page.Stages.Items.Count);
        Assert.False(page.Stages.Items.Single(item => item.StageCode == "BM").IsIncluded);
        Assert.True(page.Stages.Items.Single(item => item.StageCode == "AON").IsCompleted);
        Assert.Equal(new DateOnly(2026, 11, 8), page.Drop.DroppedOn);
        Assert.NotEmpty(page.RowVersionToken);
        Assert.Null(page.UpdatedByName); // the seeded actor has no user row, so the header shows no name
    }

    [Fact]
    public async Task Get_ForAnUnknownOrDeletedProjectIsNotFound()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, rowVersion) = await db.SeedAsync();
        Assert.True((await db.Commands().DeleteAsync(id, rowVersion, ItProjectTestDb.Actor())).Succeeded);

        var page = Page(db, ItProjectTestDb.User("u"));
        Assert.IsType<NotFoundResult>(await page.OnGetAsync(id, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await page.OnGetAsync(id + 50, CancellationToken.None));
    }

    [Fact]
    public async Task Stages_SavesTheChainAndTellsWhatElseWasCompleted()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();
        var page = Page(db, ItProjectTestDb.User("ito-1", RoleNames.Ito));
        await page.OnGetAsync(id, CancellationToken.None);

        page.Stages.Items.Single(item => item.StageCode == "TEC").IsCompleted = true;
        page.Stages.Items.Single(item => item.StageCode == "TEC").CompletedOn = new DateOnly(2025, 11, 10);
        page.Stages.Items.Single(item => item.StageCode == "PNC").IsIncluded = true;
        page.Stages.Items.Single(item => item.StageCode == "SO").CostLakhs = 46.8m;
        var result = await page.OnPostStagesAsync(id, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Null(redirect.PageName);
        Assert.Equal(id, redirect.RouteValues!["id"]);
        Assert.Equal("Stages saved. Completed: IT PPP / IPA, AoN, Bid, TEC.", page.TempData["ToastSuccess"]);

        await using var context = db.NewContext();
        var stages = await context.ItProjectStages.Where(stage => stage.ItProjectId == id).OrderBy(stage => stage.SortOrder).ToListAsync();
        Assert.Equal(new[] { "PPP", "AON", "BID", "TEC" }, stages.Where(stage => stage.IsCompleted).Select(stage => stage.StageCode));
        Assert.False(stages.Single(stage => stage.StageCode == "PNC").IsSkipped);
        Assert.True(stages.Single(stage => stage.StageCode == "BM").IsSkipped);
        Assert.Equal(46.8m, stages.Single(stage => stage.StageCode == "SO").CostLakhs);
    }

    [Fact]
    public async Task Stages_ReturnsTheDrawerWithErrorsWhenDatesGoBackwards()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();
        var page = Page(db, ItProjectTestDb.User("hod-1", RoleNames.HoD));
        await page.OnGetAsync(id, CancellationToken.None);

        page.Stages.Items.Single(item => item.StageCode == "PPP").CompletedOn = new DateOnly(2026, 6, 1);
        page.Stages.Items.Single(item => item.StageCode == "AON").CompletedOn = new DateOnly(2026, 5, 1);
        var result = await page.OnPostStagesAsync(id, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(DetailsModel.OpenStages, page.Open);
        var errors = page.ModelState[DetailsModel.OpenStages]!.Errors.Select(error => error.ErrorMessage).ToList();
        Assert.Contains(errors, error => error.Contains("AoN cannot be completed before IT PPP / IPA"));
        Assert.Equal(new DateOnly(2026, 5, 1), page.Stages.Items.Single(item => item.StageCode == "AON").CompletedOn); // the posted values stay in the drawer
        await using var context = db.NewContext();
        Assert.False(await context.ItProjectStages.AnyAsync(stage => stage.ItProjectId == id && stage.IsCompleted));
    }

    [Fact]
    public async Task Stages_ABindingErrorOnADateOrCostIsNotSavedAsEmpty()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, rowVersion) = await db.SeedAsync();
        var seeded = await db.Commands().SaveStagesAsync(id, Input(S("AON", true, new DateOnly(2026, 6, 19)), S("SO", cost: 46.8m)), rowVersion, ItProjectTestDb.Actor());
        Assert.True(seeded.Result.Succeeded);

        var page = Page(db, ItProjectTestDb.User("ito-1", RoleNames.Ito));
        await page.OnGetAsync(id, CancellationToken.None);
        // What model binding does when "31-02-2026" or "abc" is posted: the value stays null and an error is recorded.
        page.Stages.Items.Single(item => item.StageCode == "AON").CompletedOn = null;
        page.ModelState.AddModelError("Stages.Items[1].CompletedOn", "The value '31-02-2026' is not valid.");
        page.Stages.Items.Single(item => item.StageCode == "SO").CostLakhs = null;
        page.ModelState.AddModelError("Stages.Items[7].CostLakhs", "The value 'abc' is not valid.");

        var result = await page.OnPostStagesAsync(id, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(DetailsModel.OpenStages, page.Open);
        var errors = page.DrawerErrors(DetailsModel.OpenStages, "Stages");
        Assert.Contains("AoN: The value '31-02-2026' is not valid.", errors);
        Assert.Contains("SO: The value 'abc' is not valid.", errors);
        Assert.NotEmpty(page.Details.RowVersion); // the other drawers still carry their row versions
        await using var context = db.NewContext();
        Assert.Equal(new DateOnly(2026, 6, 19), (await context.ItProjectStages.SingleAsync(s => s.ItProjectId == id && s.StageCode == "AON")).CompletedOn);
        Assert.Equal(46.8m, (await context.ItProjectStages.SingleAsync(s => s.ItProjectId == id && s.StageCode == "SO")).CostLakhs);
        Assert.DoesNotContain(db.Audit.Entries.Skip(2), entry => entry.Action == "ItProjects.StagesChanged");
    }

    [Fact]
    public async Task Details_InvalidPostKeepsEveryOtherFormUsable()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();
        var page = Page(db, ItProjectTestDb.User("hod-1", RoleNames.HoD));
        await page.OnGetAsync(id, CancellationToken.None);
        // A real POST of the edit form carries only the Details fields.
        page.Stages = new DetailsModel.StagesInput();
        page.Drop = new DetailsModel.DropInput();
        page.RowVersion = null;
        page.Details.Title = "";
        page.ModelState.AddModelError("Details.Title", "Give the project a title.");

        var result = await page.OnPostDetailsAsync(id, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(DetailsModel.OpenEdit, page.Open);
        Assert.Equal("", page.Details.Title); // the typed value stays in the drawer
        Assert.Equal(page.RowVersionToken, page.RowVersion);
        Assert.Equal(page.RowVersionToken, page.Drop.RowVersion);
        Assert.Equal(page.RowVersionToken, page.Stages.RowVersion);
        Assert.Equal(9, page.Stages.Items.Count);
        Assert.Contains("Give the project a title.", page.DrawerErrors(DetailsModel.OpenEdit, "Details"));
    }

    [Fact]
    public async Task Preview_ReturnsTheNormalisedChainAndWhatElseASaveWouldDo()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, rowVersion) = await db.SeedAsync();
        Assert.True((await db.Commands().SaveStagesAsync(id, Input(S("AON", true)), rowVersion, ItProjectTestDb.Actor())).Result.Succeeded);
        var page = Page(db, ItProjectTestDb.User("ito-1", RoleNames.Ito));
        await page.OnGetAsync(id, CancellationToken.None);

        // The user ticks TEC: the server completes Bid as well and says so.
        page.Stages.Items.Single(item => item.StageCode == "TEC").IsCompleted = true;
        var ticked = Preview(await page.OnPostPreviewAsync(id, reopen: null, touched: "TEC", CancellationToken.None));
        Assert.True(ticked.valid);
        Assert.Equal(new[] { "PPP", "AON", "BID", "TEC" }, ticked.completedCodes);
        Assert.Equal(new[] { "BID" }, ticked.completed);
        Assert.Equal("Saving will also complete Bid.", ticked.summary);

        // The user then unticks AoN: Bid and TEC reopen too; AoN itself is left out of the sentence.
        var reopened = Preview(await page.OnPostPreviewAsync(id, reopen: "AON", touched: "TEC,AON", CancellationToken.None));
        Assert.Equal(new[] { "PPP" }, reopened.completedCodes);
        Assert.Empty(reopened.completed);
        Assert.Empty(reopened.reopened); // Bid and TEC were never saved as complete, so nothing saved is reopened
        Assert.Null(reopened.summary);

        // A date out of order is reported, not normalised away.
        page.Stages.Items.Single(item => item.StageCode == "PPP").CompletedOn = new DateOnly(2026, 6, 1);
        page.Stages.Items.Single(item => item.StageCode == "AON").CompletedOn = new DateOnly(2026, 5, 1);
        var invalid = Preview(await page.OnPostPreviewAsync(id, reopen: null, touched: "", CancellationToken.None));
        Assert.False(invalid.valid);
        Assert.Contains(invalid.errors, error => error.Contains("AoN cannot be completed before"));

        Assert.Empty(db.Audit.Entries.Where(entry => entry.Action == "ItProjects.StagesChanged").Skip(1)); // previews never write
    }

    [Fact]
    public async Task Preview_IsForbiddenForViewers()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();
        var page = Page(db, ItProjectTestDb.User("po-1", RoleNames.ProjectOfficer));
        await page.OnGetAsync(id, CancellationToken.None);

        Assert.IsType<ForbidResult>(await page.OnPostPreviewAsync(id, null, null, CancellationToken.None));
    }

    [Fact]
    public void CascadeSummary_JoinsLabelsAndIsNullWhenNothingElseChanges()
    {
        Assert.Null(ItProjectBoardPresentation.CascadeSummary(Array.Empty<string>(), Array.Empty<string>()));
        Assert.Equal("Saving will also complete Bid.", ItProjectBoardPresentation.CascadeSummary(new[] { "BID" }, Array.Empty<string>()));
        Assert.Equal(
            "Saving will also complete IT PPP / IPA, AoN, Bid, TEC, COB and SO.",
            ItProjectBoardPresentation.CascadeSummary(new[] { "PPP", "AON", "BID", "TEC", "COB", "SO" }, Array.Empty<string>()));
        Assert.Equal(
            "Saving will also complete Bid and also reopen SO and Delivery and ATP.",
            ItProjectBoardPresentation.CascadeSummary(new[] { "BID" }, new[] { "SO", "DLV" }));
    }

    [Fact]
    public async Task Stages_WithAStaleRowVersionRedirectsWithAnErrorToast()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, stale) = await db.SeedAsync();
        Assert.True((await db.Commands().SaveStagesAsync(id, Input(S("AON", true)), stale, ItProjectTestDb.Actor())).Result.Succeeded);

        var page = Page(db, ItProjectTestDb.User("ito-1", RoleNames.Ito));
        await page.OnGetAsync(id, CancellationToken.None);
        page.Stages.RowVersion = DetailsModel.EncodeRowVersion(stale);
        page.Stages.Items.Single(item => item.StageCode == "BID").IsCompleted = true;
        var result = await page.OnPostStagesAsync(id, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(ItProjectCommandService.ConcurrencyConflictMessage, page.TempData["ToastError"]);
    }

    [Fact]
    public async Task Details_UpdatesTheProjectAndRejectsAnEmptyTitle()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();
        var page = Page(db, ItProjectTestDb.User("admin-1", RoleNames.Admin));
        await page.OnGetAsync(id, CancellationToken.None);

        page.Details.Title = "Core switches (HA pair)";
        page.Details.FinancialYearStart = 2026;
        var ok = await page.OnPostDetailsAsync(id, CancellationToken.None);
        Assert.IsType<RedirectToPageResult>(ok);
        Assert.Equal("Project details saved.", page.TempData["ToastSuccess"]);
        await using var context = db.NewContext();
        var project = await context.ItProjects.SingleAsync(p => p.Id == id);
        Assert.Equal("Core switches (HA pair)", project.Title);
        Assert.Equal(2026, project.FinancialYearStart);

        var again = Page(db, ItProjectTestDb.User("admin-1", RoleNames.Admin));
        await again.OnGetAsync(id, CancellationToken.None);
        again.Details.Title = "";
        again.ModelState.AddModelError("Details.Title", "Give the project a title.");
        var bad = await again.OnPostDetailsAsync(id, CancellationToken.None);
        Assert.IsType<PageResult>(bad);
        Assert.Equal(DetailsModel.OpenEdit, again.Open);
        Assert.NotEmpty(again.Stages.Items); // the stage drawer still renders
    }

    [Fact]
    public async Task Drop_ReinstateAndDelete_RoundTrip()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();

        var dropPage = Page(db, ItProjectTestDb.User("ito-1", RoleNames.Ito));
        await dropPage.OnGetAsync(id, CancellationToken.None);
        dropPage.Drop.Reason = "Merged into the HQ rate contract";
        var dropped = await dropPage.OnPostDropAsync(id, CancellationToken.None);
        Assert.IsType<RedirectToPageResult>(dropped);
        Assert.Contains("dropped", (string)dropPage.TempData["ToastSuccess"]!);

        var view = Page(db, ItProjectTestDb.User("ito-1", RoleNames.Ito));
        await view.OnGetAsync(id, CancellationToken.None);
        Assert.True(view.IsDropped);
        Assert.Equal(ItProjectStatusKind.Dropped, view.Status.Kind);
        Assert.DoesNotContain(view.StageRows, row => row.IsCurrent); // a dropped project has no "now"

        var reinstate = Page(db, ItProjectTestDb.User("ito-1", RoleNames.Ito));
        await reinstate.OnGetAsync(id, CancellationToken.None);
        Assert.IsType<RedirectToPageResult>(await reinstate.OnPostReinstateAsync(id, CancellationToken.None));
        Assert.Equal("Project reinstated.", reinstate.TempData["ToastSuccess"]);

        var delete = Page(db, ItProjectTestDb.User("ito-1", RoleNames.Ito));
        await delete.OnGetAsync(id, CancellationToken.None);
        var deleted = Assert.IsType<RedirectToPageResult>(await delete.OnPostDeleteAsync(id, CancellationToken.None));
        Assert.Equal("./Index", deleted.PageName);
        Assert.Null(await db.Reads().GetAsync(id));
    }

    [Fact]
    public async Task Drop_WithoutAReasonKeepsTheDrawerOpen()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();
        var page = Page(db, ItProjectTestDb.User("ito-1", RoleNames.Ito));
        await page.OnGetAsync(id, CancellationToken.None);
        page.Drop.Reason = "";
        page.ModelState.AddModelError("Drop.Reason", "Give the reason the project was dropped.");

        var result = await page.OnPostDropAsync(id, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(DetailsModel.OpenDrop, page.Open);
        Assert.False(page.IsDropped);
    }

    [Fact]
    public async Task Writes_AreForbiddenForViewers()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();
        var page = Page(db, ItProjectTestDb.User("po-1", RoleNames.ProjectOfficer));
        await page.OnGetAsync(id, CancellationToken.None);

        Assert.IsType<ForbidResult>(await page.OnPostStagesAsync(id, CancellationToken.None));
        Assert.IsType<ForbidResult>(await page.OnPostDetailsAsync(id, CancellationToken.None));
        Assert.IsType<ForbidResult>(await page.OnPostDropAsync(id, CancellationToken.None));
        Assert.IsType<ForbidResult>(await page.OnPostReinstateAsync(id, CancellationToken.None));
        Assert.IsType<ForbidResult>(await page.OnPostDeleteAsync(id, CancellationToken.None));
        Assert.Empty(db.Audit.Entries.Where(entry => entry.Action != "ItProjects.Created"));
    }

    [Fact]
    public void RowVersion_RoundTripsThroughBase64AndTreatsGarbageAsEmpty()
    {
        var bytes = Guid.NewGuid().ToByteArray();
        Assert.Equal(bytes, DetailsModel.DecodeRowVersion(DetailsModel.EncodeRowVersion(bytes)));
        Assert.Empty(DetailsModel.DecodeRowVersion("not base64!"));
        Assert.Empty(DetailsModel.DecodeRowVersion(null));
    }

    // ---- helpers ---------------------------------------------------------------------------

    private sealed record PreviewView(bool valid, IReadOnlyList<string> errors, IReadOnlyList<string> completedCodes, IReadOnlyList<string> completed, IReadOnlyList<string> reopened, string? summary);

    /// <summary>Reads the anonymous JSON the Preview handler returns, the way the script does.</summary>
    private static PreviewView Preview(IActionResult result)
    {
        var json = Assert.IsType<JsonResult>(result);
        var element = System.Text.Json.JsonSerializer.SerializeToElement(json.Value);
        var valid = element.GetProperty("valid").GetBoolean();
        if (!valid)
        {
            return new PreviewView(false, Strings(element.GetProperty("errors")), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), null);
        }

        var completedCodes = element.GetProperty("stages").EnumerateArray()
            .Where(stage => stage.GetProperty("completed").GetBoolean())
            .Select(stage => stage.GetProperty("code").GetString()!)
            .ToList();
        var summary = element.GetProperty("summary");
        return new PreviewView(
            true,
            Array.Empty<string>(),
            completedCodes,
            Strings(element.GetProperty("completed")),
            Strings(element.GetProperty("reopened")),
            summary.ValueKind == System.Text.Json.JsonValueKind.Null ? null : summary.GetString());
    }

    private static IReadOnlyList<string> Strings(System.Text.Json.JsonElement array)
        => array.EnumerateArray().Select(item => item.GetString()!).ToList();

    private static DetailsModel Page(ItProjectTestDb db, ClaimsPrincipal user)
    {
        var httpContext = new DefaultHttpContext { User = user };
        var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor(), new ModelStateDictionary());
        var context = new PageContext(actionContext) { ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), actionContext.ModelState) };
        return new DetailsModel(db.Reads(), db.Commands(), db.Clock)
        {
            PageContext = context,
            TempData = new TempDataDictionary(httpContext, new NullTempDataProvider())
        };
    }

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
