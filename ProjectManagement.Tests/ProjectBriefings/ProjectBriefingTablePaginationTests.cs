using System.Linq;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

// The status table (docs/ux/briefing-slides-review.md, PR 3): 12 pt rows, up to 12 a slide, and a
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

        Assert.Equal(5, pages.Count);
        Assert.Equal(items, pages.SelectMany(page => page.Items));
        Assert.All(pages, page => Assert.InRange(page.Items.Count, 9, ProjectBriefingTablePagination.MaximumRowsPerSlide));
        Assert.All(pages, page => Assert.True(page.RowHeights.Sum() <= ProjectBriefingTablePagination.AvailableBodyHeight + .0001));
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
