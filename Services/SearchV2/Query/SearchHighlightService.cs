using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using ProjectManagement.Services.SearchV2.Models;

namespace ProjectManagement.Services.SearchV2.Query;

public interface ISearchHighlightService
{
    /// <param name="stems">English stems, marked only with their inflections (<see cref="SearchInflections"/>).</param>
    IReadOnlyList<SearchTextSegment> Highlight(string? text, IReadOnlyList<string> terms, IReadOnlyList<string>? stems = null);
    string? BuildSnippet(string? structuredText, string? narrativeText, IReadOnlyList<string> terms, IReadOnlyList<string>? stems = null);
    string PlainLegacySnippet(string? value);
}

public sealed partial class SearchHighlightService : ISearchHighlightService
{
    private readonly SearchV2Options _options;

    public SearchHighlightService(IOptions<SearchV2Options> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public IReadOnlyList<SearchTextSegment> Highlight(string? text, IReadOnlyList<string> terms, IReadOnlyList<string>? stems = null)
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

        var stemPatterns = (stems ?? Array.Empty<string>())
            .Where(stem => !string.IsNullOrWhiteSpace(stem))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .Select(SearchInflections.Pattern)
            .ToArray();
        if (effectiveTerms.Length == 0 && stemPatterns.Length == 0)
        {
            return [new SearchTextSegment(text, false)];
        }

        var pattern = string.Join('|', effectiveTerms.Select(BuildHighlightPattern).Concat(stemPatterns));
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

    public string? BuildSnippet(string? structuredText, string? narrativeText, IReadOnlyList<string> terms, IReadOnlyList<string>? stems = null)
    {
        var structured = SearchTextQuality.SanitizeForDisplay(structuredText);
        var narrative = SearchTextQuality.SanitizeForDisplay(narrativeText);
        var narrativeQuality = SearchTextQuality.Score(narrativeText);
        stems ??= Array.Empty<string>();

        var source = ChooseSource(structured, narrative, narrativeQuality, terms, stems);
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        var normalized = WhitespaceRegex().Replace(source, " ").Trim();
        var max = Math.Max(120, _options.MaxSnippetCharacters);

        // Start a few words before the match, not with it centred and not with the text's
        // opening words: phones show two lines of the excerpt, and a match further in fell
        // below them. Short text is trimmed at the front too when the match is well into it.
        var matchIndex = FindFirstMatch(normalized, terms, stems);
        var lead = Math.Min(60, max / 5);
        if (normalized.Length <= max && matchIndex <= lead)
        {
            return normalized;
        }

        var start = matchIndex < 0 ? 0 : Math.Max(0, matchIndex - lead);
        var end = Math.Min(normalized.Length, start + max);

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

    private static string? ChooseSource(string? structured, string? narrative, double narrativeQuality, IReadOnlyList<string> terms, IReadOnlyList<string> stems)
    {
        if (!string.IsNullOrWhiteSpace(structured) && FindFirstMatch(structured, terms, stems) >= 0)
        {
            return structured;
        }

        if (!string.IsNullOrWhiteSpace(narrative)
            && narrativeQuality >= .42d
            && FindFirstMatch(narrative, terms, stems) >= 0)
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

    private static int FindFirstMatch(string text, IReadOnlyList<string> terms, IReadOnlyList<string>? stems = null)
    {
        var best = -1;
        // The same patterns the highlighter uses: a term counts where it starts a word ("ir"
        // in "IR sensor", not in "their"), so the chosen excerpt is one that gets marked.
        foreach (var term in terms)
        {
            if (string.IsNullOrWhiteSpace(term)) continue;
            var match = Regex.Match(text, BuildHighlightPattern(term), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success && (best < 0 || match.Index < best)) best = match.Index;
        }
        foreach (var stem in stems ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(stem)) continue;
            var match = Regex.Match(text, SearchInflections.Pattern(stem), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success && (best < 0 || match.Index < best)) best = match.Index;
        }
        return best;
    }

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
