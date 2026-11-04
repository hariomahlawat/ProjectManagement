using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;

namespace ProjectManagement.Services.IndustryPartners;

public sealed class IndustryPartnerAttachmentValidator
{
    // Matches IndustryPartnerAttachment.OriginalFileName.
    public const int MaxFileNameLength = 260;

    private const string Pdf = "application/pdf";
    private const string Doc = "application/msword";
    private const string Docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string Ppt = "application/vnd.ms-powerpoint";
    private const string Pptx = "application/vnd.openxmlformats-officedocument.presentationml.presentation";
    private const string Jpeg = "image/jpeg";
    private const string Png = "image/png";

    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();
    private static readonly byte[] OleCompoundSignature = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
    private static readonly byte[] ZipSignature = { 0x50, 0x4B, 0x03, 0x04 };
    private static readonly byte[] JpegSignature = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private const string ContentMismatchMessage =
        "The file's contents do not match its type. Upload a genuine PDF, Word, PowerPoint, JPEG or PNG file.";

    private readonly long _maxSizeBytes;

    // SECTION: Industry partner attachment MIME allow-list
    private static readonly IReadOnlyDictionary<string, string[]> AllowedExtensionsByType = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["application/pdf"] = new[] { ".pdf" },
        ["application/msword"] = new[] { ".doc" },
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = new[] { ".docx" },
        ["application/vnd.ms-powerpoint"] = new[] { ".ppt" },
        ["application/vnd.openxmlformats-officedocument.presentationml.presentation"] = new[] { ".pptx" },
        ["image/jpeg"] = new[] { ".jpg", ".jpeg" },
        ["image/png"] = new[] { ".png" }
    };

    public IndustryPartnerAttachmentValidator(IOptions<ProjectDocumentOptions> options)
    {
        _maxSizeBytes = Math.Max(1, options.Value.MaxSizeMb) * 1024L * 1024L;
    }

    public void Validate(string fileName, string contentType, long length)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(fileName)) Add(errors, "File", "File name is required.");
        else if (Path.GetFileName(fileName).Length > MaxFileNameLength) Add(errors, "File", $"File name must be {MaxFileNameLength} characters or fewer.");
        if (length <= 0) Add(errors, "File", "File must be larger than zero bytes.");
        if (length > _maxSizeBytes) Add(errors, "File", $"File exceeds the maximum size of {_maxSizeBytes / (1024 * 1024)} MB.");
        if (!AllowedExtensionsByType.TryGetValue(contentType ?? string.Empty, out var allowed))
        {
            Add(errors, "ContentType", "File type is not allowed.");
        }
        else
        {
            var ext = Path.GetExtension(fileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(ext) || Array.IndexOf(allowed, ext.ToLowerInvariant()) < 0)
            {
                Add(errors, "File", $"File extension does not match the declared file type. Allowed: {string.Join(", ", allowed)}.");
            }
        }

        if (errors.Count > 0) throw new IndustryPartnerValidationException(errors);
    }

    // SECTION: Industry partner attachment content check
    // The declared type and extension come from the browser; this checks that the bytes
    // really are that kind of file before the upload is stored or served back.
    public void ValidateContent(string contentType, Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanSeek)
        {
            throw new ArgumentException("Content must be seekable.", nameof(content));
        }

        var start = content.Position;
        bool valid;
        try
        {
            valid = (contentType ?? string.Empty).ToLowerInvariant() switch
            {
                Pdf => StartsWith(content, PdfSignature),
                Doc or Ppt => StartsWith(content, OleCompoundSignature),
                Docx => IsOfficeOpenXml(content, start, "word/"),
                Pptx => IsOfficeOpenXml(content, start, "ppt/"),
                Jpeg => StartsWith(content, JpegSignature),
                Png => StartsWith(content, PngSignature),
                _ => false
            };
        }
        catch (InvalidDataException)
        {
            valid = false;
        }
        finally
        {
            content.Position = start;
        }

        if (!valid)
        {
            throw new IndustryPartnerValidationException(new Dictionary<string, List<string>>
            {
                ["File"] = new() { ContentMismatchMessage }
            });
        }
    }

    private static bool StartsWith(Stream content, byte[] signature)
    {
        var header = new byte[signature.Length];
        var read = 0;
        while (read < header.Length)
        {
            var count = content.Read(header, read, header.Length - read);
            if (count == 0)
            {
                break;
            }

            read += count;
        }

        return read == signature.Length && header.AsSpan().SequenceEqual(signature);
    }

    private static bool IsOfficeOpenXml(Stream content, long start, string partPrefix)
    {
        if (!StartsWith(content, ZipSignature))
        {
            return false;
        }

        content.Position = start;
        using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
        var hasContentTypes = archive.Entries.Any(entry =>
            string.Equals(entry.FullName, "[Content_Types].xml", StringComparison.OrdinalIgnoreCase));
        var hasMainPart = archive.Entries.Any(entry =>
            entry.FullName.StartsWith(partPrefix, StringComparison.OrdinalIgnoreCase));
        return hasContentTypes && hasMainPart;
    }

    private static void Add(IDictionary<string, List<string>> errors, string key, string value)
    {
        if (!errors.TryGetValue(key, out var list)) errors[key] = list = new List<string>();
        list.Add(value);
    }
}
