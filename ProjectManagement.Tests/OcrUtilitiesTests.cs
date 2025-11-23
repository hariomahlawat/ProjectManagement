using ProjectManagement.Services.Ocr;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class OcrUtilitiesTests
{
    [Fact]
    public void CleanBanners_RemovesSkipNotices()
    {
        var input = "\ufeff[OCR skipped on page 1]\nPrior OCR detected\nReal text";

        var cleaned = OcrTextUtilities.CleanBanners(input);

        Assert.Equal("Real text", cleaned);
    }

    [Fact]
    public void HasUsefulText_ReturnsTrue_WhenContentFollowsBanner()
    {
        var input = "[OCR skipped on page 1-2]\nThis document already had text.";

        Assert.True(OcrTextUtilities.HasUsefulText(input));
    }
}
