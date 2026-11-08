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

// Redesign PR 6: the project page hero's "now strip" (test clock: 8 Nov 2026, 15:00 IST).
public class ItProjectPageHeroTests
{
    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private static async Task<IReadOnlyList<ItProjectPagePresentation.NowStripCell>> StripAsync(ItProjectTestDb db, int id)
    {
        var reads = db.Reads();
        var project = (await reads.GetAsync(id))!;
        return ItProjectPagePresentation.NowStrip(project, ItProjectStatus.Resolve(project), reads.GetTiming(project), reads.GetYearTypes(project));
    }

    [Fact]
    public async Task InProgress_ShowsDaysSinceThePreviousStageClosed_AndTheLatestYear()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var id = await SeedAsync(db, 2025, Input(S("PPP", true, D(2025, 4, 10)), S("SO", true, D(2025, 12, 12), cost: 86.40m)));
        var saved = await db.Commands().SaveYearAsync(id, 2026, new ItProjectYearInput(null, "IT PPP 04/2026"), await db.RowVersionAsync(id), ItProjectTestDb.Actor());
        Assert.True(saved.Succeeded);

        var strip = await StripAsync(db, id);

        Assert.Equal(5, strip.Count);
        Assert.Equal(new ItProjectPagePresentation.NowStripCell("At Delivery and ATP for", "331 days", "since SO closed on 12 Dec 2025", "late"), strip[0]);
        Assert.Equal(new ItProjectPagePresentation.NowStripCell("Supply order", "₹ 86.40 L", "placed 12 Dec 2025", "calm"), strip[1]);
        Assert.Equal(new ItProjectPagePresentation.NowStripCell("Started", "10 Apr 2025", "IT PPP / IPA closed", "calm"), strip[2]);
        Assert.Equal("577 days", strip[3].Value);
        Assert.Equal("about 19 months so far", strip[3].Note);
        Assert.Equal(new ItProjectPagePresentation.NowStripCell("FY 2026-27 IT PPP", "CL", "IT PPP 04/2026", "type-cl"), strip[4]);
    }

    [Fact]
    public async Task PreviousStageWithoutDate_SaysWhenTheMoveWasRecorded()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var id = await SeedAsync(db, 2026, Input(S("BID", true)));

        var where = (await StripAsync(db, id))[0];

        Assert.Equal("At TEC", where.Key);
        Assert.Equal("since 08 Nov 2026", where.Value);
        Assert.Equal("when the move was recorded; Bid has no date", where.Note);
        Assert.Equal("nodate", where.Tone);
    }

    [Fact]
    public async Task NotStarted_DeliveredAndDropped_ReadPlainly()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var notStarted = await SeedAsync(db, 2026, null);
        var delivered = await SeedAsync(db, 2024, Input(S("PPP", true, D(2024, 7, 9)), S("SO", true, D(2025, 2, 24), cost: 34.90m), S("DLV", true, D(2025, 6, 30))));
        var dropped = await SeedAsync(db, 2025, Input(S("BID", true, D(2025, 10, 15))));
        Assert.True((await db.Commands().DropAsync(dropped, D(2026, 1, 19), "Merged into the HQ contract", await db.RowVersionAsync(dropped), ItProjectTestDb.Actor())).Succeeded);

        var a = await StripAsync(db, notStarted);
        Assert.Equal(("Not started", "added today"), (a[0].Key, a[0].Value));
        Assert.Equal(("—", "no stage dated yet"), (a[2].Value, a[2].Note));
        Assert.Equal(("New", "IT PPP Case"), (a[4].Value, a[4].Note));

        var b = await StripAsync(db, delivered);
        Assert.Equal(new ItProjectPagePresentation.NowStripCell("Delivered", "30 Jun 2025", "356 days end to end", "ok"), b[0]);
        Assert.Equal("about 12 months, to delivery", b[3].Note);
        Assert.Equal("FY 2025-26 IT PPP", b[4].Key); // the last year it counted in

        var c = await StripAsync(db, dropped);
        Assert.Equal(new ItProjectPagePresentation.NowStripCell("Dropped at TEC", "19 Jan 2026", "Merged into the HQ contract", "drop"), c[0]);
        Assert.Equal("not placed yet", c[1].Note);
    }

    [Fact]
    public async Task Page_LoadsTheStripAndDefaultsDatesToTodayInIndia()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var id = await SeedAsync(db, 2026, null);
        // 8 Nov 2026, 20:00 UTC is already 9 Nov in India.
        db.Clock.Set(new DateTimeOffset(2026, 11, 8, 20, 0, 0, TimeSpan.Zero));

        var page = ItProjectDetailsPageTests.PageFor(db, ItProjectTestDb.User("hod-1", RoleNames.HoD));
        await page.OnGetAsync(id, CancellationToken.None);

        Assert.Equal(5, page.NowStrip.Count);
        Assert.NotNull(page.Timing);
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
