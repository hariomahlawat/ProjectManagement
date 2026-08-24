using ProjectManagement.Services.Compendiums;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class CompendiumPhase44SemanticJustificationTests
{
    [Theory]
    [InlineData(120f)]
    [InlineData(223f)]
    [InlineData(300f)]
    public void JustifiedPublisherChoice_IsHonouredAcrossBalancedSideWidths(float sideWidth)
    {
        Assert.Equal(
            CompendiumNarrativeAlignment.Justified,
            CompendiumNarrativeTypographyPolicy.ResolveSideAlignment(
                CompendiumNarrativeAlignment.Justified,
                sideWidth));
    }

    [Fact]
    public void LeftPublisherChoice_RemainsLeftAcrossEveryNarrativeRegion()
    {
        Assert.Equal(
            CompendiumNarrativeAlignment.Left,
            CompendiumNarrativeTypographyPolicy.ResolveAlignment(
                CompendiumNarrativeAlignment.Left,
                CompendiumNarrativeSegment.BalancedSide));
        Assert.Equal(
            CompendiumNarrativeAlignment.Left,
            CompendiumNarrativeTypographyPolicy.ResolveAlignment(
                CompendiumNarrativeAlignment.Left,
                CompendiumNarrativeSegment.BelowImage));
        Assert.Equal(
            CompendiumNarrativeAlignment.Left,
            CompendiumNarrativeTypographyPolicy.ResolveAlignment(
                CompendiumNarrativeAlignment.Left,
                CompendiumNarrativeSegment.Continuation));
    }

    [Fact]
    public void BalancedFlow_UsesSameSemanticSplitForLeftAndJustified()
    {
        var narrative = string.Join(" ", Enumerable.Repeat(
            "The simulator provides realistic and repeatable training while preserving equipment availability and allowing instructors to assess performance under controlled operational conditions.",
            12));

        var left = CompendiumDossierNarrativeFlowPlanner.Resolve(
            narrative,
            CompendiumBalancedTextFlowMode.FlowBelowImage,
            CompendiumDossierLayout.Balanced,
            hasPrimaryImage: true,
            primaryImageHeightPoints: 270f,
            narrativeFontScale: 1f,
            firstPageNarrativeBudget: 2600,
            narrativeAlignment: CompendiumNarrativeAlignment.Left,
            sideColumnWidthPoints: 223f);

        var justified = CompendiumDossierNarrativeFlowPlanner.Resolve(
            narrative,
            CompendiumBalancedTextFlowMode.FlowBelowImage,
            CompendiumDossierLayout.Balanced,
            hasPrimaryImage: true,
            primaryImageHeightPoints: 270f,
            narrativeFontScale: 1f,
            firstPageNarrativeBudget: 2600,
            narrativeAlignment: CompendiumNarrativeAlignment.Justified,
            sideColumnWidthPoints: 223f);

        Assert.Equal(left.SideSegment, justified.SideSegment);
        Assert.Equal(left.BelowImageSegment, justified.BelowImageSegment);
        Assert.Equal(left.ContinuationSegments, justified.ContinuationSegments);
        Assert.Equal(CompendiumNarrativeAlignment.Justified, justified.SideAlignment);
        Assert.Equal(CompendiumNarrativeAlignment.Justified, justified.BelowAlignment);
    }

    [Fact]
    public void SideColumn_HonoursJustifiedPublisherChoice()
    {
        var plan = CompendiumDossierNarrativeFlowPlanner.Resolve(
            "A concise project brief remains readable beside the primary image while the publisher's selected editorial alignment is preserved.",
            CompendiumBalancedTextFlowMode.SideColumn,
            CompendiumDossierLayout.Balanced,
            hasPrimaryImage: true,
            primaryImageHeightPoints: 270f,
            narrativeFontScale: 1f,
            firstPageNarrativeBudget: 1600,
            narrativeAlignment: CompendiumNarrativeAlignment.Justified,
            sideColumnWidthPoints: 223f);

        Assert.Equal(CompendiumNarrativeAlignment.Justified, plan.SideAlignment);
    }
}
