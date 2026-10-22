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

    // SECTION: Which tiles show at each phase
    [Fact]
    public void BeforeTheSupplyOrder_TracksScheduleCostUpdateAndHealth()
    {
        var strip = Build(Portfolio("TEC", pdcInDays: -76), Procurement(aon: 46_500_000m, ipa: 48_000_000m));

        Assert.Equal(new[] { ProjectHeaderTile.Stage, ProjectHeaderTile.Cost, ProjectHeaderTile.LastUpdate, ProjectHeaderTile.RecordHealth }, strip.Tiles);
        Assert.Equal(new[] { ProjectHeaderTile.Jdp, ProjectHeaderTile.Proliferation }, strip.InOverviewCard);
    }

    [Theory]
    [InlineData("DEVP", false)]
    [InlineData("EAS", true)]
    public void FromTheSupplyOrder_JdpTakesRecordHealthsPlace(string currentStage, bool supplyOrderDated)
    {
        var strip = Build(
            Portfolio(currentStage, pdcInDays: 10),
            Procurement(l1: 2_599_000m, supplyOrder: supplyOrderDated ? new DateOnly(2026, 1, 29) : null));

        Assert.Equal(new[] { ProjectHeaderTile.Stage, ProjectHeaderTile.Cost, ProjectHeaderTile.Jdp, ProjectHeaderTile.LastUpdate }, strip.Tiles);
        Assert.Equal(new[] { ProjectHeaderTile.Proliferation, ProjectHeaderTile.RecordHealth }, strip.InOverviewCard);
    }

    [Fact]
    public void CompletedProject_LeadsWithTransferOfTechnology()
    {
        var strip = Build(Portfolio(null, pdcInDays: null), Procurement(), ProjectLifecycleStatus.Completed);

        Assert.Equal(new[] { ProjectHeaderTile.Tot, ProjectHeaderTile.Cost, ProjectHeaderTile.Proliferation, ProjectHeaderTile.RecordHealth }, strip.Tiles);
        Assert.Equal(new[] { ProjectHeaderTile.Jdp }, strip.InOverviewCard);
    }

    [Fact]
    public void EveryTrackedTileRendersExactlyOnce()
    {
        foreach (var lifecycle in Enum.GetValues<ProjectLifecycleStatus>())
        {
            var strip = Build(Portfolio("DEVP", pdcInDays: 5), Procurement(), lifecycle);
            foreach (var tile in new[] { ProjectHeaderTile.Jdp, ProjectHeaderTile.Proliferation, ProjectHeaderTile.RecordHealth })
            {
                Assert.True(strip.Shows(tile) ^ strip.InCard(tile), $"{tile} must render once for {lifecycle}.");
            }
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

    // SECTION: Latest cost
    [Fact]
    public void CostTile_ShowsTheLatestCost_WithTheEarlierOneBeneath()
    {
        var cost = ProjectHeaderStripBuilder.BuildCost(Procurement(aon: 46_500_000m, ipa: 48_000_000m));

        Assert.Equal(("₹4.65 Cr", "AoN", "IPA ₹4.8 Cr"), (cost.Value, cost.Basis, cost.Detail));
    }

    [Fact]
    public void CostTile_ShowsTheSupplyOrderOnceItIsPlaced()
    {
        var cost = ProjectHeaderStripBuilder.BuildCost(Procurement(l1: 2_599_000m, aon: 3_000_000m, supplyOrder: new DateOnly(2026, 1, 29)));

        Assert.Equal(("₹25.99 Lakh", "L1", "Supply order · 29 Jan 2026"), (cost.Value, cost.Basis, cost.Detail));
    }

    [Fact]
    public void CostTile_WithNothingRecorded_SaysSo()
    {
        var cost = ProjectHeaderStripBuilder.BuildCost(Procurement());

        Assert.Equal(("Not recorded", null, "No cost recorded yet"), (cost.Value, cost.Basis, cost.Detail));
    }

    // SECTION: Last update
    [Fact]
    public void UpdateTile_ShowsPlainTextAuthorDateAndAge_AndMarksOldUpdates()
    {
        var remarks = new ProjectRemarkSummaryViewModel
        {
            InternalCount = 1,
            LastActivityUtc = new DateTime(2026, 6, 8, 5, 0, 0, DateTimeKind.Utc),
            LastRemarkPreview = "Review done. <span class=\"remark-mention\" data-user-id=\"x\">@Arpan</span> to follow up &amp; close.",
            LastRemarkAuthorDisplayName = "Abhishek Khajuria"
        };

        var update = ProjectHeaderStripBuilder.BuildLastUpdate(remarks, NowUtc);

        Assert.True(update.HasRemark);
        Assert.Equal("Review done. @Arpan to follow up & close.", update.Preview);
        Assert.Equal("Abhishek Khajuria · 08 Jun 2026 · 4 months ago", update.Meta);
        Assert.True(update.IsStale);
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
        => ProjectHeaderStripBuilder.Build(lifecycle, portfolio, procurement, ProjectRemarkSummaryViewModel.Empty, Today, NowUtc);

    private static ProjectPortfolioPresentationVm Portfolio(string? currentCode, int? pdcInDays)
        => new()
        {
            CurrentStage = currentCode is null
                ? null
                : new TimelineItemVm
                {
                    Code = currentCode,
                    Name = currentCode == "TEC" ? "Technical Evaluation" : currentCode,
                    Status = StageStatus.InProgress,
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
