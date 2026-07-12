using System.Net;
using System.Text.RegularExpressions;

namespace ProjectManagement.Services.ConferenceRemarks;

/// <summary>
/// Converts stored remark content into safe, readable plain text for the compact
/// conference review. Structural line breaks are preserved so numbered directions,
/// paragraphs and lists remain intelligible across all source modules.
/// </summary>
public static class ConferenceDirectionTextFormatter
{
    private static readonly Regex EmbeddedContentRegex = new(
        @"<(script|style)\b[^>]*>.*?</\1\s*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex BreakRegex = new(
        @"<\s*(?:br|hr)\b[^>]*\/?>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ListItemStartRegex = new(
        @"<\s*li\b[^>]*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BlockEndRegex = new(
        @"<\s*/\s*(?:p|div|li|h[1-6]|blockquote|section|article|tr|ul|ol)\s*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex HtmlTagRegex = new(
        @"<[^>]+>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex HorizontalWhitespaceRegex = new(
        @"[^\S\r\n]+",
        RegexOptions.Compiled);

    private static readonly Regex WhitespaceAroundLineBreakRegex = new(
        @"[ \t\f\v]*\n[ \t\f\v]*",
        RegexOptions.Compiled);

    private static readonly Regex ExcessBlankLinesRegex = new(
        @"\n{3,}",
        RegexOptions.Compiled);

    public static string ToDisplayText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        text = EmbeddedContentRegex.Replace(text, string.Empty);
        text = BreakRegex.Replace(text, "\n");
        text = ListItemStartRegex.Replace(text, "• ");
        text = BlockEndRegex.Replace(text, "\n");
        text = HtmlTagRegex.Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text)
            .Replace('\u00A0', ' ')
            .Replace("\u200B", string.Empty, StringComparison.Ordinal);

        text = HorizontalWhitespaceRegex.Replace(text, " ");
        text = WhitespaceAroundLineBreakRegex.Replace(text, "\n");
        text = ExcessBlankLinesRegex.Replace(text, "\n\n");

        return text.Trim();
    }
}
