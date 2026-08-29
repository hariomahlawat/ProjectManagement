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
    public NormalizedSearchQuery Normalize(string query)
    {
        var original = string.Join(' ', (query ?? string.Empty)
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

    public string NormalizeExact(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var previousWasSpace = false;
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            var category = char.GetUnicodeCategory(ch);
            var normalized = ch == '_' || category == UnicodeCategory.DashPunctuation ? ' ' : ch;
            if (char.IsWhiteSpace(normalized))
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                    previousWasSpace = true;
                }
                continue;
            }

            builder.Append(normalized);
            previousWasSpace = false;
        }

        return builder.ToString().Trim();
    }

    [GeneratedRegex("[\\p{L}\\p{N}][\\p{L}\\p{N}._/-]*", RegexOptions.CultureInvariant)]
    private static partial Regex HighlightTokenRegex();
}

