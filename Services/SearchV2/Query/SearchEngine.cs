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
    private readonly ISearchHighlightService _highlight;
    private readonly ISearchCursorCodec _cursorCodec;
    private readonly SearchV2Options _options;
    private readonly ILogger<SearchEngine> _logger;

    public SearchEngine(
        ApplicationDbContext db,
        ISearchIndexStore store,
        ISearchAccessContextFactory accessFactory,
        ISearchQueryNormalizer normalizer,
        ISearchHighlightService highlight,
        ISearchCursorCodec cursorCodec,
        IOptions<SearchV2Options> options,
        ILogger<SearchEngine> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _accessFactory = accessFactory ?? throw new ArgumentNullException(nameof(accessFactory));
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
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

        if (!_options.Enabled || !await _store.IsReadyAsync(_options.IndexVersion, cancellationToken))
        {
            return SearchResponse.NotReady(normalized.Original);
        }

        var stopwatch = Stopwatch.StartNew();
        var pageSize = Math.Clamp(request.PageSize ?? _options.PageSize, 5, Math.Max(5, _options.MaxPageSize));
        var cursorKey = BuildCursorKey(normalized.Original, request.Categories, request.Sources);
        if (!_cursorCodec.TryDecode(cursorKey, request.Cursor, out var afterRank)) afterRank = 0;
        var access = await _accessFactory.CreateAsync(user, cancellationToken);

        try
        {
            await _db.Database.OpenConnectionAsync(cancellationToken);
            await using var command = _db.Database.GetDbConnection().CreateCommand();
            var sqlParts = BuildScopeSql(command, access, request.Categories, request.Sources);
            command.CommandText = BuildSearchSql(sqlParts);
            Add(command, "indexVersion", _options.IndexVersion);
            Add(command, "exact", normalized.Exact);
            Add(command, "webQuery", normalized.WebSearchQuery);
            Add(command, "fuzzyThreshold", _options.FuzzyThreshold);
            Add(command, "rrfK", _options.ReciprocalRankK);
            Add(command, "afterRank", afterRank);
            Add(command, "take", pageSize + 1);

            var rows = new List<SearchRow>(pageSize + 1);
            long totalHits = 0;
            SearchFacets facets = SearchFacets.Empty;

            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (rows.Count == 0)
                    {
                        totalHits = reader.GetInt64(reader.GetOrdinal("TotalHits"));
                        facets = new SearchFacets(
                            ParseFacets(reader, "CategoryFacets"),
                            ParseFacets(reader, "SourceFacets"));
                    }

                    rows.Add(ReadRow(reader));
                }
            }

            var hasMore = rows.Count > pageSize;
            if (hasMore) rows.RemoveAt(rows.Count - 1);

            var results = rows.Select(row => ToResult(row, normalized)).ToArray();
            var nextCursor = hasMore && rows.Count > 0
                ? _cursorCodec.Encode(cursorKey, rows[^1].GlobalRank)
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
        if (!_options.Enabled || normalized.Exact.Length < 2 || !await _store.IsReadyAsync(_options.IndexVersion, cancellationToken))
        {
            return Array.Empty<SearchSuggestion>();
        }

        var access = await _accessFactory.CreateAsync(user, cancellationToken);
        var take = Math.Clamp(limit ?? _options.SuggestionLimit, 3, 20);
        try
        {
            await _db.Database.OpenConnectionAsync(cancellationToken);
            await using var command = _db.Database.GetDbConnection().CreateCommand();
            var scope = BuildScopeSql(command, access, null, null);
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
                               WHEN e."NormalizedTitle" = @exact THEN 0
                               WHEN LEFT(e."NormalizedTitle", LENGTH(@exact)) = @exact THEN 1
                               WHEN EXISTS (
                                   SELECT 1 FROM "SearchEntryTerms" t
                                   WHERE t."SearchEntryId" = e."Id" AND t."NormalizedTerm" = @exact
                               ) THEN 2
                               ELSE 3
                           END AS priority,
                           similarity(e."FuzzyText", @exact) AS fuzzy_score
                    FROM authorised e
                    WHERE e."NormalizedTitle" = @exact
                       OR LEFT(e."NormalizedTitle", LENGTH(@exact)) = @exact
                       OR EXISTS (
                           SELECT 1 FROM "SearchEntryTerms" t
                           WHERE t."SearchEntryId" = e."Id" AND LEFT(t."NormalizedTerm", LENGTH(@exact)) = @exact
                       )
                       OR similarity(e."FuzzyText", @exact) >= @fuzzyThreshold
                )
                SELECT "Title", "Subtitle", "CanonicalUrl", "SourceModule", "ResultCategory", NULLIF("IdentifierText", '') AS "IdentifierText"
                FROM scored
                ORDER BY priority, fuzzy_score DESC, "UpdatedAtUtc" DESC, "Id"
                LIMIT @take;
                """;
            Add(command, "indexVersion", _options.IndexVersion);
            Add(command, "exact", normalized.Exact);
            Add(command, "fuzzyThreshold", Math.Max(_options.FuzzyThreshold, 0.34));
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

    private SearchResult ToResult(SearchRow row, NormalizedSearchQuery query)
    {
        var snippet = _highlight.BuildSnippet(row.StructuredText, row.NarrativeText, query.HighlightTerms);
        var matchedField = MatchedField(row.Channels, row, query);
        var related = (row.RelatedSources ?? string.Empty)
            .Split('·', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(source => !string.Equals(source, row.SourceModule, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(source => new SearchRelatedResult(source, source))
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
            row.MetadataJson);
    }

    private static string? MatchedField(string channels, SearchRow row, NormalizedSearchQuery query)
    {
        if (channels.Contains("exact_identifier", StringComparison.Ordinal)) return "Identifier";
        if (channels.Contains("exact_title", StringComparison.Ordinal) || channels.Contains("title_prefix", StringComparison.Ordinal)) return "Title";
        if (channels.Contains("alias", StringComparison.Ordinal)) return "Alias";
        if (query.HighlightTerms.Any(term => row.Title.Contains(term, StringComparison.OrdinalIgnoreCase))) return "Title";
        if (query.HighlightTerms.Any(term => (row.StructuredText ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase))) return "Details";
        if (query.HighlightTerms.Any(term => (row.NarrativeText ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase))) return "Content";
        if (channels.Contains("fuzzy", StringComparison.Ordinal)) return "Similar terminology";
        return null;
    }

    private static bool ShouldOfferCorrection(NormalizedSearchQuery query, IReadOnlyList<SearchRow> rows)
    {
        if (query.HighlightTerms.Count != 1 || query.Exact.Length is < 4 or > 32) return false;
        return rows.Count == 0 || rows[0].Channels.Contains("fuzzy", StringComparison.Ordinal);
    }

    private async Task<string?> FindCorrectionAsync(
        DbConnection connection,
        string exact,
        SearchAccessContext access,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var scope = BuildScopeSql(command, access, null, null);
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
            WHERE similarity(token, @exact) >= 0.48
            ORDER BY similarity(token, @exact) DESC, LENGTH(token), token
            LIMIT 1;
            """;
        Add(command, "indexVersion", _options.IndexVersion);
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
                   'exact_title'::text AS channel, 4.0::double precision AS weight
            FROM authorised e
            WHERE e."NormalizedTitle" = @exact
        ), alias_matches AS (
            SELECT e."Id", 2 AS tier,
                   ROW_NUMBER() OVER (ORDER BY e."UpdatedAtUtc" DESC, e."Id") AS channel_rank,
                   'alias'::text AS channel, 2.0::double precision AS weight
            FROM authorised e
            WHERE EXISTS (
                SELECT 1 FROM "SearchEntryTerms" t
                WHERE t."SearchEntryId" = e."Id" AND t."TermType" = 'Alias' AND t."NormalizedTerm" = @exact
            )
        ), title_prefix AS (
            SELECT e."Id", 3 AS tier,
                   ROW_NUMBER() OVER (ORDER BY LENGTH(e."NormalizedTitle"), e."UpdatedAtUtc" DESC, e."Id") AS channel_rank,
                   'title_prefix'::text AS channel, 1.6::double precision AS weight
            FROM authorised e
            WHERE e."NormalizedTitle" <> @exact AND LEFT(e."NormalizedTitle", LENGTH(@exact)) = @exact
        ), simple_fts AS (
            SELECT e."Id", 3 AS tier,
                   ROW_NUMBER() OVER (ORDER BY ts_rank_cd(e."SearchVectorSimple", websearch_to_tsquery('simple', @webQuery)) DESC, e."UpdatedAtUtc" DESC, e."Id") AS channel_rank,
                   'simple_fts'::text AS channel, 1.25::double precision AS weight
            FROM authorised e
            WHERE e."SearchVectorSimple" @@ websearch_to_tsquery('simple', @webQuery)
        ), english_fts AS (
            SELECT e."Id", 3 AS tier,
                   ROW_NUMBER() OVER (ORDER BY ts_rank_cd(e."SearchVectorEnglish", websearch_to_tsquery('english', @webQuery)) DESC, e."UpdatedAtUtc" DESC, e."Id") AS channel_rank,
                   'english_fts'::text AS channel, 1.0::double precision AS weight
            FROM authorised e
            WHERE e."SearchVectorEnglish" @@ websearch_to_tsquery('english', @webQuery)
        ), fuzzy_matches AS (
            SELECT e."Id", 4 AS tier,
                   ROW_NUMBER() OVER (ORDER BY similarity(e."FuzzyText", @exact) DESC, e."UpdatedAtUtc" DESC, e."Id") AS channel_rank,
                   'fuzzy'::text AS channel, 0.55::double precision AS weight
            FROM authorised e
            WHERE LENGTH(@exact) >= 3 AND similarity(e."FuzzyText", @exact) >= @fuzzyThreshold
        ), channels AS (
            SELECT * FROM exact_identifier
            UNION ALL SELECT * FROM exact_title
            UNION ALL SELECT * FROM alias_matches
            UNION ALL SELECT * FROM title_prefix
            UNION ALL SELECT * FROM simple_fts
            UNION ALL SELECT * FROM english_fts
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
        ), related AS (
            SELECT "CanonicalEntityType", "CanonicalEntityKey",
                   STRING_AGG(DISTINCT "SourceModule", ' · ') AS related_sources
            FROM candidate_scored
            GROUP BY "CanonicalEntityType", "CanonicalEntityKey"
        ), facet_clustered AS (
            SELECT f.*
            FROM (
                SELECT c.*,
                       ROW_NUMBER() OVER (
                           PARTITION BY c."CanonicalEntityType", c."CanonicalEntityKey"
                           ORDER BY c.tier, c.fusion_score DESC, c."EventDateUtc" DESC NULLS LAST, c."UpdatedAtUtc" DESC, c."Id"
                       ) AS facet_cluster_rank
                FROM candidate_scored c
            ) f
            WHERE f.facet_cluster_rank = 1
        ), filtered_candidates AS (
            SELECT c.*
            FROM candidate_scored c
            WHERE TRUE {scope.FilterClause}
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
        ), category_facets AS (
            SELECT COALESCE(jsonb_object_agg("ResultCategory", item_count), jsonb_build_object()) AS value
            FROM (SELECT "ResultCategory", COUNT(*) AS item_count FROM facet_clustered GROUP BY "ResultCategory") x
        ), source_facets AS (
            SELECT COALESCE(jsonb_object_agg("SourceModule", item_count), jsonb_build_object()) AS value
            FROM (SELECT "SourceModule", COUNT(*) AS item_count FROM facet_clustered GROUP BY "SourceModule") x
        )
        SELECT r."Id", r."EntityType", r."EntityKey", r."CanonicalEntityType", r."CanonicalEntityKey", r."ParentProjectId",
               r."SourceModule", r."ResultCategory", r."Title", r."Subtitle", r."CanonicalUrl", r."StructuredText", r."NarrativeText",
               r."Status", r."FileType", r."EventDateUtc", r."UpdatedAtUtc", r."MetadataJson"::text,
               r.tier, r.fusion_score, r.channels, r.global_rank::int AS "GlobalRank", r.related_sources AS "RelatedSources",
               (SELECT COUNT(*) FROM ranked) AS "TotalHits",
               (SELECT value::text FROM category_facets) AS "CategoryFacets",
               (SELECT value::text FROM source_facets) AS "SourceFacets"
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
                .Select(property => new SearchFacetValue(property.Name, property.Value.GetInt64()))
                .OrderByDescending(value => value.Count)
                .ThenBy(value => value.Value, StringComparer.OrdinalIgnoreCase)
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
        IReadOnlyList<string>? sources)
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

        var filters = new List<string>();
        AddListFilter(command, filters, "ResultCategory", "category", categories);
        AddListFilter(command, filters, "SourceModule", "source", sources);
        var filterClause = filters.Count == 0 ? string.Empty : $" AND {string.Join(" AND ", filters)}";
        return new SearchSqlScope(authorization, filterClause);
    }

    private static void AddListFilter(DbCommand command, ICollection<string> filters, string column, string prefix, IReadOnlyList<string>? values)
    {
        var effective = values?.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray();
        if (effective is not { Length: > 0 }) return;

        var names = new List<string>(effective.Length);
        for (var index = 0; index < effective.Length; index++)
        {
            var name = $"{prefix}{index}";
            Add(command, name, effective[index]);
            names.Add($"@{name}");
        }
        filters.Add($"c.\"{column}\" IN ({string.Join(",", names)})");
    }

    private static string BuildCursorKey(
        string query,
        IReadOnlyList<string>? categories,
        IReadOnlyList<string>? sources)
    {
        static string Normalize(IReadOnlyList<string>? values) => string.Join(
            '\u001F',
            (values ?? Array.Empty<string>())
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase));

        return $"{query.Trim()}\u001EC:{Normalize(categories)}\u001ES:{Normalize(sources)}";
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

    private sealed record SearchSqlScope(string AuthorizationClause, string FilterClause);

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
