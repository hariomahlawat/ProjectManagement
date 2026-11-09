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

// The project page header: one status line (owner, 9 Nov 2026: no metric cards) and the Journey's
// days between stages. Test clock: 8 Nov 2026, 15:00 IST.
public class ItProjectPageHeroTests
{
    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private static async Task<(ItProjectPagePresentation.HeroStatus Status, IReadOnlyList<ItProjectPagePresentation.JourneyStep> Journey)> ReadAsync(ItProjectTestDb db, int id)
    {
        var reads = db.Reads();
        var project = (await reads.GetAsync(id))!;
        var status = ItProjectStatus.Resolve(project);
        var timing = reads.GetTiming(project);
        return (ItProjectPagePresentation.Status(project, status, timing), ItProjectPagePresentation.Journey(project, status, timing));
    }

    [Fact]
    public async Task InProgress_SaysTheStageAndTheDaysSinceThePreviousStageClosed()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var id = await SeedAsync(db, 2025, Input(S("PPP", true, D(2025, 4, 10)), S("AON", true, D(2025, 5, 22)), S("SO", true, D(2025, 12, 12), cost: 86.40m)));

        var (status, journey) = await ReadAsync(db, id);

        Assert.Equal(new ItProjectPagePresentation.HeroStatus("At Delivery and ATP for 331 days", "since SO closed on 12 Dec 2025", "late"), status);

        var steps = journey.ToDictionary(step => step.StageCode);
        Assert.Equal(9, journey.Count);
        Assert.Null(steps["PPP"].Gap);
        Assert.Equal("42 d", steps["AON"].Gap);
        Assert.Null(steps["BID"].Gap);                      // auto-completed without a date
        Assert.Equal("nodate", steps["BID"].State);
        Assert.Equal("date not recorded", steps["BID"].DateText);
        Assert.Null(steps["SO"].Gap);                       // COB has no date: not measured, not guessed
        Assert.Equal("₹ 86.40 L", steps["SO"].Cost);
        Assert.Equal("skip", steps["BM"].State);
        Assert.True(steps["BM"].Travelled);                  // the line runs through a skipped stop
        Assert.Equal(("cur", "late", "331 d so far"), (steps["DLV"].State, steps["DLV"].Tone, steps["DLV"].Gap));
        Assert.True(steps["DLV"].Travelled);
    }

    [Fact]
    public async Task PreviousStageWithoutDate_SaysWhenTheMoveWasRecorded()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var id = await SeedAsync(db, 2026, Input(S("BID", true)));

        var (status, journey) = await ReadAsync(db, id);

        Assert.Equal(new ItProjectPagePresentation.HeroStatus("At TEC since 08 Nov 2026", "when the move was recorded; Bid has no date", "nodate"), status);
        Assert.Null(journey.Single(step => step.StageCode == "TEC").Gap); // no "so far" without a stage date
    }

    [Fact]
    public async Task NotStarted_DeliveredAndDropped_ReadPlainly()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var notStarted = await SeedAsync(db, 2026, null);
        var delivered = await SeedAsync(db, 2024, Input(S("PPP", true, D(2024, 7, 9)), S("SO", true, D(2025, 2, 24), cost: 34.90m), S("DLV", true, D(2025, 6, 30))));
        var dropped = await SeedAsync(db, 2025, Input(S("BID", true, D(2025, 10, 15))));
        Assert.True((await db.Commands().DropAsync(dropped, D(2026, 1, 19), "Merged into the HQ contract", await db.RowVersionAsync(dropped), ItProjectTestDb.Actor())).Succeeded);

        Assert.Equal(new ItProjectPagePresentation.HeroStatus("Not started, added today", null, "calm"), (await ReadAsync(db, notStarted)).Status);
        Assert.Equal(new ItProjectPagePresentation.HeroStatus("Delivered on 30 Jun 2025", "356 days end to end", "ok"), (await ReadAsync(db, delivered)).Status);

        var (droppedStatus, droppedJourney) = await ReadAsync(db, dropped);
        Assert.Equal(new ItProjectPagePresentation.HeroStatus("Dropped at TEC on 19 Jan 2026", "Merged into the HQ contract", "drop"), droppedStatus);
        Assert.Equal("stop", droppedJourney.Single(step => step.StageCode == "TEC").State);
    }

    [Fact]
    public async Task Page_LoadsTheHeaderAndDefaultsDatesToTodayInIndia()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var id = await SeedAsync(db, 2025, null);
        // 8 Nov 2026, 20:00 UTC is already 9 Nov in India.
        db.Clock.Set(new DateTimeOffset(2026, 11, 8, 20, 0, 0, TimeSpan.Zero));

        var page = ItProjectDetailsPageTests.PageFor(db, ItProjectTestDb.User("hod-1", RoleNames.HoD));
        await page.OnGetAsync(id, CancellationToken.None);

        Assert.NotNull(page.HeroStatus);
        Assert.Equal(9, page.Journey.Count);
        Assert.Equal("CF", page.LatestYear!.Code); // carried into FY 2026-27 without an SO
        Assert.Equal((DateOnly?)D(2026, 11, 9), page.Remark.EventDate);
        Assert.Equal((DateOnly?)D(2026, 11, 9), page.Drop.DroppedOn);
        Assert.Equal(D(2026, 11, 9), ItProjectYearTypes.TodayIst(db.Clock.UtcNow));
    }

    private static async Task<int> SeedAsync(ItProjectTestDb db, int fy, List<ItProjectStageInput>? stages)
    {
        var (id, rowVersion) = await db.SeedAsync("Case", "IT PPP Case", fy);
        if (stages is not null)
        {
            var save = await db.Commands().SaveStagesAsync(id, stages, rowVersion, ItProjectTestDb.Actor());
            Assert.True(save.Result.Succeeded, string.Join("; ", save.Result.Errors));
        }

        return id;
    }
}
