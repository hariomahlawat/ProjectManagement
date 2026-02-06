using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;

namespace ProjectManagement.Services.IndustryPartners;

public sealed class IndustryPartnerAttachmentValidator
{
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

    private static void Add(IDictionary<string, List<string>> errors, string key, string value)
    {
        if (!errors.TryGetValue(key, out var list)) errors[key] = list = new List<string>();
        list.Add(value);
    }
}
