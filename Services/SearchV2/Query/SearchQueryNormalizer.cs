using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ProjectManagement.Services.SearchV2.Query;

public sealed record NormalizedSearchQuery(
    string Original,
    string Exact,
    string WebSearchQuery,
    IReadOnlyList<string> HighlightTerms,
    IReadOnlyList<string> Expansions);

public interface ISearchQueryNormalizer
{
    NormalizedSearchQuery Normalize(string query);
    string NormalizeExact(string value);
}

public sealed partial class SearchQueryNormalizer : ISearchQueryNormalizer
{
    /// <summary>
    /// Queries longer than this are truncated (at a word boundary when possible). Real
    /// navigation and topical queries are far shorter; the clamp bounds SQL/tsquery work.
    /// </summary>
    public const int MaxQueryLength = 256;

    public NormalizedSearchQuery Normalize(string query)
    {
        var original = string.Join(' ', ClampLength(query)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (string.IsNullOrWhiteSpace(original))
        {
            return new NormalizedSearchQuery(string.Empty, string.Empty, string.Empty, Array.Empty<string>(), Array.Empty<string>());
        }

        var exact = NormalizeExact(original);
        // Runtime terminology expansion is owned by SearchAliasProvider/SearchAliasQueryExpander.
        // The normalizer remains deterministic and database-independent.
        var expansions = Array.Empty<string>();
        var webSearch = exact;
        var highlights = HighlightTokenRegex()
            .Matches(original)
            .Select(match => match.Value.Trim('"', '\'', '(', ')'))
            .Concat(exact.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(value => value.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(value => value.Length)
            .ToArray();

        return new NormalizedSearchQuery(original, exact, webSearch, highlights, expansions);
    }

    /// <summary>
    /// Canonical comparison form shared by indexed titles/terms and queries: NFKC, lower
    /// case, dashes/underscores and stand-alone punctuation (quotes, brackets, commas…)
    /// folded to single spaces. Joiners that carry meaning inside a token (0.22, 1:4,
    /// IR&amp;D, 30102/SDD) are kept. Combining marks (for example Devanagari matras) are kept.
    /// </summary>
    public string NormalizeExact(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim();
        try
        {
            text = text.Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            // Invalid surrogate sequences cannot be normalised; keep the literal text.
        }

        text = text.ToLowerInvariant();
        var builder = new StringBuilder(text.Length);
        var previousWasSpace = false;
        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            var keep = IsWordCharacter(ch)
                       || (IsTokenJoiner(ch)
                           && index > 0
                           && index < text.Length - 1
                           && IsWordCharacter(text[index - 1])
                           && IsWordCharacter(text[index + 1]));

            if (!keep || char.IsWhiteSpace(ch))
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

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Separator-insensitive identifier key: letters and digits only, with leading zeros
    /// removed from every digit run, so PRJ-0042, PRJ0042 and "prj 42" share the key
    /// "prj42". Returns an empty string when the value has no digits (not identifier-like).
    /// </summary>
    public static string IdentifierKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var builder = new StringBuilder(value.Length);
        var inDigits = false;
        var digitRunHasValue = false;
        var hasDigit = false;
        foreach (var raw in value.ToLowerInvariant())
        {
            if (char.IsDigit(raw))
            {
                hasDigit = true;
                if (!inDigits)
                {
                    inDigits = true;
                    digitRunHasValue = false;
                }

                if (raw == '0' && !digitRunHasValue) continue;
                digitRunHasValue = true;
                builder.Append(raw);
                continue;
            }

            if (inDigits && !digitRunHasValue) builder.Append('0');
            inDigits = false;
            if (char.IsLetter(raw)) builder.Append(raw);
        }

        if (inDigits && !digitRunHasValue) builder.Append('0');
        return hasDigit ? builder.ToString() : string.Empty;
    }

    /// <summary>
    /// Letter/digit spelling variants of a normalized query: "t90" ↔ "t 90" (the indexed
    /// form of "T-90"), "84mm" ↔ "84 mm". General search-language rule, not per-query tuning.
    /// </summary>
    public static IReadOnlyList<string> AlphanumericVariants(string exact)
    {
        if (string.IsNullOrWhiteSpace(exact)) return Array.Empty<string>();
        var tokens = exact.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var split = string.Join(' ', tokens.Select(token =>
            token.All(char.IsLetterOrDigit) ? LetterDigitBoundaryRegex().Replace(token, " ") : token));

        var joined = new List<string>(tokens.Length);
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (index + 1 < tokens.Length && IsJoinablePair(token, tokens[index + 1]))
            {
                joined.Add(token + tokens[index + 1]);
                index++;
                continue;
            }

            joined.Add(token);
        }

        return new[] { split, string.Join(' ', joined) }
            .Where(variant => !string.Equals(variant, exact, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        static bool IsJoinablePair(string left, string right) =>
            (left.Length <= 4 && left.All(char.IsLetter) && right.All(char.IsDigit))
            || (left.All(char.IsDigit) && right.Length <= 3 && right.All(char.IsLetter));
    }

    /// <summary>All query tokens joined with AND, as quoted to_tsquery operands.</summary>
    public static string BuildTokenTsQuery(string exact) => BuildTsQuery(exact, prefixLastToken: false);

    /// <summary>All query tokens joined with AND; the final token is a prefix operand.</summary>
    public static string BuildPrefixTsQuery(string exact) => BuildTsQuery(exact, prefixLastToken: true);

    /// <summary>Escapes LIKE wildcards with the default backslash escape character.</summary>
    public static string EscapeLike(string value) => (value ?? string.Empty)
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);

    internal static string ClampLength(string? value)
    {
        value ??= string.Empty;
        if (value.Length <= MaxQueryLength) return value;

        var cut = value[..MaxQueryLength];
        if (char.IsHighSurrogate(cut[^1])) cut = cut[..^1];
        var lastSpace = cut.LastIndexOf(' ');
        return lastSpace >= MaxQueryLength / 2 ? cut[..lastSpace] : cut;
    }

    private static string BuildTsQuery(string exact, bool prefixLastToken)
    {
        // Each token is passed to to_tsquery as a quoted operand so PostgreSQL's own
        // parser tokenises it exactly as it tokenised the indexed text (0.22, 30102/sdd,
        // Devanagari with combining marks). Nothing is stripped character-by-character.
        var tokens = (exact ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Any(char.IsLetterOrDigit))
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToArray();
        if (tokens.Length == 0) return string.Empty;

        return string.Join(" & ", tokens.Select((token, index) =>
        {
            var operand = $"'{token.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "''", StringComparison.Ordinal)}'";
            return prefixLastToken && index == tokens.Length - 1 ? operand + ":*" : operand;
        }));
    }

    private static bool IsWordCharacter(char ch)
    {
        if (char.IsLetterOrDigit(ch)) return true;
        var category = char.GetUnicodeCategory(ch);
        return category is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark
            or UnicodeCategory.Surrogate;
    }

    private static bool IsTokenJoiner(char ch) => ch is '.' or '/' or '&' or ':' or '+' or '#' or '@';

    [GeneratedRegex("[\\p{L}\\p{M}\\p{N}][\\p{L}\\p{M}\\p{N}._/-]*", RegexOptions.CultureInvariant)]
    private static partial Regex HighlightTokenRegex();

    [GeneratedRegex("(?<=\\p{L})(?=\\p{Nd})|(?<=\\p{Nd})(?=\\p{L})", RegexOptions.CultureInvariant)]
    private static partial Regex LetterDigitBoundaryRegex();
}
