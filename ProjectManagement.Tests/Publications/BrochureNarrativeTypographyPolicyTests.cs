using ProjectManagement.Services.Publications;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class BrochureNarrativeTypographyPolicyTests
{
    [Theory]
    [InlineData(BrochureNarrativeSegment.FullWidth)]
    [InlineData(BrochureNarrativeSegment.Leading)]
    [InlineData(BrochureNarrativeSegment.Continuation)]
    [InlineData(BrochureNarrativeSegment.Trailing)]
    public void LeftAlignment_NeverJustifiesProjectNarrative(BrochureNarrativeSegment segment)
    {
        Assert.False(BrochureNarrativeTypographyPolicy.ShouldJustify(
            BrochureNarrativeAlignment.Left,
            segment));
    }

    [Theory]
    [InlineData(BrochureNarrativeSegment.FullWidth)]
    [InlineData(BrochureNarrativeSegment.Leading)]
    [InlineData(BrochureNarrativeSegment.Trailing)]
    public void JustifiedAlignment_JustifiesNormalPublicationSegments(BrochureNarrativeSegment segment)
    {
        Assert.True(BrochureNarrativeTypographyPolicy.ShouldJustify(
            BrochureNarrativeAlignment.Justified,
            segment));
    }

    [Fact]
    public void JustifiedAlignment_LeavesForcedContinuationRaggedRight()
    {
        Assert.False(BrochureNarrativeTypographyPolicy.ShouldJustify(
            BrochureNarrativeAlignment.Justified,
            BrochureNarrativeSegment.Continuation));
    }

    [Fact]
    public void InvalidAlignment_FallsBackToLeft()
    {
        var invalid = (BrochureNarrativeAlignment)999;

        Assert.Equal(
            BrochureNarrativeAlignment.Left,
            BrochureNarrativeTypographyPolicy.Normalize(invalid));
    }
}
