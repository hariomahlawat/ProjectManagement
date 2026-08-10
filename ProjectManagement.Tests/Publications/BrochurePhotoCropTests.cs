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
}
