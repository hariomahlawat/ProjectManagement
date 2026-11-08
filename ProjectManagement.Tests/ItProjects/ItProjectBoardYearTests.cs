using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Models.ItProjects;
using ProjectManagement.Pages.ItProjects;
using ProjectManagement.Services.ItProjects;
using Xunit;
using static ProjectManagement.Tests.ItProjects.ItProjectStageRulesTests;

namespace ProjectManagement.Tests.ItProjects;

// Redesign PR 3 (docs/ux/it-projects-redesign.md): a financial year on the board shows every case
// that counts in it, New ones and those carried into it as CL or CF, with that year's IT PPP number.
public class ItProjectBoardYearTests
{
    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private sealed record Seeded(int Lan, int Core, int Ups, int Fibre);

    /// <summary>Today is 8 Nov 2026 (FY 2026-27).</summary>
    private static async Task<Seeded> SeedAsync(ItProjectTestDb db)
    {
        var lan = await SeedWithAsync(db, "LAN upgrade", 2026, null);
        var core = await SeedWithAsync(db, "Core switches", 2025, Input(S("TEC", true, D(2025, 11, 18))));
        var ups = await SeedWithAsync(db, "Server room UPS", 2025, Input(S("COB", true, D(2025, 11, 4)), S("SO", true, D(2026, 1, 8), cost: 62.00m)));
        var fibre = await SeedWithAsync(db, "Fibre ring", 2024, Input(S("SO", true, D(2025, 2, 24), cost: 34.90m), S("DLV", true, D(2025, 6, 30))));
        return new Seeded(lan, core, ups, fibre);
    }

    [Fact]
    public async Task AYear_ShowsItsNewCasesAndTheOnesCarriedIntoIt()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var seeded = await SeedAsync(db);

        var board = await db.Reads().GetBoardAsync(new ItProjectBoardQuery(ItProjectBoardScope.All, FinancialYearStart: 2026));

        Assert.Equal(new[] { seeded.Lan, seeded.Core, seeded.Ups }.OrderBy(id => id), board.Rows.Select(row => row.Id).OrderBy(id => id));
        Assert.Equal("New", board.Rows.Single(row => row.Id == seeded.Lan).YearType!.Code);
        Assert.Equal("CF", board.Rows.Single(row => row.Id == seeded.Core).YearType!.Code);
        Assert.Equal("CL", board.Rows.Single(row => row.Id == seeded.Ups).YearType!.Code);

        var figures = board.Figures;
        Assert.Equal(2026, figures.FinancialYearStart);
        Assert.Equal((1, 1, 1), (figures.ActiveNew, figures.ActiveCommittedLiability, figures.ActiveCarriedForward));
        Assert.Equal(1, figures.NotStartedCount);
        Assert.Equal(0, figures.DeliveredInFinancialYear);
        Assert.Null(figures.LastDeliveredOn); // Fibre ring was delivered before FY 2026-27, so it is not on this board
    }

    [Fact]
    public async Task AnEarlierYear_CountsItsDeliveriesAndItsTypes()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var seeded = await SeedAsync(db);

        var board = await db.Reads().GetBoardAsync(new ItProjectBoardQuery(ItProjectBoardScope.All, FinancialYearStart: 2025));

        Assert.DoesNotContain(board.Rows, row => row.Id == seeded.Lan);
        Assert.Equal("CL", board.Rows.Single(row => row.Id == seeded.Fibre).YearType!.Code); // SO in Feb 2025, delivered in Jun 2025
        Assert.Equal("New", board.Rows.Single(row => row.Id == seeded.Core).YearType!.Code);
        Assert.Equal(1, board.Figures.DeliveredInFinancialYear);
        Assert.Equal(D(2025, 6, 30), board.Figures.LastDeliveredOn);
    }

    [Fact]
    public async Task AllYears_ShowsEveryCaseWithItsTypeThisYear()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var seeded = await SeedAsync(db);

        var board = await db.Reads().GetBoardAsync(new ItProjectBoardQuery(ItProjectBoardScope.All));

        Assert.Equal(4, board.Rows.Count);
        Assert.Null(board.Rows.Single(row => row.Id == seeded.Fibre).YearType); // delivered before FY 2026-27
        Assert.Equal("CF", board.Rows.Single(row => row.Id == seeded.Core).YearType!.Code);
        Assert.Equal(new[] { 2026, 2025, 2024 }, board.FinancialYears);
        Assert.Equal(2026, board.Figures.FinancialYearStart);
    }

    [Fact]
    public async Task TheYearsNumberIsShownWhenEntered()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var seeded = await SeedAsync(db);
        var saved = await db.Commands().SaveYearAsync(seeded.Core, 2026, new ItProjectYearInput(null, "IT PPP 07/2026"), await db.RowVersionAsync(seeded.Core), ItProjectTestDb.Actor());
        Assert.True(saved.Succeeded);

        var thisYear = await db.Reads().GetBoardAsync(new ItProjectBoardQuery(ItProjectBoardScope.All, FinancialYearStart: 2026));
        var lastYear = await db.Reads().GetBoardAsync(new ItProjectBoardQuery(ItProjectBoardScope.All, FinancialYearStart: 2025));

        Assert.Equal("IT PPP 07/2026", thisYear.Rows.Single(row => row.Id == seeded.Core).DisplayReference);
        Assert.Equal("IT PPP Core switches", lastYear.Rows.Single(row => row.Id == seeded.Core).DisplayReference);
        Assert.Equal("IT PPP Server room UPS", thisYear.Rows.Single(row => row.Id == seeded.Ups).DisplayReference); // not entered: the first number
    }

    [Fact]
    public async Task Page_ShowsTheNewestYearsAsButtonsAndOlderOnesInAList()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        foreach (var year in new[] { 2026, 2025, 2024, 2023, 2022 })
        {
            await SeedWithAsync(db, $"Case {year}", year, null);
        }

        var page = new IndexModel(db.Reads()) { PageContext = ItProjectBoardPageTests.Context(ItProjectTestDb.User("u")) };
        await page.OnGetAsync(CancellationToken.None);

        Assert.Equal(new[] { 2026, 2025, 2024, 2023 }, page.RecentYears);
        Assert.Equal(new[] { 2022 }, page.EarlierYears);
    }

    [Fact]
    public void Notes_ReadPlainly()
    {
        var figures = new ItProjectBoardFigures(null, 2, 2, 0m, 0, 0, D(2025, 6, 30), 2026, 2026, 3, 2, 3, 1);

        Assert.Equal("1 not started yet · 2 missing a stage date", ItProjectBoardPresentation.InProgressNote(figures));
        Assert.Equal(" · 2 cases without stage dates left out", ItProjectBoardPresentation.LeftOutNote(2));
        Assert.Equal("none yet in FY 2026-27 · last on 30 Jun 2025", ItProjectBoardPresentation.DeliveredNote(figures));
        Assert.Equal("₹1,48,400.00", ItProjectBoardPresentation.Lakhs(148400m)); // Indian grouping
    }

    private static async Task<int> SeedWithAsync(ItProjectTestDb db, string title, int fy, System.Collections.Generic.List<ItProjectStageInput>? stages)
    {
        var (id, rowVersion) = await db.SeedAsync(title, $"IT PPP {title}", fy);
        if (stages is not null)
        {
            var save = await db.Commands().SaveStagesAsync(id, stages, rowVersion, ItProjectTestDb.Actor());
            Assert.True(save.Result.Succeeded, string.Join("; ", save.Result.Errors));
        }

        return id;
    }
}
