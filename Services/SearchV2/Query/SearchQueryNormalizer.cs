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
    private static readonly IReadOnlyDictionary<string, string[]> Terminology =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["trg"] = ["training"],
            ["training"] = ["trg"],
            ["tot"] = ["transfer of technology"],
            ["transfer of technology"] = ["tot"],
            ["aon"] = ["approval of necessity"],
            ["approval of necessity"] = ["aon"],
            ["ipr"] = ["intellectual property"],
            ["intellectual property"] = ["ipr"],
            ["arpp"] = ["annual rolled-on procurement plan", "annual rolled on procurement plan"],
            ["annual rolled-on procurement plan"] = ["arpp"],
            ["ffc"] = ["ffc"],
            ["high-tech"] = ["high tech", "hightech"],
            ["high tech"] = ["high-tech", "hightech"]
        };

    public NormalizedSearchQuery Normalize(string query)
    {
        var original = string.Join(' ', (query ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (string.IsNullOrWhiteSpace(original))
        {
            return new NormalizedSearchQuery(string.Empty, string.Empty, string.Empty, Array.Empty<string>(), Array.Empty<string>());
        }

        var exact = NormalizeExact(original);
        var variants = BuildExpansionVariants(exact);
        var expansions = variants
            .Skip(1)
            .Select(variant => variant.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
        var webSearch = string.Join(" OR ", variants.Select(variant => variant.Query).Distinct(StringComparer.OrdinalIgnoreCase));
        var highlights = HighlightTokenRegex()
            .Matches(original)
            .Select(match => match.Value.Trim('"', '\'', '(', ')'))
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
            var normalized = ch is '-' or '_' ? ' ' : ch;
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

    internal static IReadOnlyList<SearchExpansionVariant> BuildExpansionVariants(string exact)
    {
        if (string.IsNullOrWhiteSpace(exact))
        {
            return Array.Empty<SearchExpansionVariant>();
        }

        var variants = new List<SearchExpansionVariant>
        {
            new(exact, exact)
        };

        foreach (var pair in Terminology.OrderByDescending(pair => pair.Key.Length))
        {
            if (!ContainsPhrase(exact, pair.Key))
            {
                continue;
            }

            var currentVariants = variants.ToArray();
            foreach (var current in currentVariants)
            {
                foreach (var expansion in pair.Value)
                {
                    if (string.Equals(expansion, pair.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var replaced = ReplacePhrase(current.Query, pair.Key, QuoteForWebSearch(expansion));
                    if (!string.Equals(replaced, current.Query, StringComparison.OrdinalIgnoreCase))
                    {
                        variants.Add(new SearchExpansionVariant(replaced, expansion));
                    }

                    if (variants.Count >= 8)
                    {
                        break;
                    }
                }

                if (variants.Count >= 8)
                {
                    break;
                }
            }

            if (variants.Count >= 8)
            {
                break;
            }
        }

        return variants
            .DistinctBy(variant => variant.Query, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
    }

    private static bool ContainsPhrase(string text, string phrase)
    {
        var normalizedPhrase = phrase.Replace('-', ' ');
        return Regex.IsMatch(
            text,
            $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(normalizedPhrase)}(?![\p{{L}}\p{{N}}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string ReplacePhrase(string text, string phrase, string replacement)
    {
        var normalizedPhrase = phrase.Replace('-', ' ');
        return Regex.Replace(
            text,
            $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(normalizedPhrase)}(?![\p{{L}}\p{{N}}])",
            replacement,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string QuoteForWebSearch(string value)
    {
        var sanitized = value.Replace("\"", string.Empty, StringComparison.Ordinal).Trim();
        return sanitized.Contains(' ') ? $"\"{sanitized}\"" : sanitized;
    }

    [GeneratedRegex("[\\p{L}\\p{N}][\\p{L}\\p{N}._/-]*", RegexOptions.CultureInvariant)]
    private static partial Regex HighlightTokenRegex();
}

internal sealed record SearchExpansionVariant(string Query, string Value);
