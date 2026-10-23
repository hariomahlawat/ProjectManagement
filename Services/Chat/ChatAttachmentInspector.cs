using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using SkiaSharp;

namespace ProjectManagement.Services.Chat;

/// <summary>What an uploaded file turned out to be; Error is set when it is refused.</summary>
public sealed record ChatFileInspection(string? Error, string ContentType, int? Width = null, int? Height = null)
{
    public bool IsAccepted => Error is null;

    public static ChatFileInspection Refuse(string error) => new(error, string.Empty);
}

/// <summary>
/// The allow-list of chat attachments (docs/chat-attachments.md, D4), checked by content as well
/// as by name, and image thumbnails. There is no malware scanning (D5): these checks, decoding
/// images and serving other files only as downloads are the protection.
/// </summary>
public static class ChatAttachmentInspector
{
    public const int MaxImageSide = 12_000;
    public const long MaxImagePixels = 60_000_000;
    public const int ThumbnailSide = 640;

    private const string Pdf = "application/pdf";
    private const string Docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string Pptx = "application/vnd.openxmlformats-officedocument.presentationml.presentation";

    /// <summary>Extension (lower case, with the dot) to the content type PRISM serves it with.</summary>
    public static readonly IReadOnlyDictionary<string, string> AllowedTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = Pdf,
        [".docx"] = Docx,
        [".xlsx"] = Xlsx,
        [".pptx"] = Pptx,
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
        [".txt"] = "text/plain",
        [".csv"] = "text/csv",
    };

    public const string AllowedSummary = "PDF, Word (.docx), Excel (.xlsx), PowerPoint (.pptx), images (.jpg, .png, .webp), text (.txt) and CSV";

    /// <summary>The extension to check and store, or null when the name is not on the allow-list.</summary>
    public static string? AllowedExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(extension) && AllowedTypes.ContainsKey(extension)
            ? extension.ToLowerInvariant()
            : null;
    }

    /// <summary>Checks a stored upload against what its name says it is.</summary>
    public static ChatFileInspection Inspect(string path, string extension)
    {
        var contentType = AllowedTypes[extension];
        try
        {
            return contentType switch
            {
                Pdf => InspectPdf(path),
                Docx => InspectOfficeOpenXml(path, "word/", contentType),
                Xlsx => InspectOfficeOpenXml(path, "xl/", contentType),
                Pptx => InspectOfficeOpenXml(path, "ppt/", contentType),
                "text/plain" or "text/csv" => InspectText(path, contentType),
                _ => InspectImage(path, contentType),
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return ChatFileInspection.Refuse("The file could not be read. It may be damaged.");
        }
    }

    private static ChatFileInspection InspectPdf(string path)
    {
        var head = new byte[1024];
        int read;
        using (var stream = File.OpenRead(path))
        {
            read = stream.Read(head, 0, head.Length);
        }

        // PDF readers accept the header anywhere in the first kilobyte.
        return IndexOf(head.AsSpan(0, read), "%PDF-"u8) >= 0
            ? new ChatFileInspection(null, Pdf)
            : ChatFileInspection.Refuse("This is not a PDF file, although its name says so.");
    }

    private static ChatFileInspection InspectOfficeOpenXml(string path, string mainFolder, string contentType)
    {
        using var stream = File.OpenRead(path);
        Span<byte> signature = stackalloc byte[4];
        if (stream.Read(signature) < 4 || signature[0] != (byte)'P' || signature[1] != (byte)'K')
        {
            return ChatFileInspection.Refuse("This is not a valid Office file, although its name says so.");
        }

        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        if (!names.Contains("[Content_Types].xml", StringComparer.OrdinalIgnoreCase)
            || !names.Any(n => n.StartsWith(mainFolder, StringComparison.OrdinalIgnoreCase)))
        {
            return ChatFileInspection.Refuse("This is not a valid Office file, although its name says so.");
        }

        // Macro-enabled documents are refused even when renamed to .docx/.xlsx/.pptx.
        if (names.Any(n => n.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase)))
        {
            return ChatFileInspection.Refuse("Office files with macros cannot be sent.");
        }

        var types = zip.GetEntry("[Content_Types].xml");
        if (types is not null && types.Length <= 1024 * 1024)
        {
            using var reader = new StreamReader(types.Open());
            if (reader.ReadToEnd().Contains("macroEnabled", StringComparison.OrdinalIgnoreCase))
            {
                return ChatFileInspection.Refuse("Office files with macros cannot be sent.");
            }
        }

        return new ChatFileInspection(null, contentType);
    }

    private static ChatFileInspection InspectText(string path, string contentType)
    {
        var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetDecoder();
        var buffer = new byte[64 * 1024];
        var chars = new char[strict.GetCharCount(buffer, 0, 0) + 64 * 1024 + 4];
        using var stream = File.OpenRead(path);
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (Array.IndexOf(buffer, (byte)0, 0, read) >= 0)
            {
                return ChatFileInspection.Refuse("This is not a text file, although its name says so.");
            }

            try
            {
                strict.GetChars(buffer, 0, read, chars, 0, flush: false);
            }
            catch (DecoderFallbackException)
            {
                return ChatFileInspection.Refuse("Text files must be saved as UTF-8.");
            }
        }

        try
        {
            strict.GetChars(Array.Empty<byte>(), 0, 0, chars, 0, flush: true);
        }
        catch (DecoderFallbackException)
        {
            return ChatFileInspection.Refuse("Text files must be saved as UTF-8.");
        }

        return new ChatFileInspection(null, contentType);
    }

    private static ChatFileInspection InspectImage(string path, string contentType)
    {
        using var codec = SKCodec.Create(path);
        if (codec is null)
        {
            return ChatFileInspection.Refuse("This is not an image PRISM can read, although its name says so.");
        }

        var expected = contentType switch
        {
            "image/jpeg" => SKEncodedImageFormat.Jpeg,
            "image/png" => SKEncodedImageFormat.Png,
            _ => SKEncodedImageFormat.Webp,
        };
        if (codec.EncodedFormat != expected)
        {
            return ChatFileInspection.Refuse("The image's type does not match its name.");
        }

        var width = codec.Info.Width;
        var height = codec.Info.Height;
        if (width <= 0 || height <= 0 || width > MaxImageSide || height > MaxImageSide || (long)width * height > MaxImagePixels)
        {
            return ChatFileInspection.Refuse("The image is too large to show (at most 12,000 pixels a side and 60 megapixels).");
        }

        return SwapsAxes(codec.EncodedOrigin)
            ? new ChatFileInspection(null, contentType, height, width)
            : new ChatFileInspection(null, contentType, width, height);
    }

    /// <summary>
    /// A WebP thumbnail (at most <see cref="ThumbnailSide"/> pixels a side), upright, carrying none
    /// of the original's metadata (location, camera). Null when the image cannot be decoded.
    /// </summary>
    public static byte[]? CreateThumbnail(string path)
    {
        using var codec = SKCodec.Create(path);
        if (codec is null)
        {
            return null;
        }

        using var decoded = DecodeNear(codec, ThumbnailSide);
        if (decoded is null)
        {
            return null;
        }

        using var upright = Orient(decoded, codec.EncodedOrigin);
        var scale = Math.Min(1d, (double)ThumbnailSide / Math.Max(upright.Width, upright.Height));
        var size = new SKImageInfo(Math.Max(1, (int)Math.Round(upright.Width * scale)), Math.Max(1, (int)Math.Round(upright.Height * scale)));
        using var thumbnail = scale < 1 ? upright.Resize(size, SKFilterQuality.Medium) : upright.Copy();
        if (thumbnail is null)
        {
            return null;
        }

        using var image = SKImage.FromBitmap(thumbnail);
        using var data = image.Encode(SKEncodedImageFormat.Webp, 80);
        return data?.ToArray();
    }

    /// <summary>Decodes at a reduced size when the format allows it, so large photos stay cheap.</summary>
    private static SKBitmap? DecodeNear(SKCodec codec, int targetSide)
    {
        var info = codec.Info;
        var scale = Math.Min(1f, (float)(targetSide * 2) / Math.Max(info.Width, info.Height));
        var scaled = codec.GetScaledDimensions(scale);
        foreach (var size in new[] { scaled, new SKSizeI(info.Width, info.Height) }.Distinct())
        {
            var target = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            var bitmap = new SKBitmap(target);
            var result = codec.GetPixels(target, bitmap.GetPixels());
            if (result is SKCodecResult.Success or SKCodecResult.IncompleteInput)
            {
                return bitmap;
            }

            bitmap.Dispose();
        }

        return null;
    }

    private static bool SwapsAxes(SKEncodedOrigin origin)
        => origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

    // Same transforms as MediaDerivativeService (EXIF orientation applied to the pixels).
    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft)
        {
            return source.Copy();
        }

        var swap = SwapsAxes(origin);
        var destination = new SKBitmap(swap ? source.Height : source.Width, swap ? source.Width : source.Height, source.ColorType, source.AlphaType);
        using var canvas = new SKCanvas(destination);
        canvas.Clear(SKColors.Transparent);
        var (w, h) = (source.Width, source.Height);
        canvas.SetMatrix(origin switch
        {
            SKEncodedOrigin.TopRight => Matrix(-1, 0, w, 0, 1, 0),
            SKEncodedOrigin.BottomRight => Matrix(-1, 0, w, 0, -1, h),
            SKEncodedOrigin.BottomLeft => Matrix(1, 0, 0, 0, -1, h),
            SKEncodedOrigin.LeftTop => Matrix(0, 1, 0, 1, 0, 0),
            SKEncodedOrigin.RightTop => Matrix(0, -1, h, 1, 0, 0),
            SKEncodedOrigin.RightBottom => Matrix(0, -1, h, -1, 0, w),
            SKEncodedOrigin.LeftBottom => Matrix(0, 1, 0, -1, 0, w),
            _ => SKMatrix.CreateIdentity(),
        });
        canvas.DrawBitmap(source, 0, 0);
        canvas.Flush();
        return destination;
    }

    private static SKMatrix Matrix(float scaleX, float skewX, float transX, float skewY, float scaleY, float transY)
        => new() { ScaleX = scaleX, SkewX = skewX, TransX = transX, SkewY = skewY, ScaleY = scaleY, TransY = transY, Persp2 = 1 };

    private static int IndexOf(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle) => haystack.IndexOf(needle);
}
