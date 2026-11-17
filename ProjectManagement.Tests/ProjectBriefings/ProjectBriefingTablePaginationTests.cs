using System.Linq;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

// The status table (docs/ux/briefing-slides-review.md, PR 3): 12 pt rows, up to 14 a slide, and a
// cell never shows more lines than its row was measured for.
public sealed class ProjectBriefingTablePaginationTests
{
    [Fact]
    public void FitToLines_KeepsTextThatFits()
    {
        Assert.Equal("VR Bailey Bridge Sml", ProjectBriefingTablePagination.FitToLines("VR  Bailey Bridge Sml", 23, 2));
    }

    [Fact]
    public void FitToLines_CutsAfterTheLastWholeWordThatFits()
    {
        var fitted = ProjectBriefingTablePagination.FitToLines("VR Based Upgraded Indigenous Forward Obsn Sml", 23, 2);

        Assert.Equal("VR Based Upgraded Indigenous Forward…", fitted);
    }

    [Fact]
    public void FitToLines_CutsInsideAWordLongerThanTheLines()
    {
        var fitted = ProjectBriefingTablePagination.FitToLines(new string('X', 60), 10, 2);

        Assert.Equal(20, fitted.Length);
        Assert.EndsWith("…", fitted);
    }

    [Fact]
    public void Paginate_SpreadsFortyNineRowsEvenlyOverAsFewSlidesAsTheyNeed()
    {
        var layout = ProjectBriefingTablePagination.Layout(ProjectBriefingCostMode.CostRdOnly, anyProliferationCost: false);
        var items = Enumerable.Range(1, 49).ToArray();

        var pages = ProjectBriefingTablePagination.Paginate(
            items,
            layout,
            item => ProjectBriefingTablePagination.Measure($"Project {item}", "Development", "Development in progress.", ProjectBriefingCostValue.Missing()));

        Assert.Equal(4, pages.Count);
        Assert.Equal(items, pages.SelectMany(page => page.Items));
        Assert.All(pages, page => Assert.InRange(page.Items.Count, 12, ProjectBriefingTablePagination.MaximumRowsPerSlide));
        Assert.All(pages, page => Assert.True(page.RowHeights.Sum() <= ProjectBriefingTablePagination.AvailableBodyHeight + .0001));
    }

    [Theory]
    // The longest name in PRISM (86 characters) and two-line ones stay whole in every layout.
    [InlineData("Lt & Rt Hand Dvg A & B Veh Sml & Rt Hand Dvg B Veh Sml (Sri Lanka 2025) for FFC trials")]
    [InlineData("VR based Physiological Response Evaluation System (Stress Detection Simulator)")]
    public void FitToWidth_KeepsLongProjectNamesWhole(string name)
    {
        foreach (var mode in new[] { ProjectBriefingCostMode.Both, ProjectBriefingCostMode.CostRdOnly, ProjectBriefingCostMode.ProliferationOnly, ProjectBriefingCostMode.None })
        {
            var layout = ProjectBriefingTablePagination.Layout(mode, anyProliferationCost: true);
            Assert.Equal(name, ProjectBriefingTablePagination.FitToWidth(name, layout.ProjectWidth, bold: true, ProjectBriefingTablePagination.MaximumProjectLines));
        }
    }

    [Fact]
    public void WrappedLines_MeasuresShortCellsByTheFontsWidths()
    {
        // "₹54.86 Lakh · L1" is one line in the cost column; the old average measure made it two.
        var layout = ProjectBriefingTablePagination.Layout(ProjectBriefingCostMode.Both, anyProliferationCost: true);
        Assert.Equal(1, ProjectBriefingTablePagination.WrappedLines("₹54.86 Lakh · L1", layout.Widths[1], bold: false, 2));
        Assert.Equal(1, ProjectBriefingTablePagination.WrappedLines("Development", layout.StageWidth, bold: false, 2));
        Assert.Equal(2, ProjectBriefingTablePagination.WrappedLines("Commercial Bid Opening", layout.StageWidth, bold: false, 2));
    }

    [Fact]
    public void Layout_LeavesTheProliferationColumnOutWhenNoProjectHasOne()
    {
        Assert.DoesNotContain("PROLIFERATION COST", ProjectBriefingTablePagination.Layout(ProjectBriefingCostMode.Both, anyProliferationCost: false).Headers);
        Assert.Contains("PROLIFERATION COST", ProjectBriefingTablePagination.Layout(ProjectBriefingCostMode.Both, anyProliferationCost: true).Headers);
        Assert.All(
            new[] { (true, true), (true, false), (false, true), (false, false) },
            combination =>
            {
                var mode = combination switch
                {
                    (true, true) => ProjectBriefingCostMode.Both,
                    (true, false) => ProjectBriefingCostMode.CostRdOnly,
                    (false, true) => ProjectBriefingCostMode.ProliferationOnly,
                    _ => ProjectBriefingCostMode.None
                };
                var layout = ProjectBriefingTablePagination.Layout(mode, anyProliferationCost: true);
                Assert.InRange(layout.Widths.Sum(), 12.14, 12.16);
                Assert.Equal(layout.Headers.Count, layout.Widths.Count);
            });
    }
}
