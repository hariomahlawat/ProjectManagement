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
        var expansions = ResolveExpansions(exact);
        var webSearch = BuildWebSearchQuery(original, expansions);
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

    private static IReadOnlyList<string> ResolveExpansions(string exact)
    {
        if (Terminology.TryGetValue(exact, out var direct))
        {
            return direct;
        }

        var values = new List<string>();
        foreach (var pair in Terminology)
        {
            if (exact.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
            {
                values.AddRange(pair.Value);
            }
        }

        return values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();
    }

    private static string BuildWebSearchQuery(string original, IReadOnlyList<string> expansions)
    {
        if (expansions.Count == 0)
        {
            return original;
        }

        var alternatives = new List<string> { original };
        alternatives.AddRange(expansions.Select(value => value.Contains(' ') ? $"\"{value.Replace("\"", string.Empty)}\"" : value));
        return string.Join(" OR ", alternatives);
    }

    [GeneratedRegex("[\\p{L}\\p{N}][\\p{L}\\p{N}._/-]*", RegexOptions.CultureInvariant)]
    private static partial Regex HighlightTokenRegex();
}
