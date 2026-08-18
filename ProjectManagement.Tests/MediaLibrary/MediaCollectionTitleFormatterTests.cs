using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Tests.MediaLibrary;

public sealed class MediaCollectionTitleFormatterTests
{
    [Theory]
    [InlineData("Lt Gen Praveen", "Visit of Lt Gen Praveen")]
    [InlineData("VISIT OF LT GEN PRAVEEN", "Visit of LT GEN PRAVEEN")]
    [InlineData("Visit of VISIT OF Lt Gen Praveen", "Visit of Lt Gen Praveen")]
    [InlineData("visit of visit of Lt Gen Praveen", "Visit of Lt Gen Praveen")]
    public void FormatVisitTitle_NormalisesMechanicalVisitPrefix(string input, string expected)
    {
        Assert.Equal(expected, MediaCollectionTitleFormatter.FormatVisitTitle(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Visit of")]
    [InlineData("VISIT OF VISIT OF")]
    public void FormatVisitTitle_UsesStableFallbackWhenNoSubjectRemains(string? input)
    {
        Assert.Equal("Visit", MediaCollectionTitleFormatter.FormatVisitTitle(input));
    }

    [Fact]
    public void FormatCollectionTitle_DoesNotRewriteNonVisitCollection()
    {
        Assert.Equal(
            "SPRINT",
            MediaCollectionTitleFormatter.FormatCollectionTitle(MediaAssetOrigin.ProjectPhoto, "  SPRINT  "));
    }
}
