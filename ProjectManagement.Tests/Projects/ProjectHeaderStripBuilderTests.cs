using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Services.Projects;
using ProjectManagement.ViewModels;
using Xunit;

namespace ProjectManagement.Tests.Projects;

public sealed class ProjectHeaderStripBuilderTests
{
    private static readonly DateOnly Today = new(2026, 10, 22);
    private static readonly DateTime NowUtc = new(2026, 10, 22, 6, 30, 0, DateTimeKind.Utc);

    // SECTION: Which tiles show at each phase (always four)
    [Theory]
    [InlineData("TEC", false)]
    [InlineData("DEVP", false)]
    [InlineData("EAS", true)]
    public void ActiveProject_ShowsStageCostJdpAndHealth_BeforeAndAfterTheSupplyOrder(string currentStage, bool supplyOrderDated)
    {
        var strip = Build(
            Portfolio(currentStage, pdcInDays: 10),
            Procurement(l1: 2_599_000m, supplyOrder: supplyOrderDated ? new DateOnly(2026, 1, 29) : null));

        Assert.Equal(new[] { ProjectHeaderTile.Stage, ProjectHeaderTile.Cost, ProjectHeaderTile.Jdp, ProjectHeaderTile.RecordHealth }, strip.Tiles);
    }

    [Fact]
    public void CompletedProject_LeadsWithToT_AndShowsProliferationCostInPlaceOfCost()
    {
        var strip = Build(Portfolio(null, pdcInDays: null), Procurement(), ProjectLifecycleStatus.Completed);

        Assert.Equal(new[] { ProjectHeaderTile.Tot, ProjectHeaderTile.Proliferation, ProjectHeaderTile.Jdp, ProjectHeaderTile.RecordHealth }, strip.Tiles);
    }

    [Fact]
    public void EveryPhaseShowsFourTiles_IncludingJdpAndRecordHealth()
    {
        foreach (var lifecycle in Enum.GetValues<ProjectLifecycleStatus>())
        {
            var strip = Build(Portfolio("DEVP", pdcInDays: 5), Procurement(), lifecycle);

            Assert.Equal(4, strip.Tiles.Count);
            Assert.Equal(strip.Tiles.Count, strip.Tiles.Distinct().Count());
            Assert.True(strip.Shows(ProjectHeaderTile.Jdp) && strip.Shows(ProjectHeaderTile.RecordHealth), $"{lifecycle} shows JDP and record health.");
        }
    }

    // SECTION: Stage & schedule
    [Theory]
    [InlineData(-76, "Overdue 76 days · PDC 07 Aug 2026", "danger")]
    [InlineData(0, "Due today · PDC 22 Oct 2026", "warn")]
    [InlineData(7, "Due in 7 days · PDC 29 Oct 2026", "warn")]
    [InlineData(45, "PDC 06 Dec 2026", "")]
    public void StageTile_SaysWhereThePdcStands(int pdcInDays, string expected, string tone)
    {
        var stage = ProjectHeaderStripBuilder.BuildStage(Portfolio("TEC", pdcInDays), Today);

        Assert.Equal("Technical Evaluation", stage.Title);
        Assert.Equal(expected, stage.Schedule);
        Assert.Equal(tone, stage.ScheduleTone);
    }

    [Fact]
    public void StageTile_FlagsAMissingPdc()
    {
        var stage = ProjectHeaderStripBuilder.BuildStage(Portfolio("TEC", pdcInDays: null), Today);

        Assert.Equal(("PDC not set", "warn"), (stage.Schedule, stage.ScheduleTone));
    }

    [Theory]
    [InlineData(StageStatus.NotStarted)]
    [InlineData(StageStatus.Blocked)]
    public void StageTile_OnlyAnInProgressStageCanBeOverdue(StageStatus status)
    {
        var stage = ProjectHeaderStripBuilder.BuildStage(Portfolio("TEC", pdcInDays: -76, status), Today);

        Assert.Equal(("PDC 07 Aug 2026", ""), (stage.Schedule, stage.ScheduleTone));
    }

    [Theory]
    [InlineData(StageStatus.NotStarted)]
    [InlineData(StageStatus.Blocked)]
    public void StageTile_DoesNotAskForAPdcBeforeTheStageIsInProgress(StageStatus status)
    {
        var stage = ProjectHeaderStripBuilder.BuildStage(Portfolio("TEC", pdcInDays: null, status), Today);

        Assert.Null(stage.Schedule);
    }

    // SECTION: Project cost (the shared rule's result, with the supply order beneath)
    [Fact]
    public void CostTile_ShowsTheProjectCostAndItsBasis_WithTheSupplyOrderBeneath()
    {
        var cost = ProjectHeaderStripBuilder.BuildCost(
            new ProjectCostResolution(0.2599m, ProjectCostSource.L1), new DateOnly(2026, 1, 29));

        Assert.Equal(("₹25.99 Lakh", "L1", "Supply order · 29 Jan 2026"), (cost.Value, cost.Basis, cost.Detail));
    }

    [Fact]
    public void CostTile_BeforeTheSupplyOrder_SaysItIsNotPlaced()
    {
        var cost = ProjectHeaderStripBuilder.BuildCost(new ProjectCostResolution(4.8m, ProjectCostSource.Ipa), null);

        Assert.Equal(("₹4.8 Cr", "IPA", "Supply order not placed"), (cost.Value, cost.Basis, cost.Detail));
    }

    [Fact]
    public void CostTile_WithNothingRecorded_SaysSo()
    {
        var cost = ProjectHeaderStripBuilder.BuildCost(new ProjectCostResolution(null, ProjectCostSource.None), null);

        Assert.Equal(("Not recorded", null, "No cost recorded yet"), (cost.Value, cost.Basis, cost.Detail));
    }

    // SECTION: Last update
    [Fact]
    public void LatestUpdate_ShowsTheWholeRemarkAsPlainText_WithAuthorDateAndAge_AndMarksOldUpdates()
    {
        var longTail = new string('b', 400);
        var remarks = new ProjectRemarkSummaryViewModel
        {
            InternalCount = 1,
            LastActivityUtc = new DateTime(2026, 6, 8, 5, 0, 0, DateTimeKind.Utc),
            LastRemarkText = "Review done. @Arpan to follow up & close. " + longTail,
            LastRemarkAuthorDisplayName = "Abhishek Khajuria"
        };

        var update = ProjectHeaderStripBuilder.BuildLastUpdate(remarks, NowUtc);

        Assert.True(update.HasRemark);
        Assert.Equal("Review done. @Arpan to follow up & close. " + longTail, update.Text);
        Assert.Equal("Abhishek Khajuria · 08 Jun 2026 · 4 months ago", update.Meta);
        Assert.True(update.IsStale);
    }

    [Fact]
    public void RemarkText_RemovesMarkup_AndKeepsTheAuthorsLineBreaks()
    {
        var body = "Review done. <span class=\"remark-mention\" data-user-id=\"x\">@Arpan</span> to follow up &amp; close.\r\n\r\n\r\n"
            + "Next:   vendor demo<br/>on Monday";

        Assert.Equal("Review done. @Arpan to follow up & close.\n\nNext: vendor demo\non Monday", ProjectHeaderStripBuilder.RemarkText(body));
    }

    [Fact]
    public void RemarkText_OfAnEmptyBody_IsEmpty()
    {
        Assert.Equal(string.Empty, ProjectHeaderStripBuilder.RemarkText("  <p> </p> "));
    }

    [Fact]
    public void UpdateTile_WithoutRemarks_InvitesOne()
    {
        var update = ProjectHeaderStripBuilder.BuildLastUpdate(ProjectRemarkSummaryViewModel.Empty, NowUtc);

        Assert.False(update.HasRemark);
        Assert.False(update.IsStale);
    }

    // SECTION: Builders
    private static ProjectHeaderStripVm Build(
        ProjectPortfolioPresentationVm portfolio,
        ProcurementAtAGlanceVm procurement,
        ProjectLifecycleStatus lifecycle = ProjectLifecycleStatus.Active)
        => ProjectHeaderStripBuilder.Build(lifecycle, portfolio, procurement, null, ProjectRemarkSummaryViewModel.Empty, Today, NowUtc);

    private static ProjectPortfolioPresentationVm Portfolio(string? currentCode, int? pdcInDays, StageStatus status = StageStatus.InProgress)
        => new()
        {
            CurrentStage = currentCode is null
                ? null
                : new TimelineItemVm
                {
                    Code = currentCode,
                    Name = currentCode == "TEC" ? "Technical Evaluation" : currentCode,
                    Status = status,
                    PlannedEnd = pdcInDays.HasValue ? Today.AddDays(pdcInDays.Value) : null,
                    Today = Today
                }
        };

    private static ProcurementAtAGlanceVm Procurement(
        decimal? ipa = null,
        decimal? aon = null,
        decimal? l1 = null,
        DateOnly? supplyOrder = null)
        => new(ipa, aon, null, l1, null, supplyOrder);
}
