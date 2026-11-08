using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using ProjectManagement.Configuration;
using ProjectManagement.Models.ItProjects;
using ProjectManagement.Pages.ItProjects;
using ProjectManagement.Services.ItProjects;
using Xunit;
using static ProjectManagement.Tests.ItProjects.ItProjectStageRulesTests;

namespace ProjectManagement.Tests.ItProjects;

public class ItProjectBoardPageTests
{
    [Fact]
    public void Pages_CarryTheModulePolicies()
    {
        Assert.Equal(Policies.ItProjects.View, PolicyOf(typeof(IndexModel)));
        Assert.Equal(Policies.ItProjects.Manage, PolicyOf(typeof(CreateModel)));
    }

    [Fact]
    public async Task Board_DefaultsToActiveAndFillsTilesFromEveryRow()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        await SeedAtAsync(db, "LAN upgrade", null, 2026);
        await SeedAtAsync(db, "Core switches", "COB", 2025);
        var completedId = await SeedAtAsync(db, "Wi-Fi", "DLV", 2025, deliveredOn: new DateOnly(2026, 9, 20));
        var droppedId = await SeedAtAsync(db, "Video conferencing", "PPP", 2025);
        var drop = await db.Commands().DropAsync(droppedId, new DateOnly(2026, 1, 1), "Merged", await db.RowVersionAsync(droppedId), ItProjectTestDb.Actor());
        Assert.True(drop.Succeeded);

        var page = Page(db, ItProjectTestDb.User("po-1", RoleNames.ProjectOfficer));
        var result = await page.OnGetAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(ItProjectBoardScope.Active, page.BoardScope);
        Assert.False(page.CanManage);
        Assert.Equal(new[] { "LAN upgrade", "Core switches" }, page.ScopedRows.Select(row => row.Title));
        var band = Assert.Single(page.Bands);
        Assert.Equal("active", band.Key);
        Assert.Equal(2, page.Board.ActiveCount);
        Assert.Equal(1, page.Board.CompletedCount);
        Assert.Equal(1, page.Board.DroppedCount);
        Assert.Equal(4, page.AllCount);
        Assert.Equal(2026, page.CurrentFinancialYearStart); // clock is 8 Nov 2026
        Assert.Equal(1, page.CompletedThisFinancialYear);   // delivered 20 Sep 2026 is in FY 2026-27
        Assert.Equal("PPP", page.BusiestStage!.StageCode);  // PPP and SO tie at 1; the earlier stage wins
        Assert.DoesNotContain(page.ScopedRows, row => row.Id == completedId);
    }

    [Fact]
    public async Task Board_ScopeAllDrawsThreeBandsAndGroupByFyDrawsOnePerYear()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        await SeedAtAsync(db, "LAN upgrade", null, 2026);
        await SeedAtAsync(db, "Wi-Fi", "DLV", 2025);
        var droppedId = await SeedAtAsync(db, "Video conferencing", "PPP", 2025);
        Assert.True((await db.Commands().DropAsync(droppedId, new DateOnly(2026, 1, 1), "Merged", await db.RowVersionAsync(droppedId), ItProjectTestDb.Actor())).Succeeded);

        var all = Page(db, ItProjectTestDb.User("ito-1", RoleNames.Ito));
        all.Scope = "all";
        await all.OnGetAsync(CancellationToken.None);
        Assert.True(all.CanManage);
        Assert.Equal(new[] { "active", "completed", "dropped" }, all.Bands.Select(band => band.Key));
        Assert.Equal(new[] { "Active", "Completed", "Dropped" }, all.Bands.Select(band => band.Title));

        var grouped = Page(db, ItProjectTestDb.User("ito-1", RoleNames.Ito));
        grouped.Scope = "ALL";
        grouped.Group = "fy";
        await grouped.OnGetAsync(CancellationToken.None);
        Assert.True(grouped.GroupByFinancialYear);
        Assert.Equal(new[] { "FY 2026-27", "FY 2025-26" }, grouped.Bands.Select(band => band.Title));
        Assert.Equal(new[] { 1, 2 }, grouped.Bands.Select(band => band.Count));
        Assert.Equal(new[] { "Wi-Fi", "Video conferencing" }, grouped.Bands[1].Rows.Select(row => row.Title)); // completed before dropped
    }

    [Fact]
    public async Task Board_FinancialYearAndSearchNarrowTheRowsAndTheTiles()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        await SeedAtAsync(db, "LAN upgrade, Technical block", null, 2026);
        await SeedAtAsync(db, "Core switches", "COB", 2025);

        var byYear = Page(db, ItProjectTestDb.User("u"));
        byYear.Fy = 2025;
        await byYear.OnGetAsync(CancellationToken.None);
        Assert.Equal(new[] { "Core switches" }, byYear.ScopedRows.Select(row => row.Title));
        Assert.Equal(1, byYear.AllCount);
        Assert.Equal(new[] { 2026, 2025 }, byYear.Board.FinancialYears); // the picker always lists every year

        var bySearch = Page(db, ItProjectTestDb.User("u"));
        bySearch.Q = "TECHNICAL";
        await bySearch.OnGetAsync(CancellationToken.None);
        Assert.Equal(new[] { "LAN upgrade, Technical block" }, bySearch.ScopedRows.Select(row => row.Title));

        var nothing = Page(db, ItProjectTestDb.User("u"));
        nothing.Scope = "dropped";
        await nothing.OnGetAsync(CancellationToken.None);
        Assert.Empty(nothing.ScopedRows);
        Assert.Empty(nothing.Bands);
        Assert.Equal(2, nothing.AllCount);
    }

    [Theory]
    [InlineData(1999)]
    [InlineData(10000)]
    public async Task Board_TreatsAnUnsupportedFinancialYearAsAllYears(int year)
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        await SeedAtAsync(db, "LAN upgrade", null, 2026);

        var page = Page(db, ItProjectTestDb.User("u"));
        page.Fy = year;
        var result = await page.OnGetAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Null(page.Fy);
        Assert.Single(page.ScopedRows);
    }

    [Theory]
    [InlineData(null, ItProjectBoardScope.Active)]
    [InlineData("", ItProjectBoardScope.Active)]
    [InlineData("nonsense", ItProjectBoardScope.Active)]
    [InlineData("Completed", ItProjectBoardScope.Completed)]
    [InlineData(" dropped ", ItProjectBoardScope.Dropped)]
    [InlineData("all", ItProjectBoardScope.All)]
    public void Scope_IsParsedLeniently(string? scope, ItProjectBoardScope expected)
        => Assert.Equal(expected, IndexModel.ParseScope(scope));

    [Fact]
    public void Presentation_FormatsCellsAndChips()
    {
        var done = new ItProjectBoardStageCell("TEC", 4, true, new DateOnly(2025, 11, 10), false, null, false);
        Assert.Equal("10 Nov 25", ItProjectBoardPresentation.CellText(done));
        Assert.Equal("TEC: done 10 Nov 25", ItProjectBoardPresentation.CellTitle(done));
        Assert.Equal("itp-cell--done", ItProjectBoardPresentation.CellClass(done));

        var doneNoDate = done with { CompletedOn = null };
        Assert.Equal("no date", ItProjectBoardPresentation.CellText(doneNoDate));
        Assert.Equal("TEC: done, date not recorded", ItProjectBoardPresentation.CellTitle(doneNoDate));

        var current = new ItProjectBoardStageCell("SO", 8, false, null, false, 46.8m, true);
        Assert.Equal("now", ItProjectBoardPresentation.CellText(current));
        Assert.Equal("₹ 46.80 L", ItProjectBoardPresentation.CellCost(current));
        Assert.Equal("SO: current stage, ₹ 46.80 L", ItProjectBoardPresentation.CellTitle(current));
        Assert.Equal("itp-cell--cur", ItProjectBoardPresentation.CellClass(current));

        // The cost stays on the cell once SO is complete (Codex review, PR 2).
        var doneWithCost = new ItProjectBoardStageCell("SO", 8, true, new DateOnly(2026, 2, 12), false, 12.4m, false);
        Assert.Equal("12 Feb 26", ItProjectBoardPresentation.CellText(doneWithCost));
        Assert.Equal("₹ 12.40 L", ItProjectBoardPresentation.CellCost(doneWithCost));

        var pending = new ItProjectBoardStageCell("DLV", 9, false, null, false, null, false);
        Assert.Null(ItProjectBoardPresentation.CellText(pending));
        Assert.Equal("Delivery and ATP: not yet", ItProjectBoardPresentation.CellTitle(pending));

        var skipped = new ItProjectBoardStageCell("BM", 6, false, null, true, null, false);
        Assert.Null(ItProjectBoardPresentation.CellText(skipped));
        Assert.Equal("Benchmarking: skipped", ItProjectBoardPresentation.CellTitle(skipped));
        Assert.Equal("itp-cell--skip", ItProjectBoardPresentation.CellClass(skipped));
        Assert.Null(ItProjectBoardPresentation.CellCost(pending));
        Assert.Null(ItProjectBoardPresentation.CellCost(skipped));

        Assert.Equal("FY 2026-27", ItProjectBoardPresentation.FinancialYear(2026));
        Assert.Equal("₹ 1,23,456.70 L", ItProjectBoardPresentation.Cost(123456.7m));
        Assert.Equal("At TEC", ItProjectBoardPresentation.StatusChip(new ItProjectStatus(ItProjectStatusKind.InProgress, "TEC")));
        Assert.Equal("1 project", ItProjectBoardPresentation.Projects(1));
        Assert.Equal("3 projects", ItProjectBoardPresentation.Projects(3));
    }

    [Fact]
    public async Task Create_RedirectsToTheBoardWithAToastAndRejectsAnEmptyForm()
    {
        await using var db = await ItProjectTestDb.CreateAsync();

        var page = CreatePage(db, ItProjectTestDb.User("ito-1", RoleNames.Ito));
        page.OnGet();
        Assert.Equal(2026, page.Input.FinancialYearStart);
        Assert.Equal("2027-28", page.FinancialYearOptions[0].Text);
        Assert.Equal("2026-27", page.FinancialYearOptions[1].Text);
        Assert.True(page.FinancialYearOptions[1].Selected);
        Assert.Equal(CreateModel.PastFinancialYearsOffered + 2, page.FinancialYearOptions.Count);

        page.Input = new CreateModel.InputModel
        {
            Title = "Core switches, Admin block",
            PppReferenceNumber = "IT PPP 09/2025",
            FinancialYearStart = 2025,
            Description = "Replacement of the core switches."
        };
        var result = await page.OnPostAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("./Index", redirect.PageName);
        Assert.Equal("active", redirect.RouteValues!["scope"]);
        Assert.Contains("created", (string)page.TempData["ToastSuccess"]!);
        await using var context = db.NewContext();
        var project = Assert.Single(context.ItProjects.ToList());
        Assert.Equal("ito-1", project.CreatedByUserId);
        Assert.Equal(2025, project.FinancialYearStart);

        var empty = CreatePage(db, ItProjectTestDb.User("ito-1", RoleNames.Ito));
        empty.ModelState.AddModelError("Input.Title", "Give the project a title.");
        var again = await empty.OnPostAsync(CancellationToken.None);
        Assert.IsType<PageResult>(again);
        Assert.NotEmpty(empty.FinancialYearOptions);
        Assert.Single(context.ItProjects.ToList());
    }

    [Fact]
    public void FinancialYearOptions_AlwaysIncludeTheChosenYearAndStayInRange()
    {
        var options = CreateModel.BuildFinancialYearOptions(2005);
        Assert.Contains(options, option => option.Value == "2005" && option.Selected);
        Assert.Equal(options.Select(option => int.Parse(option.Value)).OrderByDescending(year => year), options.Select(option => int.Parse(option.Value)));
        Assert.All(options, option => Assert.True(int.Parse(option.Value) >= 2000));

        var atTop = CreateModel.BuildFinancialYearOptions(9998);
        Assert.Equal("9998", atTop[0].Value); // nothing above the helper's maximum
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateModel.BuildFinancialYearOptions(1999));
    }

    [Theory]
    [InlineData(1999)]
    [InlineData(10000)]
    public async Task Create_RejectsAnOutOfRangeFinancialYearInsteadOfFailing(int year)
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var page = CreatePage(db, ItProjectTestDb.User("ito-1", RoleNames.Ito));
        page.Input = new CreateModel.InputModel
        {
            Title = "Core switches",
            PppReferenceNumber = "IT PPP 09/2025",
            FinancialYearStart = year,
            Description = "Out of range year."
        };

        var result = await page.OnPostAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.False(page.ModelState.IsValid);
        Assert.Contains("Choose the financial year.", page.ModelState["Input.FinancialYearStart"]!.Errors.Select(error => error.ErrorMessage));
        Assert.Contains(page.FinancialYearOptions, option => option.Selected && option.Value == "2026"); // falls back to the current FY
        await using var context = db.NewContext();
        Assert.Empty(context.ItProjects.ToList());
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static string? PolicyOf(Type pageModel)
        => Assert.Single(pageModel.GetCustomAttributes<AuthorizeAttribute>(inherit: false)).Policy;

    private static IndexModel Page(ItProjectTestDb db, System.Security.Claims.ClaimsPrincipal user)
        => new(db.Reads(), db.Clock) { PageContext = Context(user) };

    private static CreateModel CreatePage(ItProjectTestDb db, System.Security.Claims.ClaimsPrincipal user)
    {
        var context = Context(user);
        return new CreateModel(db.Commands(), db.Clock)
        {
            PageContext = context,
            TempData = new TempDataDictionary(context.HttpContext, new NullTempDataProvider())
        };
    }

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public System.Collections.Generic.IDictionary<string, object> LoadTempData(HttpContext context) => new System.Collections.Generic.Dictionary<string, object>();

        public void SaveTempData(HttpContext context, System.Collections.Generic.IDictionary<string, object> values)
        {
        }
    }

    private static PageContext Context(System.Security.Claims.ClaimsPrincipal user)
    {
        var httpContext = new DefaultHttpContext { User = user };
        var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor(), new ModelStateDictionary());
        return new PageContext(actionContext) { ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), actionContext.ModelState) };
    }

    private static async Task<int> SeedAtAsync(ItProjectTestDb db, string title, string? highestCompletedStage, int fy, DateOnly? deliveredOn = null)
    {
        var (id, rowVersion) = await db.SeedAsync(title, $"IT PPP {title}", fy);
        if (highestCompletedStage is not null)
        {
            var save = await db.Commands().SaveStagesAsync(id, Input(S(highestCompletedStage, true, highestCompletedStage == "DLV" ? deliveredOn : null)), rowVersion, ItProjectTestDb.Actor());
            Assert.True(save.Result.Succeeded, string.Join("; ", save.Result.Errors));
        }

        return id;
    }
}
