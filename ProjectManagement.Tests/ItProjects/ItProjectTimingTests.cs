using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Models.ItProjects;
using ProjectManagement.Services.ItProjects;
using Xunit;
using static ProjectManagement.Tests.ItProjects.ItProjectStageRulesTests;

namespace ProjectManagement.Tests.ItProjects;

// The time facts of the redesign (docs/ux/it-projects-redesign.md, "Derived figures"): stage
// dates first, the recorded move as a light fallback, an earlier stage's date never borrowed.
public class ItProjectTimingTests
{
    private static readonly DateOnly Today = new(2026, 11, 8);
    private static readonly TimeZoneInfo Ist = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private static DateOnly ToIst(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Ist));

    /// <summary>A project with the named stages complete (date or null) and the named optional stages included.</summary>
    private static ItProject Project(params (string Code, DateOnly? Date)[] completed) => Project(Array.Empty<string>(), completed);

    private static ItProject Project(string[] include, params (string Code, DateOnly? Date)[] completed)
    {
        var project = new ItProject { CreatedAtUtc = new DateTime(2025, 4, 1, 4, 0, 0, DateTimeKind.Utc), Stages = ItProjectStages.NewStageRows() };
        foreach (var stage in project.Stages)
        {
            if (include.Contains(stage.StageCode))
            {
                stage.IsSkipped = false;
            }

            var match = completed.FirstOrDefault(item => item.Code == stage.StageCode);
            if (match.Code is not null)
            {
                stage.IsCompleted = true;
                stage.CompletedOn = match.Date;
            }
        }

        return project;
    }

    private static ItProjectTiming Resolve(ItProject project) => ItProjectTiming.Resolve(project, Today, ToIst);

    [Theory]
    [InlineData(0, ItProjectAgeBand.Normal)]
    [InlineData(30, ItProjectAgeBand.Normal)]
    [InlineData(31, ItProjectAgeBand.Amber)]
    [InlineData(60, ItProjectAgeBand.Amber)]
    [InlineData(61, ItProjectAgeBand.Red)]
    [InlineData(355, ItProjectAgeBand.Red)]
    public void BandOf_FollowsTheOwnerThresholds(int days, ItProjectAgeBand expected)
        => Assert.Equal(expected, ItProjectTiming.BandOf(days));

    [Fact]
    public void PreviousStageDated_CountsFromItsDateWithAgeing()
    {
        var timing = Resolve(Project(("PPP", D(2025, 5, 12)), ("AON", D(2025, 7, 3)), ("BID", D(2025, 9, 29)), ("TEC", D(2025, 11, 18))));

        Assert.Equal(ItProjectTimeBasis.StageDate, timing.Basis);
        Assert.Equal(D(2025, 11, 18), timing.Since);
        Assert.Equal(355, timing.DaysAtStage);
        Assert.Equal(ItProjectAgeBand.Red, timing.Band);
        Assert.Equal(D(2025, 5, 12), timing.StartedOn);
        Assert.Equal(Today.DayNumber - D(2025, 5, 12).DayNumber, timing.ElapsedDays);
        Assert.False(timing.HasMissingDates);
    }

    [Fact]
    public void PreviousStageUndated_UsesTheRecordedMoveWithoutAgeing()
    {
        var project = Project(("PPP", D(2026, 4, 22)), ("AON", D(2026, 6, 5)), ("BID", null));
        project.StageMovedAtUtc = new DateTime(2026, 8, 20, 20, 0, 0, DateTimeKind.Utc); // 21 Aug, 01:30 IST

        var timing = Resolve(project);

        Assert.Equal(ItProjectTimeBasis.RecordedMove, timing.Basis);
        Assert.Equal(D(2026, 8, 21), timing.Since);
        Assert.Equal(79, timing.DaysAtStage);
        Assert.Equal(ItProjectAgeBand.None, timing.Band);
        Assert.True(timing.HasMissingDates);
    }

    [Fact]
    public void PreviousStageUndated_NeverBorrowsAnEarlierStageDate()
    {
        var timing = Resolve(Project(("PPP", D(2025, 6, 11)), ("AON", D(2025, 7, 30)), ("BID", null)));

        Assert.Equal(ItProjectTimeBasis.NotRecorded, timing.Basis);
        Assert.Null(timing.Since);
        Assert.Null(timing.DaysAtStage);
        Assert.Equal(ItProjectAgeBand.None, timing.Band);
    }

    [Fact]
    public void SkippedStagesAreIgnored_ThePreviousCountedStageDecides()
    {
        // Benchmarking and PNC are skipped, so SO follows COB.
        var timing = Resolve(Project(("PPP", D(2026, 1, 5)), ("AON", D(2026, 2, 5)), ("BID", D(2026, 3, 5)), ("TEC", D(2026, 4, 5)), ("COB", D(2026, 10, 1))));

        Assert.Equal(ItProjectTimeBasis.StageDate, timing.Basis);
        Assert.Equal(D(2026, 10, 1), timing.Since);
        Assert.Equal(38, timing.DaysAtStage);
        Assert.Equal(ItProjectAgeBand.Amber, timing.Band);
    }

    [Fact]
    public void IncludedOptionalStageWithoutDate_DoesNotFallBackToTheStageBeforeIt()
    {
        var timing = Resolve(Project(new[] { "BM" },
            ("PPP", D(2026, 1, 5)), ("AON", D(2026, 2, 5)), ("BID", D(2026, 3, 5)), ("TEC", D(2026, 4, 5)), ("COB", D(2026, 10, 1)), ("BM", null)));

        Assert.Equal(ItProjectTimeBasis.NotRecorded, timing.Basis);
        Assert.Null(timing.DaysAtStage);
    }

    [Fact]
    public void NotStarted_CountsFromTheDayTheCaseWasAdded()
    {
        var project = Project();
        project.CreatedAtUtc = new DateTime(2026, 11, 2, 20, 0, 0, DateTimeKind.Utc); // 3 Nov IST

        var timing = Resolve(project);

        Assert.Equal(ItProjectTimeBasis.SinceAdded, timing.Basis);
        Assert.Equal(D(2026, 11, 3), timing.Since);
        Assert.Equal(5, timing.DaysAtStage);
        Assert.Equal(ItProjectAgeBand.None, timing.Band);
        Assert.Null(timing.StartedOn);
        Assert.Null(timing.ElapsedDays);
    }

    [Fact]
    public void Delivered_IsClosedWithEndToEndDays()
    {
        var timing = Resolve(Project(
            ("PPP", D(2024, 7, 9)), ("AON", D(2024, 8, 21)), ("BID", D(2024, 11, 6)), ("TEC", D(2024, 12, 17)),
            ("COB", D(2025, 1, 15)), ("SO", D(2025, 2, 24)), ("DLV", D(2025, 6, 30))));

        Assert.Equal(ItProjectTimeBasis.Closed, timing.Basis);
        Assert.Null(timing.DaysAtStage);
        Assert.Equal(ItProjectAgeBand.None, timing.Band);
        Assert.Equal(356, timing.ElapsedDays);
    }

    [Fact]
    public void Dropped_IsClosedAndElapsedStopsAtTheDroppedDate()
    {
        var project = Project(("PPP", D(2025, 6, 11)), ("AON", D(2025, 7, 30)), ("BID", D(2025, 10, 15)));
        project.DroppedOn = D(2026, 1, 19);

        var timing = Resolve(project);

        Assert.Equal(ItProjectTimeBasis.Closed, timing.Basis);
        Assert.Equal(D(2026, 1, 19).DayNumber - D(2025, 6, 11).DayNumber, timing.ElapsedDays);
    }

    [Fact]
    public void Gaps_AreBlankWhereEitherDateIsMissing()
    {
        var timing = Resolve(Project(("PPP", D(2025, 4, 10)), ("AON", D(2025, 5, 22)), ("BID", null), ("TEC", D(2025, 9, 15))));

        var gaps = timing.Gaps.ToDictionary(gap => gap.StageCode, gap => gap.Days);
        Assert.Equal(7, gaps.Count); // the two skipped stages have no gap
        Assert.Null(gaps["PPP"]);
        Assert.Equal(42, gaps["AON"]);
        Assert.Null(gaps["BID"]);
        Assert.Null(gaps["TEC"]); // not measured from AoN: that would add Bid's time to TEC
        Assert.Null(gaps["COB"]); // not complete yet
        Assert.True(timing.HasMissingDates);
    }

    // ---- through the services ----------------------------------------------------------------

    [Fact]
    public async Task StageSave_RecordsTheMoveOnlyWhenTheCurrentStageChanges()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, rowVersion) = await db.SeedAsync();
        Assert.Null(await MovedAtAsync(db, id));

        var move = await db.Commands().SaveStagesAsync(id, Input(S("AON", true)), rowVersion, ItProjectTestDb.Actor());
        Assert.True(move.Result.Succeeded);
        Assert.Equal(ItProjectTestDb.Now.UtcDateTime, await MovedAtAsync(db, id));

        // A later save that only adds dates keeps the case at Bid: no movement.
        db.Clock.Set(ItProjectTestDb.Now.AddDays(3));
        var dates = await db.Commands().SaveStagesAsync(id, Input(S("PPP", true, D(2026, 9, 1)), S("AON", true, D(2026, 10, 1))), await db.RowVersionAsync(id), ItProjectTestDb.Actor());
        Assert.True(dates.Result.Succeeded);
        Assert.Equal(ItProjectTestDb.Now.UtcDateTime, await MovedAtAsync(db, id));

        // Reopening moves it back: that is movement.
        var reopen = await db.Commands().SaveStagesAsync(id, Input(S("PPP", true, D(2026, 9, 1))), await db.RowVersionAsync(id), ItProjectTestDb.Actor());
        Assert.True(reopen.Result.Succeeded);
        Assert.Equal(ItProjectTestDb.Now.AddDays(3).UtcDateTime, await MovedAtAsync(db, id));
    }

    [Fact]
    public async Task Board_FiguresUseStageDatesAndCountTheCasesLeftOut()
    {
        await using var db = await ItProjectTestDb.CreateAsync();

        var stuck = await SeedWithAsync(db, "Core switches", Input(
            S("PPP", true, D(2025, 5, 12)), S("AON", true, D(2025, 7, 3)), S("BID", true, D(2025, 9, 29)), S("TEC", true, D(2025, 11, 18))));
        var recent = await SeedWithAsync(db, "Secure printing", Input(
            S("PPP", true, D(2026, 4, 9)), S("AON", true, D(2026, 10, 20))));
        var onOrder = await SeedWithAsync(db, "Server room UPS", Input(
            S("PPP", true, D(2025, 5, 2)), S("COB", true, D(2025, 11, 4)), S("SO", true, D(2026, 1, 8), cost: 62.00m)));
        var undated = await SeedWithAsync(db, "Laptops", Input(S("BID", true)));
        var deliveredThisYear = await SeedWithAsync(db, "Firewall", Input(S("SO", true, D(2026, 5, 1), cost: 10m), S("DLV", true, D(2026, 7, 1))));
        var deliveredEarlier = await SeedWithAsync(db, "Fibre ring", Input(S("DLV", true, D(2025, 6, 30))));
        await db.SeedAsync("Endpoint licences", "IT PPP 12/2026", 2026);

        var board = await db.Reads().GetBoardAsync(new ItProjectBoardQuery(ItProjectBoardScope.All));
        var figures = board.Figures;

        Assert.NotNull(figures.LongestWait);
        Assert.Equal(stuck, figures.LongestWait!.Id);
        Assert.Equal("COB", figures.LongestWait.StageCode);
        Assert.Equal(355, figures.LongestWait.Days);

        // "Laptops" moved to TEC with no Bid date: shown as a recorded move, left out of the longest wait.
        var laptops = board.Rows.Single(row => row.Id == undated).Timing;
        Assert.Equal(ItProjectTimeBasis.RecordedMove, laptops.Basis);
        Assert.Equal(1, figures.ActiveWithoutStageDate);

        // Dates are missing on Laptops (PPP to Bid) and on UPS (AoN to TEC were auto-completed).
        Assert.Equal(2, figures.DatesMissingCount);

        Assert.Equal(62.00m, figures.OnOrderLakhs);
        Assert.Equal(1, figures.OnOrderCount);
        Assert.Equal(0, figures.OnOrderWithoutValue);

        Assert.Equal(1, figures.DeliveredInFinancialYear);
        Assert.Equal(D(2026, 7, 1), figures.LastDeliveredOn);

        var recentRow = board.Rows.Single(row => row.Id == recent).Timing;
        Assert.Equal(19, recentRow.DaysAtStage);
        Assert.Equal(ItProjectAgeBand.Normal, recentRow.Band);

        Assert.Equal(ItProjectTimeBasis.Closed, board.Rows.Single(row => row.Id == deliveredEarlier).Timing.Basis);
        Assert.Equal(ItProjectAgeBand.Red, board.Rows.Single(row => row.Id == onOrder).Timing.Band);
        Assert.Equal(ItProjectTimeBasis.Closed, board.Rows.Single(row => row.Id == deliveredThisYear).Timing.Basis);
    }

    [Fact]
    public async Task Board_FinancialYearFollowsIstAtTheYearBoundary()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        await SeedWithAsync(db, "Firewall", Input(S("DLV", true, D(2026, 4, 1))));

        // 31 Mar 2026, 20:00 UTC is 1 Apr 2026, 01:30 IST: FY 2026-27 has begun in India.
        db.Clock.Set(new DateTimeOffset(2026, 3, 31, 20, 0, 0, TimeSpan.Zero));
        var figures = (await db.Reads().GetBoardAsync(new ItProjectBoardQuery(ItProjectBoardScope.All))).Figures;

        Assert.Equal(2026, figures.CurrentFinancialYearStart);
        Assert.Equal(1, figures.DeliveredInFinancialYear);
    }

    [Fact]
    public async Task GetTiming_MatchesTheBoardRow()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var id = await SeedWithAsync(db, "Core switches", Input(S("PPP", true, D(2026, 10, 1))));

        var reads = db.Reads();
        var project = await reads.GetAsync(id);
        var board = await reads.GetBoardAsync(new ItProjectBoardQuery(ItProjectBoardScope.All));

        Assert.Equal(board.Rows.Single(row => row.Id == id).Timing.DaysAtStage, reads.GetTiming(project!).DaysAtStage);
        Assert.Equal(38, reads.GetTiming(project!).DaysAtStage);
        Assert.Equal(ItProjectAgeBand.Amber, reads.GetTiming(project!).Band);
    }

    private static async Task<int> SeedWithAsync(ItProjectTestDb db, string title, System.Collections.Generic.List<ItProjectStageInput> stages)
    {
        var (id, rowVersion) = await db.SeedAsync(title, $"IT PPP {title}", 2026);
        var save = await db.Commands().SaveStagesAsync(id, stages, rowVersion, ItProjectTestDb.Actor());
        Assert.True(save.Result.Succeeded, string.Join("; ", save.Result.Errors));
        return id;
    }

    private static async Task<DateTime?> MovedAtAsync(ItProjectTestDb db, int id)
    {
        await using var context = db.NewContext();
        return await context.ItProjects.Where(project => project.Id == id).Select(project => project.StageMovedAtUtc).SingleAsync();
    }

    [Fact]
    public void Elapsed_FromTheFirstStage_IsEndToEnd()
    {
        var timing = Resolve(Project(
            ("PPP", D(2024, 7, 10)), ("AON", null), ("BID", null), ("TEC", null), ("COB", null), ("SO", null), ("DLV", D(2025, 6, 30))));

        Assert.Equal("PPP", timing.StartedStageCode);
        Assert.True(timing.ElapsedIsEndToEnd);
        Assert.Equal("end to end", ProjectManagement.Pages.ItProjects.ItProjectBoardPresentation.ElapsedSpan(timing));
    }

    [Fact]
    public void Elapsed_WhenTheEarlyStagesHaveNoDate_SaysWhichStageItRunsFrom()
    {
        // Only SO and Delivery are dated: 132 days is SO to delivery, not the whole case.
        var timing = Resolve(Project(
            ("PPP", null), ("AON", null), ("BID", null), ("TEC", null), ("COB", null), ("SO", D(2026, 4, 10)), ("DLV", D(2026, 8, 20))));

        Assert.Equal(132, timing.ElapsedDays);
        Assert.Equal("SO", timing.StartedStageCode);
        Assert.False(timing.ElapsedIsEndToEnd);
        Assert.Equal("from EAS & SO", ProjectManagement.Pages.ItProjects.ItProjectBoardPresentation.ElapsedSpan(timing));
    }

    [Theory]
    [InlineData(60, ItProjectAgeBand.Normal)]
    [InlineData(61, ItProjectAgeBand.Amber)]
    [InlineData(90, ItProjectAgeBand.Amber)]
    [InlineData(91, ItProjectAgeBand.Red)]
    public void BandOf_AtDelivery_Uses60And90Days(int days, ItProjectAgeBand expected)
    {
        Assert.Equal(expected, ItProjectTiming.BandOf(days, ItProjectStages.Delivery));
        Assert.Equal(days <= 30 ? ItProjectAgeBand.Normal : days <= 60 ? ItProjectAgeBand.Amber : ItProjectAgeBand.Red, ItProjectTiming.BandOf(days, ItProjectStages.Bid));
    }

    [Fact]
    public void Resolve_AtDelivery_BandsByTheDeliveryLimits()
    {
        // SO closed 75 days before the test day: amber at Delivery, where at any other stage it would be red.
        var timing = Resolve(Project(("PPP", D(2026, 1, 5)), ("AON", D(2026, 2, 1)), ("BID", D(2026, 3, 1)), ("TEC", D(2026, 4, 1)), ("COB", D(2026, 5, 1)), ("SO", Today.AddDays(-75))));

        Assert.Equal(75, timing.DaysAtStage);
        Assert.Equal(ItProjectAgeBand.Amber, timing.Band);
    }
}
