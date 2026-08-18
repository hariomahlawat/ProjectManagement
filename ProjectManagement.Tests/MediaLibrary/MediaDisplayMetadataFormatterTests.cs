using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Tests.MediaLibrary;

public sealed class MediaDisplayMetadataFormatterTests
{
    [Fact]
    public void Format_RemovesRepeatedVisitTitleFromSecondaryMetadata()
    {
        var result = MediaDisplayMetadataFormatter.Format(
            MediaAssetOrigin.VisitPhoto,
            "Visit of Test Visit",
            null,
            null,
            "VISIT OF Test Visit",
            "Civil Dignitaries");

        Assert.Equal("Visit of Test Visit", result.DisplayTitle);
        Assert.Equal("Civil Dignitaries", result.DisplayContext);
        Assert.Null(result.DisplaySubtitle);
    }

    [Fact]
    public void Format_PrefersEditorialCaptionWithoutChangingContextProvenance()
    {
        var result = MediaDisplayMetadataFormatter.Format(
            MediaAssetOrigin.ProjectPhoto,
            "IMG_001",
            "Source caption",
            "Capability demonstration",
            "Project A",
            "Project media");

        Assert.Equal("Capability demonstration", result.DisplayTitle);
        Assert.Equal("Project A", result.DisplayContext);
        Assert.Equal("Capability demonstration", result.EffectiveCaption);
    }
}
