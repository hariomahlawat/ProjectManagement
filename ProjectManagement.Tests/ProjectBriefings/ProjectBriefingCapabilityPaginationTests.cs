using ProjectManagement.Services.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings.Presentation;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

public sealed class ProjectBriefingCapabilityPaginationTests
{
    [Fact]
    public void Parser_PreservesHeadingsParagraphsAndListStructure()
    {
        const string source = """
        Purpose

        The system provides immersive training.

        KEY DELIVERABLES

        • Instructor station
        • Trainee station
        1. Installation package
        (a) Technical documentation
        """;

        var blocks = ProjectBriefingRichTextParser.Parse(source);

        Assert.Contains(blocks, block =>
            block.Type == ProjectBriefingCapabilityBlockType.Heading
            && block.Text == "Purpose");
        Assert.Contains(blocks, block =>
            block.Type == ProjectBriefingCapabilityBlockType.Heading
            && block.Text == "KEY DELIVERABLES");
        Assert.Contains(blocks, block =>
            block.Type == ProjectBriefingCapabilityBlockType.Bullet
            && block.Marker == "•"
            && block.Text == "Instructor station");
        Assert.Contains(blocks, block =>
            block.Type == ProjectBriefingCapabilityBlockType.NumberedItem
            && block.Marker == "1.");
        Assert.Contains(blocks, block =>
            block.Type == ProjectBriefingCapabilityBlockType.LetteredItem
            && block.Marker == "(a)");
    }



    [Fact]
    public void TextNormalizer_PreservesFullStructuredSourceForPresentationPagination()
    {
        var longParagraph = string.Join(
            " ",
            Enumerable.Repeat(
                "This sentence carries complete audience-facing capability information and must not be shortened.",
                24));
        var source = $"# KEY DELIVERABLES\n\n- Instructor station\n- Trainee station\n\n{longParagraph}";

        var normalized = ProjectBriefingTextNormalizer.NormalizeFull(source);

        Assert.True(normalized.StartsWith("# KEY DELIVERABLES", StringComparison.Ordinal));
        Assert.Contains("- Instructor station", normalized, StringComparison.Ordinal);
        Assert.Contains(longParagraph, normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("…", normalized, StringComparison.Ordinal);
        Assert.True(normalized.Length > 1200);
    }

    [Fact]
    public void Paginator_PreservesCompleteLongTextWithoutArtificialEllipsis()
    {
        var paragraphs = Enumerable.Range(1, 24)
            .Select(index =>
                $"Paragraph {index} contains complete capability information for the audience, including operational purpose, training effect, system behaviour, user employment and the final verifiable sentence number {index}.")
            .ToArray();
        var source = string.Join("\n\n", paragraphs);

        var pagination = ProjectBriefingCapabilityPaginator.Paginate(source);
        var rendered = pagination.Pages
            .SelectMany(page => page.Blocks)
            .Select(block => block.Text)
            .ToArray();

        Assert.True(pagination.ContinuationSlideCount > 0);
        Assert.Contains("Paragraph 1 contains complete capability information", rendered[0], StringComparison.Ordinal);
        Assert.Contains("final verifiable sentence number 24.", string.Join(" ", rendered), StringComparison.Ordinal);
        Assert.All(rendered, block =>
            Assert.False(block.EndsWith("…", StringComparison.Ordinal)));
    }

    [Fact]
    public void Paginator_UsesSingleMutedPageForMissingCapability()
    {
        var pagination = ProjectBriefingCapabilityPaginator.Paginate(null);

        var page = Assert.Single(pagination.Pages);
        var block = Assert.Single(page.Blocks);
        Assert.True(page.IsPrimary);
        Assert.True(block.IsMuted);
        Assert.Equal("Capability overview not recorded.", block.Text);
    }
}
