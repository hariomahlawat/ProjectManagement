using System;
using System.Text.RegularExpressions;

namespace ProjectManagement.Services.Ocr;

// SECTION: OCR text normalization helpers
public static class OcrTextUtilities
{
    private static readonly Regex BannerPattern = new(
        "\\[?OCR skipped on page[^\\r\\n]*\\]?|Prior OCR[^\\r\\n]*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string CleanBanners(string text)
    {
        var t = TrimLeadingBom(text);

        t = BannerPattern.Replace(t, string.Empty);

        return t.Trim();
    }

    public static bool HasUsefulText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return CleanBanners(text).Length > 0;
    }

    public static string TrimLeadingBom(string value)
    {
        return value.Length > 0 ? value.TrimStart('\ufeff') : value;
    }
}
