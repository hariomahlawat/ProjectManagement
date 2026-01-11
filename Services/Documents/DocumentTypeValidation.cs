using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectManagement.Services.Documents;

internal enum DocumentSignatureType
{
    None,
    Pdf,
    Zip
}

// SECTION: Document type validation helpers
internal static class DocumentTypeValidation
{
    // SECTION: Signature rules
    internal static readonly IReadOnlyDictionary<string, DocumentSignatureRule> SignatureRules =
        new Dictionary<string, DocumentSignatureRule>(StringComparer.OrdinalIgnoreCase)
        {
            ["application/pdf"] = new DocumentSignatureRule("pdf", DocumentSignatureType.Pdf),
            ["application/msword"] = new DocumentSignatureRule("doc", DocumentSignatureType.None),
            ["application/vnd.ms-powerpoint"] = new DocumentSignatureRule("ppt", DocumentSignatureType.None),
            ["application/vnd.ms-excel"] = new DocumentSignatureRule("xls", DocumentSignatureType.None),
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = new DocumentSignatureRule("docx", DocumentSignatureType.Zip),
            ["application/vnd.openxmlformats-officedocument.presentationml.presentation"] = new DocumentSignatureRule("pptx", DocumentSignatureType.Zip),
            ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = new DocumentSignatureRule("xlsx", DocumentSignatureType.Zip)
        };

    // SECTION: Extension helpers
    internal static IReadOnlyCollection<string> GetAllowedExtensions(IEnumerable<string>? contentTypes)
    {
        if (contentTypes is null)
        {
            return Array.Empty<string>();
        }

        return contentTypes
            .Select(type => SignatureRules.TryGetValue(type, out var rule) ? rule.Extension : string.Empty)
            .Where(ext => !string.IsNullOrWhiteSpace(ext))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(ext => ext, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static bool TryGetContentTypeForExtension(string extension, out string contentType)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            contentType = string.Empty;
            return false;
        }

        foreach (var pair in SignatureRules)
        {
            if (string.Equals(pair.Value.Extension, extension, StringComparison.OrdinalIgnoreCase))
            {
                contentType = pair.Key;
                return true;
            }
        }

        contentType = string.Empty;
        return false;
    }
}

internal sealed record DocumentSignatureRule(string Extension, DocumentSignatureType SignatureType);
