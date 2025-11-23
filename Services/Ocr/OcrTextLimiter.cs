using System;

namespace ProjectManagement.Services.Ocr;

// SECTION: Common OCR text limiting helpers
public static class OcrTextLimiter
{
    public static string? CapExtractedText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        return text.Length > 200_000 ? text[..200_000] : text;
    }

    public static string? TrimForFailure(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return value.Length > 1000 ? value[..1000] : value;
    }
}
