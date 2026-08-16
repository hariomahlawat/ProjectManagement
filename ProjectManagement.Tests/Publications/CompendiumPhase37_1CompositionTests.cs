using ProjectManagement.Services.Compendiums;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class CompendiumPhase37_1CompositionTests
{
    [Fact]
    public void FitGeometry_UsesActualAspectRatioHeightInsteadOfInvisibleFrameHeight()
    {
        var geometry = CompendiumDossierImageGeometryPolicy.Resolve(
            frameWidthPoints: 283f,
            maximumHeightPoints: 246f,
            sourceWidth: 1600,
            sourceHeight: 600,
            fitMode: CompendiumImageFitMode.Fit);

        Assert.True(geometry.HasKnownSourceGeometry);
        Assert.InRange(geometry.RenderedHeightPoints, 105f, 107f);
        Assert.InRange(geometry.RenderedWidthPoints, 282f, 284f);
    }

    [Fact]
    public void FillGeometry_RetainsRequestedFrameHeight()
    {
        var geometry = CompendiumDossierImageGeometryPolicy.Resolve(
            frameWidthPoints: 283f,
            maximumHeightPoints: 246f,
            sourceWidth: 1600,
            sourceHeight: 600,
            fitMode: CompendiumImageFitMode.Fill);

        Assert.Equal(246f, geometry.RenderedHeightPoints);
        Assert.Equal(283f, geometry.RenderedWidthPoints);
    }

    [Fact]
    public void PhysicalMeasurement_DistinguishesDifferentGlyphWidthsAtSameCharacterCount()
    {
        var narrow = string.Join(" ", Enumerable.Repeat("iiii", 80));
        var wide = string.Join(" ", Enumerable.Repeat("WWWW", 80));

        var narrowMeasurement = CompendiumDossierTextMeasurementService.Measure(narrow, 223f, 1f);
        var wideMeasurement = CompendiumDossierTextMeasurementService.Measure(wide, 223f, 1f);

        Assert.True(wideMeasurement.LineCount > narrowMeasurement.LineCount);
        Assert.True(wideMeasurement.HeightPoints > narrowMeasurement.HeightPoints);
    }

    [Fact]
    public void FitAwarePagination_ReturnsActualRenderedHeightForWideDiagram()
    {
        var narrative = string.Join(" ", Enumerable.Repeat(
            "A measured dossier paragraph should flow immediately after a wide fitted diagram while preserving publication readability.",
            6));

        var decision = CompendiumDossierPaginationPlanner.Resolve(
            CompendiumDossierLayout.Balanced,
            CompendiumDossierLayout.Balanced,
            availablePhotoCount: 1,
            narrative,
            Array.Empty<string>(),
            programmeModuleCount: 1,
            projectName: "Wide diagram geometry test",
            primaryImageEffectiveDpi: 220,
            balancedTextFlowMode: CompendiumBalancedTextFlowMode.FlowBelowImage,
            primaryImageSourceWidth: 1600,
            primaryImageSourceHeight: 600,
            primaryImageFitMode: CompendiumImageFitMode.Fit);

        Assert.True(decision.PrimaryImageHeightPoints < 150f);
    }

    [Theory]
    [InlineData("Stn. HQ remains operational. The simulator continues." )]
    [InlineData("Inf. Wpn. training remains available. The next sentence follows." )]
    [InlineData("No. 5 system remains ready. The next sentence follows." )]
    public void MilitaryAbbreviations_DoNotCreateFalseSentenceBreakPressure(string narrative)
    {
        var assessment = CompendiumDossierNarrativeFlowPlanner.AssessSideFlow(
            narrative,
            imageHeightPoints: 180f,
            narrativeFontScale: 1f,
            sideColumnWidthPoints: 223f);

        Assert.DoesNotContain("Stn.\n\n", assessment.SideSegment);
        Assert.DoesNotContain("Wpn.\n\n", assessment.SideSegment);
        Assert.DoesNotContain("No.\n\n", assessment.SideSegment);
    }

    [Fact]
    public void NarrativeScale_UsesOneSharedMaximum()
    {
        Assert.Equal(1.10f, CompendiumNarrativeTypographyPolicy.MaximumScale);
        Assert.Equal(
            CompendiumNarrativeTypographyPolicy.MaximumScale,
            CompendiumNarrativeTypographyPolicy.NormalizeScale(5f));
    }
}
