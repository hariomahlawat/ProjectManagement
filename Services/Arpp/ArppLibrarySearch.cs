using ProjectManagement.Models.Arpp;

namespace ProjectManagement.Services.Arpp;

/// <summary>
/// Defines the public-library search contract in one place so navigation,
/// document rows, current position and project history use the same semantics.
/// </summary>
public static class ArppLibrarySearch
{
    private static readonly IReadOnlyDictionary<ArppCategory, string[]> CategoryAliases =
        new Dictionary<ArppCategory, string[]>
        {
            [ArppCategory.New] = ["new"],
            [ArppCategory.CommittedLiability] = ["cl", "committed liability", "committed"],
            [ArppCategory.CarryForward] = ["cf", "carry forward", "carry-forward"],
            [ArppCategory.Delisted] = ["delisted", "delist"]
        };

    public static string? Normalize(string? query)
    {
        var value = query?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public static IReadOnlyList<ArppCategory> ResolveCategories(string? query)
    {
        var normalized = NormalizeTerm(query);
        if (normalized is null)
        {
            return Array.Empty<ArppCategory>();
        }

        return CategoryAliases
            .Where(pair => pair.Value.Any(alias => AliasMatches(alias, normalized)))
            .Select(pair => pair.Key)
            .ToArray();
    }

    public static bool Matches(ArppLibraryRow row, string query)
        => MatchesValues(
               query,
               row.SerialNumber,
               row.ProjectReference,
               row.ProjectName,
               row.Cfa,
               row.Fund,
               row.DfpdsSchedule)
           || MatchesCategory(row.Category, query);

    public static bool Matches(ArppLibraryCurrentRow row, string query)
        => MatchesValues(
               query,
               row.SerialNumber,
               row.ProjectReference,
               row.ProjectName,
               row.SourceIssueName,
               row.Cfa,
               row.Fund,
               row.DfpdsSchedule)
           || MatchesCategory(row.Category, query);

    public static bool Matches(ArppLibraryUnlinkedRow row, string query)
        => MatchesValues(
               query,
               row.SerialNumber,
               row.ProjectReference,
               row.SourceIssueName,
               row.Cfa,
               row.Fund,
               row.DfpdsSchedule)
           || MatchesCategory(row.Category, query);

    public static bool MatchesCategory(ArppCategory category, string query)
    {
        var normalized = NormalizeTerm(query);
        return normalized is not null
               && CategoryAliases.TryGetValue(category, out var aliases)
               && aliases.Any(alias => AliasMatches(alias, normalized));
    }

    public static bool Contains(string? value, string query)
        => !string.IsNullOrWhiteSpace(value)
           && value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesValues(string query, params string?[] values)
        => values.Any(value => Contains(value, query));

    private static bool AliasMatches(string alias, string normalizedQuery)
    {
        var normalizedAlias = NormalizeTerm(alias)!;

        if (normalizedQuery.Length <= 2)
        {
            return string.Equals(normalizedAlias, normalizedQuery, StringComparison.Ordinal);
        }

        return normalizedAlias.Contains(normalizedQuery, StringComparison.Ordinal)
               || normalizedQuery.Contains(normalizedAlias, StringComparison.Ordinal);
    }

    private static string? NormalizeTerm(string? value)
    {
        var normalized = Normalize(value);
        if (normalized is null)
        {
            return null;
        }

        return string.Join(
            " ",
            normalized
                .ToLowerInvariant()
                .Replace('-', ' ')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
