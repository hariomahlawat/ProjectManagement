using ProjectManagement.Services.Compendiums;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class CompendiumPhase36CompositionTests
{
    [Fact]
    public void PortfolioQuartet_RequiresFourDistinctFillOnlySlots()
    {
        var slots = CompendiumCoverTemplatePolicy.ResolveSlots(
                CompendiumFrontCoverTemplate.PortfolioQuartet,
                CompendiumBackCoverTemplate.MinimalInstitutional)
            .Where(item => item.Surface == CompendiumCoverSurface.Front)
            .ToArray();

        Assert.Equal(4, slots.Length);
        Assert.All(slots, item => Assert.True(item.Required));
        Assert.All(slots, item => Assert.True(item.FillOnly));
        Assert.Equal(new[] { "Hero", "Secondary1", "Secondary2", "Secondary3" }, slots.Select(item => item.SlotKey));
        Assert.Equal(4, CompendiumCoverTemplatePolicy.MinimumDistinctImages(CompendiumFrontCoverTemplate.PortfolioQuartet));
        Assert.Equal(
            CompendiumImageFitMode.Fill,
            CompendiumCoverTemplatePolicy.NormalizeFitMode(
                CompendiumCoverSurface.Front,
                CompendiumFrontCoverTemplate.PortfolioQuartet,
                CompendiumImageFitMode.Fit));
    }

    [Fact]
    public void ExistingMosaics_KeepSupportingSlotsOptionalForAdaptiveFallback()
    {
        var split = CompendiumCoverTemplatePolicy.ResolveSlots(
                CompendiumFrontCoverTemplate.EditorialSplit,
                CompendiumBackCoverTemplate.MinimalInstitutional)
            .Where(item => item.Surface == CompendiumCoverSurface.Front)
            .ToArray();
        var triptych = CompendiumCoverTemplatePolicy.ResolveSlots(
                CompendiumFrontCoverTemplate.Triptych,
                CompendiumBackCoverTemplate.MinimalInstitutional)
            .Where(item => item.Surface == CompendiumCoverSurface.Front)
            .ToArray();

        Assert.Equal(2, split.Length);
        Assert.True(split[0].Required);
        Assert.False(split[1].Required);
        Assert.Equal(3, triptych.Length);
        Assert.True(triptych[0].Required);
        Assert.All(triptych.Skip(1), item => Assert.False(item.Required));
    }

    [Fact]
    public void FlowBelowImage_PrefersParagraphThenSentenceBoundaries()
    {
        var firstParagraph = "First sentence remains intact. Second sentence also remains intact. Third sentence completes the opening paragraph.";
        var secondParagraph = "This second paragraph must remain available for the full-width region below the image. It must never be cut in the middle of a word.";
        var narrative = firstParagraph + "\n\n" + secondParagraph;

        var plan = CompendiumDossierNarrativeFlowPlanner.Resolve(
            narrative,
            CompendiumBalancedTextFlowMode.FlowBelowImage,
            CompendiumDossierLayout.Balanced,
            hasPrimaryImage: true,
            primaryImageHeightPoints: 145f,
            narrativeFontScale: 1f,
            firstPageNarrativeBudget: 3000);

        Assert.NotEmpty(plan.SideSegment);
        Assert.EndsWith(".", plan.SideSegment);
        Assert.NotEmpty(plan.BelowImageSegment);
        Assert.Contains("second paragraph", plan.BelowImageSegment, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("paragrap\n", plan.BelowImageSegment, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NaturalSplitter_NeverSlicesAnOversizedSingleSentence()
    {
        var sentence = "A" + new string('x', 1800) + ".";
        var chunks = CompendiumDossierNarrativeFlowPlanner.SplitNatural(sentence, 400, 500);

        Assert.Single(chunks);
        Assert.Equal(sentence, chunks[0]);
    }

    [Fact]
    public void SideColumn_RetainsLegacyRigidFirstPageFlow()
    {
        var narrative = string.Join(" ", Enumerable.Repeat(
            "The simulator provides a repeatable training capability with controlled assessment.",
            30));

        var plan = CompendiumDossierNarrativeFlowPlanner.Resolve(
            narrative,
            CompendiumBalancedTextFlowMode.SideColumn,
            CompendiumDossierLayout.Balanced,
            hasPrimaryImage: true,
            primaryImageHeightPoints: 246f,
            narrativeFontScale: 1f,
            firstPageNarrativeBudget: 1500);

        Assert.NotEmpty(plan.SideSegment);
        Assert.Empty(plan.BelowImageSegment);
    }

    [Fact]
    public void LowResolutionPhotography_IsNotExpandedBeyondPreferredFrame()
    {
        var narrative = string.Join(" ", Enumerable.Repeat(
            "Compact narrative leaves substantial residual page space for editorial balancing.",
            4));
        var preferred = CompendiumDossierPaginationPlanner.PreferredImageHeight(CompendiumDossierLayout.VisualHero, 1);

        var decision = CompendiumDossierPaginationPlanner.Resolve(
            CompendiumDossierLayout.VisualHero,
            CompendiumDossierLayout.VisualHero,
            1,
            narrative,
            Array.Empty<string>(),
            0,
            "Image fidelity test",
            primaryImageEffectiveDpi: 120);

        Assert.True(decision.PrimaryImageHeightPoints <= preferred + .1f);
    }
}
