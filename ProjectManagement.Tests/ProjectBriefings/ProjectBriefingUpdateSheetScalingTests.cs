using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings.Presentation;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

// A fact sheet of few rows sets them larger (docs/ux/briefing-slides-review.md, step 7), but a row
// is enlarged only when its text still fits the row the planner gives it.
public sealed class ProjectBriefingUpdateSheetScalingTests
{
    [Fact]
    public void ShortRows_AreEnlarged()
    {
        var rows = ProjectBriefingUpdateSheetPlanner.ResolveRows(Project("TEC under progress."), ProjectBriefingUpdateSheetOptions.Default);
        var nine = ProjectBriefingUpdateSheetPlanner.ResolveRows(Project("TEC under progress."), ProjectBriefingUpdateSheetOptions.Default with { Rows = ProjectBriefingUpdateSheetOptions.AllRows });

        Assert.True(rows.Count <= 6);
        Assert.True(Status(rows).FontSize > Status(nine).FontSize, $"{Status(rows).FontSize} pt");
    }

    [Fact]
    public void LongMultilineStatus_KeepsItsSize_OtherRowsStillEnlarged()
    {
        var shortRows = ProjectBriefingUpdateSheetPlanner.ResolveRows(Project("TEC under progress."), ProjectBriefingUpdateSheetOptions.Default);
        var longStatus = string.Join('\n', Enumerable.Range(1, 8).Select(index => $"Step {index}: trial findings reviewed and closed out."));
        var longRows = ProjectBriefingUpdateSheetPlanner.ResolveRows(Project(longStatus), ProjectBriefingUpdateSheetOptions.Default);

        // Nine rows enlarge by 15% at most, five by 30%: the long status, too tall for 30%, is not
        // set larger on the five-row sheet than on the nine-row one.
        var nine = ProjectBriefingUpdateSheetPlanner.ResolveRows(Project(longStatus), ProjectBriefingUpdateSheetOptions.Default with { Rows = ProjectBriefingUpdateSheetOptions.AllRows });
        Assert.Equal(9, nine.Count);
        Assert.True(Status(longRows).FontSize <= Status(nine).FontSize, $"{Status(longRows).FontSize} pt against {Status(nine).FontSize} pt");
        var cost = longRows.Single(row => row.Key == ProjectBriefingUpdateSheetRow.ProjectCost);
        Assert.Equal(shortRows.Single(row => row.Key == ProjectBriefingUpdateSheetRow.ProjectCost).FontSize, cost.FontSize);
    }

    private static ProjectBriefingUpdateSheetPlanningRow Status(IReadOnlyList<ProjectBriefingUpdateSheetPlanningRow> rows)
        => rows.Single(row => row.Key == ProjectBriefingUpdateSheetRow.PresentStatus);

    private static ProjectBriefingProjectVm Project(string status)
        => new()
        {
            ProjectId = 1,
            ProjectName = "Project 1",
            ExternalStatus = status,
            CostRd = new ProjectBriefingCostValue(10_000_000m, ProjectBriefingCostBasis.AoN, "₹1 Cr", "AoN"),
            ProjectBrief = "A man-portable system."
        };
}
