using ProjectManagement.Services.Compendiums;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class CompendiumPhase45FlowParityTests
{
    [Theory]
    [InlineData(18f, 36)]
    [InlineData(30f, 18)]
    [InlineData(40f, 3)]
    public void FlowBelowGapScore_PrefersSmallSemanticResiduals(float gap, int minimumExpected)
    {
        Assert.True(CompendiumDossierEditorialPolicy.FlowBelowGapScore(gap) >= minimumExpected);
    }

    [Fact]
    public void FlowBelowGapScore_StronglyPenalisesEditoriallyExcessiveGap()
    {
        var preferred = CompendiumDossierEditorialPolicy.FlowBelowGapScore(
            CompendiumDossierEditorialPolicy.PreferredFlowBelowGapPoints);
        var excessive = CompendiumDossierEditorialPolicy.FlowBelowGapScore(
            CompendiumDossierEditorialPolicy.MaximumFlowBelowGapPoints + 20f);

        Assert.True(preferred > 0);
        Assert.True(excessive < -100);
        Assert.True(preferred - excessive > 120);
    }

    [Fact]
    public void FitGeometry_ReportsActualOccupiedHeightRatherThanMaximumFrameHeight()
    {
        var geometry = CompendiumDossierImageGeometryPolicy.Resolve(
            frameWidthPoints: 283f,
            maximumHeightPoints: 300f,
            sourceWidth: 2400,
            sourceHeight: 800,
            CompendiumImageFitMode.Fit);

        Assert.True(geometry.RenderedHeightPoints < geometry.MaximumHeightPoints);
        Assert.InRange(Math.Abs(geometry.RenderedHeightPoints - (283f / 3f)), 0f, .01f);
    }
}
