using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ProjectManagement.Services.SearchV2.Query;

/// <summary>
/// Deterministic quality heuristics for indexed/search-display text. The score is
/// deliberately conservative: it is a relevance/display signal, never an OCR
/// correctness decision and never mutates the authoritative document text.
/// </summary>
public static partial class SearchTextQuality
{
    public static double Score(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0d;

        var text = value.AsSpan();
        var meaningful = 0;
        var lettersOrDigits = 0;
        var replacement = 0;
        var controls = 0;
        var symbols = 0;

        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch)) continue;
            meaningful++;

            if (ch == '\uFFFD')
            {
                replacement++;
                continue;
            }

            if (char.IsControl(ch))
            {
                controls++;
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                lettersOrDigits++;
                continue;
            }

            var category = char.GetUnicodeCategory(ch);
            if (category is UnicodeCategory.MathSymbol
                or UnicodeCategory.CurrencySymbol
                or UnicodeCategory.ModifierSymbol
                or UnicodeCategory.OtherSymbol)
            {
                symbols++;
            }
        }

        if (meaningful == 0) return 0d;

        var tokens = TokenRegex().Matches(value)
            .Select(match => match.Value)
            .Where(token => token.Length > 0)
            .Take(300)
            .ToArray();

        var suspiciousTokens = tokens.Count(IsSuspiciousToken);
        var tokenRatio = tokens.Length == 0 ? 0d : suspiciousTokens / (double)tokens.Length;
        var alphaNumericRatio = lettersOrDigits / (double)meaningful;
        var replacementRatio = replacement / (double)meaningful;
        var controlRatio = controls / (double)meaningful;
        var symbolRatio = symbols / (double)meaningful;

        var score = 1d;
        score -= Math.Min(.45d, replacementRatio * 8d + (replacement > 0 ? .12d : 0d));
        score -= Math.Min(.35d, controlRatio * 8d + (controls > 0 ? .08d : 0d));
        score -= Math.Max(0d, symbolRatio - .08d) * 1.75d;
        score -= Math.Max(0d, .72d - alphaNumericRatio) * .9d;
        score -= Math.Max(0d, tokenRatio - .18d) * .7d;

        return Math.Clamp(score, .05d, 1d);
    }

    public static string SanitizeForDisplay(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var normalized = value.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalized.Length);
        var previousWasSpace = false;

        foreach (var ch in normalized)
        {
            var category = char.GetUnicodeCategory(ch);
            var replaceWithSpace = ch == '\uFFFD'
                || char.IsControl(ch)
                || category is UnicodeCategory.Format
                    or UnicodeCategory.PrivateUse
                    or UnicodeCategory.Surrogate;

            if (replaceWithSpace || char.IsWhiteSpace(ch))
            {
                if (!previousWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                    previousWasSpace = true;
                }
                continue;
            }

            builder.Append(ch);
            previousWasSpace = false;
        }

        return WhitespaceRegex().Replace(builder.ToString(), " ").Trim();
    }

    private static bool IsSuspiciousToken(string token)
    {
        if (token.Length <= 1) return true;

        var alphaNumeric = token.Count(char.IsLetterOrDigit);
        if (alphaNumeric == 0) return true;

        var nonAlphaNumeric = token.Length - alphaNumeric;
        return nonAlphaNumeric >= 2 && nonAlphaNumeric >= alphaNumeric;
    }

    [GeneratedRegex("\\S+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
