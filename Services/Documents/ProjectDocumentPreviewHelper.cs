using System;
using System.Collections.Generic;
using System.IO;
using ProjectManagement.Configuration;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Documents;

// SECTION: Project document preview helpers
public static class ProjectDocumentPreviewHelper
{
    // SECTION: Content type constants
    public const string PdfContentType = "application/pdf";

    private static readonly HashSet<string> OfficeContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    };

    // SECTION: Content type checks
    public static bool IsPdfContentType(string? contentType)
    {
        return string.Equals(contentType, PdfContentType, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsOfficeContentType(string? contentType)
    {
        return !string.IsNullOrWhiteSpace(contentType) && OfficeContentTypes.Contains(contentType);
    }

    // SECTION: Preview naming helpers
    public static string GetPdfPreviewFileName(ProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var baseName = Path.GetFileNameWithoutExtension(document.OriginalFileName);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = $"document-{document.Id}";
        }

        return $"{baseName}.pdf";
    }

    // SECTION: Derivative storage resolution
    public static bool TryGetPdfDerivativeStorageKey(
        ProjectDocument document,
        ProjectDocumentTextExtractorOptions options,
        IProjectDocumentStorageResolver storageResolver,
        out string storageKey)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(storageResolver);

        storageKey = string.Empty;

        if (!IsOfficeContentType(document.ContentType))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(document.StorageKey))
        {
            return false;
        }

        var derivativeKey = BuildPdfDerivativeStorageKey(document.StorageKey, document.Id, options.DerivativeStoragePrefix);
        var path = storageResolver.ResolveAbsolutePath(derivativeKey);
        if (!File.Exists(path))
        {
            return false;
        }

        storageKey = derivativeKey;
        return true;
    }

    private static string BuildPdfDerivativeStorageKey(string storageKey, int documentId, string derivativePrefix)
    {
        var normalized = NormalizeStorageKey(storageKey);
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var fileName = segments.Length > 0 ? segments[^1] : $"document-{documentId}";
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = $"document-{documentId}";
        }

        var directory = segments.Length > 1 ? string.Join('/', segments[..^1]) : string.Empty;
        var prefix = (derivativePrefix ?? string.Empty).Trim().Trim('/');

        if (string.IsNullOrEmpty(prefix))
        {
            return string.IsNullOrEmpty(directory)
                ? $"{baseName}.pdf"
                : $"{directory}/{baseName}.pdf";
        }

        return string.IsNullOrEmpty(directory)
            ? $"{prefix}/{baseName}.pdf"
            : $"{prefix}/{directory}/{baseName}.pdf";
    }

    private static string NormalizeStorageKey(string storageKey)
    {
        var normalized = storageKey.Replace('\\', '/').Trim();
        return normalized.TrimStart('/');
    }
}
