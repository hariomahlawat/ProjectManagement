using System.Text;
using SkiaSharp;

namespace ProjectManagement.Tests;

/// <summary>
/// Builds the small image files the tests upload. Everything is made with SkiaSharp or
/// written by hand, so the tests need no image library beyond the one PRISM ships.
/// </summary>
internal static class TestImageFactory
{
    public static byte[] TinyPng() => SolidPng(1, 1, SKColors.White);

    public static byte[] SolidPng(int width, int height, SKColor colour)
        => Encode(Solid(width, height, colour), SKEncodedImageFormat.Png, 100);

    public static byte[] SolidJpeg(int width, int height, SKColor colour, int quality = 90)
        => Encode(Solid(width, height, colour), SKEncodedImageFormat.Jpeg, quality);

    public static MemoryStream SolidPngStream(int width, int height, SKColor colour)
        => new(SolidPng(width, height, colour));

    public static MemoryStream SolidJpegStream(int width, int height, SKColor colour, int quality = 90)
        => new(SolidJpeg(width, height, colour, quality));

    /// <summary>Encodes pixels given row by row, top to bottom, as a JPEG.</summary>
    public static byte[] JpegFromPixels(int width, int height, SKColor[] pixels, int quality = 75)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKImageInfo.PlatformColorType, SKAlphaType.Opaque))
        {
            Pixels = pixels
        };
        return Encode(bitmap, SKEncodedImageFormat.Jpeg, quality);
    }

    /// <summary>
    /// A 24-bit uncompressed BMP. SkiaSharp reads BMP but does not write it, and the tests
    /// only need one to prove an unsupported format is refused.
    /// </summary>
    public static byte[] SolidBmp(int width, int height, SKColor colour)
    {
        var rowSize = ((width * 3) + 3) & ~3;
        var pixelBytes = rowSize * height;
        const int headerBytes = 54;

        using var stream = new MemoryStream(headerBytes + pixelBytes);
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(headerBytes + pixelBytes);
        writer.Write(0);
        writer.Write(headerBytes);
        writer.Write(40);
        writer.Write(width);
        writer.Write(height);
        writer.Write((short)1);
        writer.Write((short)24);
        writer.Write(0);
        writer.Write(pixelBytes);
        writer.Write(2835);
        writer.Write(2835);
        writer.Write(0);
        writer.Write(0);

        var row = new byte[rowSize];
        for (var x = 0; x < width; x++)
        {
            row[x * 3] = colour.Blue;
            row[(x * 3) + 1] = colour.Green;
            row[(x * 3) + 2] = colour.Red;
        }

        for (var y = 0; y < height; y++)
        {
            writer.Write(row);
        }

        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>Adds an EXIF block carrying only the orientation tag to a JPEG.</summary>
    public static byte[] WithExifOrientation(byte[] jpeg, ushort orientation)
        => WithExif(jpeg, BuildTiff(
            tag: 0x0112,
            type: 3,
            count: 1,
            inlineValue: new byte[] { (byte)(orientation & 0xFF), (byte)(orientation >> 8), 0, 0 },
            trailingData: Array.Empty<byte>()));

    /// <summary>Adds an EXIF block carrying only an image description to a JPEG.</summary>
    public static byte[] WithExifDescription(byte[] jpeg, string description)
    {
        var text = Encoding.ASCII.GetBytes(description + "\0");

        // The text does not fit in the entry, so the entry holds the offset of the data that
        // follows the directory: 8-byte header + 2-byte count + 12-byte entry + 4-byte link.
        const int dataOffset = 26;
        return WithExif(jpeg, BuildTiff(
            tag: 0x010E,
            type: 2,
            count: text.Length,
            inlineValue: BitConverter.GetBytes(dataOffset),
            trailingData: text));
    }

    /// <summary>
    /// The metadata segments in a JPEG: EXIF or XMP (APP1) and IPTC (APP13). Empty when the
    /// file carries none.
    /// </summary>
    public static IReadOnlyList<string> JpegMetadataSegments(byte[] jpeg)
    {
        var found = new List<string>();
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            throw new InvalidOperationException("Expected a JPEG.");
        }

        var position = 2;
        while (position + 4 <= jpeg.Length && jpeg[position] == 0xFF)
        {
            var marker = jpeg[position + 1];
            if (marker == 0xDA || marker == 0xD9)
            {
                break;
            }

            var length = (jpeg[position + 2] << 8) | jpeg[position + 3];
            if (marker == 0xE1)
            {
                found.Add("APP1");
            }
            else if (marker == 0xED)
            {
                found.Add("APP13");
            }

            position += 2 + length;
        }

        return found;
    }

    private static SKBitmap Solid(int width, int height, SKColor colour)
    {
        var alphaType = colour.Alpha == 255 ? SKAlphaType.Opaque : SKAlphaType.Premul;
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKImageInfo.PlatformColorType, alphaType));
        bitmap.Erase(colour);
        return bitmap;
    }

    private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality)
    {
        using (bitmap)
        using (var image = SKImage.FromBitmap(bitmap))
        using (var data = image.Encode(format, quality))
        {
            return data.ToArray();
        }
    }

    private static byte[] BuildTiff(ushort tag, ushort type, int count, byte[] inlineValue, byte[] trailingData)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)0x49);
        writer.Write((byte)0x49);
        writer.Write((ushort)0x002A);
        writer.Write(8);
        writer.Write((ushort)1);
        writer.Write(tag);
        writer.Write(type);
        writer.Write(count);
        writer.Write(inlineValue);
        writer.Write(0);
        writer.Write(trailingData);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] WithExif(byte[] jpeg, byte[] tiff)
    {
        if (jpeg.Length < 2 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            throw new InvalidOperationException("Expected a JPEG.");
        }

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
