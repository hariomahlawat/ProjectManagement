using System.Globalization;
using System.Text;

namespace ProjectManagement.Utilities.Reporting;

/// <summary>
/// Normalises publication text before both planning and rendering so pagination and the PDF
/// text layer operate on the same clean Unicode stream.
/// </summary>
public static class CompendiumPublicationTextSanitizer
{
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Normalize(NormalizationForm.FormC);

        var builder = new StringBuilder(normalized.Length);
        foreach (var rune in normalized.EnumerateRunes())
        {
            if (rune.Value == '\n')
            {
                builder.Append('\n');
                continue;
            }

            if (rune.Value == '\t')
            {
                builder.Append(' ');
                continue;
            }

            if (rune.Value == 0x00A0)
            {
                builder.Append(' ');
                continue;
            }

            if (rune.Value is 0x00AD or 0x2011)
            {
                builder.Append('-');
                continue;
            }

            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control
                or UnicodeCategory.Format
                or UnicodeCategory.PrivateUse
                or UnicodeCategory.OtherNotAssigned
                or UnicodeCategory.Surrogate)
            {
                continue;
            }

            builder.Append(rune.ToString());
        }

        var lines = builder.ToString()
            .Split('\n')
            .Select(line => line.TrimEnd())
            .ToArray();
        return string.Join('\n', lines).Trim();
    }
}
