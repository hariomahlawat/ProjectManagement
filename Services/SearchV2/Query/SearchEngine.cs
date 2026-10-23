using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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
    // Index expressions created by AddSearchV2QueryAccelerators use LEFT(column, 160).
    private const int PatternIndexPrefixLength = 160;
    // The English vector stores the title with weight A only (search_v2_refresh_vectors).
    private const string TitleWeightFilter = "'{a}'";
    private const int HeadlineWindowCharacters = 6000;
    private const int HeadlineLeadCharacters = 1500;

    private readonly ApplicationDbContext _db;
    private readonly ISearchIndexStore _store;
    private readonly ISearchAccessContextFactory _accessFactory;
    private readonly ISearchQueryNormalizer _normalizer;
    private readonly ISearchAliasProvider _aliases;
    private readonly ISearchCorrectionService _corrections;
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
        ISearchCorrectionService corrections,
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
        _corrections = corrections ?? throw new ArgumentNullException(nameof(corrections));
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
        if (string.IsNullOrWhiteSpace(normalized.Exact))
        {
            return SearchResponse.Empty(normalized.Original);
        }

        if (!_options.Enabled)
        {
            return SearchResponse.NotReady(normalized.Original, SearchV2ExecutionStatus.Disabled);
        }

        var aliasRules = await _aliases.GetActiveAsync(cancellationToken);
        var expanded = SearchAliasQueryExpander.Expand(normalized.Exact, aliasRules);
        normalized = normalized with
        {
            WebSearchQuery = expanded.WebSearchQuery,
            Expansions = expanded.Expansions
        };
        // Letter/digit variants (T90 / T-90) ride on the alias channels and the exact-title test.
        var variants = SearchQueryNormalizer.AlphanumericVariants(normalized.Exact);
        var aliasWebSearchQuery = string.Join(
            " OR ",
            new[] { expanded.AliasWebSearchQuery }.Concat(variants).Where(value => !string.IsNullOrWhiteSpace(value)));

        // Cheap readiness probe (one primary-key row). Entry/work-item counts belong to the
        // administrative health page, not to every committed search.
        var activeGeneration = await _store.GetActiveGenerationAsync(_options.ProjectionVersion, cancellationToken);
        if (activeGeneration <= 0)
        {
            _logger.LogDebug(
                "Search V2 index is not ready for ProjectionVersion {ProjectionVersion}.",
                _options.ProjectionVersion);
            return SearchResponse.NotReady(normalized.Original, SearchV2ExecutionStatus.IndexNotReady);
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
        SearchCursorPosition? after = null;
        var cursorState = request.FacetsOnly
            ? SearchCursorState.None
            : _cursorCodec.DecodeKeyset(cursorKey, request.Cursor, activeGeneration, out after);
        if (cursorState != SearchCursorState.Valid) after = null;
        if (cursorState == SearchCursorState.Stale)
        {
            _logger.LogDebug("Search V2 received a stale or foreign cursor; restarting at the first page.");
        }

        var access = await _accessFactory.CreateAsync(user, cancellationToken);
        var terms = SearchQueryTerms.Create(
            normalized,
            expanded.AliasExactQueries.Select(_normalizer.NormalizeExact).Concat(variants),
            variants);

        try
        {
            await _db.Database.OpenConnectionAsync(cancellationToken);
            var connection = _db.Database.GetDbConnection();
            var rows = new List<SearchRow>(pageSize + 1);
            long totalHits = 0;
            long filteredHits = 0;
            SearchFacets facets = SearchFacets.Empty;

            var databaseStopwatch = Stopwatch.StartNew();
            await using (var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken))
            {
                await SetTrigramThresholdsAsync(
                    connection,
                    transaction,
                    _options.FuzzyThreshold,
                    _options.FuzzyWordSimilarityThreshold,
                    cancellationToken);

                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
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
                command.CommandText = BuildSearchSql(sqlParts, request.IncludeDetailedFacets);
                Add(command, "indexVersion", _options.ProjectionVersion);
                Add(command, "exact", terms.Exact);
                Add(command, "exactTitles", terms.ExactTitles);
                Add(command, "exactTitlePrefixes", terms.IndexedExactTitles);
                Add(command, "webQuery", normalized.WebSearchQuery);
                Add(command, "aliasWebQuery", aliasWebSearchQuery);
                Add(command, "exactTokenTsQuery", terms.ExactTokenTsQuery);
                Add(command, "prefixTsQuery", terms.PrefixTsQuery);
                Add(command, "prefixLike", terms.PrefixLike);
                Add(command, "prefixLike160", terms.IndexedPrefixLike);
                Add(command, "containsLike", terms.ContainsLike);
                Add(command, "identifierKey", terms.IdentifierKey);
                Add(command, "identifierKeyPrefixLike", terms.IdentifierKeyPrefixLike);
                Add(command, "identifierDigits", terms.IdentifierDigits);
                Add(command, "aliasPhrases", terms.AliasPhrases);
                Add(command, "fuzzyTokens", terms.FuzzyTokens);
                Add(command, "fuzzyLead", terms.FuzzyLead);
                Add(command, "snippetAnchor", terms.SnippetAnchor);
                Add(command, "fuzzyStrongThreshold", _options.FuzzyFallbackStrongCandidateThreshold);
                Add(command, "canonicalEntityBoost", _options.CanonicalEntityBoost);
                Add(command, "rrfK", _options.ReciprocalRankK);
                Add(command, "hasAfter", after is not null);
                Add(command, "afterTier", after?.Tier ?? 0);
                Add(command, "afterPriority", after?.EntityPriority ?? 0);
                Add(command, "afterScore", after?.Score ?? 0d);
                Add(command, "afterEventIsNull", after?.EventDate is null);
                Add(command, "afterEvent", after?.EventDate ?? DateTimeOffset.UnixEpoch);
                Add(command, "afterUpdated", after?.UpdatedAt ?? DateTimeOffset.UnixEpoch);
                Add(command, "afterId", after?.Id ?? 0L);
                Add(command, "take", pageSize + 1);
                Add(command, "facetsOnly", request.FacetsOnly);
                Add(command, "maxSnippetSourceCharacters", Math.Max(_options.MaxSnippetCharacters * 2, 600));
                Add(command, "headlineWindowCharacters", HeadlineWindowCharacters);
                Add(command, "headlineLeadCharacters", HeadlineLeadCharacters);

                await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                {
                    // Summary/facets and paged rows deliberately share one PostgreSQL
                    // statement. PostgreSQL CTEs are statement-scoped, so a second
                    // SELECT cannot safely reference the ranked CTE.
                    if (await reader.ReadAsync(cancellationToken))
                    {
                        totalHits = reader.GetInt64(reader.GetOrdinal("TotalHits"));
                        filteredHits = reader.GetInt64(reader.GetOrdinal("FilteredHits"));
                        facets = new SearchFacets(
                            ParseFacets(reader, "CategoryFacets"),
                            ParseFacets(reader, "SourceFacets"),
                            ParseFacets(reader, "ProjectFacets"),
                            ParseFacets(reader, "StatusFacets"),
                            ParseFacets(reader, "FileTypeFacets"),
                            ParseFacets(reader, "StageFacets"),
                            request.IncludeDetailedFacets);

                        var idOrdinal = reader.GetOrdinal("Id");
                        do
                        {
                            if (!reader.IsDBNull(idOrdinal))
                            {
                                rows.Add(ReadRow(reader));
                            }
                        }
                        while (await reader.ReadAsync(cancellationToken));
                    }
                }

                await transaction.CommitAsync(cancellationToken);
            }
            databaseStopwatch.Stop();

            var hasMore = rows.Count > pageSize;
            if (hasMore) rows.RemoveAt(rows.Count - 1);

            var results = rows.Select(row => ToResult(row, normalized)).ToArray();
            var nextCursor = hasMore && rows.Count > 0
                ? _cursorCodec.EncodeKeyset(cursorKey, ToCursorPosition(rows[^1]), activeGeneration)
                : null;

            string? correctedQuery = null;
            var correctionMilliseconds = 0L;
            if (!request.FacetsOnly && after is null && ShouldOfferCorrection(normalized, rows))
            {
                var correctionStopwatch = Stopwatch.StartNew();
                correctedQuery = await _corrections.TryCorrectAsync(connection, normalized, access, cancellationToken);
                correctionStopwatch.Stop();
                correctionMilliseconds = correctionStopwatch.ElapsedMilliseconds;
                if (string.Equals(correctedQuery, normalized.Exact, StringComparison.OrdinalIgnoreCase)) correctedQuery = null;
            }

            stopwatch.Stop();
            _logger.LogDebug(
                "Search V2 timing. TotalMs={TotalMs}, DatabaseMs={DatabaseMs}, CorrectionMs={CorrectionMs}, DetailedFacets={DetailedFacets}, FacetsOnly={FacetsOnly}, ResultCount={ResultCount}.",
                stopwatch.ElapsedMilliseconds,
                databaseStopwatch.ElapsedMilliseconds,
                correctionMilliseconds,
                request.IncludeDetailedFacets,
                request.FacetsOnly,
                results.Length);
            return new SearchResponse(
                normalized.Original,
                results,
                totalHits,
                filteredHits,
                facets,
                nextCursor,
                stopwatch.ElapsedMilliseconds,
                true,
                false,
                correctedQuery,
                cursorState == SearchCursorState.Stale ? SearchV2ExecutionStatus.StaleCursor : SearchV2ExecutionStatus.Success);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            var diagnosticId = CreateDiagnosticId();
            _logger.LogError(
                ex,
                "Search V2 query failed. DiagnosticId={DiagnosticId}, QueryFingerprint={QueryFingerprint}, QueryLength={QueryLength}, ProjectionVersion={ProjectionVersion}, Categories={CategoryCount}, Sources={SourceCount}, Projects={ProjectCount}, Statuses={StatusCount}, FileTypes={FileTypeCount}, Stages={StageCount}.",
                diagnosticId,
                Fingerprint(normalized.Exact),
                normalized.Exact.Length,
                _options.ProjectionVersion,
                request.Categories?.Count ?? 0,
                request.Sources?.Count ?? 0,
                request.ProjectIds?.Count ?? 0,
                request.Statuses?.Count ?? 0,
                request.FileTypes?.Count ?? 0,
                request.Stages?.Count ?? 0);
            return SearchResponse.NotReady(normalized.Original, SearchV2ExecutionStatus.QueryFailed, diagnosticId);
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
        try
        {
            // Autocomplete runs on every keystroke, so it never invokes spelling correction:
            // a bounded trigram title branch inside the single indexed query covers typos.
            await _db.Database.OpenConnectionAsync(cancellationToken);
            return await ReadSuggestionsAsync(_db.Database.GetDbConnection(), normalized, access, take, cancellationToken);
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

    private async Task<IReadOnlyList<SearchSuggestion>> ReadSuggestionsAsync(
        DbConnection connection,
        NormalizedSearchQuery normalized,
        SearchAccessContext access,
        int take,
        CancellationToken cancellationToken)
    {
        var terms = SearchQueryTerms.Create(normalized, Array.Empty<string>(), Array.Empty<string>());
        await using var command = connection.CreateCommand();
        var scope = BuildScopeSql(command, access, null, null, null, null, null, null, null, null);
        var scopeWhere = ScopeWhere(scope);
        // Every candidate branch is an index-backed predicate (pattern btree on LEFT(title),
        // GIN tsvector, term pattern btree, GIN trigram); the expensive scoring runs only on
        // the union of candidate ids.
        command.CommandText = $"""
            WITH state AS MATERIALIZED (
                SELECT "ActiveGeneration" AS generation
                FROM "SearchIndexState"
                WHERE "Id" = 1 AND "IndexVersion" = @indexVersion
            ), candidate_ids AS (
                SELECT e."Id"
                FROM "SearchEntries" e
                WHERE {scopeWhere}
                  AND LEFT(e."NormalizedTitle", {PatternIndexPrefixLength}) LIKE @prefixLike160
                  AND e."NormalizedTitle" LIKE @prefixLike
                UNION
                SELECT e."Id"
                FROM "SearchEntries" e
                WHERE @prefixTsQuery <> ''
                  AND {scopeWhere}
                  AND e."SearchVectorSimple" @@ to_tsquery('simple', @prefixTsQuery)
                  AND to_tsvector('simple', e."Title") @@ to_tsquery('simple', @prefixTsQuery)
                UNION
                SELECT e."Id"
                FROM "SearchEntryTerms" t
                JOIN "SearchEntries" e ON e."Id" = t."SearchEntryId"
                WHERE t."TermType" IN ('Identifier', 'Alias')
                  AND LEFT(t."NormalizedTerm", {PatternIndexPrefixLength}) LIKE @prefixLike160
                  AND t."NormalizedTerm" LIKE @prefixLike
                  AND {scopeWhere}
                UNION
                SELECT e."Id"
                FROM "SearchEntryTerms" t
                JOIN "SearchEntries" e ON e."Id" = t."SearchEntryId"
                WHERE LENGTH(@identifierKey) >= 3
                  AND t."TermType" = 'IdentifierKey'
                  AND LEFT(t."NormalizedTerm", {PatternIndexPrefixLength}) LIKE @identifierKeyPrefixLike
                  AND {scopeWhere}
                UNION
                SELECT e."Id"
                FROM "SearchEntries" e
                WHERE LENGTH(@exact) >= 4
                  AND {scopeWhere}
                  AND e."NormalizedTitle" % @exact
                  AND similarity(e."NormalizedTitle", @exact) >= @suggestionFuzzyThreshold
            ), scored AS (
                SELECT e."Id", e."Title", e."NormalizedTitle", e."Subtitle", e."CanonicalUrl", e."SourceModule",
                       e."ResultCategory", e."IdentifierText", e."CanonicalEntityType", e."CanonicalEntityKey", e."UpdatedAtUtc",
                       CASE
                           WHEN EXISTS (
                               SELECT 1 FROM "SearchEntryTerms" t
                               WHERE t."SearchEntryId" = e."Id"
                                 AND ((t."TermType" = 'Identifier' AND t."NormalizedTerm" = @exact)
                                   OR (@identifierKey <> '' AND t."TermType" = 'IdentifierKey' AND t."NormalizedTerm" = @identifierKey))
                           ) THEN 0
                           WHEN EXISTS (
                               SELECT 1 FROM "SearchEntryTerms" t
                               WHERE t."SearchEntryId" = e."Id"
                                 AND ((t."TermType" = 'Identifier' AND t."NormalizedTerm" LIKE @prefixLike)
                                   OR (LENGTH(@identifierKey) >= 3 AND t."TermType" = 'IdentifierKey' AND t."NormalizedTerm" LIKE @identifierKeyPrefixLike))
                           ) THEN 1
                           WHEN e."NormalizedTitle" = @exact THEN 2
                           WHEN @exactTokenTsQuery <> '' AND to_tsvector('simple', e."Title") @@ to_tsquery('simple', @exactTokenTsQuery) THEN 3
                           WHEN e."NormalizedTitle" LIKE @prefixLike THEN 4
                           WHEN @prefixTsQuery <> '' AND to_tsvector('simple', e."Title") @@ to_tsquery('simple', @prefixTsQuery) THEN 5
                           WHEN EXISTS (
                               SELECT 1 FROM "SearchEntryTerms" t
                               WHERE t."SearchEntryId" = e."Id" AND t."TermType" = 'Alias' AND t."NormalizedTerm" = @exact
                           ) THEN 6
                           WHEN EXISTS (
                               SELECT 1 FROM "SearchEntryTerms" t
                               WHERE t."SearchEntryId" = e."Id" AND t."TermType" = 'Alias' AND t."NormalizedTerm" LIKE @prefixLike
                           ) THEN 7
                           ELSE 8
                       END AS priority,
                       CASE WHEN e."EntityType" = 'Project' THEN 0 ELSE 1 END AS entity_priority,
                       CASE WHEN e."EntityType" = e."CanonicalEntityType" AND e."EntityKey" = e."CanonicalEntityKey" THEN 0 ELSE 1 END AS canonical_order,
                       GREATEST(similarity(e."NormalizedTitle", @exact), word_similarity(@exact, e."NormalizedTitle")) AS fuzzy_score
                FROM candidate_ids c
                JOIN "SearchEntries" e ON e."Id" = c."Id"
            ), deduplicated AS (
                SELECT s.*,
                       ROW_NUMBER() OVER (
                           PARTITION BY s."CanonicalEntityType", s."CanonicalEntityKey"
                           ORDER BY s.priority, s.canonical_order, s.fuzzy_score DESC, s."UpdatedAtUtc" DESC, s."Id"
                       ) AS canonical_rank
                FROM scored s
            ), distinct_titles AS (
                SELECT d.*,
                       ROW_NUMBER() OVER (
                           PARTITION BY d."NormalizedTitle"
                           ORDER BY d.priority, d.entity_priority, d.fuzzy_score DESC, d."UpdatedAtUtc" DESC, d."Id"
                       ) AS title_rank
                FROM deduplicated d
                WHERE d.canonical_rank = 1
            )
            SELECT "Title", "Subtitle", "CanonicalUrl", "SourceModule", "ResultCategory", NULLIF("IdentifierText", '') AS "IdentifierText"
            FROM distinct_titles
            WHERE title_rank = 1
            ORDER BY priority, entity_priority, fuzzy_score DESC, "UpdatedAtUtc" DESC, "Id"
            LIMIT @take;
            """;
        Add(command, "indexVersion", _options.ProjectionVersion);
        Add(command, "exact", terms.Exact);
        Add(command, "exactTokenTsQuery", terms.ExactTokenTsQuery);
        Add(command, "prefixTsQuery", terms.PrefixTsQuery);
        Add(command, "prefixLike", terms.PrefixLike);
        Add(command, "prefixLike160", terms.IndexedPrefixLike);
        Add(command, "identifierKey", terms.IdentifierKey);
        Add(command, "identifierKeyPrefixLike", terms.IdentifierKeyPrefixLike);
        // Kept at or above pg_trgm's default 0.3 so the GIN-backed % operator needs no SET LOCAL.
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

    /// <summary>
    /// pg_trgm's % / &lt;% operators use session thresholds (default 0.3 / 0.6), not the
    /// similarity() filter written next to them. SET LOCAL makes the configured thresholds
    /// govern the GIN-indexed operators for this transaction only (pooled connections
    /// are not affected).
    /// </summary>
    internal static async Task SetTrigramThresholdsAsync(
        DbConnection connection,
        DbTransaction transaction,
        double similarityThreshold,
        double wordSimilarityThreshold,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT set_config('pg_trgm.similarity_threshold', @similarity, true),
                   set_config('pg_trgm.word_similarity_threshold', @wordSimilarity, true);
            """;
        Add(command, "similarity", Math.Clamp(similarityThreshold, 0d, 1d).ToString("0.###", CultureInfo.InvariantCulture));
        Add(command, "wordSimilarity", Math.Clamp(wordSimilarityThreshold, 0d, 1d).ToString("0.###", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private SearchResult ToResult(SearchRow row, NormalizedSearchQuery query)
    {
        var snippet = _highlight.BuildSnippet(row.StructuredText, row.NarrativeText, query.HighlightTerms);
        var matchedField = SearchMatchEvidenceResolver.Resolve(
            query,
            row.Title,
            row.StructuredText,
            row.NarrativeText,
            row.MetadataJson,
            row.EntityType,
            row.Channels);
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
            SearchDisplayValueFormatter.Subtitle(row.Subtitle, row.Status),
            row.CanonicalUrl,
            snippet,
            _highlight.Highlight(snippet, query.HighlightTerms),
            matchedField,
            row.EventDateUtc,
            row.FileType,
            SearchDisplayValueFormatter.Status(row.Status),
            row.FusionScore,
            row.GlobalRank,
            related,
            row.MetadataJson,
            row.Tier,
            row.Channels);
    }

    private static SearchCursorPosition ToCursorPosition(SearchRow row) => new(
        row.Tier,
        row.EntityPriority,
        row.FusionScore,
        row.EventDateUtc,
        row.UpdatedAtUtc,
        row.Id,
        row.GlobalRank);

    private static SearchRelatedResult? ParseRelatedResult(string value)
    {
        var separator = value.LastIndexOf(':');
        if (separator <= 0 || separator == value.Length - 1) return null;
        var source = value[..separator].Trim();
        if (!long.TryParse(value[(separator + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)) return null;
        return new SearchRelatedResult(source, source, Math.Max(1, count));
    }

    internal static bool ShouldOfferCorrection(NormalizedSearchQuery query, IReadOnlyList<SearchResultChannels> rows)
    {
        var tokenCount = query.Exact.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
        if (tokenCount == 0 || tokenCount > 6 || query.Exact.Length is < 4 or > 96) return false;

        // Offer "did you mean" whenever nothing on the first page matched lexically: either
        // no results, or only trigram look-alikes (however many there are).
        return rows.All(row => !HasStrongLexicalChannel(row.Channels));
    }

    private static bool ShouldOfferCorrection(NormalizedSearchQuery query, IReadOnlyList<SearchRow> rows) =>
        ShouldOfferCorrection(query, rows.Select(row => new SearchResultChannels(row.Channels)).ToArray());

    private static bool HasStrongLexicalChannel(string channels) =>
        channels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(channel => channel is not ("title_fuzzy" or "fuzzy"));

    private static string ScopeWhere(SearchSqlScope scope) =>
        $"""e."Generation" = (SELECT generation FROM state) AND e."IndexVersion" = @indexVersion {scope.AuthorizationClause}""";

    private static string BuildSearchSql(SearchSqlScope scope, bool includeDetailedFacets)
    {
        var sourceFacetValue = includeDetailedFacets ? "(SELECT value::text FROM source_facets)" : "'{}'::text";
        var projectFacetValue = includeDetailedFacets ? "(SELECT value::text FROM project_facets)" : "'{}'::text";
        var statusFacetValue = includeDetailedFacets ? "(SELECT value::text FROM status_facets)" : "'{}'::text";
        var fileTypeFacetValue = includeDetailedFacets ? "(SELECT value::text FROM file_type_facets)" : "'{}'::text";
        var stageFacetValue = includeDetailedFacets ? "(SELECT value::text FROM stage_facets)" : "'{}'::text";
        var scopeWhere = ScopeWhere(scope);

        // Every channel reads "SearchEntries" directly with the active-generation and
        // authorization predicates inline, so PostgreSQL can drive it from the matching
        // index (btree equality/pattern, GIN tsvector, GIN trigram). No channel scans a
        // materialised copy of the whole authorised index. Wide text columns are read only
        // for the final page.
        //
        // Tiers (lower wins; equal tiers are ordered Project-first, then by fused score):
        //   0 exact identifier            1 exact title, identifier prefix
        //   2 title phrase, exact alias   3 alias title phrase, all title tokens, name, identifier number
        //   4 title token/word prefix, alias prefix, English-stemmed title match
        //   5 any-field simple FTS        6 English body FTS, alias FTS        7 trigram fallback
        return $"""
        WITH state AS MATERIALIZED (
            SELECT "ActiveGeneration" AS generation
            FROM "SearchIndexState"
            WHERE "Id" = 1 AND "IndexVersion" = @indexVersion
        ), exact_identifier AS (
            SELECT e."Id", 0 AS tier,
                   ROW_NUMBER() OVER (ORDER BY e."EventDateUtc" DESC NULLS LAST, e."UpdatedAtUtc" DESC, e."Id") AS channel_rank,
                   'exact_identifier'::text AS channel, 5.0::double precision AS weight
            FROM "SearchEntries" e
            WHERE {scopeWhere}
              AND e."Id" IN (
                  SELECT t."SearchEntryId"
                  FROM "SearchEntryTerms" t
                  WHERE (t."TermType" = 'Identifier' AND t."NormalizedTerm" = @exact)
                     OR (@identifierKey <> '' AND t."TermType" = 'IdentifierKey' AND t."NormalizedTerm" = @identifierKey))
        ), exact_title AS (
            SELECT e."Id", 1 AS tier,
                   ROW_NUMBER() OVER (ORDER BY e."EventDateUtc" DESC NULLS LAST, e."UpdatedAtUtc" DESC, e."Id") AS channel_rank,
                   'exact_title'::text AS channel, 4.5::double precision AS weight
            FROM "SearchEntries" e
            WHERE {scopeWhere}
              AND LEFT(e."NormalizedTitle", {PatternIndexPrefixLength}) = ANY(@exactTitlePrefixes)
              AND e."NormalizedTitle" = ANY(@exactTitles)
        ), identifier_prefix AS (
            SELECT x."Id", 1 AS tier,
                   ROW_NUMBER() OVER (ORDER BY x.term_length, x."UpdatedAtUtc" DESC, x."Id") AS channel_rank,
                   'identifier_prefix'::text AS channel, 4.0::double precision AS weight
            FROM (
                SELECT e."Id", e."UpdatedAtUtc", MIN(LENGTH(t."NormalizedTerm")) AS term_length
                FROM "SearchEntryTerms" t
                JOIN "SearchEntries" e ON e."Id" = t."SearchEntryId"
                WHERE LENGTH(@exact) >= 2
                  AND (
                      (t."TermType" = 'Identifier'
                       AND LEFT(t."NormalizedTerm", {PatternIndexPrefixLength}) LIKE @prefixLike160
                       AND t."NormalizedTerm" LIKE @prefixLike
                       AND t."NormalizedTerm" <> @exact)
                      OR (LENGTH(@identifierKey) >= 3
                       AND t."TermType" = 'IdentifierKey'
                       AND LEFT(t."NormalizedTerm", {PatternIndexPrefixLength}) LIKE @identifierKeyPrefixLike
                       AND t."NormalizedTerm" <> @identifierKey))
                  AND NOT EXISTS (
                      SELECT 1 FROM "SearchEntryTerms" x
                      WHERE x."SearchEntryId" = e."Id"
                        AND ((x."TermType" = 'Identifier' AND x."NormalizedTerm" = @exact)
                          OR (@identifierKey <> '' AND x."TermType" = 'IdentifierKey' AND x."NormalizedTerm" = @identifierKey)))
                  AND {scopeWhere}
                GROUP BY e."Id", e."UpdatedAtUtc"
            ) x
        ), title_phrase AS (
            SELECT e."Id", 2 AS tier,
                   ROW_NUMBER() OVER (
                       ORDER BY STRPOS(' ' || e."NormalizedTitle" || ' ', ' ' || @exact || ' '),
                                LENGTH(e."NormalizedTitle"),
                                e."UpdatedAtUtc" DESC,
                                e."Id") AS channel_rank,
                   'title_phrase'::text AS channel, 3.6::double precision AS weight
            FROM "SearchEntries" e
            WHERE {scopeWhere}
              AND e."NormalizedTitle" LIKE @containsLike
              AND e."NormalizedTitle" <> @exact
              AND STRPOS(' ' || e."NormalizedTitle" || ' ', ' ' || @exact || ' ') > 0
        ), alias_matches AS (
            SELECT e."Id", 2 AS tier,
                   ROW_NUMBER() OVER (ORDER BY e."UpdatedAtUtc" DESC, e."Id") AS channel_rank,
                   'alias'::text AS channel, 3.0::double precision AS weight
            FROM "SearchEntries" e
            WHERE {scopeWhere}
              AND e."Id" IN (
                  SELECT t."SearchEntryId" FROM "SearchEntryTerms" t
                  WHERE t."TermType" = 'Alias' AND t."NormalizedTerm" = @exact)
        ), alias_title_phrase AS (
            SELECT e."Id", 3 AS tier,
                   ROW_NUMBER() OVER (
                       ORDER BY LENGTH(e."NormalizedTitle"), e."UpdatedAtUtc" DESC, e."Id") AS channel_rank,
                   'alias_title_phrase'::text AS channel, 3.15::double precision AS weight
            FROM "SearchEntries" e
            WHERE cardinality(@aliasPhrases) > 0
              AND {scopeWhere}
              AND EXISTS (
                  SELECT 1
                  FROM unnest(@aliasPhrases) AS phrases(phrase)
                  WHERE STRPOS(' ' || e."NormalizedTitle" || ' ', ' ' || phrase || ' ') > 0
              )
        ), title_tokens_exact AS (
            SELECT e."Id", 3 AS tier,
                   ROW_NUMBER() OVER (
                       ORDER BY ts_rank_cd(to_tsvector('simple', e."Title"), to_tsquery('simple', @exactTokenTsQuery)) DESC,
                                LENGTH(e."NormalizedTitle"), e."UpdatedAtUtc" DESC, e."Id") AS channel_rank,
                   'title_tokens_exact'::text AS channel, 2.8::double precision AS weight
            FROM "SearchEntries" e
            WHERE @exactTokenTsQuery <> ''
              AND {scopeWhere}
              AND e."SearchVectorSimple" @@ to_tsquery('simple', @exactTokenTsQuery)
              AND to_tsvector('simple', e."Title") @@ to_tsquery('simple', @exactTokenTsQuery)
              AND e."NormalizedTitle" <> @exact
              AND STRPOS(' ' || e."NormalizedTitle" || ' ', ' ' || @exact || ' ') = 0
        ), name_matches AS (
            SELECT x."Id", 3 AS tier,
                   ROW_NUMBER() OVER (ORDER BY x.exactness, x.term_length, x."UpdatedAtUtc" DESC, x."Id") AS channel_rank,
                   'name'::text AS channel, 1.75::double precision AS weight
            FROM (
                SELECT e."Id", e."UpdatedAtUtc",
                       MIN(CASE WHEN t."NormalizedTerm" = @exact THEN 0 ELSE 1 END) AS exactness,
                       MIN(LENGTH(t."NormalizedTerm")) AS term_length
                FROM "SearchEntryTerms" t
                JOIN "SearchEntries" e ON e."Id" = t."SearchEntryId"
                WHERE t."TermType" = 'Name'
                  AND (t."NormalizedTerm" = @exact
                       OR (LENGTH(@exact) >= 3
                           AND LEFT(t."NormalizedTerm", {PatternIndexPrefixLength}) LIKE @prefixLike160
                           AND t."NormalizedTerm" LIKE @prefixLike))
                  AND {scopeWhere}
                GROUP BY e."Id", e."UpdatedAtUtc"
            ) x
        ), identifier_number AS (
            SELECT x."Id", 3 AS tier,
                   ROW_NUMBER() OVER (ORDER BY x.term_length, x."UpdatedAtUtc" DESC, x."Id") AS channel_rank,
                   'identifier_number'::text AS channel, 1.5::double precision AS weight
            FROM (
                SELECT e."Id", e."UpdatedAtUtc", MIN(LENGTH(t."NormalizedTerm")) AS term_length
                FROM "SearchEntryTerms" t
                JOIN "SearchEntries" e ON e."Id" = t."SearchEntryId"
                WHERE @identifierDigits <> ''
                  AND t."TermType" = 'IdentifierKey'
                  AND t."NormalizedTerm" <> @identifierDigits
                  AND substring(t."NormalizedTerm" from '[0-9]+$') = @identifierDigits
                  AND {scopeWhere}
                GROUP BY e."Id", e."UpdatedAtUtc"
            ) x
        ), alias_prefix AS (
            SELECT x."Id", 4 AS tier,
                   ROW_NUMBER() OVER (ORDER BY x.term_length, x."UpdatedAtUtc" DESC, x."Id") AS channel_rank,
                   'alias_prefix'::text AS channel, 1.6::double precision AS weight
            FROM (
                SELECT e."Id", e."UpdatedAtUtc", MIN(LENGTH(t."NormalizedTerm")) AS term_length
                FROM "SearchEntryTerms" t
                JOIN "SearchEntries" e ON e."Id" = t."SearchEntryId"
                WHERE LENGTH(@exact) >= 2
                  AND t."TermType" = 'Alias'
                  AND LEFT(t."NormalizedTerm", {PatternIndexPrefixLength}) LIKE @prefixLike160
                  AND t."NormalizedTerm" LIKE @prefixLike
                  AND t."NormalizedTerm" <> @exact
                  AND {scopeWhere}
                GROUP BY e."Id", e."UpdatedAtUtc"
            ) x
        ), title_token_prefix AS (
            SELECT e."Id", 4 AS tier,
                   ROW_NUMBER() OVER (
                       ORDER BY ts_rank_cd(to_tsvector('simple', e."Title"), to_tsquery('simple', @prefixTsQuery)) DESC,
                                LENGTH(e."NormalizedTitle"),
                                e."UpdatedAtUtc" DESC,
                                e."Id") AS channel_rank,
                   'title_token_prefix'::text AS channel, 2.05::double precision AS weight
            FROM "SearchEntries" e
            WHERE @prefixTsQuery <> ''
              AND {scopeWhere}
              AND e."SearchVectorSimple" @@ to_tsquery('simple', @prefixTsQuery)
              AND to_tsvector('simple', e."Title") @@ to_tsquery('simple', @prefixTsQuery)
        ), title_prefix AS (
            SELECT e."Id", 4 AS tier,
                   ROW_NUMBER() OVER (ORDER BY LENGTH(e."NormalizedTitle"), e."UpdatedAtUtc" DESC, e."Id") AS channel_rank,
                   'title_prefix'::text AS channel, 2.0::double precision AS weight
            FROM "SearchEntries" e
            WHERE {scopeWhere}
              AND LEFT(e."NormalizedTitle", {PatternIndexPrefixLength}) LIKE @prefixLike160
              AND e."NormalizedTitle" LIKE @prefixLike
              AND e."NormalizedTitle" <> @exact
        ), english_title AS (
            SELECT e."Id", 4 AS tier,
                   ROW_NUMBER() OVER (
                       ORDER BY ts_rank_cd(ts_filter(e."SearchVectorEnglish", {TitleWeightFilter}), websearch_to_tsquery('english', @webQuery)) DESC,
                                LENGTH(e."NormalizedTitle"),
                                e."UpdatedAtUtc" DESC,
                                e."Id") AS channel_rank,
                   'english_title'::text AS channel, 1.9::double precision AS weight
            FROM "SearchEntries" e
            WHERE {scopeWhere}
              AND e."SearchVectorEnglish" @@ websearch_to_tsquery('english', @webQuery)
              AND ts_filter(e."SearchVectorEnglish", {TitleWeightFilter}) @@ websearch_to_tsquery('english', @webQuery)
        ), simple_fts AS (
            SELECT e."Id", 5 AS tier,
                   ROW_NUMBER() OVER (ORDER BY ts_rank_cd(e."SearchVectorSimple", websearch_to_tsquery('simple', @webQuery)) DESC, e."UpdatedAtUtc" DESC, e."Id") AS channel_rank,
                   'simple_fts'::text AS channel, 1.25::double precision AS weight
            FROM "SearchEntries" e
            WHERE {scopeWhere}
              AND e."SearchVectorSimple" @@ websearch_to_tsquery('simple', @webQuery)
        ), alias_fts AS (
            SELECT e."Id", 6 AS tier,
                   ROW_NUMBER() OVER (
                       ORDER BY ts_rank_cd(e."SearchVectorSimple", websearch_to_tsquery('simple', @aliasWebQuery)) DESC,
                                e."UpdatedAtUtc" DESC,
                                e."Id") AS channel_rank,
                   'alias_fts'::text AS channel, 0.75::double precision AS weight
            FROM "SearchEntries" e
            WHERE @aliasWebQuery <> ''
              AND {scopeWhere}
              AND e."SearchVectorSimple" @@ websearch_to_tsquery('simple', @aliasWebQuery)
        ), english_fts AS (
            SELECT e."Id", 6 AS tier,
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
            FROM "SearchEntries" e
            WHERE {scopeWhere}
              AND e."SearchVectorEnglish" @@ websearch_to_tsquery('english', @webQuery)
        ), strong_candidate_count AS (
            SELECT COUNT(DISTINCT "Id")::int AS value
            FROM (
                SELECT "Id" FROM exact_identifier
                UNION ALL SELECT "Id" FROM exact_title
                UNION ALL SELECT "Id" FROM identifier_prefix
                UNION ALL SELECT "Id" FROM title_phrase
                UNION ALL SELECT "Id" FROM alias_matches
                UNION ALL SELECT "Id" FROM alias_title_phrase
                UNION ALL SELECT "Id" FROM title_tokens_exact
                UNION ALL SELECT "Id" FROM name_matches
                UNION ALL SELECT "Id" FROM identifier_number
                UNION ALL SELECT "Id" FROM alias_prefix
                UNION ALL SELECT "Id" FROM title_token_prefix
                UNION ALL SELECT "Id" FROM title_prefix
                UNION ALL SELECT "Id" FROM english_title
                UNION ALL SELECT "Id" FROM simple_fts
                UNION ALL SELECT "Id" FROM alias_fts
                UNION ALL SELECT "Id" FROM english_fts
            ) strong
        ), title_fuzzy AS (
            -- % and <% honour pg_trgm.similarity_threshold / word_similarity_threshold, which the
            -- engine sets per transaction from FuzzyThreshold / FuzzyWordSimilarityThreshold.
            SELECT e."Id", 7 AS tier,
                   ROW_NUMBER() OVER (
                       ORDER BY GREATEST(similarity(e."NormalizedTitle", @exact), word_similarity(@exact, e."NormalizedTitle")) DESC,
                                e."UpdatedAtUtc" DESC, e."Id") AS channel_rank,
                   'title_fuzzy'::text AS channel, 0.5::double precision AS weight
            FROM "SearchEntries" e
            WHERE LENGTH(@exact) >= 4
              AND (SELECT value FROM strong_candidate_count) < @fuzzyStrongThreshold
              AND {scopeWhere}
              AND (e."NormalizedTitle" % @exact OR @exact <% e."NormalizedTitle")
        ), fuzzy_matches AS (
            -- Every query token must have a close word somewhere in the record. Word
            -- similarity, unlike similarity(), does not decay with the record's length.
            SELECT e."Id", 7 AS tier,
                   ROW_NUMBER() OVER (
                       ORDER BY (SELECT SUM(word_similarity(q.token, e."FuzzyText")) FROM unnest(@fuzzyTokens) AS q(token)) DESC,
                                e."UpdatedAtUtc" DESC, e."Id") AS channel_rank,
                   'fuzzy'::text AS channel, 0.45::double precision AS weight
            FROM "SearchEntries" e
            WHERE @fuzzyLead <> ''
              AND (SELECT value FROM strong_candidate_count) < @fuzzyStrongThreshold
              AND {scopeWhere}
              AND @fuzzyLead <% e."FuzzyText"
              AND NOT EXISTS (
                  SELECT 1 FROM unnest(@fuzzyTokens) AS q(token)
                  WHERE NOT (q.token <% e."FuzzyText"))
        ), channels AS (
            SELECT * FROM exact_identifier
            UNION ALL SELECT * FROM exact_title
            UNION ALL SELECT * FROM identifier_prefix
            UNION ALL SELECT * FROM title_phrase
            UNION ALL SELECT * FROM alias_matches
            UNION ALL SELECT * FROM alias_title_phrase
            UNION ALL SELECT * FROM title_tokens_exact
            UNION ALL SELECT * FROM name_matches
            UNION ALL SELECT * FROM identifier_number
            UNION ALL SELECT * FROM alias_prefix
            UNION ALL SELECT * FROM title_token_prefix
            UNION ALL SELECT * FROM title_prefix
            UNION ALL SELECT * FROM english_title
            UNION ALL SELECT * FROM simple_fts
            UNION ALL SELECT * FROM alias_fts
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
            -- Narrow columns only: facets and ordering never need the large text fields.
            -- The score is rounded so a keyset cursor compares it exactly across executions.
            SELECT e."Id", e."EntityType", e."EntityKey", e."CanonicalEntityType", e."CanonicalEntityKey", e."ParentProjectId",
                   e."SourceModule", e."ResultCategory", e."Status", e."FileType", e."EventDateUtc", e."UpdatedAtUtc", e."MetadataJson",
                   f.tier,
                   CASE WHEN e."EntityType" = 'Project' THEN 0 ELSE 1 END AS entity_priority,
                   CASE WHEN e."EntityType" = e."CanonicalEntityType" AND e."EntityKey" = e."CanonicalEntityKey" THEN 0 ELSE 1 END AS canonical_order,
                   ROUND((f.fusion_score + CASE
                       WHEN e."EntityType" = e."CanonicalEntityType" AND e."EntityKey" = e."CanonicalEntityKey"
                           THEN @canonicalEntityBoost
                       ELSE 0::double precision
                   END)::numeric, 9)::double precision AS fusion_score,
                   f.channels
            FROM fused f
            JOIN "SearchEntries" e ON e."Id" = f."Id"
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
            -- One row per canonical entity. The entity's own record (the Project itself, not
            -- its ToT/Training/Proliferation/IPR row) represents the cluster whenever it
            -- matched, unless another member is a strictly better exact (tier 0/1) hit. The
            -- cluster is ranked by the best evidence of any member.
            SELECT x."Id", x."EntityType", x."EntityKey", x."CanonicalEntityType", x."CanonicalEntityKey", x."ParentProjectId",
                   x."SourceModule", x."ResultCategory", x."Status", x."FileType", x."EventDateUtc", x."UpdatedAtUtc", x."MetadataJson",
                   x.cluster_tier AS tier, x.entity_priority, x.cluster_score AS fusion_score, x.channels, r.related_sources
            FROM (
                SELECT c.*,
                       ROW_NUMBER() OVER (
                           PARTITION BY c."CanonicalEntityType", c."CanonicalEntityKey"
                           ORDER BY LEAST(c.tier, 2), c.canonical_order, c.tier, c.fusion_score DESC, c."EventDateUtc" DESC NULLS LAST, c."UpdatedAtUtc" DESC, c."Id"
                       ) AS cluster_rank,
                       MIN(c.tier) OVER (PARTITION BY c."CanonicalEntityType", c."CanonicalEntityKey") AS cluster_tier,
                       MAX(c.fusion_score) OVER (PARTITION BY c."CanonicalEntityType", c."CanonicalEntityKey") AS cluster_score
                FROM filtered_candidates c
            ) x
            JOIN related r USING ("CanonicalEntityType", "CanonicalEntityKey")
            WHERE x.cluster_rank = 1
        ), ranked AS (
            SELECT c.*,
                   ROW_NUMBER() OVER (
                       ORDER BY c.tier, c.entity_priority, c.fusion_score DESC, c."EventDateUtc" DESC NULLS LAST, c."UpdatedAtUtc" DESC, c."Id"
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
                FROM jsonb_array_elements_text(
                    CASE
                        WHEN jsonb_typeof(c."MetadataJson"->'projectStages') = 'array'
                            THEN c."MetadataJson"->'projectStages'
                        ELSE '[]'::jsonb
                    END) value
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
        ), project_facet_counts AS (
            SELECT f."ParentProjectId",
                   COUNT(DISTINCT (f."CanonicalEntityType", f."CanonicalEntityKey")) AS item_count
            FROM project_facet_candidates f
            WHERE f."ParentProjectId" IS NOT NULL
            GROUP BY f."ParentProjectId"
        ), project_facet_rows AS (
            SELECT f."ParentProjectId"::text AS value,
                   COALESCE(p."Title", 'Project ' || f."ParentProjectId"::text) AS label,
                   f.item_count
            FROM project_facet_counts f
            LEFT JOIN LATERAL (
                SELECT e."Title"
                FROM "SearchEntries" e
                WHERE e."EntityType" = 'Project'
                  AND e."EntityKey" = f."ParentProjectId"::text
                  AND {scopeWhere}
                LIMIT 1
            ) p ON TRUE
        ), project_facets AS (
            SELECT COALESCE(
                jsonb_object_agg(value, jsonb_build_object('label', label, 'count', item_count)),
                jsonb_build_object()) AS value
            FROM project_facet_rows
        ), summary AS (
            SELECT
                (SELECT COUNT(DISTINCT (c."CanonicalEntityType", c."CanonicalEntityKey")) FROM category_facet_candidates c) AS "TotalHits",
                (SELECT COUNT(*) FROM ranked) AS "FilteredHits",
                (SELECT value::text FROM category_facets) AS "CategoryFacets",
                {sourceFacetValue} AS "SourceFacets",
                {projectFacetValue} AS "ProjectFacets",
                {statusFacetValue} AS "StatusFacets",
                {fileTypeFacetValue} AS "FileTypeFacets",
                {stageFacetValue} AS "StageFacets"
        ), page AS (
            -- Keyset continuation: rows strictly after the previous page's last row in the
            -- ranked order. Stable when entries are re-indexed between page requests.
            SELECT r.*
            FROM ranked r
            WHERE @facetsOnly = FALSE
              AND (
                  @hasAfter = FALSE
                  OR r.tier > @afterTier
                  OR (r.tier = @afterTier AND (
                      r.entity_priority > @afterPriority
                      OR (r.entity_priority = @afterPriority AND (
                          r.fusion_score < @afterScore
                          OR (r.fusion_score = @afterScore AND (
                              COALESCE(r."EventDateUtc", '-infinity'::timestamptz)
                                  < CASE WHEN @afterEventIsNull THEN '-infinity'::timestamptz ELSE @afterEvent END
                              OR (COALESCE(r."EventDateUtc", '-infinity'::timestamptz)
                                  = CASE WHEN @afterEventIsNull THEN '-infinity'::timestamptz ELSE @afterEvent END
                                  AND (
                                      r."UpdatedAtUtc" < @afterUpdated
                                      OR (r."UpdatedAtUtc" = @afterUpdated AND r."Id" > @afterId))))))))))
            ORDER BY r.global_rank
            LIMIT @take
        ), paged_results AS (
            SELECT p."Id", p."EntityType", p."EntityKey", p."CanonicalEntityType", p."CanonicalEntityKey", p."ParentProjectId",
                   p."SourceModule", p."ResultCategory", e."Title", e."Subtitle", e."CanonicalUrl",
                   LEFT(COALESCE(e."StructuredText", ''), @maxSnippetSourceCharacters) AS "StructuredText",
                   CASE
                       WHEN e."NarrativeText" IS NULL THEN NULL
                       ELSE LEFT(
                           regexp_replace(
                               ts_headline('simple',
                                   -- A bounded window around the first literal hit, never the
                                   -- whole OCR body (documents can hold hundreds of KB).
                                   SUBSTRING(e."NarrativeText"
                                       FROM GREATEST(1, STRPOS(LOWER(e."NarrativeText"), @snippetAnchor) - @headlineLeadCharacters)
                                       FOR @headlineWindowCharacters),
                                   websearch_to_tsquery('simple', @webQuery),
                                   'StartSel=<<,StopSel=>>,MaxWords=60,MinWords=15,ShortWord=2,MaxFragments=2,FragmentDelimiter=...'),
                               '<<|>>', '', 'g'),
                           @maxSnippetSourceCharacters)
                   END AS "NarrativeText",
                   p."Status", p."FileType", p."EventDateUtc", p."UpdatedAtUtc", p."MetadataJson"::text AS "MetadataJson",
                   p.tier, p.entity_priority, p.fusion_score, p.channels, p.global_rank::int AS "GlobalRank", p.related_sources AS "RelatedSources"
            FROM page p
            JOIN "SearchEntries" e ON e."Id" = p."Id"
        )
        SELECT s."TotalHits", s."FilteredHits", s."CategoryFacets", s."SourceFacets", s."ProjectFacets",
               s."StatusFacets", s."FileTypeFacets", s."StageFacets",
               p."Id", p."EntityType", p."EntityKey", p."CanonicalEntityType", p."CanonicalEntityKey", p."ParentProjectId",
               p."SourceModule", p."ResultCategory", p."Title", p."Subtitle", p."CanonicalUrl",
               p."StructuredText", p."NarrativeText", p."Status", p."FileType", p."EventDateUtc", p."UpdatedAtUtc", p."MetadataJson",
               p.tier, p.entity_priority, p.fusion_score, p.channels, p."GlobalRank", p."RelatedSources"
        FROM summary s
        LEFT JOIN paged_results p ON TRUE
        ORDER BY p."GlobalRank" NULLS LAST;
        """;
    }

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
        reader.GetInt32(reader.GetOrdinal("entity_priority")),
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
        var authorization = SearchAuthorizationSql.Build(command, access);

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
        return $"(COALESCE(c.\"MetadataJson\"->>'currentStage', '') IN ({inList}) OR EXISTS (SELECT 1 FROM jsonb_array_elements_text(CASE WHEN jsonb_typeof(c.\"MetadataJson\"->'projectStages') = 'array' THEN c.\"MetadataJson\"->'projectStages' ELSE '[]'::jsonb END) stage(value) WHERE stage.value IN ({inList})))";
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

    private static string CreateDiagnosticId() => Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

    private static string Fingerprint(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
        return Convert.ToHexString(bytes.AsSpan(0, 6));
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
        int EntityPriority,
        double FusionScore,
        string Channels,
        int GlobalRank,
        string? RelatedSources);
}

/// <summary>Channel list of one returned row, used by the correction-offer rule.</summary>
internal sealed record SearchResultChannels(string Channels);

/// <summary>
/// Query-derived SQL parameter values computed once per request (LIKE patterns are
/// escaped; tsquery operands are quoted; fuzzy tokens are bounded).
/// </summary>
internal sealed record SearchQueryTerms(
    string Exact,
    string ExactTokenTsQuery,
    string PrefixTsQuery,
    string PrefixLike,
    string IndexedPrefixLike,
    string ContainsLike,
    string IdentifierKey,
    string IdentifierKeyPrefixLike,
    string IdentifierDigits,
    string[] ExactTitles,
    string[] IndexedExactTitles,
    string[] AliasPhrases,
    string[] FuzzyTokens,
    string FuzzyLead,
    string SnippetAnchor)
{
    public static SearchQueryTerms Create(
        NormalizedSearchQuery query,
        IEnumerable<string> aliasPhrases,
        IReadOnlyList<string> exactVariants)
    {
        var exact = query.Exact;
        var tokens = exact.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var identifierKey = SearchQueryNormalizer.IdentifierKey(query.Original);
        var fuzzyTokens = tokens
            .Where(token => token.Length >= 3)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(token => token.Length)
            .Take(6)
            .ToArray();
        var indexedPrefix = exact.Length > 160 ? exact[..160] : exact;
        var exactTitles = new[] { exact }.Concat(exactVariants).Distinct(StringComparer.Ordinal).ToArray();

        return new SearchQueryTerms(
            exact,
            SearchQueryNormalizer.BuildTokenTsQuery(exact),
            SearchQueryNormalizer.BuildPrefixTsQuery(exact),
            SearchQueryNormalizer.EscapeLike(exact) + "%",
            SearchQueryNormalizer.EscapeLike(indexedPrefix) + "%",
            "%" + SearchQueryNormalizer.EscapeLike(exact) + "%",
            identifierKey,
            identifierKey.Length >= 3 ? SearchQueryNormalizer.EscapeLike(identifierKey) + "%" : string.Empty,
            identifierKey.Length >= 2 && identifierKey.All(char.IsDigit) ? identifierKey : string.Empty,
            exactTitles,
            exactTitles.Select(value => value.Length > 160 ? value[..160] : value).Distinct(StringComparer.Ordinal).ToArray(),
            aliasPhrases
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToArray(),
            fuzzyTokens,
            fuzzyTokens.FirstOrDefault() ?? string.Empty,
            tokens.OrderByDescending(token => token.Length).FirstOrDefault() ?? string.Empty);
    }
}
