using ProjectManagement.Services.Publications;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class BrochurePhotoCropTests
{
    [Fact]
    public void CalculateCropRectangle_CentresLandscapeWhenFocalIsCentre()
    {
        var crop = BrochurePhotoService.CalculateCropRectangle(
            sourceWidth: 2400,
            sourceHeight: 1200,
            targetWidth: 1600,
            targetHeight: 900,
            focalX: .5,
            focalY: .5);

        Assert.Equal(2133, crop.Width);
        Assert.Equal(1200, crop.Height);
        Assert.InRange(crop.X, 133, 134);
        Assert.Equal(0, crop.Y);
    }

    [Fact]
    public void CalculateCropRectangle_UsesRightFocalPointWithoutLeavingImageBounds()
    {
        var crop = BrochurePhotoService.CalculateCropRectangle(
            sourceWidth: 2400,
            sourceHeight: 1200,
            targetWidth: 1600,
            targetHeight: 900,
            focalX: 1,
            focalY: .5);

        Assert.Equal(2400 - crop.Width, crop.X);
        Assert.Equal(0, crop.Y);
    }

    [Fact]
    public void CalculateCropRectangle_UsesVerticalFocalPointForPortraitSource()
    {
        var top = BrochurePhotoService.CalculateCropRectangle(1200, 2000, 1600, 900, .5, 0);
        var bottom = BrochurePhotoService.CalculateCropRectangle(1200, 2000, 1600, 900, .5, 1);

        Assert.Equal(1200, top.Width);
        Assert.Equal(675, top.Height);
        Assert.Equal(0, top.Y);
        Assert.Equal(2000 - 675, bottom.Y);
    }

    [Theory]
    [InlineData(-4, 5)]
    [InlineData(double.NaN, double.PositiveInfinity)]
    public void CalculateCropRectangle_ClampsInvalidFocalValues(double focalX, double focalY)
    {
        var crop = BrochurePhotoService.CalculateCropRectangle(1600, 900, 1600, 900, focalX, focalY);

        Assert.Equal(0, crop.X);
        Assert.Equal(0, crop.Y);
        Assert.Equal(1600, crop.Width);
        Assert.Equal(900, crop.Height);
    }

    [Fact]
    public void EffectiveCropDimensions_UsesCoverBAspectIndependentlyFromProjectCardAspect()
    {
        var cover = BrochurePhotoService.EffectiveCropDimensions(2400, 1600, 1800d / 1100d);
        var card = BrochurePhotoService.EffectiveWideCropDimensions(2400, 1600);

        Assert.True(cover.Height > card.Height);
        Assert.Equal(2400d, cover.Width);
        Assert.InRange(cover.Height, 1466d, 1467d);
    }

    [Theory]
    [InlineData(2400, 1350, BrochurePhotoQuality.Excellent)]
    [InlineData(1600, 900, BrochurePhotoQuality.PrintReady)]
    [InlineData(1200, 675, BrochurePhotoQuality.Acceptable)]
    [InlineData(800, 450, BrochurePhotoQuality.Low)]
    public void DetermineQuality_UsesEffectiveSixteenByNinePublicationCrop(
        int width,
        int height,
        BrochurePhotoQuality expected)
    {
        Assert.Equal(expected, BrochurePhotoService.DetermineQuality(width, height));
    }

    [Fact]
    public void DetermineQuality_DowngradesTallSourceWhenWideCropHasTooFewPixels()
    {
        // A 1600x2400 portrait image contains many pixels overall, but its 16:9
        // publication crop can only use 1600x900 pixels. That is PrintReady, not Excellent.
        Assert.Equal(
            BrochurePhotoQuality.PrintReady,
            BrochurePhotoService.DetermineQuality(1600, 2400));
    }

}
