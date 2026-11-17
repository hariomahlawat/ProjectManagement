using ProjectManagement.Services.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings.Presentation;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

public sealed class ProjectBriefingCapabilityPaginationTests
{
    // Fit before splitting (docs/ux/briefing-slides-review.md, PR 2). The counter-UAS project on
    // the review deck: six bullets on a key points slide went two on the slide and four alone on a
    // continuation slide, although all six fit at the 14 pt floor.
    private const string SixBullets = """
        • Detects Group 1 and Group 2 UAVs at up to 3 km by passive RF and at up to 2.5 km by radar
        • Tracks up to 8 targets at once and fuses RF, radar and EO/IR tracks into one picture
        • Defeats drones by directional jamming of the 433 MHz, 915 MHz, 2.4 GHz and 5.8 GHz control links and of GNSS
        • Classifies drone type from its RF signature and shows the likely pilot direction
        • A crew of two sets it up in under 2 minutes; it carries as two loads of under 13 kg each
        • Runs for 6 hours on hot-swappable batteries, or indefinitely from a vehicle supply
        """;

    [Fact]
    public void TextThatFitsAtASmallerSize_StaysOnOneSlide()
    {
        // A panel where the six bullets do not fit at their own size (16 pt) but do at the floor.
        var atOwnSize = ProjectBriefingNarrativeTypography.ResolveCapabilityDensity(ProjectBriefingRichTextParser.Parse(SixBullets));
        Assert.Equal(ProjectBriefingNarrativeDensity.Standard, atOwnSize);
        var pagination = ProjectBriefingCapabilityPaginator.Paginate(SixBullets, 3.5, 90);

        var page = Assert.Single(pagination.Pages);
        Assert.Equal(6, page.Blocks.Count(block => block.Type == ProjectBriefingCapabilityBlockType.Bullet));
        Assert.All(page.Blocks, block => Assert.True(block.FontSize >= 14.0, "Never below the 14 pt floor."));
    }

    [Fact]
    public void TextThatFitsAtItsOwnSize_KeepsThatSize()
    {
        var pagination = ProjectBriefingCapabilityPaginator.Paginate("• Instructor station\n• Trainee station", 3.3, 72);

        var page = Assert.Single(pagination.Pages);
        Assert.All(page.Blocks, block => Assert.Equal(18.0, block.FontSize));
    }

    [Fact]
    public void TextTooLongForOneSlide_ContinuesAtTheFloorSize()
    {
        var long_ = string.Join("\n", Enumerable.Repeat(SixBullets.Trim(), 4));

        var pagination = ProjectBriefingCapabilityPaginator.Paginate(long_, 3.3, 72);

        Assert.True(pagination.Pages.Count > 1);
        Assert.All(pagination.Pages.SelectMany(item => item.Blocks), block => Assert.Equal(14.0, block.FontSize));
    }

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
    public void Parser_DoesNotPromoteNumberedOrLetteredItemsToHeadings()
    {
        const string source = """
        1. Engine Familiarisation
        2. Parts Familiarisation With CAT Part Number
        (a) Portable System
        (b) Indigenous Design
        """;

        var blocks = ProjectBriefingRichTextParser.Parse(source);

        Assert.DoesNotContain(blocks, block => block.Type == ProjectBriefingCapabilityBlockType.Heading);
        Assert.Equal(2, blocks.Count(block => block.Type == ProjectBriefingCapabilityBlockType.NumberedItem));
        Assert.Equal(2, blocks.Count(block => block.Type == ProjectBriefingCapabilityBlockType.LetteredItem));
    }


    [Fact]
    public void Parser_MergesStandaloneListMarkersWithFollowingText()
    {
        const string source = """
        1.
        Engine Familiarisation
        2.
        Valve Timing Setting Procedure
        (a)
        Portable System
        •
        Offline operation
        """;

        var blocks = ProjectBriefingRichTextParser.Parse(source);

        Assert.Collection(
            blocks,
            block =>
            {
                Assert.Equal(ProjectBriefingCapabilityBlockType.NumberedItem, block.Type);
                Assert.Equal("1.", block.Marker);
                Assert.Equal("Engine Familiarisation", block.Text);
            },
            block =>
            {
                Assert.Equal(ProjectBriefingCapabilityBlockType.NumberedItem, block.Type);
                Assert.Equal("2.", block.Marker);
                Assert.Equal("Valve Timing Setting Procedure", block.Text);
            },
            block =>
            {
                Assert.Equal(ProjectBriefingCapabilityBlockType.LetteredItem, block.Type);
                Assert.Equal("(a)", block.Marker);
                Assert.Equal("Portable System", block.Text);
            },
            block =>
            {
                Assert.Equal(ProjectBriefingCapabilityBlockType.Bullet, block.Type);
                Assert.Equal("•", block.Marker);
                Assert.Equal("Offline operation", block.Text);
            });
    }

    [Fact]
    public void Parser_PreservesNestedBulletIndentAcrossPaginationBlocks()
    {
        const string source = """
        3. Eight vehicle variants i.e.
        • Maruti Gypsy
        • HMV 6x6
        4. Training under varied terrain and weather conditions.
        """;

        var blocks = ProjectBriefingRichTextParser.Parse(source);

        Assert.Equal(0, blocks[0].IndentLevel);
        Assert.Equal(1, blocks[1].IndentLevel);
        Assert.Equal(1, blocks[2].IndentLevel);
        Assert.Equal(0, blocks[3].IndentLevel);

        var pagination = ProjectBriefingCapabilityPaginator.Paginate(source);
        var rendered = pagination.Pages.SelectMany(page => page.Blocks).ToArray();
        Assert.Equal(1, rendered.Single(block => block.Text == "Maruti Gypsy").IndentLevel);
        Assert.Equal(1, rendered.Single(block => block.Text == "HMV 6x6").IndentLevel);
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

        Assert.StartsWith("# KEY DELIVERABLES", normalized, StringComparison.Ordinal);
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

    [Fact]
    public void Paginator_UsesSparseTypographyForConciseCapabilityLists()
    {
        const string source = """
        • Detects and classifies targets.
        • Presents a real-time cue to the operator.
        • Supports day and night employment.
        • Records engagement data for review.
        """;

        var pagination = ProjectBriefingCapabilityPaginator.Paginate(source);

        var page = Assert.Single(pagination.Pages);
        Assert.Equal(ProjectBriefingNarrativeDensity.Sparse, page.Density);
        Assert.All(
            page.Blocks.Where(block => block.Type == ProjectBriefingCapabilityBlockType.Bullet),
            block => Assert.True(block.FontSize >= 14.4));
    }

    [Fact]
    public void Paginator_UsesDenseTypographyWithoutDiscardingLongCapabilityContent()
    {
        var source = string.Join(
            "\n",
            Enumerable.Range(1, 18).Select(index =>
                $"{index}. Capability {index} provides a complete operational function, user action, measurable training effect and support outcome without removing source information."));

        var pagination = ProjectBriefingCapabilityPaginator.Paginate(source);
        var rendered = string.Join(" ", pagination.Pages.SelectMany(page => page.Blocks).Select(block => block.Text));

        Assert.All(pagination.Pages, page => Assert.Equal(ProjectBriefingNarrativeDensity.Dense, page.Density));
        Assert.Contains("Capability 18", rendered, StringComparison.Ordinal);
        Assert.All(
            pagination.Pages.SelectMany(page => page.Blocks)
                .Where(block => block.Type != ProjectBriefingCapabilityBlockType.Heading),
            // Dense text is the compact size, but never below what a room can read.
            block => Assert.InRange(block.FontSize, 13.5, 14.0));
        // The larger type moves more of a long overview onto continuation slides.
        Assert.True(pagination.Pages.Count >= 2);
    }

    [Fact]
    public void ListText_IsAtLeastFourteenPoints_AndTheLineBudgetFollowsTheSize()
    {
        foreach (var density in Enum.GetValues<ProjectBriefingNarrativeDensity>())
        {
            var bullet = ProjectBriefingNarrativeTypography.ResolveCapabilityBlock(ProjectBriefingCapabilityBlockType.Bullet, false, density);
            Assert.True(bullet.FontSize >= 14.0, $"{density} bullets are {bullet.FontSize} pt");
            Assert.True(bullet.LineHeight >= bullet.FontSize * 1.2 / 72, $"{density} line height is shorter than the renderer's line spacing");
        }

        // Larger type fits fewer characters on a line of the same width.
        Assert.True(
            ProjectBriefingNarrativeTypography.AdjustCapabilityCharactersPerLine(72, 16.0)
            < ProjectBriefingNarrativeTypography.AdjustCapabilityCharactersPerLine(72, 12.7));
    }

    [Fact]
    public void NarrativeTypography_UsesControlledProfilesForProjectBriefs()
    {
        var sparse = ProjectBriefingNarrativeTypography.ResolveProjectBrief(
            "A concise project brief describing purpose, employment and expected operational effect.");
        var standard = ProjectBriefingNarrativeTypography.ResolveProjectBrief(
            string.Join(" ", Enumerable.Repeat("This paragraph provides project context, operational purpose and implementation detail.", 12)));
        var dense = ProjectBriefingNarrativeTypography.ResolveProjectBrief(
            string.Join(" ", Enumerable.Repeat("This longer brief contains detailed context, delivery information, dependencies and outcomes.", 40)));

        Assert.Equal(ProjectBriefingNarrativeDensity.Sparse, sparse.Density);
        Assert.Equal(ProjectBriefingNarrativeDensity.Standard, standard.Density);
        Assert.Equal(ProjectBriefingNarrativeDensity.Dense, dense.Density);
        Assert.True(sparse.BodyFontSize > standard.BodyFontSize);
        Assert.True(standard.BodyFontSize > dense.BodyFontSize);
    }

}
