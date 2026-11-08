using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Configuration;
using ProjectManagement.Models.ItProjects;
using ProjectManagement.Pages.ItProjects;
using ProjectManagement.Services.ItProjects;
using Xunit;
using static ProjectManagement.Tests.ItProjects.ItProjectStageRulesTests;

namespace ProjectManagement.Tests.ItProjects;

// Redesign PR 5: the register's track rows, Now column, sort and "Dates missing" filter.
public class ItProjectRegisterTests
{
    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    [Fact]
    public async Task LongestWait_SortsLiveCasesByStageDateDaysThenTheUndated()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var recent = await SeedAsync(db, "A recent", Input(S("BID", true, D(2026, 10, 20))));
        var stuck = await SeedAsync(db, "B stuck", Input(S("PPP", true, D(2025, 12, 1))));
        var undated = await SeedAsync(db, "C undated", Input(S("TEC", true)));
        var notStarted = await SeedAsync(db, "D not started", null);
        var delivered = await SeedAsync(db, "E delivered", Input(S("DLV", true, D(2026, 6, 30))));

        var byStage = await db.Reads().GetBoardAsync(new ItProjectBoardQuery(ItProjectBoardScope.All));
        var byWait = await db.Reads().GetBoardAsync(new ItProjectBoardQuery(ItProjectBoardScope.All, Sort: ItProjectBoardSort.LongestWait));

        Assert.Equal(new[] { notStarted, stuck, recent, undated, delivered }, byStage.Rows.Select(row => row.Id));
        Assert.Equal(new[] { stuck, recent, notStarted, undated, delivered }, byWait.Rows.Select(row => row.Id));
    }

    [Fact]
    public async Task Track_DrawsTheTravelledLineAcrossSkippedStops()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var id = await SeedAsync(db, "UPS", Input(S("PPP", true, D(2025, 5, 2)), S("PNC", true, D(2025, 11, 27), skipped: false), S("SO", true, D(2026, 1, 8), cost: 62m)));

        var row = (await db.Reads().GetBoardAsync(new ItProjectBoardQuery(ItProjectBoardScope.All))).Rows.Single(item => item.Id == id);
        var stops = ItProjectBoardPresentation.Track(row).ToDictionary(stop => stop.StageCode);

        Assert.Equal("done", stops["PPP"].State);
        Assert.Equal("nodate", stops["AON"].State);   // auto-completed without a date
        Assert.Equal("no date", stops["AON"].Label);
        Assert.Equal("skip", stops["BM"].State);
        Assert.True(stops["BM"].LineIn);             // the line runs through a skipped stop
        Assert.True(stops["BM"].LineOut);
        Assert.Equal("cur", stops["DLV"].State);
        Assert.Equal("late", stops["DLV"].Tone);     // 304 days since SO on 8 Jan 2026 (test clock 8 Nov 2026)
        Assert.Equal("304 d", stops["DLV"].Label);
        Assert.False(stops["PPP"].LineIn);
        Assert.False(stops["DLV"].LineOut);
    }

    [Fact]
    public async Task Now_ReadsForEachKindOfCase()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var atDelivery = await SeedAsync(db, "UPS", Input(S("SO", true, D(2026, 1, 8), cost: 62m)));
        var delivered = await SeedAsync(db, "Fibre", Input(S("PPP", true, D(2024, 7, 9)), S("SO", true, D(2025, 2, 24), cost: 34.9m), S("DLV", true, D(2025, 6, 30))));
        var dropped = await SeedAsync(db, "RFID", Input(S("BID", true, D(2025, 10, 15))));
        Assert.True((await db.Commands().DropAsync(dropped, D(2026, 1, 19), "Merged", await db.RowVersionAsync(dropped), ItProjectTestDb.Actor())).Succeeded);
        var notStarted = await SeedAsync(db, "Licences", null);

        var rows = (await db.Reads().GetBoardAsync(new ItProjectBoardQuery(ItProjectBoardScope.All))).Rows.ToDictionary(row => row.Id);

        var deliveryNow = ItProjectBoardPresentation.Now(rows[atDelivery]);
        Assert.Equal("At Delivery and ATP", deliveryNow.Label);
        Assert.Equal("304 d", deliveryNow.Badge);
        Assert.Equal("late", deliveryNow.BadgeTone);
        Assert.Equal("SO ₹ 62.00 L", deliveryNow.Note); // kept on one line

        var deliveredNow = ItProjectBoardPresentation.Now(rows[delivered]);
        Assert.Equal("Delivered 30 Jun 25", deliveredNow.Label);
        Assert.Null(deliveredNow.Badge);
        Assert.Equal("356 days end to end · SO ₹ 34.90 L", deliveredNow.Note);

        var droppedNow = ItProjectBoardPresentation.Now(rows[dropped]);
        Assert.Equal("Dropped 19 Jan 26", droppedNow.Label);
        Assert.Equal("at TEC", droppedNow.Note);
        Assert.Contains(ItProjectBoardPresentation.Track(rows[dropped]), stop => stop.State == "stop" && stop.StageCode == "TEC");

        var notStartedNow = ItProjectBoardPresentation.Now(rows[notStarted]);
        Assert.Equal("Not started", notStartedNow.Label);
        Assert.Equal("added today", notStartedNow.Badge); // seeded on the test clock's day
    }

    [Fact]
    public async Task Page_DatesMissingShowsOnlyLiveCasesWithAnUndatedStage()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var undated = await SeedAsync(db, "Laptops", Input(S("BID", true)));
        await SeedAsync(db, "Core switches", Input(S("TEC", true, D(2025, 11, 18)), S("PPP", true, D(2025, 5, 12)), S("AON", true, D(2025, 7, 3)), S("BID", true, D(2025, 9, 29))));
        await SeedAsync(db, "Fibre", Input(S("DLV", true)));   // delivered without dates: not live, not listed

        var page = new IndexModel(db.Reads()) { PageContext = ItProjectBoardPageTests.Context(ItProjectTestDb.User("ito-1", RoleNames.Ito)), Dates = "missing", Sort = "WAIT" };
        await page.OnGetAsync(CancellationToken.None);

        Assert.True(page.OnlyMissingDates);
        Assert.Equal(ItProjectBoardSort.LongestWait, page.BoardSort);
        Assert.Equal("wait", page.SortKey);
        Assert.Equal(new[] { undated }, page.ScopedRows.Select(row => row.Id));
        Assert.Equal(1, page.Board.Figures.DatesMissingCount);
    }

    private static async Task<int> SeedAsync(ItProjectTestDb db, string title, List<ItProjectStageInput>? stages)
    {
        var (id, rowVersion) = await db.SeedAsync(title, $"IT PPP {title}", 2025);
        if (stages is not null)
        {
            var save = await db.Commands().SaveStagesAsync(id, stages, rowVersion, ItProjectTestDb.Actor());
            Assert.True(save.Result.Succeeded, string.Join("; ", save.Result.Errors));
        }

        return id;
    }
}
