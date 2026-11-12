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

// Redesign PR 8: remarks and stage completions in one feed, newest first.
public class ItProjectFeedTests
{
    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private static ItProjectRemarkView Remark(int id, DateOnly on) =>
        new(id, "u", "Mahesh Kini", RoleNames.HoD, $"Remark {id}", on, ItProjectRemarkType.General, DateTime.UtcNow, null, Array.Empty<byte>());

    private static ItProject Project(params (string Code, DateOnly? On, decimal? Cost)[] done)
    {
        var project = new ItProject { Stages = ItProjectStages.NewStageRows() };
        foreach (var stage in project.Stages)
        {
            var match = done.FirstOrDefault(item => item.Code == stage.StageCode);
            if (match.Code is not null)
            {
                stage.IsCompleted = true;
                stage.CompletedOn = match.On;
                stage.CostLakhs = match.Cost;
            }
        }

        return project;
    }

    [Fact]
    public void Feed_MixesRemarksAndDatedStages_NewestFirst_AndFoldsLongRuns()
    {
        var project = Project(
            ("PPP", D(2025, 4, 10), null), ("AON", D(2025, 5, 22), null), ("BID", D(2025, 8, 1), null),
            ("TEC", D(2025, 9, 15), null), ("COB", null, null), ("SO", D(2025, 12, 12), 86.40m));
        var remarks = new[] { Remark(2, D(2026, 10, 24)), Remark(1, D(2025, 9, 15)) };

        var feed = ItProjectPagePresentation.Feed(project, remarks);

        // newest remark; SO alone; the TEC-day remark before TEC's own completion; then a folded run
        Assert.Equal(2, feed[0].Remark!.Id);
        Assert.Equal("EAS & SO completed · ₹ 86.40 L", Assert.Single(feed[1].Stages).Text);
        Assert.Equal(1, feed[2].Remark!.Id);
        Assert.True(feed[3].Folded);
        Assert.Equal(new[] { "TEC", "BID", "AON", "PPP" }, feed[3].Stages.Select(item => item.StageCode));
        Assert.Equal("4 stages completed, IT ARPPP / IPA to TEC", feed[3].FoldedText);
        Assert.Equal(4, feed.Count);
        Assert.DoesNotContain(feed.SelectMany(entry => entry.Stages), item => item.StageCode == "COB"); // no date: not on the timeline
    }

    [Fact]
    public void Feed_ShowsShortRunsOneByOne_AndTheDropOnTop()
    {
        var project = Project(("PPP", D(2025, 6, 11), null), ("AON", D(2025, 7, 30), null));
        project.DroppedOn = D(2026, 1, 19);

        var feed = ItProjectPagePresentation.Feed(project, Array.Empty<ItProjectRemarkView>());

        Assert.Equal(new[] { "DROPPED", "AON", "PPP" }, feed.SelectMany(entry => entry.Stages).Select(item => item.StageCode));
        Assert.Equal(3, feed.Count);                       // two stages and the drop: not enough to fold
        Assert.All(feed, entry => Assert.False(entry.Folded));
    }

    [Fact]
    public async Task Page_StartsTheComdtOnAConferenceRemarkAndOthersOnARemark()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, rowVersion) = await db.SeedAsync();
        Assert.True((await db.Commands().SaveStagesAsync(id, Input(S("BID", true, D(2026, 9, 1))), rowVersion, ItProjectTestDb.Actor())).Result.Succeeded);

        var hod = ItProjectDetailsPageTests.PageFor(db, ItProjectTestDb.User("hod-1", RoleNames.HoD));
        await hod.OnGetAsync(id, CancellationToken.None);
        Assert.Equal("General", hod.Remark.Type);
        Assert.True(hod.CanWriteConference);
        Assert.Contains(hod.Feed, entry => entry.Stages.Any(item => item.StageCode == "BID"));

        var comdt = ItProjectDetailsPageTests.PageFor(db, ItProjectTestDb.User("comdt-1", RoleNames.Comdt));
        await comdt.OnGetAsync(id, CancellationToken.None);
        Assert.Equal("Conference", comdt.Remark.Type);

        var ito = ItProjectDetailsPageTests.PageFor(db, ItProjectTestDb.User("ito-1", RoleNames.Ito));
        await ito.OnGetAsync(id, CancellationToken.None);
        Assert.False(ito.CanWriteConference);
    }
}
