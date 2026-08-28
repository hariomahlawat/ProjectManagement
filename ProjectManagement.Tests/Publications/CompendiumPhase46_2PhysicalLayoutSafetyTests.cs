using ProjectManagement.Services.Compendiums;
using ProjectManagement.Utilities.Reporting;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class CompendiumPhase46_2PhysicalLayoutSafetyTests
{
    [Fact]
    public void ShapingReserve_CoversMaximumScaleBodyLineAndNativeTolerance()
    {
        var maximumBodyLine =
            CompendiumLayoutMetrics.ProjectBodyFontSize
            * CompendiumLayoutMetrics.ProjectBodyMaximumNarrativeScale
            * CompendiumLayoutMetrics.ProjectBodyLineHeightMultiplier;

        Assert.Equal(
            (double)maximumBodyLine,
            (double)CompendiumLayoutMetrics.MaximumProjectBodyLineHeightPoints,
            3);
        Assert.True(CompendiumLayoutMetrics.PhysicalPaginationNativeShapingTolerancePoints >= 2f);
        Assert.Equal(
            (double)(maximumBodyLine + CompendiumLayoutMetrics.PhysicalPaginationNativeShapingTolerancePoints),
            (double)CompendiumLayoutMetrics.PhysicalPaginationReservePoints,
            3);
    }

    [Fact]
    public void ProjectAndContinuationBudgets_ConsumeTheSamePhysicalReserve()
    {
        var expectedProjectHeight =
            CompendiumLayoutMetrics.PageHeightPoints
            - CompendiumLayoutMetrics.TopMarginPoints
            - CompendiumLayoutMetrics.RunningHeaderHeightPoints
            - CompendiumLayoutMetrics.FooterHeightPoints
            - CompendiumLayoutMetrics.ProjectContentTopPaddingPoints
            - CompendiumLayoutMetrics.PhysicalPaginationReservePoints;
        var expectedSecondaryHeight =
            CompendiumLayoutMetrics.PageHeightPoints
            - CompendiumLayoutMetrics.TopMarginPoints
            - CompendiumLayoutMetrics.RunningHeaderHeightPoints
            - CompendiumLayoutMetrics.FooterHeightPoints
            - CompendiumLayoutMetrics.SecondaryContentTopPaddingPoints
            - CompendiumLayoutMetrics.PhysicalPaginationReservePoints;

        Assert.Equal(
            (double)expectedProjectHeight,
            (double)CompendiumLayoutMetrics.ProjectContentHeightPoints,
            3);
        Assert.Equal(
            (double)expectedSecondaryHeight,
            (double)CompendiumLayoutMetrics.SecondaryContentHeightPoints,
            3);
    }

    [Fact]
    public void NarrativeTypography_UsesTheSamePhysicalBodyMetricsAsPagination()
    {
        Assert.Equal(CompendiumLayoutMetrics.ProjectBodyFontSize, CompendiumNarrativeTypographyPolicy.BodyFontSizePoints);
        Assert.Equal(CompendiumLayoutMetrics.ProjectBodyMaximumNarrativeScale, CompendiumNarrativeTypographyPolicy.MaximumScale);
        Assert.Equal(CompendiumLayoutMetrics.ProjectBodyLineHeightMultiplier, CompendiumNarrativeTypographyPolicy.BodyLineHeightMultiplier);
        Assert.Equal("physical-a4-v46.2", CompendiumBuildIdentity.PdfContract);
    }
}
