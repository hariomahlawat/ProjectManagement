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
        var result = await page.OnPostStagesAsync(id, null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Null(redirect.PageName);
        Assert.Equal(id, redirect.RouteValues!["id"]);
        Assert.Equal("Stages saved. Completed: IT ARPPP / IPA, AoN, Bid, TEC.", page.TempData["ToastSuccess"]);

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
        var result = await page.OnPostStagesAsync(id, null, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(DetailsModel.OpenStages, page.Open);
        var errors = page.ModelState[DetailsModel.OpenStages]!.Errors.Select(error => error.ErrorMessage).ToList();
        Assert.Contains(errors, error => error.Contains("AoN cannot be completed before IT ARPPP / IPA"));
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

        var result = await page.OnPostStagesAsync(id, null, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(DetailsModel.OpenStages, page.Open);
        var errors = page.DrawerErrors(DetailsModel.OpenStages, "Stages");
        Assert.Contains("AoN: The value '31-02-2026' is not valid.", errors);
        Assert.Contains("EAS & SO: The value 'abc' is not valid.", errors);
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
    public async Task Preview_ReportsABindingErrorInsteadOfNormalisingItAway()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, rowVersion) = await db.SeedAsync();
        Assert.True((await db.Commands().SaveStagesAsync(id, Input(S("AON", true, new DateOnly(2026, 6, 19))), rowVersion, ItProjectTestDb.Actor())).Result.Succeeded);
        var page = Page(db, ItProjectTestDb.User("ito-1", RoleNames.Ito));
        await page.OnGetAsync(id, CancellationToken.None);
        page.Stages.Items.Single(item => item.StageCode == "AON").CompletedOn = null;
        page.ModelState.AddModelError("Stages.Items[1].CompletedOn", "The value '10000-01-01' is not valid.");

        var result = Preview(await page.OnPostPreviewAsync(id, null, "AON", CancellationToken.None));

        Assert.False(result.valid);
        Assert.Contains("AoN: The value '10000-01-01' is not valid.", result.errors);
    }

    [Fact]
    public async Task Stages_HonoursAReopenGestureThatArrivesWithTheSave()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, rowVersion) = await db.SeedAsync();
        Assert.True((await db.Commands().SaveStagesAsync(id, Input(S("SO", true, new DateOnly(2026, 3, 4), cost: 46.8m)), rowVersion, ItProjectTestDb.Actor())).Result.Succeeded);
        var page = Page(db, ItProjectTestDb.User("ito-1", RoleNames.Ito));
        await page.OnGetAsync(id, CancellationToken.None);

        // The user unticked COB and pressed Save before the preview answered: the form still says
        // SO is complete, but the reopen field carries the gesture.
        page.Stages.Items.Single(item => item.StageCode == "COB").IsCompleted = false;
        var result = await page.OnPostStagesAsync(id, reopen: "COB", CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Stages saved. Reopened: COB, EAS & SO.", page.TempData["ToastSuccess"]);
        await using var context = db.NewContext();
        var stages = await context.ItProjectStages.Where(stage => stage.ItProjectId == id).OrderBy(stage => stage.SortOrder).ToListAsync();
        Assert.Equal(new[] { "PPP", "AON", "BID", "TEC" }, stages.Where(stage => stage.IsCompleted).Select(stage => stage.StageCode));
        Assert.Null(stages.Single(stage => stage.StageCode == "SO").CompletedOn);
        Assert.Equal(46.8m, stages.Single(stage => stage.StageCode == "SO").CostLakhs);
    }

    [Fact]
    public async Task Stages_AFailedSaveKeepsThePendingReopenForTheNextAttempt()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, rowVersion) = await db.SeedAsync();
        Assert.True((await db.Commands().SaveStagesAsync(id, Input(S("SO", true, new DateOnly(2026, 3, 4))), rowVersion, ItProjectTestDb.Actor())).Result.Succeeded);
        var page = Page(db, ItProjectTestDb.User("ito-1", RoleNames.Ito));
        await page.OnGetAsync(id, CancellationToken.None);
        Assert.Null(page.ReopenPending);

        // Untick COB (reopen pending) but post a negative SO cost: validation fails.
        page.Stages.Items.Single(item => item.StageCode == "COB").IsCompleted = false;
        page.Stages.Items.Single(item => item.StageCode == "SO").CostLakhs = -1m;
        var result = await page.OnPostStagesAsync(id, reopen: "COB", CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(DetailsModel.OpenStages, page.Open);
        Assert.Equal("COB", page.ReopenPending); // the hidden field carries it into the next attempt

        // The user corrects the cost and saves again with the kept reopen: COB and SO reopen.
        page.Stages.Items.Single(item => item.StageCode == "SO").CostLakhs = 46.8m;
        var again = await page.OnPostStagesAsync(id, reopen: page.ReopenPending, CancellationToken.None);
        Assert.IsType<RedirectToPageResult>(again);
        await using var context = db.NewContext();
        Assert.Equal(new[] { "PPP", "AON", "BID", "TEC" },
            (await context.ItProjectStages.Where(stage => stage.ItProjectId == id && stage.IsCompleted).OrderBy(stage => stage.SortOrder).Select(stage => stage.StageCode).ToListAsync()));
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
            "Saving will also complete IT ARPPP / IPA, AoN, Bid, TEC, COB and EAS & SO.",
            ItProjectBoardPresentation.CascadeSummary(new[] { "PPP", "AON", "BID", "TEC", "COB", "SO" }, Array.Empty<string>()));
        Assert.Equal(
            "Saving will also complete Bid and also reopen EAS & SO and Delivery and ATP.",
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
        var result = await page.OnPostStagesAsync(id, null, CancellationToken.None);

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

    [Theory]
    [InlineData(RoleNames.Ito, true, false)]
    [InlineData(RoleNames.Mco, true, false)]
    [InlineData(RoleNames.HoD, true, false)]
    [InlineData(RoleNames.Comdt, true, false)]
    [InlineData(RoleNames.Admin, false, true)]
    [InlineData(RoleNames.ProjectOfficer, false, false)]
    public async Task Remarks_ComposerAndModerationFollowTheRoles(string role, bool canRemark, bool isAdmin)
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();
        var page = Page(db, ItProjectTestDb.User("u", role));

        await page.OnGetAsync(id, CancellationToken.None);

        Assert.Equal(canRemark, page.CanRemark);
        Assert.Equal(isAdmin, page.IsAdmin);
        Assert.Equal(canRemark ? role : null, page.RemarkRole);
        Assert.Equal(new DateOnly(2026, 11, 8), page.Remark.EventDate);
    }

    [Fact]
    public async Task Remarks_AddEditAndDeleteThroughThePage()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();

        var ito = Page(db, ItProjectTestDb.User("ito-1", RoleNames.Ito));
        await ito.OnGetAsync(id, CancellationToken.None);
        ito.Remark = new DetailsModel.RemarkInput { Body = "SO draft with Accounts.", EventDate = new DateOnly(2026, 11, 2) };
        var added = Assert.IsType<RedirectToPageResult>(await ito.OnPostRemarkAddAsync(id, CancellationToken.None));
        Assert.Equal("remarks", added.Fragment);
        Assert.Equal("Remark added.", ito.TempData["ToastSuccess"]);

        var view = Page(db, ItProjectTestDb.User("po-1", RoleNames.ProjectOfficer));
        await view.OnGetAsync(id, CancellationToken.None);
        var remark = Assert.Single(view.Remarks);
        Assert.Equal(RoleNames.Ito, remark.AuthorRole);
        Assert.False(view.CanEditRemark(remark));
        Assert.False(view.CanDeleteRemark(remark));
        Assert.IsType<ForbidResult>(await view.OnPostRemarkAddAsync(id, CancellationToken.None));

        var hod = Page(db, ItProjectTestDb.User("hod-1", RoleNames.HoD));
        await hod.OnGetAsync(id, CancellationToken.None);
        Assert.False(hod.CanEditRemark(remark));
        Assert.True(hod.CanDeleteRemark(remark)); // the HoD may delete any remark (owner, 11 Nov 2026)

        var comdt = Page(db, ItProjectTestDb.User("comdt-1", RoleNames.Comdt));
        await comdt.OnGetAsync(id, CancellationToken.None);
        Assert.False(comdt.CanDeleteRemark(remark)); // ...the Comdt only their own
        hod.RemarkEdit = new DetailsModel.RemarkEditInput { Id = remark.Id, Body = "Hijacked.", EventDate = remark.EventDate, RowVersion = DetailsModel.EncodeRowVersion(remark.RowVersion) };
        Assert.IsType<RedirectToPageResult>(await hod.OnPostRemarkEditAsync(id, CancellationToken.None));
        Assert.Equal(ItProjectRemarkService.NotAuthorMessage, hod.TempData["ToastError"]);

        var author = Page(db, ItProjectTestDb.User("ito-1", RoleNames.Ito));
        await author.OnGetAsync(id, CancellationToken.None);
        Assert.True(author.CanEditRemark(remark));
        author.RemarkEdit = new DetailsModel.RemarkEditInput { Id = remark.Id, Body = "SO issued on 12 Nov.", EventDate = new DateOnly(2026, 11, 12), RowVersion = DetailsModel.EncodeRowVersion(remark.RowVersion) };
        Assert.IsType<RedirectToPageResult>(await author.OnPostRemarkEditAsync(id, CancellationToken.None));
        Assert.Equal("Remark saved.", author.TempData["ToastSuccess"]);

        var admin = Page(db, ItProjectTestDb.User("admin-1", RoleNames.Admin));
        await admin.OnGetAsync(id, CancellationToken.None);
        var edited = Assert.Single(admin.Remarks);
        Assert.Equal("SO issued on 12 Nov.", edited.Body);
        Assert.False(admin.CanEditRemark(edited));
        Assert.True(admin.CanDeleteRemark(edited));
        Assert.IsType<RedirectToPageResult>(await admin.OnPostRemarkDeleteAsync(id, edited.Id, DetailsModel.EncodeRowVersion(edited.RowVersion), CancellationToken.None));
        Assert.Equal("Remark deleted.", admin.TempData["ToastSuccess"]);
        Assert.Empty(await db.Remarks().ListAsync(id));
    }

    [Fact]
    public async Task Remarks_AnEmptyRemarkStaysInTheComposerWithTheError()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();
        var page = Page(db, ItProjectTestDb.User("mco-1", RoleNames.Mco));
        await page.OnGetAsync(id, CancellationToken.None);
        page.Remark = new DetailsModel.RemarkInput { Body = "", EventDate = new DateOnly(2026, 11, 8) };
        page.ModelState.AddModelError("Remark.Body", "Write the remark.");

        var result = await page.OnPostRemarkAddAsync(id, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(DetailsModel.OpenRemark, page.Open);
        Assert.Contains("Write the remark.", page.DrawerErrors(DetailsModel.OpenRemark, "Remark"));
        Assert.Equal(page.RowVersionToken, page.Stages.RowVersion); // the rest of the page is still usable
        Assert.Empty(await db.Remarks().ListAsync(id));
    }

    [Fact]
    public async Task Writes_AreForbiddenForViewers()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();
        var page = Page(db, ItProjectTestDb.User("po-1", RoleNames.ProjectOfficer));
        await page.OnGetAsync(id, CancellationToken.None);

        Assert.IsType<ForbidResult>(await page.OnPostStagesAsync(id, null, CancellationToken.None));
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

    // ---- attachments -------------------------------------------------------------------------

    [Fact]
    public async Task Attachments_ManageUsersUploadAndRemove_EveryoneDownloads()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();

        var hod = Page(db, ItProjectTestDb.User("hod-1", RoleNames.HoD));
        hod.AttachmentFile = ItProjectAttachmentServiceTests.FormFile("IT PPP 09-2025.pdf", ItProjectAttachmentServiceTests.PdfType, ItProjectAttachmentServiceTests.PdfBytes);
        var uploaded = Assert.IsType<RedirectToPageResult>(await hod.OnPostAttachmentUploadAsync(id, CancellationToken.None));
        Assert.Equal(DetailsModel.AttachmentsFragment, uploaded.Fragment);
        Assert.Equal(id, uploaded.RouteValues?["id"]);
        Assert.Equal("\"IT PPP 09-2025.pdf\" attached.", hod.TempData["ToastSuccess"]);

        var viewer = Page(db, ItProjectTestDb.User("viewer", RoleNames.ProjectOfficer));
        Assert.IsType<PageResult>(await viewer.OnGetAsync(id, CancellationToken.None));
        var file = Assert.Single(viewer.Attachments);
        Assert.Equal("IT PPP 09-2025.pdf", file.FileName);
        Assert.Equal("hod-1", file.UploadedByName); // no user row in the test db, so the id shows

        var download = Assert.IsType<FileStreamResult>(await viewer.OnGetAttachmentAsync(id, file.Id, CancellationToken.None));
        Assert.Equal(ItProjectAttachmentServiceTests.PdfType, download.ContentType);
        Assert.Equal("IT PPP 09-2025.pdf", download.FileDownloadName);
        await download.FileStream.DisposeAsync();

        var removed = Assert.IsType<RedirectToPageResult>(await hod.OnPostAttachmentDeleteAsync(id, file.Id, CancellationToken.None));
        Assert.Equal(DetailsModel.AttachmentsFragment, removed.Fragment);
        Assert.Equal("File removed.", hod.TempData["ToastSuccess"]);
        Assert.Empty(await db.Attachments().ListAsync(id));
        Assert.IsType<NotFoundResult>(await viewer.OnGetAttachmentAsync(id, file.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Attachments_UploadAndRemoveAreForbiddenForViewers()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();
        var viewer = Page(db, ItProjectTestDb.User("viewer", RoleNames.ProjectOfficer));
        viewer.AttachmentFile = ItProjectAttachmentServiceTests.FormFile("ppp.pdf", ItProjectAttachmentServiceTests.PdfType, ItProjectAttachmentServiceTests.PdfBytes);

        Assert.IsType<ForbidResult>(await viewer.OnPostAttachmentUploadAsync(id, CancellationToken.None));
        Assert.IsType<ForbidResult>(await viewer.OnPostAttachmentDeleteAsync(id, Guid.NewGuid(), CancellationToken.None));
        Assert.Empty(await db.Attachments().ListAsync(id));
    }

    [Fact]
    public async Task Attachments_ARejectedUploadShowsTheReasonAndStoresNothing()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();
        var ito = Page(db, ItProjectTestDb.User("ito-1", RoleNames.Ito));
        ito.AttachmentFile = ItProjectAttachmentServiceTests.FormFile("fake.pdf", ItProjectAttachmentServiceTests.PdfType, System.Text.Encoding.UTF8.GetBytes("not a pdf"));

        Assert.IsType<RedirectToPageResult>(await ito.OnPostAttachmentUploadAsync(id, CancellationToken.None));

        Assert.Equal(ItProjectAttachmentValidator.ContentMismatchMessage, ito.TempData["ToastError"]);
        Assert.Empty(db.Storage.Saved);

        ito.AttachmentFile = null;
        Assert.IsType<RedirectToPageResult>(await ito.OnPostAttachmentUploadAsync(id, CancellationToken.None));
        Assert.Equal("Choose a file to attach.", ito.TempData["ToastError"]);
    }

    [Fact]
    public async Task Attachments_DownloadIsScopedToTheRouteProject()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();
        var (otherId, _) = await db.SeedAsync("Other", "IT PPP 10/2025");
        var (_, attachmentId) = await db.Attachments().UploadAsync(id, ItProjectAttachmentServiceTests.FormFile("ppp.pdf", ItProjectAttachmentServiceTests.PdfType, ItProjectAttachmentServiceTests.PdfBytes), ItProjectTestDb.Actor());
        var viewer = Page(db, ItProjectTestDb.User("viewer", RoleNames.ProjectOfficer));

        Assert.IsType<NotFoundResult>(await viewer.OnGetAttachmentAsync(otherId, attachmentId, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await viewer.OnGetAttachmentAsync(9999, attachmentId, CancellationToken.None));
        Assert.IsType<FileStreamResult>(await viewer.OnGetAttachmentAsync(id, attachmentId, CancellationToken.None));
    }

    [Fact]
    public async Task Year_ViewersCannotChangeIt()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync(fy: 2025);
        var page = Page(db, ItProjectTestDb.User("po-1", RoleNames.ProjectOfficer));
        await page.OnGetAsync(id, CancellationToken.None);

        Assert.IsType<ForbidResult>(await page.OnPostYearAsync(id, 2026, "CL", "IT PPP 03/2026", CancellationToken.None));
    }

    [Fact]
    public async Task Year_ManageSetsNumberAndTypeAndThePageShowsEveryLiveYear()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync(fy: 2025);
        var page = Page(db, ItProjectTestDb.User("hod-1", RoleNames.HoD));
        await page.OnGetAsync(id, CancellationToken.None);
        Assert.Equal(new[] { "New", "CF" }, page.YearTypes.Select(type => type.Code));
        // Until this year's number is entered the page goes by the first one, as the register does.
        Assert.Equal(page.Project.PppReferenceNumber, page.DisplayReference);

        Assert.IsType<RedirectToPageResult>(await page.OnPostYearAsync(id, 2026, "CL", "IT PPP 03/2026", CancellationToken.None));

        var reloaded = Page(db, ItProjectTestDb.User("ito-1", RoleNames.Ito));
        await reloaded.OnGetAsync(id, CancellationToken.None);
        Assert.Equal("IT PPP 03/2026", reloaded.DisplayReference);
        var year = reloaded.YearTypes.Single(type => type.FinancialYearStart == 2026);
        Assert.Equal("CL", year.Code);
        Assert.True(year.IsManual);
        Assert.Equal("IT PPP 03/2026", year.PppReferenceNumber);

        Assert.IsType<RedirectToPageResult>(await reloaded.OnPostYearAsync(id, 2026, "auto", "IT PPP 03/2026", CancellationToken.None));
        var reset = Page(db, ItProjectTestDb.User("ito-1", RoleNames.Ito));
        await reset.OnGetAsync(id, CancellationToken.None);
        Assert.False(reset.YearTypes.Single(type => type.FinancialYearStart == 2026).IsManual);
        Assert.Equal("IT PPP 03/2026", reset.YearTypes.Single(type => type.FinancialYearStart == 2026).PppReferenceNumber);
    }

    [Fact]
    public async Task Year_TheFirstYearsNumberIsChangedFromItsRow()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync(fy: 2025);
        var page = Page(db, ItProjectTestDb.User("hod-1", RoleNames.HoD));
        await page.OnGetAsync(id, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(await page.OnPostYearAsync(id, 2025, "auto", "IT PPP 11/2025", CancellationToken.None));

        var reloaded = Page(db, ItProjectTestDb.User("hod-1", RoleNames.HoD));
        await reloaded.OnGetAsync(id, CancellationToken.None);
        Assert.Equal("IT PPP 11/2025", reloaded.Project.PppReferenceNumber);
        var first = reloaded.YearTypes.Single(type => type.IsOwnYear);
        Assert.Equal("IT PPP 11/2025", first.PppReferenceNumber);
        Assert.Equal("New", first.Code);
        Assert.False(first.IsManual);
    }

    [Fact]
    public async Task Year_AFirstYearTypeSetByHandCanBePutBackToAutomatic()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, rowVersion) = await db.SeedAsync(fy: 2026);
        var set = await db.Commands().SaveYearAsync(id, 2026, new ItProjectYearInput(ItProjectYearType.CarriedForward, null), rowVersion, ItProjectTestDb.Actor());
        Assert.True(set.Succeeded);

        var page = Page(db, ItProjectTestDb.User("hod-1", RoleNames.HoD));
        await page.OnGetAsync(id, CancellationToken.None);
        Assert.True(page.YearTypes.Single().IsManual);

        Assert.IsType<RedirectToPageResult>(await page.OnPostYearAsync(id, 2026, "auto", null, CancellationToken.None));
        var reloaded = Page(db, ItProjectTestDb.User("hod-1", RoleNames.HoD));
        await reloaded.OnGetAsync(id, CancellationToken.None);
        Assert.Equal("New", reloaded.YearTypes.Single().Code);
        Assert.False(reloaded.YearTypes.Single().IsManual);
    }

    [Fact]
    public async Task DroppedProject_StageSaveAndPreviewAreRefusedWithAMessage()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, rowVersion) = await db.SeedAsync();
        Assert.True((await db.Commands().DropAsync(id, new DateOnly(2026, 11, 1), "Merged", rowVersion, ItProjectTestDb.Actor())).Succeeded);

        var page = Page(db, ItProjectTestDb.User("ito-1", RoleNames.Ito));
        await page.OnGetAsync(id, CancellationToken.None);
        Assert.True(page.IsDropped);
        page.Stages.Items.Single(item => item.StageCode == "DLV").IsCompleted = true;

        var save = await page.OnPostStagesAsync(id, null, CancellationToken.None);
        Assert.IsType<RedirectToPageResult>(save);
        Assert.Equal(ItProjectCommandService.DroppedStagesMessage, page.TempData["ToastError"]);

        var preview = Assert.IsType<JsonResult>(await page.OnPostPreviewAsync(id, null, null, CancellationToken.None));
        Assert.Contains("\"valid\":false", System.Text.Json.JsonSerializer.Serialize(preview.Value));

        await using var context = db.NewContext();
        Assert.DoesNotContain(await context.ItProjectStages.Where(stage => stage.ItProjectId == id).ToListAsync(), stage => stage.IsCompleted);
    }

    [Fact]
    public async Task RecordDates_AreIndianCalendarDays()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();
        await using (var context = db.NewContext())
        {
            var project = await context.ItProjects.SingleAsync(p => p.Id == id);
            // 20:00 UTC on 7 Nov is 01:30 IST on 8 Nov.
            project.CreatedAtUtc = new DateTime(2026, 11, 7, 20, 0, 0, DateTimeKind.Utc);
            project.UpdatedAtUtc = new DateTime(2026, 11, 7, 18, 0, 0, DateTimeKind.Utc);
            await context.SaveChangesAsync();
        }

        var page = Page(db, ItProjectTestDb.User("viewer"));
        await page.OnGetAsync(id, CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 11, 8), page.AddedOn);
        Assert.Equal(new DateOnly(2026, 11, 7), page.LastChangedOn);
        Assert.Equal("2026-11-08", page.DropDateMax);
    }

    internal static DetailsModel PageFor(ItProjectTestDb db, ClaimsPrincipal user) => Page(db, user);

    private static DetailsModel Page(ItProjectTestDb db, ClaimsPrincipal user)
    {
        var httpContext = new DefaultHttpContext { User = user };
        var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor(), new ModelStateDictionary());
        var context = new PageContext(actionContext) { ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), actionContext.ModelState) };
        return new DetailsModel(db.Reads(), db.Commands(), db.Remarks(), db.Attachments(), db.Clock)
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
