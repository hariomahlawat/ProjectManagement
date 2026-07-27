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
    [Fact]
    public void NormalizeProjectBrief_RemovesMarkdownDecorationWithoutTruncatingNarrative()
    {
        var result = ProjectManagement.Services.ProjectBriefings.ProjectBriefingTextNormalizer
            .NormalizeProjectBrief("## Heading\n\nThis is **important** project content.");

        Assert.Equal("Heading\n\nThis is important project content.", result);
    }

}
