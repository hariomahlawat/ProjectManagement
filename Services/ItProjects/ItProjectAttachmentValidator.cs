using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Models.ItProjects;

namespace ProjectManagement.Services.ItProjects;

/// <summary>
/// What an IT project attachment may be: a PDF or an Office document (Word, Excel, PowerPoint),
/// within the configured size, whose bytes really are that kind of file. The declared type and
/// the extension come from the browser, so the content is checked too before anything is stored.
/// Returns errors rather than throwing, like the rest of the module.
/// </summary>
public sealed class ItProjectAttachmentValidator
{
    public const string TypeNotAllowedMessage = "Only PDF, Word, Excel and PowerPoint files can be attached.";
    public const string ContentMismatchMessage = "The file's contents do not match its type. Upload a genuine PDF, Word, Excel or PowerPoint file.";

    private const string Pdf = "application/pdf";
    private const string Doc = "application/msword";
    private const string Docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string Xls = "application/vnd.ms-excel";
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string Ppt = "application/vnd.ms-powerpoint";
    private const string Pptx = "application/vnd.openxmlformats-officedocument.presentationml.presentation";

    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();
    private static readonly byte[] OleCompoundSignature = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
    private static readonly byte[] ZipSignature = { 0x50, 0x4B, 0x03, 0x04 };

    // SECTION: IT project attachment allow-list (content type -> extensions)
    private static readonly IReadOnlyDictionary<string, string[]> AllowedExtensionsByType = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        [Pdf] = new[] { ".pdf" },
        [Doc] = new[] { ".doc" },
        [Docx] = new[] { ".docx" },
        [Xls] = new[] { ".xls" },
        [Xlsx] = new[] { ".xlsx" },
        [Ppt] = new[] { ".ppt" },
        [Pptx] = new[] { ".pptx" }
    };

    // Types a browser sends when it does not know the file (no Office installed, an unusual
    // registry, a drag from a zip). For these the extension decides, and the content check below
    // still has to agree, so nothing is let through on the name alone.
    private static readonly HashSet<string> GenericContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "",
        "application/octet-stream",
        "application/zip",
        "application/x-zip-compressed",
        "application/x-pdf",
        "application/vnd.ms-office",
        "binary/octet-stream"
    };

    private readonly long _maxSizeBytes;

    public ItProjectAttachmentValidator(IOptions<ProjectDocumentOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _maxSizeBytes = Math.Max(1, options.Value.MaxSizeMb) * 1024L * 1024L;
    }

    public long MaxSizeBytes => _maxSizeBytes;

    /// <summary>The accept list for the file input, so the browser offers the same types the server allows.</summary>
    public static string AcceptList => string.Join(",", AllowedExtensionsByType.Values.SelectMany(ext => ext).OrderBy(ext => ext, StringComparer.Ordinal));

    /// <summary>
    /// The content type to check and store: an allowed declared type as is (in its canonical
    /// spelling), or, when the browser sent a generic one, the type the extension names. Any other
    /// declared type is returned unchanged so <see cref="Validate"/> rejects it.
    /// </summary>
    public static string ResolveContentType(string? fileName, string? declaredContentType)
    {
        var declared = (declaredContentType ?? string.Empty).Split(';')[0].Trim();
        foreach (var allowed in AllowedExtensionsByType.Keys)
        {
            if (string.Equals(allowed, declared, StringComparison.OrdinalIgnoreCase))
            {
                return allowed;
            }
        }

        if (!GenericContentTypes.Contains(declared))
        {
            return declared;
        }

        var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        foreach (var (type, extensions) in AllowedExtensionsByType)
        {
            if (Array.IndexOf(extensions, ext) >= 0)
            {
                return type;
            }
        }

        return declared;
    }

    /// <summary>Name, declared type and size. Empty when acceptable.</summary>
    public IReadOnlyList<string> Validate(string? fileName, string? contentType, long length)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            errors.Add("Choose a file.");
        }
        else if (fileName.Length > ItProjectAttachment.FileNameMaxLength)
        {
            errors.Add($"The file name must be {ItProjectAttachment.FileNameMaxLength} characters or fewer.");
        }

        if (length <= 0)
        {
            errors.Add("The file is empty.");
        }
        else if (length > _maxSizeBytes)
        {
            errors.Add($"The file is larger than the {_maxSizeBytes / (1024 * 1024)} MB limit.");
        }

        if (!AllowedExtensionsByType.TryGetValue(contentType ?? string.Empty, out var allowed))
        {
            errors.Add(TypeNotAllowedMessage);
        }
        else
        {
            var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext) || Array.IndexOf(allowed, ext) < 0)
            {
                errors.Add($"The file's extension does not match its type. Expected {string.Join(" or ", allowed)}.");
            }
        }

        return errors;
    }

    // SECTION: Content check
    /// <summary>True when the bytes are the kind of file the declared type says. Leaves the stream where it was.</summary>
    public bool ContentMatches(string? contentType, Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanSeek)
        {
            throw new ArgumentException("Content must be seekable.", nameof(content));
        }

        var start = content.Position;
        try
        {
            return (contentType ?? string.Empty).ToLowerInvariant() switch
            {
                Pdf => StartsWith(content, PdfSignature),
                Doc or Xls or Ppt => StartsWith(content, OleCompoundSignature),
                Docx => IsOfficeOpenXml(content, start, "word/"),
                Xlsx => IsOfficeOpenXml(content, start, "xl/"),
                Pptx => IsOfficeOpenXml(content, start, "ppt/"),
                _ => false
            };
        }
        catch (InvalidDataException)
        {
            return false;
        }
        finally
        {
            content.Position = start;
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
        var hasContentTypes = archive.Entries.Any(entry => string.Equals(entry.FullName, "[Content_Types].xml", StringComparison.OrdinalIgnoreCase));
        var hasMainPart = archive.Entries.Any(entry => entry.FullName.StartsWith(partPrefix, StringComparison.OrdinalIgnoreCase));
        return hasContentTypes && hasMainPart;
    }
}
