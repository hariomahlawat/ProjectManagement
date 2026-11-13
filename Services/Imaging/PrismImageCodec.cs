using SkiaSharp;

namespace ProjectManagement.Services.Imaging;

/// <summary>The image formats PRISM tells apart when a photo is uploaded.</summary>
public enum PrismImageFormat
{
    /// <summary>An image the decoder recognises but PRISM does not accept as a photo.</summary>
    Other = 0,
    Jpeg,
    Png,
    Webp,
    Gif,
    Bmp
}

/// <summary>
/// What an image's header says about it, read without decoding any pixels.
/// <see cref="Width"/> and <see cref="Height"/> are the stored dimensions;
/// <see cref="OrientedWidth"/> and <see cref="OrientedHeight"/> are the dimensions once the
/// EXIF orientation has been applied, which is how the image is displayed.
/// </summary>
public sealed record PrismImageInfo(
    PrismImageFormat Format,
    int Width,
    int Height,
    int OrientedWidth,
    int OrientedHeight)
{
    public string MimeType => Format switch
    {
        PrismImageFormat.Jpeg => "image/jpeg",
        PrismImageFormat.Png => "image/png",
        PrismImageFormat.Webp => "image/webp",
        PrismImageFormat.Gif => "image/gif",
        PrismImageFormat.Bmp => "image/bmp",
        _ => "application/octet-stream"
    };

    /// <summary>The file extension, with its leading dot, used when the original is stored.</summary>
    public string FileExtension => Format switch
    {
        PrismImageFormat.Jpeg => ".jpg",
        PrismImageFormat.Png => ".png",
        PrismImageFormat.Webp => ".webp",
        PrismImageFormat.Gif => ".gif",
        PrismImageFormat.Bmp => ".bmp",
        _ => ".bin"
    };
}

/// <summary>The bytes are not an image PRISM can decode, or decoding it would be unsafe.</summary>
public sealed class PrismImageException : Exception
{
    public PrismImageException(string message)
        : base(message)
    {
    }

    public PrismImageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The one place photo services identify, decode, resize and encode images. It is built on
/// SkiaSharp, which ships with the application, so no image work depends on a licence key
/// or on anything outside the deployment package.
/// </summary>
public static class PrismImageCodec
{
    /// <summary>
    /// The most pixels an image may declare before it is decoded when the caller sets no
    /// limit of its own. One hundred megapixels is about 400 MB of decoded pixels, well above
    /// any camera photo and far below what a crafted header can claim.
    /// </summary>
    public const long DefaultMaxPixels = 100_000_000;

    /// <summary>
    /// Reads the format and dimensions from the header. Returns null when the bytes are not
    /// an image the decoder recognises. No pixel buffer is allocated.
    /// </summary>
    public static PrismImageInfo? Identify(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return null;
        }

        try
        {
            using var data = SKData.CreateCopy(bytes.Span);
            using var codec = data is null ? null : SKCodec.Create(data);
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
            {
                return null;
            }

            var swapsAxes = SwapsAxes(codec.EncodedOrigin);
            return new PrismImageInfo(
                MapFormat(codec.EncodedFormat),
                codec.Info.Width,
                codec.Info.Height,
                swapsAxes ? codec.Info.Height : codec.Info.Width,
                swapsAxes ? codec.Info.Width : codec.Info.Height);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>
    /// Decodes the image and applies its EXIF orientation, so the result is upright. The
    /// declared dimensions are checked against <paramref name="maximumPixels"/> before any
    /// pixel buffer is allocated. The caller owns the returned bitmap.
    /// </summary>
    /// <exception cref="PrismImageException">The image cannot be decoded, or is too large.</exception>
    public static SKBitmap DecodeOriented(ReadOnlyMemory<byte> bytes, long maximumPixels = DefaultMaxPixels)
    {
        if (bytes.IsEmpty)
        {
            throw new PrismImageException("The image file is empty.");
        }

        try
        {
            using var data = SKData.CreateCopy(bytes.Span)
                ?? throw new PrismImageException("The image could not be prepared for decoding.");
            using var codec = SKCodec.Create(data)
                ?? throw new PrismImageException("The image format is not supported.");

            var width = codec.Info.Width;
            var height = codec.Info.Height;
            if (width <= 0 || height <= 0)
            {
                throw new PrismImageException("The image has invalid dimensions.");
            }

            if ((long)width * height > maximumPixels)
            {
                throw new PrismImageException(
                    $"The image is {width:N0} x {height:N0} pixels, which is more than the limit of {maximumPixels:N0} pixels.");
            }

            // One pixel layout for every source (grey, palette, CMYK, 16-bit), so the rest of
            // the pipeline never has to care how the file was stored.
            var info = new SKImageInfo(width, height, SKImageInfo.PlatformColorType, SKAlphaType.Premul);
            SKBitmap? source = new SKBitmap(info);
            try
            {
                var decoded = source;
                var result = codec.GetPixels(info, decoded.GetPixels());

                // IncompleteInput is a file cut short: the decoder still returns the rows it
                // read. It is accepted, as it was before, rather than newly refusing photos.
                if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
                {
                    throw new PrismImageException($"The image could not be decoded ({result}).");
                }

                var origin = codec.EncodedOrigin;
                if (origin == SKEncodedOrigin.TopLeft)
                {
                    source = null;
                    return decoded;
                }

                var swapsAxes = SwapsAxes(origin);
                var destination = new SKBitmap(new SKImageInfo(
                    swapsAxes ? height : width,
                    swapsAxes ? width : height,
                    info.ColorType,
                    info.AlphaType));
                try
                {
                    using var canvas = new SKCanvas(destination);
                    canvas.Clear(SKColors.Transparent);
                    canvas.SetMatrix(CreateOrientationMatrix(origin, width, height));
                    canvas.DrawBitmap(decoded, 0, 0);
                    canvas.Flush();
                }
                catch
                {
                    destination.Dispose();
                    throw;
                }

                return destination;
            }
            finally
            {
                source?.Dispose();
            }
        }
        catch (PrismImageException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
        {
            throw new PrismImageException("The image could not be decoded.", ex);
        }
    }

    /// <summary>
    /// Scales the image, keeping its proportions, to the largest size that fits inside the
    /// box. An image smaller than the box is enlarged to fit it, which is what the photo
    /// derivatives have always done. The caller owns the returned bitmap.
    /// </summary>
    public static SKBitmap ResizeToFit(SKBitmap source, int boxWidth, int boxHeight)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (boxWidth <= 0 || boxHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(boxWidth), "The target box must have a positive width and height.");
        }

        var scale = Math.Min(boxWidth / (double)source.Width, boxHeight / (double)source.Height);
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));

        if (width == source.Width && height == source.Height)
        {
            return source.Copy()
                   ?? throw new PrismImageException("The image could not be copied.");
        }

        return source.Resize(new SKImageInfo(width, height, source.ColorType, source.AlphaType), SKFilterQuality.High)
               ?? throw new PrismImageException("The image could not be resized.");
    }

    /// <summary>
    /// Encodes the image as JPEG. JPEG has no transparency, so a transparent image is laid
    /// on white first.
    /// </summary>
    public static byte[] EncodeJpeg(SKBitmap source, int quality)
    {
        ArgumentNullException.ThrowIfNull(source);
        quality = Math.Clamp(quality, 1, 100);

        if (source.AlphaType == SKAlphaType.Opaque)
        {
            return Encode(source, SKEncodedImageFormat.Jpeg, quality);
        }

        using var flattened = new SKBitmap(new SKImageInfo(
            source.Width,
            source.Height,
            SKImageInfo.PlatformColorType,
            SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(flattened))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(source, 0, 0);
            canvas.Flush();
        }

        return Encode(flattened, SKEncodedImageFormat.Jpeg, quality);
    }

    private static byte[] Encode(SKBitmap source, SKEncodedImageFormat format, int quality)
    {
        using var image = SKImage.FromBitmap(source)
            ?? throw new PrismImageException("The image could not be prepared for encoding.");
        using var encoded = image.Encode(format, quality)
            ?? throw new PrismImageException($"The image could not be encoded as {format}.");

        var bytes = encoded.ToArray();
        if (bytes.Length == 0)
        {
            throw new PrismImageException($"Encoding the image as {format} produced no data.");
        }

        return bytes;
    }

    private static PrismImageFormat MapFormat(SKEncodedImageFormat format) => format switch
    {
        SKEncodedImageFormat.Jpeg => PrismImageFormat.Jpeg,
        SKEncodedImageFormat.Png => PrismImageFormat.Png,
        SKEncodedImageFormat.Webp => PrismImageFormat.Webp,
        SKEncodedImageFormat.Gif => PrismImageFormat.Gif,
        SKEncodedImageFormat.Bmp => PrismImageFormat.Bmp,
        _ => PrismImageFormat.Other
    };

    private static bool SwapsAxes(SKEncodedOrigin origin) => origin is SKEncodedOrigin.LeftTop
        or SKEncodedOrigin.RightTop
        or SKEncodedOrigin.RightBottom
        or SKEncodedOrigin.LeftBottom;

    private static SKMatrix CreateOrientationMatrix(SKEncodedOrigin origin, int width, int height) => origin switch
    {
        SKEncodedOrigin.TopRight => Matrix(-1, 0, width, 0, 1, 0),
        SKEncodedOrigin.BottomRight => Matrix(-1, 0, width, 0, -1, height),
        SKEncodedOrigin.BottomLeft => Matrix(1, 0, 0, 0, -1, height),
        SKEncodedOrigin.LeftTop => Matrix(0, 1, 0, 1, 0, 0),
        SKEncodedOrigin.RightTop => Matrix(0, -1, height, 1, 0, 0),
        SKEncodedOrigin.RightBottom => Matrix(0, -1, height, -1, 0, width),
        SKEncodedOrigin.LeftBottom => Matrix(0, 1, 0, -1, 0, width),
        _ => SKMatrix.CreateIdentity()
    };

    private static SKMatrix Matrix(
        float scaleX,
        float skewX,
        float transX,
        float skewY,
        float scaleY,
        float transY) => new()
    {
        ScaleX = scaleX,
        SkewX = skewX,
        TransX = transX,
        SkewY = skewY,
        ScaleY = scaleY,
        TransY = transY,
        Persp0 = 0,
        Persp1 = 0,
        Persp2 = 1
    };
}
