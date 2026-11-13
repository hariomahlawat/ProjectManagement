using System;
using System.Linq;
using ProjectManagement.Services.Imaging;
using SkiaSharp;
using Xunit;

namespace ProjectManagement.Tests.Imaging;

public sealed class PrismImageCodecTests
{
    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg, PrismImageFormat.Jpeg, "image/jpeg", ".jpg")]
    [InlineData(SKEncodedImageFormat.Png, PrismImageFormat.Png, "image/png", ".png")]
    [InlineData(SKEncodedImageFormat.Webp, PrismImageFormat.Webp, "image/webp", ".webp")]
    public void Identify_reads_format_and_size_from_the_header(
        SKEncodedImageFormat encoded,
        PrismImageFormat expected,
        string mimeType,
        string extension)
    {
        var bytes = Encode(TwoHalves(60, 40, SKColors.Red, SKColors.Blue), encoded);

        var info = PrismImageCodec.Identify(bytes);

        Assert.NotNull(info);
        Assert.Equal(expected, info!.Format);
        Assert.Equal(mimeType, info.MimeType);
        Assert.Equal(extension, info.FileExtension);
        Assert.Equal((60, 40), (info.Width, info.Height));
        Assert.Equal((60, 40), (info.OrientedWidth, info.OrientedHeight));
    }

    [Fact]
    public void Identify_returns_null_for_bytes_that_are_not_an_image()
    {
        Assert.Null(PrismImageCodec.Identify(ReadOnlyMemory<byte>.Empty));
        Assert.Null(PrismImageCodec.Identify(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }));
        Assert.Null(PrismImageCodec.Identify("%PDF-1.7 not an image at all"u8.ToArray()));
    }

    [Fact]
    public void Identify_reports_the_upright_size_of_a_rotated_photo()
    {
        var bytes = WithExifOrientation(
            Encode(TwoHalves(60, 40, SKColors.Red, SKColors.Blue), SKEncodedImageFormat.Jpeg),
            orientation: 6);

        var info = PrismImageCodec.Identify(bytes);

        Assert.NotNull(info);
        Assert.Equal((60, 40), (info!.Width, info.Height));
        Assert.Equal((40, 60), (info.OrientedWidth, info.OrientedHeight));
    }

    [Fact]
    public void DecodeOriented_turns_a_rotated_photo_upright()
    {
        // Stored 60 x 40 with red on the left. Orientation 6 says the stored left edge is
        // the top of the picture, so upright it is 40 x 60 with red at the top.
        var bytes = WithExifOrientation(
            Encode(TwoHalves(60, 40, SKColors.Red, SKColors.Blue), SKEncodedImageFormat.Jpeg),
            orientation: 6);

        using var upright = PrismImageCodec.DecodeOriented(bytes);

        Assert.Equal((40, 60), (upright.Width, upright.Height));
        AssertMostly(upright.GetPixel(20, 8), red: true);
        AssertMostly(upright.GetPixel(20, 52), red: false);
    }

    [Fact]
    public void DecodeOriented_leaves_an_upright_photo_as_it_is()
    {
        var bytes = Encode(TwoHalves(60, 40, SKColors.Red, SKColors.Blue), SKEncodedImageFormat.Png);

        using var decoded = PrismImageCodec.DecodeOriented(bytes);

        Assert.Equal((60, 40), (decoded.Width, decoded.Height));
        AssertMostly(decoded.GetPixel(8, 20), red: true);
        AssertMostly(decoded.GetPixel(52, 20), red: false);
    }

    [Fact]
    public void DecodeOriented_refuses_an_image_over_the_pixel_limit_before_decoding()
    {
        var bytes = Encode(TwoHalves(60, 40, SKColors.Red, SKColors.Blue), SKEncodedImageFormat.Png);

        var error = Assert.Throws<PrismImageException>(
            () => PrismImageCodec.DecodeOriented(bytes, maximumPixels: 60 * 40 - 1));

        Assert.Contains("limit", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DecodeOriented_refuses_bytes_that_are_not_an_image()
    {
        Assert.Throws<PrismImageException>(() => PrismImageCodec.DecodeOriented(ReadOnlyMemory<byte>.Empty));
        Assert.Throws<PrismImageException>(
            () => PrismImageCodec.DecodeOriented("definitely not an image"u8.ToArray()));
    }

    [Theory]
    [InlineData(400, 200, 100, 100, 100, 50)]
    [InlineData(200, 400, 100, 100, 50, 100)]
    [InlineData(400, 300, 200, 150, 200, 150)]
    [InlineData(50, 25, 100, 100, 100, 50)]
    [InlineData(1000, 3, 100, 100, 100, 1)]
    public void ResizeToFit_keeps_proportions_inside_the_box(
        int width,
        int height,
        int boxWidth,
        int boxHeight,
        int expectedWidth,
        int expectedHeight)
    {
        using var source = TwoHalves(width, height, SKColors.Red, SKColors.Blue);

        using var resized = PrismImageCodec.ResizeToFit(source, boxWidth, boxHeight);

        Assert.Equal((expectedWidth, expectedHeight), (resized.Width, resized.Height));
    }

    [Fact]
    public void ResizeToFit_returns_a_separate_copy_when_no_resize_is_needed()
    {
        using var source = TwoHalves(80, 40, SKColors.Red, SKColors.Blue);

        using var resized = PrismImageCodec.ResizeToFit(source, 80, 40);

        Assert.NotSame(source, resized);
        Assert.Equal((80, 40), (resized.Width, resized.Height));
    }

    [Fact]
    public void EncodeJpeg_writes_a_jpeg_of_the_same_size()
    {
        using var source = TwoHalves(64, 48, SKColors.Red, SKColors.Blue);

        var jpeg = PrismImageCodec.EncodeJpeg(source, 85);

        var info = PrismImageCodec.Identify(jpeg);
        Assert.NotNull(info);
        Assert.Equal(PrismImageFormat.Jpeg, info!.Format);
        Assert.Equal((64, 48), (info.Width, info.Height));
    }

    [Fact]
    public void EncodeJpeg_lays_a_transparent_image_on_white()
    {
        using var source = new SKBitmap(new SKImageInfo(32, 32, SKImageInfo.PlatformColorType, SKAlphaType.Premul));
        source.Erase(SKColors.Transparent);

        var jpeg = PrismImageCodec.EncodeJpeg(source, 90);

        using var decoded = PrismImageCodec.DecodeOriented(jpeg);
        var pixel = decoded.GetPixel(16, 16);
        Assert.True(pixel.Red > 240 && pixel.Green > 240 && pixel.Blue > 240, $"Expected white, got {pixel}.");
    }

    [Fact]
    public void EncodeJpeg_makes_a_smaller_file_at_lower_quality()
    {
        using var source = Noise(256, 256);

        var high = PrismImageCodec.EncodeJpeg(source, 95);
        var low = PrismImageCodec.EncodeJpeg(source, 40);

        Assert.True(low.Length < high.Length, $"Quality 40 gave {low.Length} bytes, quality 95 gave {high.Length}.");
    }

    private static void AssertMostly(SKColor pixel, bool red)
    {
        if (red)
        {
            Assert.True(pixel.Red > 180 && pixel.Blue < 80, $"Expected red, got {pixel}.");
        }
        else
        {
            Assert.True(pixel.Blue > 180 && pixel.Red < 80, $"Expected blue, got {pixel}.");
        }
    }

    private static SKBitmap TwoHalves(int width, int height, SKColor left, SKColor right)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKImageInfo.PlatformColorType, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(right);
        using var paint = new SKPaint { Color = left, IsAntialias = false };
        canvas.DrawRect(new SKRect(0, 0, width / 2f, height), paint);
        canvas.Flush();
        return bitmap;
    }

    private static SKBitmap Noise(int width, int height)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKImageInfo.PlatformColorType, SKAlphaType.Opaque));
        var random = new Random(1234);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)));
            }
        }

        return bitmap;
    }

    private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format)
    {
        using (bitmap)
        using (var image = SKImage.FromBitmap(bitmap))
        using (var data = image.Encode(format, 95))
        {
            return data.ToArray();
        }
    }

    /// <summary>Inserts a minimal EXIF block, carrying only the orientation tag, after the JPEG start marker.</summary>
    private static byte[] WithExifOrientation(byte[] jpeg, ushort orientation)
    {
        Assert.True(jpeg.Length > 2 && jpeg[0] == 0xFF && jpeg[1] == 0xD8, "Expected a JPEG.");

        byte[] tiff =
        {
            0x49, 0x49, 0x2A, 0x00,             // little-endian TIFF header
            0x08, 0x00, 0x00, 0x00,             // first directory at offset 8
            0x01, 0x00,                         // one entry
            0x12, 0x01,                         // tag 0x0112: orientation
            0x03, 0x00,                         // type SHORT
            0x01, 0x00, 0x00, 0x00,             // one value
            (byte)(orientation & 0xFF), (byte)(orientation >> 8), 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00              // no further directory
        };
        byte[] exifHeader = { 0x45, 0x78, 0x69, 0x66, 0x00, 0x00 };
        var length = 2 + exifHeader.Length + tiff.Length;
        byte[] marker = { 0xFF, 0xE1, (byte)(length >> 8), (byte)(length & 0xFF) };

        return jpeg.Take(2)
            .Concat(marker)
            .Concat(exifHeader)
            .Concat(tiff)
            .Concat(jpeg.Skip(2))
            .ToArray();
    }
}
