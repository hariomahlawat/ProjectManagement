using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using ProjectManagement.Services.SearchV2.Models;

namespace ProjectManagement.Services.SearchV2.Query;

public interface ISearchHighlightService
{
    IReadOnlyList<SearchTextSegment> Highlight(string? text, IReadOnlyList<string> terms);
    string? BuildSnippet(string? structuredText, string? narrativeText, IReadOnlyList<string> terms);
    string PlainLegacySnippet(string? value);
}

public sealed partial class SearchHighlightService : ISearchHighlightService
{
    private readonly SearchV2Options _options;

    public SearchHighlightService(IOptions<SearchV2Options> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public IReadOnlyList<SearchTextSegment> Highlight(string? text, IReadOnlyList<string> terms)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<SearchTextSegment>();
        }

        var effectiveTerms = terms
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(term => term.Length)
            .Take(12)
            .ToArray();

        if (effectiveTerms.Length == 0)
        {
            return [new SearchTextSegment(text, false)];
        }

        var pattern = string.Join('|', effectiveTerms.Select(BuildHighlightPattern));
        var matches = Regex.Matches(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (matches.Count == 0)
        {
            return [new SearchTextSegment(text, false)];
        }

        var segments = new List<SearchTextSegment>();
        var cursor = 0;
        foreach (Match match in matches)
        {
            if (match.Index < cursor)
            {
                continue;
            }

            if (match.Index > cursor)
            {
                segments.Add(new SearchTextSegment(text[cursor..match.Index], false));
            }

            segments.Add(new SearchTextSegment(match.Value, true));
            cursor = match.Index + match.Length;
        }

        if (cursor < text.Length)
        {
            segments.Add(new SearchTextSegment(text[cursor..], false));
        }

        return segments;
    }

    public string? BuildSnippet(string? structuredText, string? narrativeText, IReadOnlyList<string> terms)
    {
        var structured = SearchTextQuality.SanitizeForDisplay(structuredText);
        var narrative = SearchTextQuality.SanitizeForDisplay(narrativeText);
        var narrativeQuality = SearchTextQuality.Score(narrativeText);

        var source = ChooseSource(structured, narrative, narrativeQuality, terms);
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        var normalized = WhitespaceRegex().Replace(source, " ").Trim();
        var max = Math.Max(120, _options.MaxSnippetCharacters);
        if (normalized.Length <= max)
        {
            return normalized;
        }

        var matchIndex = FindFirstMatch(normalized, terms);
        var half = max / 2;
        var start = matchIndex < 0 ? 0 : Math.Max(0, matchIndex - half);
        var end = Math.Min(normalized.Length, start + max);

        if (end - start < max && start > 0)
        {
            start = Math.Max(0, end - max);
        }

        while (start > 0 && start < normalized.Length && !char.IsWhiteSpace(normalized[start - 1]))
        {
            start--;
        }

        while (end < normalized.Length && end > 0 && !char.IsWhiteSpace(normalized[end - 1]))
        {
            end--;
        }

        var value = normalized[start..Math.Max(start, end)].Trim();
        return $"{(start > 0 ? "…" : string.Empty)}{value}{(end < normalized.Length ? "…" : string.Empty)}";
    }

    public string PlainLegacySnippet(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var stripped = value
            .Replace("<mark>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("</mark>", string.Empty, StringComparison.OrdinalIgnoreCase);
        return SearchTextQuality.SanitizeForDisplay(stripped);
    }

    private static string? ChooseSource(string? structured, string? narrative, double narrativeQuality, IReadOnlyList<string> terms)
    {
        if (!string.IsNullOrWhiteSpace(structured) && FindFirstMatch(structured, terms) >= 0)
        {
            return structured;
        }

        if (!string.IsNullOrWhiteSpace(narrative)
            && narrativeQuality >= .42d
            && FindFirstMatch(narrative, terms) >= 0)
        {
            return narrative;
        }

        if (!string.IsNullOrWhiteSpace(structured))
        {
            return structured;
        }

        return narrativeQuality >= .60d && !string.IsNullOrWhiteSpace(narrative) ? narrative : null;
    }

    private static string BuildHighlightPattern(string term)
    {
        var escaped = Regex.Escape(term);
        return term.All(char.IsLetterOrDigit)
            ? $@"(?<![\p{{L}}\p{{N}}]){escaped}[\p{{L}}\p{{N}}]*(?![\p{{L}}\p{{N}}])"
            : escaped;
    }

    private static int FindFirstMatch(string text, IReadOnlyList<string> terms)
    {
        var best = -1;
        foreach (var term in terms)
        {
            if (string.IsNullOrWhiteSpace(term)) continue;
            var index = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && (best < 0 || index < best)) best = index;
        }
        return best;
    }

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
