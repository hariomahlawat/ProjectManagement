using ProjectManagement.Services.Projects;

namespace ProjectManagement.Tests.Projects;

public sealed class ProjectContentRulesTests
{
    [Fact]
    public void CountWords_UsesNonWhitespaceTokens()
    {
        Assert.Equal(5, ProjectContentRules.CountWords("One  two\nthree\tfour five"));
    }

    [Fact]
    public void NormalizeCapabilities_RemovesBlankRowsAndPreservesOrder()
    {
        var result = ProjectContentRules.NormalizeCapabilities(new string?[]
        {
            "  First capability  ",
            null,
            " ",
            "Second capability"
        });

        Assert.Equal(new[] { "First capability", "Second capability" }, result);
    }

    [Fact]
    public void NormalizeNarrative_UsesConsistentNewlinesAndTrimsOuterWhitespace()
    {
        var result = ProjectContentRules.NormalizeNarrative("  First\r\nSecond  ");

        Assert.Equal("First\nSecond", result);
    }

    [Theory]
    [InlineData(0, ProjectBriefReadiness.NotRecorded)]
    [InlineData(1, ProjectBriefReadiness.Concise)]
    [InlineData(99, ProjectBriefReadiness.Concise)]
    [InlineData(100, ProjectBriefReadiness.Recommended)]
    [InlineData(150, ProjectBriefReadiness.Recommended)]
    [InlineData(151, ProjectBriefReadiness.AboveRecommended)]
    [InlineData(200, ProjectBriefReadiness.AboveRecommended)]
    [InlineData(201, ProjectBriefReadiness.ExceedsMaximum)]
    public void GetBriefReadiness_UsesConfiguredReportingBands(
        int wordCount,
        ProjectBriefReadiness expected)
    {
        Assert.Equal(expected, ProjectContentRules.GetBriefReadiness(wordCount));
    }

    [Theory]
    [InlineData(0, ProjectCapabilityReadiness.NotRecorded)]
    [InlineData(1, ProjectCapabilityReadiness.Draft)]
    [InlineData(4, ProjectCapabilityReadiness.Draft)]
    [InlineData(5, ProjectCapabilityReadiness.PresentationReady)]
    [InlineData(8, ProjectCapabilityReadiness.PresentationReady)]
    public void GetCapabilityReadiness_UsesPresentationThreshold(
        int statementCount,
        ProjectCapabilityReadiness expected)
    {
        Assert.Equal(expected, ProjectContentRules.GetCapabilityReadiness(statementCount));
    }

    [Fact]
    public void NormalizeProjectBrief_RemovesMarkdownDecorationWithoutTruncatingNarrative()
    {
        var result = ProjectManagement.Services.ProjectBriefings.ProjectBriefingTextNormalizer
            .NormalizeProjectBrief("## Heading\n\nThis is **important** project content.");

        Assert.Equal("Heading\n\nThis is important project content.", result);
    }
}
