using ProjectManagement.Services.Compendiums;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class CompendiumPhase27ContractTests
{
    [Fact]
    public void NarrativeLineEstimator_RespondsToVisualPressureRatherThanRawTextOnly()
    {
        var shortText = "Compact project brief.";
        var mediumText = string.Join(' ', Enumerable.Repeat("Operational capability statement with representative detail.", 18));
        var longText = string.Join('\n', Enumerable.Range(1, 30).Select(index => $"{index}. Capability statement with enough detail to consume a rendered publication line."));

        Assert.True(CompendiumPublicationImagePolicy.EstimateNarrativeLines(shortText) <
                    CompendiumPublicationImagePolicy.EstimateNarrativeLines(mediumText));
        Assert.True(CompendiumPublicationImagePolicy.EstimateNarrativeLines(mediumText) <
                    CompendiumPublicationImagePolicy.EstimateNarrativeLines(longText));

        Assert.Equal(CompendiumPublicationImagePolicy.ShortFrameHeightPoints,
            CompendiumPublicationImagePolicy.ResolveFrameHeightPoints(shortText));
        Assert.Equal(CompendiumPublicationImagePolicy.LongFrameHeightPoints,
            CompendiumPublicationImagePolicy.ResolveFrameHeightPoints(longText));
    }

    [Fact]
    public void NarrativeLineEstimator_GivesListItemsTheirOwnEditorialWeight()
    {
        var prose = "Alpha beta gamma delta epsilon.";
        var list = "1. Alpha beta gamma delta epsilon.\n2. Alpha beta gamma delta epsilon.\n3. Alpha beta gamma delta epsilon.";

        Assert.True(CompendiumPublicationImagePolicy.EstimateNarrativeLines(list) >
                    CompendiumPublicationImagePolicy.EstimateNarrativeLines(prose));
    }
}
