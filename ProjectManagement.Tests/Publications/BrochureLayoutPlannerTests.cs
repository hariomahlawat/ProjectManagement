using ProjectManagement.Services.Publications;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class BrochureLayoutPlannerTests
{
    [Fact]
    public void Plan_UsesFourProjectLayoutForConciseNarratives()
    {
        var projects = Enumerable.Range(1, 4)
            .Select(id => Project(id, 70))
            .ToArray();

        var page = Assert.Single(BrochureLayoutPlanner.Plan(projects));

        Assert.Equal(BrochurePageLayoutKind.FourCompact, page.Layout);
        Assert.Equal(new[] { 1, 2, 3, 4 }, page.Items.Select(item => item.Project.ProjectId));
    }

    [Fact]
    public void Plan_UsesThreeProjectLayoutWhenCopyNeedsMoreRoom()
    {
        var projects = Enumerable.Range(1, 3)
            .Select(id => Project(id, 110))
            .ToArray();

        var page = Assert.Single(BrochureLayoutPlanner.Plan(projects));

        Assert.Equal(BrochurePageLayoutKind.ThreeStandard, page.Layout);
        Assert.Equal(3, page.Items.Count);
    }

    [Fact]
    public void Plan_UsesTwoProjectLayoutForLongerStandardBriefs()
    {
        var projects = new[] { Project(1, 180), Project(2, 190) };

        var page = Assert.Single(BrochureLayoutPlanner.Plan(projects));

        Assert.Equal(BrochurePageLayoutKind.TwoFeature, page.Layout);
        Assert.Equal(2, page.Items.Count);
    }

    [Fact]
    public void Plan_SplitsExceptionalLongNarrativeWithoutChangingProjectOrder()
    {
        var projects = new[]
        {
            Project(11, 60),
            Project(12, 760),
            Project(13, 60)
        };

        var pages = BrochureLayoutPlanner.Plan(projects);
        var flattened = pages.SelectMany(page => page.Items).ToArray();

        Assert.Equal(11, flattened[0].Project.ProjectId);
        Assert.All(flattened.Where(item => item.Project.ProjectId == 12), item => Assert.InRange(item.NarrativeWordCount, 1, 210));
        Assert.True(flattened.Count(item => item.Project.ProjectId == 12) >= 4);
        Assert.Equal(13, flattened[^1].Project.ProjectId);
        Assert.All(flattened.Where(item => item.Project.ProjectId == 12), item =>
            Assert.Equal(BrochurePageLayoutKind.SingleFeature, pages.Single(page => page.Items.Contains(item)).Layout));
    }

    [Fact]
    public void CountWords_TreatsProjectNomenclatureAsWordsWithoutRewritingIt()
    {
        Assert.Equal(9, BrochureLayoutPlanner.CountWords("VR based T-72 / T-90 simulator for 8×8 platform"));
    }

    private static BrochurePublicationProject Project(int id, int wordCount)
    {
        var narrative = string.Join(" ", Enumerable.Range(1, wordCount).Select(index => $"w{index}"));
        return new BrochurePublicationProject(
            id,
            $"Project {id}",
            "Other R&D Projects",
            "AR / VR",
            narrative,
            wordCount,
            Photo: null,
            PhotoIsLowResolution: false,
            PhotoSourceVariant: null);
    }
}
