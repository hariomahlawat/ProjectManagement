using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProjectManagement.Services.SearchV2.Query;

/// <summary>
/// Builds concise, user-facing evidence for why a result matched. Evidence is based on
/// coverage of the complete normalized query, not on the first field containing any one
/// token. Search/ranking channels remain diagnostic-only implementation details.
/// </summary>
public static partial class SearchMatchEvidenceResolver
{
    public static string? Resolve(
        NormalizedSearchQuery query,
        string? title,
        string? structuredText,
        string? narrativeText,
        string? metadataJson,
        string entityType,
        string channels)
    {
        if (string.IsNullOrWhiteSpace(query.Exact)) return null;

        if (ContainsChannel(channels, "exact_identifier") || ContainsChannel(channels, "identifier_prefix"))
        {
            return "Identifier";
        }

        // A configured title-phrase alias represents the complete query concept even when
        // the literal tokens differ (for example HIGH TECH ↔ HI TECH).
        if (ContainsChannel(channels, "alias_title_phrase")) return "Title";

        var terms = QueryTerms(query.Exact);
        if (terms.Length == 0) return null;

        var fields = new List<FieldCoverage>();
        AddField(fields, "Title", title, terms,
            allowPrefix: ContainsChannel(channels, "title_token_prefix") || ContainsChannel(channels, "title_prefix"));

        AddMetadataFields(fields, metadataJson, terms);
        AddField(fields, "structured details", structuredText, terms, allowPrefix: false);
        AddField(fields, NarrativeLabel(entityType), narrativeText, terms, allowPrefix: false);

        // Prefer one authoritative field that explains the complete query.
        var single = fields.FirstOrDefault(field => field.CoveredTerms.Count == terms.Length);
        if (single is not null) return single.Label;

        var remaining = new HashSet<string>(terms, StringComparer.OrdinalIgnoreCase);
        var selected = new List<string>();
        foreach (var field in fields)
        {
            var contributes = field.CoveredTerms.Any(remaining.Contains);
            if (!contributes) continue;

            selected.Add(field.Label);
            foreach (var term in field.CoveredTerms) remaining.Remove(term);
            if (remaining.Count == 0) break;
        }

        if (remaining.Count == 0 && selected.Count > 0)
        {
            return string.Join(" + ", selected.Distinct(StringComparer.OrdinalIgnoreCase));
        }

        if (ContainsChannel(channels, "alias") || ContainsChannel(channels, "alias_prefix") || ContainsChannel(channels, "configured_alias_fts"))
        {
            return "Alias";
        }

        if (ContainsChannel(channels, "name")) return "Name";
        if (ContainsChannel(channels, "title_fuzzy")) return "similar title";
        if (ContainsChannel(channels, "fuzzy")) return "similar terminology";
        return fields.FirstOrDefault(field => field.CoveredTerms.Count > 0)?.Label;
    }

    private static void AddMetadataFields(List<FieldCoverage> fields, string? metadataJson, IReadOnlyList<string> terms)
    {
        if (string.IsNullOrWhiteSpace(metadataJson)) return;
        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            if (!document.RootElement.TryGetProperty("matchFields", out var matchFields)
                || matchFields.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var property in matchFields.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String) continue;
                AddField(fields, Humanize(property.Name), property.Value.GetString(), terms, allowPrefix: false);
            }
        }
        catch (JsonException)
        {
            // Search metadata is display enrichment. Malformed legacy JSON must never make
            // otherwise valid search results fail.
        }
    }

    private static void AddField(
        List<FieldCoverage> fields,
        string label,
        string? value,
        IReadOnlyList<string> terms,
        bool allowPrefix)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var words = WordRegex().Matches(Normalize(value))
            .Select(match => match.Value)
            .Where(word => word.Length > 0)
            .ToArray();

        var covered = terms
            .Where(term => words.Any(word => string.Equals(word, term, StringComparison.OrdinalIgnoreCase)
                                             || (allowPrefix && word.StartsWith(term, StringComparison.OrdinalIgnoreCase))))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (covered.Length > 0) fields.Add(new FieldCoverage(label, covered));
    }

    private static string[] QueryTerms(string exact) => WordRegex().Matches(exact)
        .Select(match => match.Value)
        .Where(term => term.Length >= 2)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.ToLowerInvariant())
        {
            var category = char.GetUnicodeCategory(ch);
            builder.Append(ch == '_' || category == UnicodeCategory.DashPunctuation ? ' ' : ch);
        }
        return builder.ToString();
    }

    private static string NarrativeLabel(string entityType) => entityType is "ProjectDocument" or "DocRepoDocument"
        ? "document text"
        : string.Equals(entityType, "Project", StringComparison.OrdinalIgnoreCase)
            ? "Project Brief"
            : "Description";

    private static string Humanize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Metadata";
        return CamelBoundaryRegex().Replace(value.Trim(), "$1 $2");
    }

    private static bool ContainsChannel(string channels, string channel) => (channels ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Any(value => string.Equals(value, channel, StringComparison.Ordinal));

    private sealed record FieldCoverage(string Label, IReadOnlyList<string> CoveredTerms);

    [GeneratedRegex("[\\p{L}\\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();

    [GeneratedRegex("([a-z0-9])([A-Z])", RegexOptions.CultureInvariant)]
    private static partial Regex CamelBoundaryRegex();
}
