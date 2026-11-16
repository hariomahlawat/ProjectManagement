using System.Text.RegularExpressions;
using ProjectManagement.Services.SearchV2.Query;

namespace ProjectManagement.Services.Search;

/// <summary>
/// The typed text split for basic-engine sources that match a phrase with ILIKE (IPR, IT
/// projects): the phrase without "-word" exclusions, and one PostgreSQL pattern per excluded
/// word, matched case-insensitively (<c>~*</c>) against the full stored text, so a record is
/// left out even when the word is outside what the result shows. Each pattern matches the
/// word whole, as typed (hyphens kept), with its usual endings, as the gateway's check does.
/// </summary>
internal sealed record SearchPhraseQuery(string Phrase, IReadOnlyList<string> ExcludedPatterns)
{
    public static SearchPhraseQuery Parse(string query)
    {
        var normalized = new SearchQueryNormalizer().Normalize(query ?? string.Empty);
        var patterns = normalized.ExcludedTyped
            .Select(static value => value.TrimStart('-').Trim())
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static word => $@"(^|[^[:alnum:]]){Regex.Escape(word)}(s|es|ed|ing|er|ers)?($|[^[:alnum:]])")
            .ToArray();
        return new SearchPhraseQuery(normalized.Searched, patterns);
    }
}
