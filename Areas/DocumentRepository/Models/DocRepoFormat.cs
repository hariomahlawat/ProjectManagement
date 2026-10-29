using System.Globalization;
using ProjectManagement.Data.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Models;

/// <summary>Display helpers shared by the repository views, so every page words things the same way.</summary>
public static class DocRepoFormat
{
    // Invariant month names ("Sep", not en-GB's "Sept"); day-month-year order as used across PRISM.
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static string Date(DateOnly? date) => date?.ToString("d MMM yyyy", Culture) ?? "No date";

    public static string Date(DateTime utc) => utc.ToString("d MMM yyyy", Culture);

    public static string DateTime(DateTime utc) => utc.ToLocalTime().ToString("d MMM yyyy, HH:mm", Culture);

    public static string FileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} bytes";
        double size = bytes / 1024d;
        if (size < 1024) return $"{size.ToString("0", Culture)} KB";
        size /= 1024;
        return $"{size.ToString(size < 10 ? "0.0" : "0", Culture)} MB";
    }

    public static string Count(int count, string singular, string? plural = null)
        => $"{count.ToString("N0", Culture)} {(count == 1 ? singular : plural ?? singular + "s")}";

    /// <summary>One of six category hues (1–6), stable per document type, for the type marker on rows and cards.</summary>
    public static int Hue(int documentCategoryId) => ((Math.Abs(documentCategoryId) - 1) % 6 + 6) % 6 + 1;

    /// <summary>
    /// A short, stable key for this user, so what the browser remembers (recent documents, last
    /// page read) stays apart for each person on a shared machine. Not a secret: only a key.
    /// </summary>
    public static string UserKey(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return "anon";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(userId));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    /// <summary>Folder of the pdf.js build shipped with the app.</summary>
    public const string PdfJsPath = "~/lib/pdfjs-dist/4.10.38/";

    /// <summary>OCR state in the words a reader uses; null when the text is ready (nothing to say).</summary>
    public static string? TextState(DocOcrStatus status) => status switch
    {
        DocOcrStatus.Pending => "Text not searchable yet",
        DocOcrStatus.Failed => "Text not readable",
        DocOcrStatus.None => "Text not read",
        _ => null
    };

    /// <summary>A plain explanation of an ocrmypdf failure for the admin page; the raw message stays alongside.</summary>
    public static string ExplainOcrFailure(string? reason)
    {
        var text = reason ?? string.Empty;
        if (text.Contains("PriorOcrFound", StringComparison.OrdinalIgnoreCase))
            return "The PDF already has a text layer that OCR would not replace.";
        if (text.Contains("Encrypted", StringComparison.OrdinalIgnoreCase) || text.Contains("password", StringComparison.OrdinalIgnoreCase))
            return "The PDF is password-protected or encrypted.";
        if (text.Contains("InputFileError", StringComparison.OrdinalIgnoreCase) || text.Contains("corrupt", StringComparison.OrdinalIgnoreCase))
            return "The PDF file is damaged or not a standard PDF.";
        if (text.Contains("not found", StringComparison.OrdinalIgnoreCase) || text.Contains("No such file", StringComparison.OrdinalIgnoreCase))
            return "The OCR tool or the stored file could not be found on the server.";
        if (text.Contains("timeout", StringComparison.OrdinalIgnoreCase) || text.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            return "OCR took too long and was stopped.";
        if (text.Contains("empty", StringComparison.OrdinalIgnoreCase) || text.Contains("no text", StringComparison.OrdinalIgnoreCase))
            return "OCR ran but found no readable text (blank or image-only pages).";
        return "OCR could not read this PDF.";
    }
}
