using ProjectManagement.Models;
using ProjectManagement.Services.Compendiums;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class CompendiumPhase37_6ProductionHardeningTests
{
    [Fact]
    public void NarrativeParser_RecognizesOnlyControlledPublicationBlocks()
    {
        var document = CompendiumNarrativeParser.Parse(
            "Opening **summary** with *emphasis*.\n\n### Deliverable and Impact\n\n- First capability\n- Second capability\n\nClosing paragraph.");

        Assert.Equal(4, document.Blocks.Count);
        Assert.Equal(CompendiumNarrativeBlockKind.Paragraph, document.Blocks[0].Kind);
        Assert.Equal(CompendiumNarrativeBlockKind.MinorHeading, document.Blocks[1].Kind);
        Assert.Equal("Deliverable and Impact", document.Blocks[1].Markdown);
        Assert.Equal(CompendiumNarrativeBlockKind.BulletList, document.Blocks[2].Kind);
        Assert.Equal(new[] { "First capability", "Second capability" }, document.Blocks[2].Items);
        Assert.Equal(CompendiumNarrativeBlockKind.Paragraph, document.Blocks[3].Kind);
    }

    [Fact]
    public void AdditionalNoteParser_DoesNotPromoteMinorHeadings()
    {
        var document = CompendiumNarrativeParser.Parse(
            "### This remains note text\n\n- Hardware will be procured by the unit.\n- SDD will provide installation and training.",
            allowMinorHeadings: false);

        Assert.DoesNotContain(document.Blocks, block => block.Kind == CompendiumNarrativeBlockKind.MinorHeading);
        Assert.Equal(CompendiumNarrativeBlockKind.Paragraph, document.Blocks[0].Kind);
        Assert.Equal("This remains note text", document.Blocks[0].Markdown);
        Assert.Equal(CompendiumNarrativeBlockKind.BulletList, document.Blocks[1].Kind);
    }

    [Fact]
    public void InlinePlainText_PreservesTechnicalUnderscoresWhileRemovingSupportedEmphasisMarkers()
    {
        var clean = CompendiumNarrativeParser.CleanInline(
            "MU_UGV uses **rugged hardware**, *offline processing* and [local tools](https://example.invalid). ");

        Assert.Contains("MU_UGV", clean, StringComparison.Ordinal);
        Assert.Contains("rugged hardware", clean, StringComparison.Ordinal);
        Assert.Contains("offline processing", clean, StringComparison.Ordinal);
        Assert.Contains("local tools", clean, StringComparison.Ordinal);
        Assert.DoesNotContain("**", clean, StringComparison.Ordinal);
    }

    [Fact]
    public void SemanticMeasurement_AccountsForMinorHeadingGeometry()
    {
        var semantic = CompendiumDossierTextMeasurementService.Measure(
            "### Deliverable and Impact\n\nA concise paragraph follows the heading.",
            widthPoints: 360f);
        var plain = CompendiumDossierTextMeasurementService.Measure(
            "Deliverable and Impact\n\nA concise paragraph follows the heading.",
            widthPoints: 360f);

        Assert.True(semantic.HeightPoints > plain.HeightPoints);
    }

    [Fact]
    public void PhysicalContinuation_DoesNotOrphanMinorHeadingAtEndOfIntermediatePage()
    {
        var source = string.Join("\n\n", new[]
        {
            string.Join(" ", Enumerable.Repeat("Opening project brief sentence with measured publication content.", 8)),
            "### Deliverable and Impact",
            "The field deliverable remains a complete sentence with enough detail to accompany its heading. A second sentence follows for continuation testing.",
            "### Operational Employment",
            "The capability is intended for representative operational employment and controlled training."
        });

        var pages = CompendiumDossierNarrativeFlowPlanner.SplitForPhysicalPages(
            source,
            widthPoints: 300f,
            pageHeightPoints: 105f,
            narrativeFontScale: 1f,
            includeHeading: false,
            allowMinorHeadings: true);

        Assert.True(pages.Count > 1);
        foreach (var page in pages.Take(pages.Count - 1))
        {
            var blocks = CompendiumNarrativeParser.Parse(page).Blocks;
            Assert.True(blocks.Count > 0);
            Assert.NotEqual(CompendiumNarrativeBlockKind.MinorHeading, blocks[^1].Kind);
        }
    }

    [Fact]
    public void LongAdditionalNote_PaginatesWithBulletsButNeverCreatesMinorHeadingBlocks()
    {
        var note = string.Join("\n\n", Enumerable.Range(1, 14).Select(index =>
            $"Publication note paragraph {index}. Software integration, installation, testing and user training remain part of the closing note."))
            + "\n\n- Unit will procure the requisite hardware.\n- SDD will provide software installation and user training.";

        var pages = CompendiumDossierNarrativeFlowPlanner.SplitForPhysicalPages(
            note,
            widthPoints: 360f,
            pageHeightPoints: 110f,
            narrativeFontScale: 1f,
            includeHeading: false,
            allowMinorHeadings: false);

        Assert.True(pages.Count > 1);
        Assert.All(pages, page => Assert.DoesNotContain(
            CompendiumNarrativeParser.Parse(page, allowMinorHeadings: false).Blocks,
            block => block.Kind == CompendiumNarrativeBlockKind.MinorHeading));
    }

    [Theory]
    [InlineData("Lorem ipsum dolor sit amet, consectetur adipiscing elit.")]
    [InlineData("TBD")]
    [InlineData("To be updated")]
    [InlineData("This is sample description text for a temporary record.")]
    public void ReadinessPolicy_FlagsConservativePlaceholderNarrative(string narrative)
    {
        var assessment = new CompendiumReadinessPolicy().Evaluate(CreateReadinessContext(narrative));

        Assert.Contains(assessment.Findings, finding => finding.Code == "placeholderNarrative");
    }

    [Fact]
    public void ReadinessPolicy_DoesNotFlagOrdinaryTechnicalUseOfTestOrUpdateWords()
    {
        var narrative = "The prototype completed environmental test activity and the software update improved offline mission planning performance.";
        var assessment = new CompendiumReadinessPolicy().Evaluate(CreateReadinessContext(narrative));

        Assert.DoesNotContain(assessment.Findings, finding => finding.Code == "placeholderNarrative");
    }

    private static CompendiumProjectReadinessContext CreateReadinessContext(string narrative)
        => new(
            ProjectId: 376,
            ProjectName: "Phase 37.6 readiness fixture",
            LifecycleStatus: ProjectLifecycleStatus.Active,
            CompletionYear: null,
            SponsoringLineDirectorate: "Inf",
            Description: narrative,
            ProliferationCostLakhs: null,
            ProliferationAvailability: null,
            ResolvedPhotoId: null,
            ResolvedPhotoUsable: false,
            ImageSelectionMode: CompendiumImageSelectionMode.Automatic,
            EffectiveDpi: null,
            ExplicitPhotoUnavailable: false,
            CurrentReviewFingerprint: "phase376-current",
            SubmittedReviewFingerprint: "phase376-current")
        {
            NarrativeLabel = "Project Brief"
        };
}
