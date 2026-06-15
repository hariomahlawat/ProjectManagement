using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.Activities;

public interface IActivityAttachmentValidator
{
    void Validate(ActivityAttachmentUpload upload);
}

internal sealed class ActivityAttachmentValidator : IActivityAttachmentValidator
{
    public const long MaxAttachmentSizeBytes = 25 * 1024 * 1024; // 25 MB

    private static readonly string[] AllowedContentTypeList =
    {
        "application/pdf",
        "application/msword",
        "application/vnd.ms-excel",
        "application/vnd.ms-powerpoint",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "image/png",
        "image/jpeg",
        "video/mp4",
        "video/quicktime",
        "video/webm"
    };

    private static readonly HashSet<string> AllowedContentTypes = new(AllowedContentTypeList, StringComparer.OrdinalIgnoreCase);

    // SECTION: Browser fallback MIME handling
    private static readonly HashSet<string> UnknownContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        string.Empty,
        "application/octet-stream"
    };

    private static readonly string[] OfficeDocumentExtensions =
    {
        ".doc",
        ".docx",
        ".xls",
        ".xlsx",
        ".ppt",
        ".pptx"
    };

    private static readonly IReadOnlyDictionary<string, string[]> AllowedExtensionsByContentType =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["application/pdf"] = new[] { ".pdf" },
            ["application/msword"] = new[] { ".doc" },
            ["application/vnd.ms-excel"] = new[] { ".xls" },
            ["application/vnd.ms-powerpoint"] = new[] { ".ppt" },
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = new[] { ".docx" },
            ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = new[] { ".xlsx" },
            ["application/vnd.openxmlformats-officedocument.presentationml.presentation"] = new[] { ".pptx" },
            ["image/png"] = new[] { ".png" },
            ["image/jpeg"] = new[] { ".jpg", ".jpeg" },
            ["video/mp4"] = new[] { ".mp4" },
            ["video/quicktime"] = new[] { ".mov" },
            ["video/webm"] = new[] { ".webm" }
        };

    public void Validate(ActivityAttachmentUpload upload)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        void AddError(string key, string message)
        {
            if (!errors.TryGetValue(key, out var list))
            {
                list = new List<string>();
                errors[key] = list;
            }

            list.Add(message);
        }

        if (upload is null)
        {
            AddError(string.Empty, "An attachment payload is required.");
            throw new ActivityValidationException(errors);
        }

        if (upload.Content is null)
        {
            AddError(nameof(upload.Content), "Attachment content stream is required.");
        }
        else if (!upload.Content.CanRead)
        {
            AddError(nameof(upload.Content), "Attachment content stream must be readable.");
        }

        if (string.IsNullOrWhiteSpace(upload.FileName))
        {
            AddError(nameof(upload.FileName), "File name is required.");
        }
        else if (upload.FileName.Length > 260)
        {
            AddError(nameof(upload.FileName), "File name must be 260 characters or fewer.");
        }

        // SECTION: File type validation
        var extension = string.IsNullOrWhiteSpace(upload.FileName)
            ? string.Empty
            : Path.GetExtension(upload.FileName);
        var contentType = NormalizeContentType(upload.ContentType);
        var hasAllowedContentType = AllowedContentTypes.Contains(contentType);
        var hasAllowedUnknownOfficeExtension = IsUnknownContentType(contentType) && IsOfficeDocumentExtension(extension);

        if (!hasAllowedContentType && !hasAllowedUnknownOfficeExtension)
        {
            AddError(nameof(upload.ContentType),
                $"File type is not allowed. Allowed types: {string.Join(", ", AllowedContentTypeList)}.");
        }

        if (!string.IsNullOrWhiteSpace(upload.FileName) &&
            hasAllowedContentType &&
            AllowedExtensionsByContentType.TryGetValue(contentType, out var allowedExtensions))
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                AddError(nameof(upload.FileName), "File must include an extension.");
            }
            else if (!allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                AddError(nameof(upload.FileName),
                    $"File extension does not match the declared file type. Allowed extensions: {string.Join(", ", allowedExtensions)}.");
            }
        }

        if (upload.Length <= 0)
        {
            AddError(nameof(upload.Length), "File must be larger than zero bytes.");
        }
        else if (upload.Length > MaxAttachmentSizeBytes)
        {
            AddError(nameof(upload.Length), $"File exceeds the maximum size of {MaxAttachmentSizeBytes / (1024 * 1024)} MB.");
        }

        if (errors.Count > 0)
        {
            throw new ActivityValidationException(errors);
        }

        if (upload.Content is not null && upload.Content.CanSeek)
        {
            upload.Content.Seek(0, SeekOrigin.Begin);
        }
    }

    // SECTION: Content type helpers
    private static string NormalizeContentType(string? contentType)
    {
        return string.IsNullOrWhiteSpace(contentType) ? string.Empty : contentType.Trim();
    }

    private static bool IsUnknownContentType(string contentType)
    {
        return UnknownContentTypes.Contains(contentType);
    }

    private static bool IsOfficeDocumentExtension(string? extension)
    {
        return !string.IsNullOrWhiteSpace(extension) &&
            OfficeDocumentExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    public static string SanitizeFileName(string fileName)
    {
        return FileNameSanitizer.Sanitize(fileName);
    }
}
