using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ProjectManagement.Data;

namespace ProjectManagement.Services.SearchV2.Query;

public sealed record SearchAliasRule(string Alias, string NormalizedAlias, string Expansion);

public interface ISearchAliasProvider
{
    Task<IReadOnlyList<SearchAliasRule>> GetActiveAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Runtime source of truth for PRISM search terminology. SearchAliases is deliberately
/// outside EF's authoritative domain model, so this provider reads it with a small,
/// bounded cache and fails open to literal search if the Search V2 schema is unavailable.
/// </summary>
public sealed class SearchAliasProvider : ISearchAliasProvider
{
    private const string CacheKey = "search-v2:active-aliases";

    // Product-owned aliases cover only stable PRISM/defence terminology that must work
    // even before an administrator adds catalogue entries. Keep this list deliberately
    // small; the database remains the extensible runtime terminology source.
    private static readonly SearchAliasRule[] BuiltInRules =
    [
        new("High Tech", "high tech", "hi tech"),
        new("Hi Tech", "hi tech", "high tech")
    ];
    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SearchAliasProvider> _logger;

    public SearchAliasProvider(ApplicationDbContext db, IMemoryCache cache, ILogger<SearchAliasProvider> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<SearchAliasRule>> GetActiveAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(CacheKey, out IReadOnlyList<SearchAliasRule>? cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            await _db.Database.OpenConnectionAsync(cancellationToken);
            await using var command = _db.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT "Alias", "NormalizedAlias", "Expansion"
                FROM "SearchAliases"
                WHERE "IsActive"
                ORDER BY LENGTH("NormalizedAlias") DESC, "NormalizedAlias", "Expansion";
                """;

            var rows = new List<SearchAliasRule>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new SearchAliasRule(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }

            var result = rows
                .Concat(BuiltInRules)
                .Where(rule => !string.IsNullOrWhiteSpace(rule.NormalizedAlias) && !string.IsNullOrWhiteSpace(rule.Expansion))
                .DistinctBy(rule => $"{rule.NormalizedAlias}\u001f{rule.Expansion}", StringComparer.OrdinalIgnoreCase)
                .ToArray();
            _cache.Set(CacheKey, result, TimeSpan.FromMinutes(5));
            return result;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "Search alias catalogue unavailable; built-in Search V2 terminology will be used.");
            return BuiltInRules;
        }
        finally
        {
            try { await _db.Database.CloseConnectionAsync(); } catch { }
        }
    }
}

public static class SearchAliasQueryExpander
{
    public static ExpandedSearchQuery Expand(string exact, IReadOnlyList<SearchAliasRule> rules)
    {
        exact = NormalizeAlias(exact);
        if (string.IsNullOrWhiteSpace(exact) || rules.Count == 0)
        {
            return new ExpandedSearchQuery(exact, string.Empty, Array.Empty<string>(), Array.Empty<string>());
        }

        // Keep the user's literal query immutable. Alias variants are generated on a
        // separate channel so literal FTS, ranking evidence and performance remain
        // independently explainable.
        var variants = new List<string> { exact };
        var expansions = new List<string>();

        foreach (var group in rules
                     .GroupBy(rule => NormalizeAlias(rule.NormalizedAlias), StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(group => group.Key.Length))
        {
            if (string.IsNullOrWhiteSpace(group.Key)) continue;

            var current = variants.ToArray();
            foreach (var variant in current)
            {
                if (!ContainsPhrase(variant, group.Key)) continue;

                foreach (var rule in group)
                {
                    var expansion = CleanExpansion(rule.Expansion);
                    if (string.IsNullOrWhiteSpace(expansion)) continue;

                    var replaced = ReplacePhrase(variant, group.Key, QuoteForWebSearch(expansion));
                    if (!string.Equals(replaced, variant, StringComparison.OrdinalIgnoreCase))
                    {
                        variants.Add(replaced);
                        expansions.Add(expansion);
                    }

                    if (variants.Count >= 12) break;
                }

                if (variants.Count >= 12) break;
            }

            if (variants.Count >= 12) break;
        }

        var aliasVariants = variants
            .Where(variant => !string.Equals(variant, exact, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var aliasExactQueries = aliasVariants
            .Select(variant => NormalizeAlias(variant.Replace('"', ' ')))
            .Where(variant => !string.IsNullOrWhiteSpace(variant))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ExpandedSearchQuery(
            exact,
            string.Join(" OR ", aliasVariants),
            aliasExactQueries,
            expansions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static string NormalizeAlias(string value) => string.Join(' ', (value ?? string.Empty)
        .Replace('-', ' ')
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string CleanExpansion(string value) => string.Join(' ', (value ?? string.Empty)
        .Replace('"', ' ')
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static bool ContainsPhrase(string text, string phrase) => Regex.IsMatch(
        text,
        $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(phrase)}(?![\p{{L}}\p{{N}}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string ReplacePhrase(string text, string phrase, string replacement) => Regex.Replace(
        text,
        $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(phrase)}(?![\p{{L}}\p{{N}}])",
        replacement,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string QuoteForWebSearch(string value) => value.Contains(' ') ? $"\"{value}\"" : value;
}

public sealed record ExpandedSearchQuery(
    string WebSearchQuery,
    string AliasWebSearchQuery,
    IReadOnlyList<string> AliasExactQueries,
    IReadOnlyList<string> Expansions);
