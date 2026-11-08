using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ProjectManagement.Models.ItProjects;
using ProjectManagement.Pages.ItProjects;
using ProjectManagement.Services.ItProjects;
using Xunit;
using static ProjectManagement.Tests.ItProjects.ItProjectStageRulesTests;

namespace ProjectManagement.Tests.ItProjects;

// Redesign PR 4: "Where the cases are now" groups the live cases under their current stage.
public class ItProjectPipelineTests
{
    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    [Fact]
    public async Task Pipeline_PutsLiveCasesUnderTheirStage_LongestStageDateWaitFirst()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var notStarted = await SeedAsync(db, "Endpoint licences", null);
        var recent = await SeedAsync(db, "Laptops", Input(S("BID", true, D(2026, 10, 20))));         // 19 d at TEC
        var stuck = await SeedAsync(db, "Core switches", Input(S("BID", true, D(2026, 6, 1))));      // 160 d at TEC
        var undated = await SeedAsync(db, "Printers", Input(S("BID", true)));                         // recorded move only
        var onOrder = await SeedAsync(db, "UPS", Input(S("SO", true, D(2026, 1, 8), cost: 62.00m)));
        var delivered = await SeedAsync(db, "Fibre ring", Input(S("DLV", true, D(2026, 6, 30))));
        var dropped = await SeedAsync(db, "RFID kiosks", Input(S("AON", true, D(2026, 5, 1))));
        Assert.True((await db.Commands().DropAsync(dropped, D(2026, 9, 1), "Merged", await db.RowVersionAsync(dropped), ItProjectTestDb.Actor())).Succeeded);

        var board = await db.Reads().GetBoardAsync(new ItProjectBoardQuery(ItProjectBoardScope.All));
        var columns = ItProjectBoardPresentation.Pipeline(board.Rows).ToDictionary(column => column.StageCode);

        Assert.Equal(9, columns.Count);
        Assert.Equal(new[] { notStarted }, columns["PPP"].Shown.Select(item => item.Id));
        Assert.Equal(new[] { stuck, recent, undated }, columns["TEC"].Shown.Select(item => item.Id));
        Assert.Equal(new[] { "late", "calm", "nodate" }, columns["TEC"].Shown.Select(item => item.Tone));
        Assert.Equal(new[] { onOrder }, columns["DLV"].Shown.Select(item => item.Id));
        Assert.Equal("₹ 62.00 L", columns["DLV"].Shown[0].SoCost);
        Assert.Equal(0, columns["AON"].Count); // the dropped case is not live
        Assert.DoesNotContain(columns.Values.SelectMany(column => column.Shown), item => item.Id == delivered || item.Id == dropped);
        Assert.True(columns["BM"].IsOptional);
        Assert.Equal("Delivery & ATP", columns["DLV"].ShortLabel);
    }

    [Fact]
    public async Task Pipeline_ShowsFourCasesAStageAndCountsTheRest()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        for (var i = 1; i <= 6; i++)
        {
            await SeedAsync(db, $"Case {i}", Input(S("PPP", true, D(2026, 10, i))));
        }

        var board = await db.Reads().GetBoardAsync(new ItProjectBoardQuery(ItProjectBoardScope.All));
        var aon = ItProjectBoardPresentation.Pipeline(board.Rows).Single(column => column.StageCode == "AON");

        Assert.Equal(6, aon.Count);
        Assert.Equal(ItProjectBoardPresentation.PipelineCasesShown, aon.Shown.Count);
        Assert.Equal(2, aon.Hidden);
        Assert.Equal(new[] { "Case 1", "Case 2", "Case 3", "Case 4" }, aon.Shown.Select(item => item.Title)); // longest wait first
    }

    [Fact]
    public void TimeText_FollowsTheTimeRule()
    {
        var gaps = Array.Empty<ItProjectStageGap>();

        Assert.Equal(("355 d", "late"), Pick(new ItProjectTiming(ItProjectTimeBasis.StageDate, D(2025, 11, 18), 355, ItProjectAgeBand.Red, gaps, null, null, false)));
        Assert.Equal(("33 d", "warn"), Pick(new ItProjectTiming(ItProjectTimeBasis.StageDate, D(2026, 10, 6), 33, ItProjectAgeBand.Amber, gaps, null, null, false)));
        Assert.Equal(("recorded 21 Aug", "nodate"), Pick(new ItProjectTiming(ItProjectTimeBasis.RecordedMove, D(2026, 8, 21), 79, ItProjectAgeBand.None, gaps, null, null, true)));
        Assert.Equal(("date not recorded", "nodate"), Pick(new ItProjectTiming(ItProjectTimeBasis.NotRecorded, null, null, ItProjectAgeBand.None, gaps, null, null, true)));
        Assert.Equal(("added 5 d ago", "calm"), Pick(new ItProjectTiming(ItProjectTimeBasis.SinceAdded, D(2026, 11, 3), 5, ItProjectAgeBand.None, gaps, null, null, false)));
        Assert.Equal(("added today", "calm"), Pick(new ItProjectTiming(ItProjectTimeBasis.SinceAdded, D(2026, 11, 8), 0, ItProjectAgeBand.None, gaps, null, null, false)));

        static (string, string) Pick(ItProjectTiming timing)
        {
            var (text, _, tone) = ItProjectBoardPresentation.TimeText(timing);
            return (text, tone);
        }
    }

    [Fact]
    public void ProjectPage_SaysDeliveredNotCompleted()
    {
        var delivered = new ItProjectStatus(ItProjectStatusKind.Completed, null);

        Assert.Equal("Delivered", ItProjectBoardPresentation.StatusLine(delivered));
        Assert.Equal("Delivered", ItProjectBoardPresentation.StatusChip(delivered));
        Assert.Equal("In progress · COB", ItProjectBoardPresentation.StatusLine(new ItProjectStatus(ItProjectStatusKind.InProgress, "COB")));
    }

    private static async Task<int> SeedAsync(ItProjectTestDb db, string title, List<ItProjectStageInput>? stages)
    {
        var (id, rowVersion) = await db.SeedAsync(title, $"IT PPP {title}", 2026);
        if (stages is not null)
        {
            var save = await db.Commands().SaveStagesAsync(id, stages, rowVersion, ItProjectTestDb.Actor());
            Assert.True(save.Result.Succeeded, string.Join("; ", save.Result.Errors));
        }

        return id;
    }
}
