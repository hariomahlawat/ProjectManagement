using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Services.SearchV2.Indexing;
using ProjectManagement.Services.SearchV2.Models;
using ProjectManagement.Services.SearchV2.Security;

namespace ProjectManagement.Services.SearchV2.Query;

public interface ISearchV2Engine
{
    Task<SearchResponse> SearchAsync(SearchRequest request, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<IReadOnlyList<SearchSuggestion>> SuggestAsync(string query, ClaimsPrincipal user, int? limit, CancellationToken cancellationToken);
}

public sealed class SearchEngine : ISearchV2Engine
{
    private readonly ApplicationDbContext _db;
    private readonly ISearchIndexStore _store;
    private readonly ISearchAccessContextFactory _accessFactory;
    private readonly ISearchQueryNormalizer _normalizer;
    private readonly ISearchAliasProvider _aliases;
    private readonly ISearchHighlightService _highlight;
    private readonly ISearchCursorCodec _cursorCodec;
    private readonly SearchV2Options _options;
    private readonly ILogger<SearchEngine> _logger;

    public SearchEngine(
        ApplicationDbContext db,
        ISearchIndexStore store,
        ISearchAccessContextFactory accessFactory,
        ISearchQueryNormalizer normalizer,
        ISearchAliasProvider aliases,
        ISearchHighlightService highlight,
        ISearchCursorCodec cursorCodec,
        IOptions<SearchV2Options> options,
        ILogger<SearchEngine> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _accessFactory = accessFactory ?? throw new ArgumentNullException(nameof(accessFactory));
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
        _aliases = aliases ?? throw new ArgumentNullException(nameof(aliases));
        _highlight = highlight ?? throw new ArgumentNullException(nameof(highlight));
        _cursorCodec = cursorCodec ?? throw new ArgumentNullException(nameof(cursorCodec));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SearchResponse> SearchAsync(SearchRequest request, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(user);

        var normalized = _normalizer.Normalize(request.Query);
        if (string.IsNullOrWhiteSpace(normalized.Original))
        {
            return new SearchResponse(normalized.Original, Array.Empty<SearchResult>(), 0, SearchFacets.Empty, null, 0, true);
        }

        var aliasRules = await _aliases.GetActiveAsync(cancellationToken);
        var expanded = SearchAliasQueryExpander.Expand(normalized.Exact, aliasRules);
        normalized = normalized with
        {
            WebSearchQuery = expanded.WebSearchQuery,
            Expansions = expanded.Expansions
        };

        var indexHealth = _options.Enabled
            ? await _store.GetHealthAsync(cancellationToken)
            : new SearchIndexHealth(false, 0, 0, 0, 0, 0, null, null, null);
        if (!indexHealth.IsReady || indexHealth.IndexVersion != _options.ProjectionVersion)
        {
            return SearchResponse.NotReady(normalized.Original);
        }

        var stopwatch = Stopwatch.StartNew();
        var pageSize = Math.Clamp(request.PageSize ?? _options.PageSize, 5, Math.Max(5, _options.MaxPageSize));
        var cursorKey = BuildCursorKey(
            normalized.Original,
            request.Categories,
            request.Sources,
            request.ProjectIds,
            request.Statuses,
            request.FileTypes,
            request.Stages,
            request.DateFrom,
            request.DateTo);
        if (!_cursorCodec.TryDecode(cursorKey, request.Cursor, indexHealth.ActiveGeneration, out var afterRank)) afterRank = 0;
        var access = await _accessFactory.CreateAsync(user, cancellationToken);
        var prefixTsQuery = BuildPrefixTsQuery(normalized.Exact);

        try
        {
            await _db.Database.OpenConnectionAsync(cancellationToken);
            await using var command = _db.Database.GetDbConnection().CreateCommand();
            var sqlParts = BuildScopeSql(
                command,
                access,
                request.Categories,
                request.Sources,
                request.ProjectIds,
                request.Statuses,
                request.FileTypes,
                request.Stages,
                request.DateFrom,
                request.DateTo);
            command.CommandText = BuildSearchSql(sqlParts);
            Add(command, "indexVersion", _options.ProjectionVersion);
            Add(command, "exact", normalized.Exact);
            Add(command, "webQuery", normalized.WebSearchQuery);
            Add(command, "prefixTsQuery", prefixTsQuery);
            Add(command, "fuzzyThreshold", _options.FuzzyThreshold);
            Add(command, "rrfK", _options.ReciprocalRankK);
            Add(command, "afterRank", afterRank);
            Add(command, "take", pageSize + 1);
            Add(command, "maxSnippetSourceCharacters", Math.Max(_options.MaxSnippetCharacters * 2, 600));

            var rows = new List<SearchRow>(pageSize + 1);
            long totalHits = 0;
            SearchFacets facets = SearchFacets.Empty;

            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                // Summary/facets are returned as their own result set so useful
                // disjunctive facets remain available even when the active filter
                // combination currently produces zero result rows.
                if (await reader.ReadAsync(cancellationToken))
                {
                    totalHits = reader.GetInt64(reader.GetOrdinal("TotalHits"));
                    facets = new SearchFacets(
                        ParseFacets(reader, "CategoryFacets"),
                        ParseFacets(reader, "SourceFacets"),
                        ParseFacets(reader, "ProjectFacets"),
                        ParseFacets(reader, "StatusFacets"),
                        ParseFacets(reader, "FileTypeFacets"),
                        ParseFacets(reader, "StageFacets"));
                }

                if (await reader.NextResultAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        rows.Add(ReadRow(reader));
                    }
                }
            }

            var hasMore = rows.Count > pageSize;
            if (hasMore) rows.RemoveAt(rows.Count - 1);

            var results = rows.Select(row => ToResult(row, normalized)).ToArray();
            var nextCursor = hasMore && rows.Count > 0
                ? _cursorCodec.Encode(cursorKey, rows[^1].GlobalRank, indexHealth.ActiveGeneration)
                : null;

            string? correctedQuery = null;
            if (ShouldOfferCorrection(normalized, rows))
            {
                correctedQuery = await FindCorrectionAsync(_db.Database.GetDbConnection(), normalized.Exact, access, cancellationToken);
                if (string.Equals(correctedQuery, normalized.Exact, StringComparison.OrdinalIgnoreCase)) correctedQuery = null;
            }

            stopwatch.Stop();
            return new SearchResponse(
                normalized.Original,
                results,
                totalHits,
                facets,
                nextCursor,
                stopwatch.ElapsedMilliseconds,
                true,
                false,
                correctedQuery);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Search V2 query failed; caller may fall back to legacy search.");
            return SearchResponse.NotReady(normalized.Original);
        }
        finally
        {
            try { await _db.Database.CloseConnectionAsync(); } catch { }
        }
    }

    public async Task<IReadOnlyList<SearchSuggestion>> SuggestAsync(
        string query,
        ClaimsPrincipal user,
        int? limit,
        CancellationToken cancellationToken)
    {
        var normalized = _normalizer.Normalize(query);
        if (!_options.Enabled || normalized.Exact.Length < 2 || !await _store.IsReadyAsync(_options.ProjectionVersion, cancellationToken))
        {
            return Array.Empty<SearchSuggestion>();
        }

        var access = await _accessFactory.CreateAsync(user, cancellationToken);
        var take = Math.Clamp(limit ?? _options.SuggestionLimit, 3, 20);
        var prefixTsQuery = BuildPrefixTsQuery(normalized.Exact);
        try
        {
            await _db.Database.OpenConnectionAsync(cancellationToken);
            await using var command = _db.Database.GetDbConnection().CreateCommand();
            var scope = BuildScopeSql(command, access, null, null, null, null, null, null, null, null);
            command.CommandText = $"""
                WITH state AS (
                    SELECT "ActiveGeneration" FROM "SearchIndexState" WHERE "Id" = 1 AND "IndexVersion" = @indexVersion
                ), authorised AS (
                    SELECT e.*
                    FROM "SearchEntries" e
                    JOIN state s ON s."ActiveGeneration" = e."Generation"
                    WHERE e."IndexVersion" = @indexVersion {scope.AuthorizationClause}
                ), scored AS (
                    SELECT e.*,
                           CASE
                               WHEN EXISTS (
                                   SELECT 1 FROM "SearchEntryTerms" t
                                   WHERE t."SearchEntryId" = e."Id" AND t."TermType" = 'Identifier' AND t."NormalizedTerm" = @exact
                               ) THEN 0
                               WHEN EXISTS (
                                   SELECT 1 FROM "SearchEntryTerms" t
                                   WHERE t."SearchEntryId" = e."Id" AND t."TermType" = 'Identifier' AND LEFT(t."NormalizedTerm", LENGTH(@exact)) = @exact
                               ) THEN 1
                               WHEN e."NormalizedTitle" = @exact THEN 2
                               WHEN LEFT(e."NormalizedTitle", LENGTH(@exact)) = @exact THEN 3
                               WHEN @prefixTsQuery <> '' AND to_tsvector('simple', e."Title") @@ to_tsquery('simple', @prefixTsQuery) THEN 4
                               WHEN EXISTS (
                                   SELECT 1 FROM "SearchEntryTerms" t
                                   WHERE t."SearchEntryId" = e."Id" AND t."TermType" = 'Alias' AND t."NormalizedTerm" = @exact
                               ) THEN 5
                               WHEN EXISTS (
                                   SELECT 1 FROM "SearchEntryTerms" t
                                   WHERE t."SearchEntryId" = e."Id" AND t."TermType" = 'Alias' AND LEFT(t."NormalizedTerm", LENGTH(@exact)) = @exact
                               ) THEN 6
                               ELSE 7
                           END AS priority,
                           GREATEST(similarity(e."NormalizedTitle", @exact), word_similarity(@exact, e."NormalizedTitle")) AS fuzzy_score
                    FROM authorised e
                    WHERE e."NormalizedTitle" = @exact
                       OR LEFT(e."NormalizedTitle", LENGTH(@exact)) = @exact
                       OR (@prefixTsQuery <> '' AND to_tsvector('simple', e."Title") @@ to_tsquery('simple', @prefixTsQuery))
                       OR EXISTS (
                           SELECT 1 FROM "SearchEntryTerms" t
                           WHERE t."SearchEntryId" = e."Id"
                             AND t."TermType" IN ('Identifier', 'Alias')
                             AND LEFT(t."NormalizedTerm", LENGTH(@exact)) = @exact
                       )
                       OR (LENGTH(@exact) >= 4 AND e."NormalizedTitle" % @exact AND similarity(e."NormalizedTitle", @exact) >= @suggestionFuzzyThreshold)
                ), deduplicated AS (
                    SELECT s.*,
                           ROW_NUMBER() OVER (
                               PARTITION BY s."CanonicalEntityType", s."CanonicalEntityKey"
                               ORDER BY s.priority, s.fuzzy_score DESC, s."UpdatedAtUtc" DESC, s."Id"
                           ) AS canonical_rank
                    FROM scored s
                )
                SELECT "Title", "Subtitle", "CanonicalUrl", "SourceModule", "ResultCategory", NULLIF("IdentifierText", '') AS "IdentifierText"
                FROM deduplicated
                WHERE canonical_rank = 1
                ORDER BY priority, fuzzy_score DESC, "UpdatedAtUtc" DESC, "Id"
                LIMIT @take;
                """;
            Add(command, "indexVersion", _options.ProjectionVersion);
            Add(command, "exact", normalized.Exact);
            Add(command, "prefixTsQuery", prefixTsQuery);
            Add(command, "suggestionFuzzyThreshold", Math.Max(_options.FuzzyThreshold, 0.42));
            Add(command, "take", take);

            var results = new List<SearchSuggestion>(take);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new SearchSuggestion(
                    reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5)));
            }
            return results;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "Search V2 suggestions unavailable.");
            return Array.Empty<SearchSuggestion>();
        }
        finally
        {
            try { await _db.Database.CloseConnectionAsync(); } catch { }
        }
    }

    private static string BuildPrefixTsQuery(string exact)
    {
        var lexemes = exact
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => new string(value.Where(char.IsLetterOrDigit).ToArray()))
            .Where(value => value.Length >= 2)
            .Take(5)
            .ToArray();
        if (lexemes.Length == 0) return string.Empty;

        return string.Join(" & ", lexemes.Select((value, index) => index == lexemes.Length - 1 ? $"{value}:*" : value));
    }

    private SearchResult ToResult(SearchRow row, NormalizedSearchQuery query)
    {
        var snippet = _highlight.BuildSnippet(row.StructuredText, row.NarrativeText, query.HighlightTerms);
        var matchedField = MatchedField(row.Channels, row, query);
        var related = (row.RelatedSources ?? string.Empty)
            .Split('·', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseRelatedResult)
            .Where(item => item is not null && !string.Equals(item.SourceModule, row.SourceModule, StringComparison.OrdinalIgnoreCase))
            .Cast<SearchRelatedResult>()
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.SourceModule, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new SearchResult(
            row.Id,
            row.EntityType,
            row.EntityKey,
            row.CanonicalEntityType,
            row.CanonicalEntityKey,
            row.ParentProjectId,
            row.SourceModule,
            row.ResultCategory,
            row.Title,
            _highlight.Highlight(row.Title, query.HighlightTerms),
            row.Subtitle,
            row.CanonicalUrl,
            snippet,
            _highlight.Highlight(snippet, query.HighlightTerms),
            matchedField,
            row.EventDateUtc,
            row.FileType,
            row.Status,
            row.FusionScore,
            row.GlobalRank,
            related,
            row.MetadataJson,
            row.Tier,
            row.Channels);
    }

    private static string? MatchedField(string channels, SearchRow row, NormalizedSearchQuery query)
    {
        if (channels.Contains("exact_identifier", StringComparison.Ordinal)
            || channels.Contains("identifier_prefix", StringComparison.Ordinal)) return "Identifier";
        if (channels.Contains("exact_title", StringComparison.Ordinal)
            || channels.Contains("title_phrase", StringComparison.Ordinal)
            || channels.Contains("title_token_prefix", StringComparison.Ordinal)
            || channels.Contains("title_prefix", StringComparison.Ordinal)
            || query.HighlightTerms.Any(term => row.Title.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            return "Title";
        }

        var metadataMatch = MatchedFieldFromMetadata(row.MetadataJson, query.HighlightTerms);
        if (!string.IsNullOrWhiteSpace(metadataMatch)) return metadataMatch;

        if (channels.Contains("alias", StringComparison.Ordinal)
            || channels.Contains("alias_prefix", StringComparison.Ordinal)) return "Alias";
        if (channels.Contains("name", StringComparison.Ordinal)) return "Name";
        if (query.HighlightTerms.Any(term => (row.StructuredText ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase))) return "structured details";
        if (query.HighlightTerms.Any(term => (row.NarrativeText ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            return row.EntityType is "ProjectDocument" or "DocRepoDocument" ? "OCR text" : "content";
        }
        if (channels.Contains("title_fuzzy", StringComparison.Ordinal)) return "similar title";
        if (channels.Contains("fuzzy", StringComparison.Ordinal)) return "similar terminology";
        return null;
    }

    private static string? MatchedFieldFromMetadata(string? metadataJson, IReadOnlyList<string> terms)
    {
        if (string.IsNullOrWhiteSpace(metadataJson) || terms.Count == 0) return null;
        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            if (!document.RootElement.TryGetProperty("matchFields", out var fields) || fields.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var property in fields.EnumerateObject())
            {
                var value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                if (!string.IsNullOrWhiteSpace(value) && terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase)))
                {
                    return property.Name;
                }
            }
        }
        catch (JsonException)
        {
            // Metadata is non-authoritative display enrichment. Ignore malformed legacy rows.
        }

        return null;
    }

    private static SearchRelatedResult? ParseRelatedResult(string value)
    {
        var separator = value.LastIndexOf(':');
        if (separator <= 0 || separator == value.Length - 1) return null;
        var source = value[..separator].Trim();
        if (!long.TryParse(value[(separator + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)) return null;
        return new SearchRelatedResult(source, source, Math.Max(1, count));
    }

    private static bool ShouldOfferCorrection(NormalizedSearchQuery query, IReadOnlyList<SearchRow> rows)
    {
        if (query.HighlightTerms.Count != 1 || query.Exact.Length is < 4 or > 32) return false;
        if (rows.Count > 3) return false;
        if (rows.Any(row => HasStrongLexicalChannel(row.Channels))) return false;
        return rows.Count == 0 || rows.All(row => row.Channels.Contains("fuzzy", StringComparison.Ordinal));
    }

    private static bool HasStrongLexicalChannel(string channels) =>
        channels.Contains("exact_identifier", StringComparison.Ordinal)
        || channels.Contains("identifier_prefix", StringComparison.Ordinal)
        || channels.Contains("exact_title", StringComparison.Ordinal)
        || channels.Contains("title_phrase", StringComparison.Ordinal)
        || channels.Contains("title_token_prefix", StringComparison.Ordinal)
        || channels.Contains("alias", StringComparison.Ordinal)
        || channels.Contains("alias_prefix", StringComparison.Ordinal)
        || channels.Contains("title_prefix", StringComparison.Ordinal)
        || channels.Contains("name", StringComparison.Ordinal)
        || channels.Contains("simple_fts", StringComparison.Ordinal)
        || channels.Contains("english_fts", StringComparison.Ordinal)
        || channels.Contains("configured_alias_fts", StringComparison.Ordinal);

    private async Task<string?> FindCorrectionAsync(
        DbConnection connection,
        string exact,
        SearchAccessContext access,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var scope = BuildScopeSql(command, access, null, null, null, null, null, null, null, null);
        command.CommandText = $"""
            WITH state AS (
                SELECT "ActiveGeneration" FROM "SearchIndexState" WHERE "Id" = 1 AND "IndexVersion" = @indexVersion
            ), authorised AS (
                SELECT e."NormalizedTitle"
                FROM "SearchEntries" e
                JOIN state s ON s."ActiveGeneration" = e."Generation"
                WHERE e."IndexVersion" = @indexVersion {scope.AuthorizationClause}
            ), vocabulary AS (
                SELECT DISTINCT token
                FROM authorised a,
                     LATERAL regexp_split_to_table(a."NormalizedTitle", '\\s+') AS token
                WHERE LENGTH(token) >= 3
            )
            SELECT token
            FROM vocabulary
            WHERE token <> @exact
              AND similarity(token, @exact) >= 0.62
            ORDER BY similarity(token, @exact) DESC, ABS(LENGTH(token) - LENGTH(@exact)), token
            LIMIT 1;
            """;
        Add(command, "indexVersion", _options.ProjectionVersion);
        Add(command, "exact", exact);
        return (await command.ExecuteScalarAsync(cancellationToken)) as string;
    }

    private string BuildSearchSql(SearchSqlScope scope) => $"""
        WITH state AS (
            SELECT "ActiveGeneration"
            FROM "SearchIndexState"
            WHERE "Id" = 1 AND "IndexVersion" = @indexVersion
        ), authorised AS (
            SELECT e.*
            FROM "SearchEntries" e
            JOIN state s ON s."ActiveGeneration" = e."Generation"
            WHERE e."IndexVersion" = @indexVersion
              {scope.AuthorizationClause}
        ), exact_identifier AS (
            SELECT e."Id", 0 AS tier,
                   ROW_NUMBER() OVER (ORDER BY e."EventDateUtc" DESC NULLS LAST, e."UpdatedAtUtc" DESC, e."Id") AS channel_rank,
                   'exact_identifier'::text AS channel, 5.0::double precision AS weight
            FROM authorised e
            WHERE EXISTS (
                SELECT 1 FROM "SearchEntryTerms" t
                WHERE t."SearchEntryId" = e."Id" AND t."TermType" = 'Identifier' AND t."NormalizedTerm" = @exact
            )
        ), exact_title AS (
            SELECT e."Id", 1 AS tier,
                   ROW_NUMBER() OVER (ORDER BY e."EventDateUtc" DESC NULLS LAST, e."UpdatedAtUtc" DESC, e."Id") AS channel_rank,
                   'exact_title'::text AS channel, 4.5::double precision AS weight
            FROM authorised e
            WHERE e."NormalizedTitle" = @exact
        ), identifier_prefix AS (
            SELECT e."Id", 1 AS tier,
                   ROW_NUMBER() OVER (
                       ORDER BY LENGTH(t."NormalizedTerm"), e."UpdatedAtUtc" DESC, e."Id") AS channel_rank,
                   'identifier_prefix'::text AS channel, 4.0::double precision AS weight
            FROM authorised e
            JOIN "SearchEntryTerms" t ON t."SearchEntryId" = e."Id" AND t."TermType" = 'Identifier'
            WHERE LENGTH(@exact) >= 2
              AND t."NormalizedTerm" <> @exact
              AND LEFT(t."NormalizedTerm", LENGTH(@exact)) = @exact
        ), title_phrase AS (
            SELECT e."Id", 2 AS tier,
                   ROW_NUMBER() OVER (
                       ORDER BY STRPOS(' ' || e."NormalizedTitle" || ' ', ' ' || @exact || ' '),
                                LENGTH(e."NormalizedTitle"),
                                e."UpdatedAtUtc" DESC,
                                e."Id") AS channel_rank,
                   'title_phrase'::text AS channel, 3.6::double precision AS weight
            FROM authorised e
            WHERE e."NormalizedTitle" <> @exact
              AND STRPOS(' ' || e."NormalizedTitle" || ' ', ' ' || @exact || ' ') > 0
        ), alias_matches AS (
            SELECT e."Id", 2 AS tier,
                   ROW_NUMBER() OVER (ORDER BY e."UpdatedAtUtc" DESC, e."Id") AS channel_rank,
                   'alias'::text AS channel, 3.0::double precision AS weight
            FROM authorised e
            WHERE EXISTS (
                SELECT 1 FROM "SearchEntryTerms" t
                WHERE t."SearchEntryId" = e."Id" AND t."TermType" = 'Alias' AND t."NormalizedTerm" = @exact
            )
        ), alias_prefix AS (
            SELECT e."Id", 3 AS tier,
                   ROW_NUMBER() OVER (
                       ORDER BY LENGTH(t."NormalizedTerm"), e."UpdatedAtUtc" DESC, e."Id") AS channel_rank,
                   'alias_prefix'::text AS channel, 1.6::double precision AS weight
            FROM authorised e
            JOIN "SearchEntryTerms" t ON t."SearchEntryId" = e."Id" AND t."TermType" = 'Alias'
            WHERE LENGTH(@exact) >= 2
              AND t."NormalizedTerm" <> @exact
              AND LEFT(t."NormalizedTerm", LENGTH(@exact)) = @exact
        ), title_token_prefix AS (
            SELECT e."Id", 3 AS tier,
                   ROW_NUMBER() OVER (
                       ORDER BY ts_rank_cd(to_tsvector('simple', e."Title"), to_tsquery('simple', @prefixTsQuery)) DESC,
                                LENGTH(e."NormalizedTitle"),
                                e."UpdatedAtUtc" DESC,
                                e."Id") AS channel_rank,
                   'title_token_prefix'::text AS channel, 2.25::double precision AS weight
            FROM authorised e
            WHERE @prefixTsQuery <> ''
              AND e."SearchVectorSimple" @@ to_tsquery('simple', @prefixTsQuery)
              AND to_tsvector('simple', e."Title") @@ to_tsquery('simple', @prefixTsQuery)
        ), title_prefix AS (
            SELECT e."Id", 3 AS tier,
                   ROW_NUMBER() OVER (ORDER BY LENGTH(e."NormalizedTitle"), e."UpdatedAtUtc" DESC, e."Id") AS channel_rank,
                   'title_prefix'::text AS channel, 2.0::double precision AS weight
            FROM authorised e
            WHERE e."NormalizedTitle" <> @exact AND LEFT(e."NormalizedTitle", LENGTH(@exact)) = @exact
        ), name_matches AS (
            SELECT e."Id", 3 AS tier,
                   ROW_NUMBER() OVER (
                       ORDER BY CASE WHEN t."NormalizedTerm" = @exact THEN 0 ELSE 1 END,
                                LENGTH(t."NormalizedTerm"), e."UpdatedAtUtc" DESC, e."Id") AS channel_rank,
                   'name'::text AS channel, 1.75::double precision AS weight
            FROM authorised e
            JOIN "SearchEntryTerms" t ON t."SearchEntryId" = e."Id" AND t."TermType" = 'Name'
            WHERE t."NormalizedTerm" = @exact
               OR (LENGTH(@exact) >= 3 AND LEFT(t."NormalizedTerm", LENGTH(@exact)) = @exact)
        ), simple_fts AS (
            SELECT e."Id", 4 AS tier,
                   ROW_NUMBER() OVER (ORDER BY ts_rank_cd(e."SearchVectorSimple", websearch_to_tsquery('simple', @webQuery)) DESC, e."UpdatedAtUtc" DESC, e."Id") AS channel_rank,
                   'simple_fts'::text AS channel, 1.25::double precision AS weight
            FROM authorised e
            WHERE e."SearchVectorSimple" @@ websearch_to_tsquery('simple', @webQuery)
        ), configured_alias_fts AS (
            SELECT e."Id", 4 AS tier,
                   ROW_NUMBER() OVER (
                       ORDER BY ts_rank_cd(e."SearchVectorSimple", websearch_to_tsquery('simple', a."Expansion")) DESC,
                                e."UpdatedAtUtc" DESC,
                                e."Id") AS channel_rank,
                   'configured_alias_fts'::text AS channel, 1.15::double precision AS weight
            FROM authorised e
            JOIN "SearchAliases" a ON a."IsActive" AND a."NormalizedAlias" = @exact
            WHERE e."SearchVectorSimple" @@ websearch_to_tsquery('simple', a."Expansion")
        ), english_fts AS (
            SELECT e."Id", 5 AS tier,
                   ROW_NUMBER() OVER (
                       ORDER BY (
                           ts_rank_cd(e."SearchVectorEnglish", websearch_to_tsquery('english', @webQuery))
                           * CASE
                               WHEN COALESCE(e."MetadataJson"->>'searchTextQuality', '') ~ '^(0(\.[0-9]+)?|1(\.0+)?)$'
                                   THEN GREATEST(.15::double precision, LEAST(1.0::double precision, (e."MetadataJson"->>'searchTextQuality')::double precision))
                               ELSE 1.0::double precision
                             END
                       ) DESC,
                       e."UpdatedAtUtc" DESC,
                       e."Id") AS channel_rank,
                   'english_fts'::text AS channel, .85::double precision AS weight
            FROM authorised e
            WHERE e."SearchVectorEnglish" @@ websearch_to_tsquery('english', @webQuery)
        ), title_fuzzy AS (
            SELECT e."Id", 6 AS tier,
                   ROW_NUMBER() OVER (
                       ORDER BY similarity(e."NormalizedTitle", @exact) DESC, e."UpdatedAtUtc" DESC, e."Id") AS channel_rank,
                   'title_fuzzy'::text AS channel, 0.5::double precision AS weight
            FROM authorised e
            WHERE LENGTH(@exact) >= 4
              AND e."NormalizedTitle" % @exact
              AND similarity(e."NormalizedTitle", @exact) >= @fuzzyThreshold
        ), fuzzy_matches AS (
            SELECT e."Id", 6 AS tier,
                   ROW_NUMBER() OVER (ORDER BY similarity(e."FuzzyText", @exact) DESC, e."UpdatedAtUtc" DESC, e."Id") AS channel_rank,
                   'fuzzy'::text AS channel, 0.45::double precision AS weight
            FROM authorised e
            WHERE LENGTH(@exact) >= 3
              AND e."FuzzyText" % @exact
              AND similarity(e."FuzzyText", @exact) >= @fuzzyThreshold
        ), channels AS (
            SELECT * FROM exact_identifier
            UNION ALL SELECT * FROM exact_title
            UNION ALL SELECT * FROM identifier_prefix
            UNION ALL SELECT * FROM title_phrase
            UNION ALL SELECT * FROM alias_matches
            UNION ALL SELECT * FROM alias_prefix
            UNION ALL SELECT * FROM title_token_prefix
            UNION ALL SELECT * FROM title_prefix
            UNION ALL SELECT * FROM name_matches
            UNION ALL SELECT * FROM simple_fts
            UNION ALL SELECT * FROM configured_alias_fts
            UNION ALL SELECT * FROM english_fts
            UNION ALL SELECT * FROM title_fuzzy
            UNION ALL SELECT * FROM fuzzy_matches
        ), fused AS (
            SELECT "Id",
                   MIN(tier) AS tier,
                   SUM(weight / (@rrfK + channel_rank)) AS fusion_score,
                   STRING_AGG(DISTINCT channel, ',') AS channels
            FROM channels
            GROUP BY "Id"
        ), candidate_scored AS (
            SELECT e.*, f.tier, f.fusion_score, f.channels
            FROM fused f
            JOIN authorised e ON e."Id" = f."Id"
        ), related_source_counts AS (
            SELECT "CanonicalEntityType", "CanonicalEntityKey", "SourceModule", COUNT(*) AS source_count
            FROM candidate_scored
            GROUP BY "CanonicalEntityType", "CanonicalEntityKey", "SourceModule"
        ), related AS (
            SELECT "CanonicalEntityType", "CanonicalEntityKey",
                   STRING_AGG("SourceModule" || ':' || source_count::text, ' · ' ORDER BY source_count DESC, "SourceModule") AS related_sources
            FROM related_source_counts
            GROUP BY "CanonicalEntityType", "CanonicalEntityKey"
        ), filtered_candidates AS (
            SELECT c.*
            FROM candidate_scored c
            WHERE TRUE {scope.ResultFilterClause}
        ), clustered AS (
            SELECT x.*, r.related_sources
            FROM (
                SELECT c.*,
                       ROW_NUMBER() OVER (
                           PARTITION BY c."CanonicalEntityType", c."CanonicalEntityKey"
                           ORDER BY c.tier, c.fusion_score DESC, c."EventDateUtc" DESC NULLS LAST, c."UpdatedAtUtc" DESC, c."Id"
                       ) AS cluster_rank
                FROM filtered_candidates c
            ) x
            JOIN related r USING ("CanonicalEntityType", "CanonicalEntityKey")
            WHERE x.cluster_rank = 1
        ), ranked AS (
            SELECT c.*,
                   ROW_NUMBER() OVER (
                       ORDER BY c.tier, c.fusion_score DESC, c."EventDateUtc" DESC NULLS LAST, c."UpdatedAtUtc" DESC, c."Id"
                   ) AS global_rank
            FROM clustered c
        ), category_facet_candidates AS (
            SELECT c.* FROM candidate_scored c WHERE TRUE {scope.CategoryFacetFilterClause}
        ), category_facets AS (
            SELECT COALESCE(jsonb_object_agg("ResultCategory", item_count), jsonb_build_object()) AS value
            FROM (
                SELECT "ResultCategory", COUNT(DISTINCT ("CanonicalEntityType", "CanonicalEntityKey")) AS item_count
                FROM category_facet_candidates
                GROUP BY "ResultCategory"
            ) x
        ), source_facet_candidates AS (
            SELECT c.* FROM candidate_scored c WHERE TRUE {scope.SourceFacetFilterClause}
        ), source_facets AS (
            SELECT COALESCE(jsonb_object_agg("SourceModule", item_count), jsonb_build_object()) AS value
            FROM (
                SELECT "SourceModule", COUNT(DISTINCT ("CanonicalEntityType", "CanonicalEntityKey")) AS item_count
                FROM source_facet_candidates
                GROUP BY "SourceModule"
            ) x
        ), status_facet_candidates AS (
            SELECT c.* FROM candidate_scored c WHERE TRUE {scope.StatusFacetFilterClause}
        ), status_facets AS (
            SELECT COALESCE(jsonb_object_agg("Status", item_count), jsonb_build_object()) AS value
            FROM (
                SELECT "Status", COUNT(DISTINCT ("CanonicalEntityType", "CanonicalEntityKey")) AS item_count
                FROM status_facet_candidates
                WHERE NULLIF("Status", '') IS NOT NULL
                GROUP BY "Status"
            ) x
        ), file_type_facet_candidates AS (
            SELECT c.* FROM candidate_scored c WHERE TRUE {scope.FileTypeFacetFilterClause}
        ), file_type_facets AS (
            SELECT COALESCE(jsonb_object_agg("FileType", item_count), jsonb_build_object()) AS value
            FROM (
                SELECT "FileType", COUNT(DISTINCT ("CanonicalEntityType", "CanonicalEntityKey")) AS item_count
                FROM file_type_facet_candidates
                WHERE NULLIF("FileType", '') IS NOT NULL
                GROUP BY "FileType"
            ) x
        ), stage_facet_candidates AS (
            SELECT c.* FROM candidate_scored c WHERE TRUE {scope.StageFacetFilterClause}
        ), stage_facet_values AS (
            SELECT DISTINCT c."CanonicalEntityType", c."CanonicalEntityKey", s.stage
            FROM stage_facet_candidates c
            CROSS JOIN LATERAL (
                SELECT NULLIF(c."MetadataJson"->>'currentStage', '') AS stage
                UNION
                SELECT NULLIF(value, '') AS stage
                FROM jsonb_array_elements_text(COALESCE(c."MetadataJson"->'projectStages', '[]'::jsonb)) value
            ) s
            WHERE s.stage IS NOT NULL
        ), stage_facets AS (
            SELECT COALESCE(jsonb_object_agg(stage, item_count), jsonb_build_object()) AS value
            FROM (
                SELECT stage, COUNT(*) AS item_count
                FROM stage_facet_values
                GROUP BY stage
            ) x
        ), project_facet_candidates AS (
            SELECT c.* FROM candidate_scored c WHERE TRUE {scope.ProjectFacetFilterClause}
        ), project_facet_rows AS (
            SELECT f."ParentProjectId"::text AS value,
                   COALESCE(p."Title", 'Project ' || f."ParentProjectId"::text) AS label,
                   COUNT(DISTINCT (f."CanonicalEntityType", f."CanonicalEntityKey")) AS item_count
            FROM project_facet_candidates f
            LEFT JOIN authorised p
              ON p."EntityType" = 'Project'
             AND p."EntityKey" = f."ParentProjectId"::text
            WHERE f."ParentProjectId" IS NOT NULL
            GROUP BY f."ParentProjectId", p."Title"
        ), project_facets AS (
            SELECT COALESCE(
                jsonb_object_agg(value, jsonb_build_object('label', label, 'count', item_count)),
                jsonb_build_object()) AS value
            FROM project_facet_rows
        )
        SELECT (SELECT COUNT(*) FROM ranked) AS "TotalHits",
               (SELECT value::text FROM category_facets) AS "CategoryFacets",
               (SELECT value::text FROM source_facets) AS "SourceFacets",
               (SELECT value::text FROM project_facets) AS "ProjectFacets",
               (SELECT value::text FROM status_facets) AS "StatusFacets",
               (SELECT value::text FROM file_type_facets) AS "FileTypeFacets",
               (SELECT value::text FROM stage_facets) AS "StageFacets";

        SELECT r."Id", r."EntityType", r."EntityKey", r."CanonicalEntityType", r."CanonicalEntityKey", r."ParentProjectId",
               r."SourceModule", r."ResultCategory", r."Title", r."Subtitle", r."CanonicalUrl",
               LEFT(COALESCE(r."StructuredText", ''), @maxSnippetSourceCharacters) AS "StructuredText",
               CASE
                   WHEN r."NarrativeText" IS NULL THEN NULL
                   ELSE LEFT(
                       regexp_replace(
                           ts_headline('simple', r."NarrativeText", websearch_to_tsquery('simple', @webQuery),
                               'StartSel=<<,StopSel=>>,MaxWords=60,MinWords=15,ShortWord=2,MaxFragments=2,FragmentDelimiter=...'),
                           '<<|>>', '', 'g'),
                       @maxSnippetSourceCharacters)
               END AS "NarrativeText",
               r."Status", r."FileType", r."EventDateUtc", r."UpdatedAtUtc", r."MetadataJson"::text,
               r.tier, r.fusion_score, r.channels, r.global_rank::int AS "GlobalRank", r.related_sources AS "RelatedSources"
        FROM ranked r
        WHERE r.global_rank > @afterRank
        ORDER BY r.global_rank
        LIMIT @take;
        """;

    private static SearchRow ReadRow(DbDataReader reader) => new(
        reader.GetInt64(reader.GetOrdinal("Id")),
        reader.GetString(reader.GetOrdinal("EntityType")),
        reader.GetString(reader.GetOrdinal("EntityKey")),
        reader.GetString(reader.GetOrdinal("CanonicalEntityType")),
        reader.GetString(reader.GetOrdinal("CanonicalEntityKey")),
        NullableInt(reader, "ParentProjectId"),
        reader.GetString(reader.GetOrdinal("SourceModule")),
        reader.GetString(reader.GetOrdinal("ResultCategory")),
        reader.GetString(reader.GetOrdinal("Title")),
        NullableString(reader, "Subtitle"),
        reader.GetString(reader.GetOrdinal("CanonicalUrl")),
        NullableString(reader, "StructuredText"),
        NullableString(reader, "NarrativeText"),
        NullableString(reader, "Status"),
        NullableString(reader, "FileType"),
        NullableDateTimeOffset(reader, "EventDateUtc"),
        reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("UpdatedAtUtc")),
        NullableString(reader, "MetadataJson"),
        reader.GetInt32(reader.GetOrdinal("tier")),
        reader.GetDouble(reader.GetOrdinal("fusion_score")),
        reader.GetString(reader.GetOrdinal("channels")),
        reader.GetInt32(reader.GetOrdinal("GlobalRank")),
        NullableString(reader, "RelatedSources"));

    private static IReadOnlyList<SearchFacetValue> ParseFacets(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal)) return Array.Empty<SearchFacetValue>();
        try
        {
            using var document = JsonDocument.Parse(reader.GetString(ordinal));
            return document.RootElement.EnumerateObject()
                .Select(property =>
                {
                    if (property.Value.ValueKind == JsonValueKind.Object)
                    {
                        var count = property.Value.TryGetProperty("count", out var countNode) ? countNode.GetInt64() : 0;
                        var label = property.Value.TryGetProperty("label", out var labelNode) ? labelNode.GetString() : null;
                        return new SearchFacetValue(property.Name, count, label);
                    }

                    return new SearchFacetValue(property.Name, property.Value.GetInt64());
                })
                .OrderByDescending(value => value.Count)
                .ThenBy(value => value.Label ?? value.Value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (JsonException)
        {
            return Array.Empty<SearchFacetValue>();
        }
    }

    private static SearchSqlScope BuildScopeSql(
        DbCommand command,
        SearchAccessContext access,
        IReadOnlyList<string>? categories,
        IReadOnlyList<string>? sources,
        IReadOnlyList<int>? projectIds,
        IReadOnlyList<string>? statuses,
        IReadOnlyList<string>? fileTypes,
        IReadOnlyList<string>? stages,
        DateOnly? dateFrom,
        DateOnly? dateTo)
    {
        var policyParts = new List<string> { "e.\"RequiredPolicy\" IS NULL" };
        for (var index = 0; index < access.AllowedPolicies.Count; index++)
        {
            var name = $"allowedPolicy{index}";
            Add(command, name, access.AllowedPolicies[index]);
            policyParts.Add($"e.\"RequiredPolicy\" = @{name}");
        }

        var principalParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(access.UserId))
        {
            Add(command, "currentUserId", access.UserId);
            principalParts.Add("(p.\"PrincipalType\" = 'User' AND p.\"PrincipalValue\" = @currentUserId)");
        }

        for (var index = 0; index < access.Roles.Count; index++)
        {
            var name = $"role{index}";
            Add(command, name, access.Roles[index]);
            principalParts.Add($"(p.\"PrincipalType\" = 'Role' AND p.\"PrincipalValue\" = @{name})");
        }

        var principalSql = principalParts.Count == 0 ? "FALSE" : string.Join(" OR ", principalParts);
        var ownerSql = string.IsNullOrWhiteSpace(access.UserId) ? "FALSE" : "e.\"OwnerUserId\" = @currentUserId";
        var authorization = $"""
            AND ({string.Join(" OR ", policyParts)})
            AND (
                e."VisibilityMode" = 0
                OR (e."VisibilityMode" = 1 AND {ownerSql})
                OR (e."VisibilityMode" = 2 AND EXISTS (
                    SELECT 1 FROM "SearchEntryPrincipals" p
                    WHERE p."SearchEntryId" = e."Id" AND ({principalSql})
                ))
            )
            """;

        var filters = new Dictionary<SearchFilterDimension, string?>
        {
            [SearchFilterDimension.Category] = BuildListFilter(command, "ResultCategory", "category", categories),
            [SearchFilterDimension.Source] = BuildListFilter(command, "SourceModule", "source", sources),
            [SearchFilterDimension.Project] = BuildProjectFilter(command, projectIds),
            [SearchFilterDimension.Status] = BuildListFilter(command, "Status", "status", statuses),
            [SearchFilterDimension.FileType] = BuildListFilter(command, "FileType", "fileType", fileTypes),
            [SearchFilterDimension.Stage] = BuildStageFilter(command, stages),
            [SearchFilterDimension.Date] = BuildDateFilter(command, dateFrom, dateTo)
        };

        string Combine(SearchFilterDimension? exclude = null)
        {
            var parts = filters
                .Where(pair => pair.Key != exclude && !string.IsNullOrWhiteSpace(pair.Value))
                .Select(pair => pair.Value!)
                .ToArray();
            return parts.Length == 0 ? string.Empty : $" AND {string.Join(" AND ", parts)}";
        }

        return new SearchSqlScope(
            authorization,
            Combine(),
            Combine(SearchFilterDimension.Category),
            Combine(SearchFilterDimension.Source),
            Combine(SearchFilterDimension.Project),
            Combine(SearchFilterDimension.Status),
            Combine(SearchFilterDimension.FileType),
            Combine(SearchFilterDimension.Stage));
    }

    private static string? BuildListFilter(
        DbCommand command,
        string column,
        string prefix,
        IReadOnlyList<string>? values)
    {
        var effective = values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToArray();
        if (effective is not { Length: > 0 }) return null;

        var names = new List<string>(effective.Length);
        for (var index = 0; index < effective.Length; index++)
        {
            var name = $"{prefix}{index}";
            Add(command, name, effective[index]);
            names.Add($"@{name}");
        }
        return $"c.\"{column}\" IN ({string.Join(",", names)})";
    }

    private static string? BuildProjectFilter(DbCommand command, IReadOnlyList<int>? projectIds)
    {
        var effective = projectIds?.Where(value => value > 0).Distinct().Take(20).ToArray();
        if (effective is not { Length: > 0 }) return null;

        var names = new List<string>(effective.Length);
        for (var index = 0; index < effective.Length; index++)
        {
            var name = $"project{index}";
            Add(command, name, effective[index]);
            names.Add($"@{name}");
        }
        return $"c.\"ParentProjectId\" IN ({string.Join(",", names)})";
    }

    private static string? BuildStageFilter(DbCommand command, IReadOnlyList<string>? stages)
    {
        var effective = stages?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToArray();
        if (effective is not { Length: > 0 }) return null;

        var names = new List<string>(effective.Length);
        for (var index = 0; index < effective.Length; index++)
        {
            var name = $"stage{index}";
            Add(command, name, effective[index]);
            names.Add($"@{name}");
        }
        var inList = string.Join(",", names);
        return $"(COALESCE(c.\"MetadataJson\"->>'currentStage', '') IN ({inList}) OR EXISTS (SELECT 1 FROM jsonb_array_elements_text(COALESCE(c.\"MetadataJson\"->'projectStages', '[]'::jsonb)) stage(value) WHERE stage.value IN ({inList})))";
    }

    private static string? BuildDateFilter(DbCommand command, DateOnly? dateFrom, DateOnly? dateTo)
    {
        var parts = new List<string>();
        if (dateFrom.HasValue)
        {
            Add(command, "dateFrom", dateFrom.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            parts.Add("c.\"EventDateUtc\" >= @dateFrom");
        }
        if (dateTo.HasValue)
        {
            if (dateTo.Value == DateOnly.MaxValue)
            {
                Add(command, "dateToInclusive", DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc));
                parts.Add("c.\"EventDateUtc\" <= @dateToInclusive");
            }
            else
            {
                Add(command, "dateToExclusive", dateTo.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
                parts.Add("c.\"EventDateUtc\" < @dateToExclusive");
            }
        }
        return parts.Count == 0 ? null : $"({string.Join(" AND ", parts)})";
    }

    private static string BuildCursorKey(
        string query,
        IReadOnlyList<string>? categories,
        IReadOnlyList<string>? sources,
        IReadOnlyList<int>? projectIds,
        IReadOnlyList<string>? statuses,
        IReadOnlyList<string>? fileTypes,
        IReadOnlyList<string>? stages,
        DateOnly? dateFrom,
        DateOnly? dateTo)
    {
        static string Normalize(IReadOnlyList<string>? values) => string.Join(
            '\u001F',
            (values ?? Array.Empty<string>())
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase));
        static string NormalizeIds(IReadOnlyList<int>? values) => string.Join(',', (values ?? Array.Empty<int>()).Where(value => value > 0).Distinct().OrderBy(value => value));

        return $"{query.Trim()}\u001EC:{Normalize(categories)}\u001ES:{Normalize(sources)}\u001EP:{NormalizeIds(projectIds)}\u001EST:{Normalize(statuses)}\u001EFT:{Normalize(fileTypes)}\u001ESG:{Normalize(stages)}\u001EDF:{dateFrom:yyyy-MM-dd}\u001EDT:{dateTo:yyyy-MM-dd}";
    }

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = $"@{name}";
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static int? NullableInt(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static string? NullableString(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTimeOffset? NullableDateTimeOffset(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    private sealed record SearchSqlScope(
        string AuthorizationClause,
        string ResultFilterClause,
        string CategoryFacetFilterClause,
        string SourceFacetFilterClause,
        string ProjectFacetFilterClause,
        string StatusFacetFilterClause,
        string FileTypeFacetFilterClause,
        string StageFacetFilterClause);

    private enum SearchFilterDimension
    {
        Category,
        Source,
        Project,
        Status,
        FileType,
        Stage,
        Date
    }

    private sealed record SearchRow(
        long Id,
        string EntityType,
        string EntityKey,
        string CanonicalEntityType,
        string CanonicalEntityKey,
        int? ParentProjectId,
        string SourceModule,
        string ResultCategory,
        string Title,
        string? Subtitle,
        string CanonicalUrl,
        string? StructuredText,
        string? NarrativeText,
        string? Status,
        string? FileType,
        DateTimeOffset? EventDateUtc,
        DateTimeOffset UpdatedAtUtc,
        string? MetadataJson,
        int Tier,
        double FusionScore,
        string Channels,
        int GlobalRank,
        string? RelatedSources);
}
