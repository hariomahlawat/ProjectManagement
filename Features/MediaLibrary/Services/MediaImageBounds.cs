namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Guards image decoding against dimensions that would exhaust server memory. Callers read
/// the dimensions from the image header first, so the check runs before any full-size
/// pixel buffer is allocated.
/// </summary>
public static class MediaImageBounds
{
    public static void EnsureWithinPixelLimit(long width, long height, long maximumPixels)
    {
        if (width <= 0 || height <= 0)
        {
            throw new MediaProcessingPermanentException("The image has invalid dimensions.");
        }

        if (width * height > maximumPixels)
        {
            throw new MediaProcessingPermanentException(
                $"The image is {width:N0} x {height:N0} pixels, which exceeds the configured processing limit of {maximumPixels:N0} pixels.");
        }
    }
}
