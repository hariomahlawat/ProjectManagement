using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ProjectManagement.Tests;

internal static class TestImageFactory
{
    public static byte[] TinyPng()
    {
        using var image = new Image<Rgba32>(1, 1, new Rgba32(255, 255, 255));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }
}
