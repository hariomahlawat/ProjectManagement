using System;
using System.Linq;
using System.Threading.Tasks;
using ProjectManagement.Models.ItProjects;
using ProjectManagement.Services.ItProjects;
using Xunit;
using static ProjectManagement.Tests.ItProjects.ItProjectStageRulesTests;

namespace ProjectManagement.Tests.ItProjects;

public class ItProjectReadServiceTests
{
    [Fact]
    public async Task Board_OrdersActiveByCurrentStageThenTitleThenCompletedThenDropped()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var atBidB = await SeedAtAsync(db, "Workstations", "AON", fy: 2026);
        var atBidA = await SeedAtAsync(db, "Firewall", "AON", fy: 2026);
        var atPpp = await SeedAtAsync(db, "LAN upgrade", null, fy: 2026);
        var atSo = await SeedAtAsync(db, "Core switches", "COB", fy: 2025);
        var completed = await SeedAtAsync(db, "Wi-Fi", "DLV", fy: 2025);
        var dropped = await SeedAtAsync(db, "Video conferencing", "PPP", fy: 2025);
        var drop = await db.Commands().DropAsync(dropped, new DateOnly(2026, 1, 1), "Merged", await db.RowVersionAsync(dropped), ItProjectTestDb.Actor());
        Assert.True(drop.Succeeded);

        var board = await db.Reads().GetBoardAsync(new ItProjectBoardQuery(ItProjectBoardScope.All));

        Assert.Equal(
            new[] { atPpp, atBidA, atBidB, atSo, completed, dropped },
            board.Rows.Select(row => row.Id));
        Assert.Equal(4, board.ActiveCount);
        Assert.Equal(1, board.CompletedCount);
        Assert.Equal(1, board.DroppedCount);
        Assert.Equal(new[] { 2026, 2025 }, board.FinancialYears);

        var counts = board.StageCounts.ToDictionary(count => count.StageCode, count => count.AtStage);
        Assert.Equal(1, counts["PPP"]);
        Assert.Equal(2, counts["BID"]);
        Assert.Equal(1, counts["SO"]);
        Assert.Equal(0, counts["AON"]); // the dropped project (at AoN) does not count as "at" a stage
        Assert.Equal(0, counts["DLV"]);

        var soRow = board.Rows.Single(row => row.Id == atSo);
        Assert.Equal(ItProjectStatusKind.InProgress, soRow.Status.Kind);
        Assert.Equal("SO", soRow.Stages.Single(cell => cell.IsCurrent).StageCode);
        Assert.Equal(9, soRow.Stages.Count);
        Assert.True(soRow.Stages.Single(cell => cell.StageCode == "BM").IsSkipped);

        var notStartedRow = board.Rows.Single(row => row.Id == atPpp);
        Assert.Equal(ItProjectStatusKind.NotStarted, notStartedRow.Status.Kind);
        Assert.Equal("PPP", notStartedRow.Stages.Single(cell => cell.IsCurrent).StageCode); // "now" sits at the first stage

        var droppedRow = board.Rows.Single(row => row.Id == dropped);
        Assert.Equal(ItProjectStatusKind.Dropped, droppedRow.Status.Kind);
        Assert.Equal("Merged", droppedRow.DroppedReason);
        Assert.DoesNotContain(droppedRow.Stages, cell => cell.IsCurrent);
    }

    [Fact]
    public async Task Board_ScopeFiltersRowsButCountsFollowYearAndSearch()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        await SeedAtAsync(db, "LAN upgrade, Technical block", null, fy: 2026);
        await SeedAtAsync(db, "Core switches", "COB", fy: 2025);
        await SeedAtAsync(db, "Wi-Fi", "DLV", fy: 2025);

        var completedOnly = await db.Reads().GetBoardAsync(new ItProjectBoardQuery(ItProjectBoardScope.Completed));
        Assert.Equal(new[] { "Wi-Fi" }, completedOnly.Rows.Select(row => row.Title));
        Assert.Equal(2, completedOnly.ActiveCount);

        var fy2025 = await db.Reads().GetBoardAsync(new ItProjectBoardQuery(ItProjectBoardScope.All, FinancialYearStart: 2025));
        Assert.Equal(new[] { "Core switches", "Wi-Fi" }, fy2025.Rows.Select(row => row.Title));
        Assert.Equal(1, fy2025.ActiveCount);
        Assert.Equal(1, fy2025.CompletedCount);

        var search = await db.Reads().GetBoardAsync(new ItProjectBoardQuery(ItProjectBoardScope.All, Search: "technical"));
        Assert.Equal(new[] { "LAN upgrade, Technical block" }, search.Rows.Select(row => row.Title));
        Assert.Equal(1, search.ActiveCount);
        Assert.Equal(0, search.CompletedCount);

        var byReference = await db.Reads().GetBoardAsync(new ItProjectBoardQuery(ItProjectBoardScope.All, Search: "ppp core"));
        Assert.Single(byReference.Rows);
    }

    [Fact]
    public async Task Get_ReturnsTheStagesInOrderAndNullForUnknownIds()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var id = await SeedAtAsync(db, "Core switches", "COB", fy: 2025);

        var project = await db.Reads().GetAsync(id);

        Assert.NotNull(project);
        Assert.Equal(ItProjectStages.Codes, project!.Stages.Select(stage => stage.StageCode));
        Assert.Null(await db.Reads().GetAsync(id + 100));
    }

    private static async Task<int> SeedAtAsync(ItProjectTestDb db, string title, string? highestCompletedStage, int fy)
    {
        var (id, rowVersion) = await db.SeedAsync(title, $"IT PPP {title}", fy);
        if (highestCompletedStage is not null)
        {
            var save = await db.Commands().SaveStagesAsync(id, Input(S(highestCompletedStage, true)), rowVersion, ItProjectTestDb.Actor());
            Assert.True(save.Result.Succeeded, string.Join("; ", save.Result.Errors));
        }

        return id;
    }
}
