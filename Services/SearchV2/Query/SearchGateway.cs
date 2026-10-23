using System.Diagnostics;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using ProjectManagement.Services.Search;
using ProjectManagement.Services.SearchV2.Analytics;
using ProjectManagement.Services.SearchV2.Models;
using ProjectManagement.Services.SearchV2.Security;

namespace ProjectManagement.Services.SearchV2.Query;

public interface ISearchGateway
{
    Task<SearchGatewayResponse> SearchAsync(SearchRequest request, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<IReadOnlyList<SearchSuggestion>> SuggestAsync(string query, ClaimsPrincipal user, int? limit, CancellationToken cancellationToken);
    Task LogClickAsync(string query, string entityType, string entityKey, int rank, string sourceModule, CancellationToken cancellationToken);
}

/// <summary>
/// Stable application boundary between the legacy fan-out engine and Search V2.
/// Only the engine that serves the user is on the request path: when V2 serves, Legacy
/// runs only as the fallback for a V2 failure. Shadow comparison with the other engine is
/// sampled (<see cref="SearchV2Options.ShadowSampleRate"/>), limited to first pages and
/// dispatched off the request path, so it never adds latency to the response.
/// </summary>
public sealed class SearchGateway : ISearchGateway
{
    private readonly ISearchV2Engine _v2;
    private readonly IGlobalSearchService _legacy;
    private readonly ISearchQueryNormalizer _normalizer;
    private readonly ISearchHighlightService _highlight;
    private readonly ISearchAnalyticsService _analytics;
    private readonly ISearchAccessContextFactory _accessFactory;
    private readonly ISearchShadowDispatcher _shadow;
    private readonly ISearchEngineLatencyRecorder _engineLatency;
    private readonly SearchV2Options _options;
    private readonly ILogger<SearchGateway> _logger;

    public SearchGateway(
        ISearchV2Engine v2,
        IGlobalSearchService legacy,
        ISearchQueryNormalizer normalizer,
        ISearchHighlightService highlight,
        ISearchAnalyticsService analytics,
        ISearchAccessContextFactory accessFactory,
        ISearchShadowDispatcher shadow,
        ISearchEngineLatencyRecorder engineLatency,
        IOptions<SearchV2Options> options,
        ILogger<SearchGateway> logger)
    {
        _v2 = v2 ?? throw new ArgumentNullException(nameof(v2));
        _legacy = legacy ?? throw new ArgumentNullException(nameof(legacy));
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
        _highlight = highlight ?? throw new ArgumentNullException(nameof(highlight));
        _analytics = analytics ?? throw new ArgumentNullException(nameof(analytics));
        _accessFactory = accessFactory ?? throw new ArgumentNullException(nameof(accessFactory));
        _shadow = shadow ?? throw new ArgumentNullException(nameof(shadow));
        _engineLatency = engineLatency ?? throw new ArgumentNullException(nameof(engineLatency));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SearchGatewayResponse> SearchAsync(
        SearchRequest request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(user);

        var normalized = _normalizer.Normalize(request.Query);
        if (string.IsNullOrWhiteSpace(normalized.Original))
        {
            return SearchGatewayResponse.Empty(string.Empty);
        }

        var stopwatch = Stopwatch.StartNew();
        var serveV2ToUser = ShouldServeV2(user);

        SearchResponse? v2Response = null;
        if (serveV2ToUser)
        {
            v2Response = await RunV2SafelyAsync(request, user, normalized, cancellationToken);
            if (v2Response.IsReady)
            {
                stopwatch.Stop();
                var gatewayLatencyMs = stopwatch.ElapsedMilliseconds;
                if (!request.FacetsOnly)
                {
                    // One query-log row per search. Engine-only time goes to the
                    // in-process recorder that feeds the search health dashboard.
                    _engineLatency.Record(v2Response.QueryTimeMilliseconds);
                    await SafeQueryLogAsync(
                        normalized.Original,
                        user,
                        v2Response.FilteredHits,
                        gatewayLatencyMs,
                        "V2",
                        v2Response.CorrectedQuery,
                        cancellationToken);
                    MaybeDispatchShadow(request, normalized.Original, user, servedByV2: true, legacyResults: null, v2Results: v2Response.Results);
                }

                return new SearchGatewayResponse(
                    v2Response.Query,
                    v2Response.Results,
                    v2Response.TotalHits,
                    v2Response.FilteredHits,
                    v2Response.Facets,
                    v2Response.NextCursor,
                    gatewayLatencyMs,
                    true,
                    v2Response.IsPartial,
                    v2Response.CorrectedQuery,
                    false,
                    v2Response.ExecutionStatus,
                    v2Response.DiagnosticId);
            }
        }

        if (request.FacetsOnly)
        {
            stopwatch.Stop();
            return new SearchGatewayResponse(
                normalized.Original,
                Array.Empty<SearchResult>(),
                0,
                0,
                SearchFacets.Empty,
                null,
                stopwatch.ElapsedMilliseconds,
                false,
                false,
                null,
                false,
                v2Response?.ExecutionStatus ?? SearchV2ExecutionStatus.IndexNotReady,
                v2Response?.DiagnosticId);
        }

        // Legacy serves: V2 is not enabled for this user, or V2 could not serve (for
        // example, the index is temporarily unavailable). Source-module authorization
        // is applied before adapting results.
        var legacyResults = await ReadLegacyAuthorizedAsync(normalized.Original, user, cancellationToken);

        var fellBackToLegacy = serveV2ToUser && v2Response is { IsReady: false };
        var v2Status = v2Response?.ExecutionStatus
            ?? (_options.Enabled ? SearchV2ExecutionStatus.IndexNotReady : SearchV2ExecutionStatus.Disabled);

        if (fellBackToLegacy)
        {
            _logger.LogWarning(
                "Search V2 fell back to Legacy. Status={Status}, DiagnosticId={DiagnosticId}, ProjectionVersion={ProjectionVersion}.",
                v2Status,
                v2Response?.DiagnosticId ?? "none",
                _options.ProjectionVersion);
        }

        // Disjunctive Legacy facets/counts mirror the V2 page contract: the All
        // count excludes only the active category, while the result list applies
        // both category and source. Legacy remains a bounded/partial candidate set.
        var allScope = ApplyLegacyFilters(legacyResults, null, request.Sources);
        var categoryFacetScope = allScope;
        var sourceFacetScope = ApplyLegacyFilters(legacyResults, request.Categories, null);
        var categoryFacets = BuildLegacyFacets(categoryFacetScope, static hit => LegacyCategory(hit.Source));
        var sourceFacets = BuildLegacyFacets(sourceFacetScope, static hit => hit.Source);
        var filtered = ApplyLegacyFilters(legacyResults, request.Categories, request.Sources);
        var adapted = filtered.Select((hit, index) => AdaptLegacy(hit, normalized, index + 1)).ToArray();

        stopwatch.Stop();
        await SafeQueryLogAsync(
            normalized.Original,
            user,
            adapted.Length,
            stopwatch.ElapsedMilliseconds,
            fellBackToLegacy ? "Legacy-Fallback" : "Legacy",
            null,
            cancellationToken);

        if (!fellBackToLegacy)
        {
            MaybeDispatchShadow(request, normalized.Original, user, servedByV2: false, legacyResults: legacyResults, v2Results: null);
        }

        return new SearchGatewayResponse(
            normalized.Original,
            adapted,
            allScope.Count,
            adapted.Length,
            new SearchFacets(
                categoryFacets,
                sourceFacets,
                Array.Empty<SearchFacetValue>(),
                Array.Empty<SearchFacetValue>(),
                Array.Empty<SearchFacetValue>(),
                Array.Empty<SearchFacetValue>()),
            null,
            stopwatch.ElapsedMilliseconds,
            false,
            true,
            null,
            fellBackToLegacy,
            v2Status,
            v2Response?.DiagnosticId);
    }

    public async Task<IReadOnlyList<SearchSuggestion>> SuggestAsync(
        string query,
        ClaimsPrincipal user,
        int? limit,
        CancellationToken cancellationToken)
    {
        // Shadow mode must be observational only. Suggestions become visible
        // when V2 itself is explicitly allowed to serve user-facing results.
        if (!_options.Enabled || !ShouldServeV2(user))
        {
            return Array.Empty<SearchSuggestion>();
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var suggestions = await _v2.SuggestAsync(query, user, limit, cancellationToken);
            stopwatch.Stop();
            await SafeQueryLogAsync(
                query?.Trim() ?? string.Empty,
                user,
                suggestions.Count,
                stopwatch.ElapsedMilliseconds,
                "V2-Suggest",
                null,
                cancellationToken);
            return suggestions;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            var diagnosticId = CreateDiagnosticId();
            _logger.LogError(
                ex,
                "Search V2 suggestions failed safely. DiagnosticId={DiagnosticId}, ProjectionVersion={ProjectionVersion}.",
                diagnosticId,
                _options.ProjectionVersion);
            return Array.Empty<SearchSuggestion>();
        }
    }

    public async Task LogClickAsync(
        string query,
        string entityType,
        string entityKey,
        int rank,
        string sourceModule,
        CancellationToken cancellationToken)
    {
        try
        {
            await _analytics.LogClickAsync(query, entityType, entityKey, rank, sourceModule, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "Search click telemetry write failed.");
        }
    }

    /// <summary>
    /// Effective shadow sample rate. Unset means 0 while V2 serves (comparison is opt-in)
    /// and 1 while Legacy serves (the original shadow-measurement rollout).
    /// </summary>
    internal double EffectiveShadowSampleRate(bool servedByV2) =>
        Math.Clamp(_options.ShadowSampleRate ?? (servedByV2 ? 0.0 : 1.0), 0.0, 1.0);

    private async Task<SearchResponse> RunV2SafelyAsync(
        SearchRequest request,
        ClaimsPrincipal user,
        NormalizedSearchQuery normalized,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _v2.SearchAsync(request, user, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            var diagnosticId = CreateDiagnosticId();
            _logger.LogError(
                ex,
                "Search V2 escaped its engine boundary. Falling back safely. DiagnosticId={DiagnosticId}, ProjectionVersion={ProjectionVersion}.",
                diagnosticId,
                _options.ProjectionVersion);
            return SearchResponse.NotReady(
                normalized.Original,
                SearchV2ExecutionStatus.QueryFailed,
                diagnosticId);
        }
    }

    private void MaybeDispatchShadow(
        SearchRequest request,
        string query,
        ClaimsPrincipal user,
        bool servedByV2,
        IReadOnlyList<GlobalSearchHit>? legacyResults,
        IReadOnlyList<SearchResult>? v2Results)
    {
        // Comparisons are only meaningful, and only cheap enough, for first pages.
        if (!_options.Enabled || !_options.ShadowMode || request.FacetsOnly || !string.IsNullOrWhiteSpace(request.Cursor))
        {
            return;
        }

        var rate = EffectiveShadowSampleRate(servedByV2);
        if (rate <= 0 || (rate < 1 && Random.Shared.NextDouble() >= rate))
        {
            return;
        }

        try
        {
            _shadow.TryDispatch(new SearchShadowJob(query, request, user, legacyResults, v2Results));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Search shadow comparison could not be dispatched.");
        }
    }

    private bool ShouldServeV2(ClaimsPrincipal user)
    {
        if (!_options.Enabled) return false;
        if (_options.ServeV2) return true;

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var userName = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name);
        if (_options.ServeV2Users.Any(value =>
                (!string.IsNullOrWhiteSpace(userId) && string.Equals(value, userId, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(userName) && string.Equals(value, userName, StringComparison.OrdinalIgnoreCase))))
        {
            return true;
        }

        return _options.ServeV2Roles.Any(role => user.IsInRole(role));
    }

    private async Task<IReadOnlyList<GlobalSearchHit>> ReadLegacyAuthorizedAsync(
        string query,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var accessTask = _accessFactory.CreateAsync(user, cancellationToken);
        var searchTask = _legacy.SearchAsync(query, cancellationToken);

        try
        {
            var hits = await searchTask;
            var access = await accessTask;
            return FilterLegacyByAccess(hits, access);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Legacy search failed while Search V2 gateway was active.");
            return Array.Empty<GlobalSearchHit>();
        }
    }

    private SearchResult AdaptLegacy(GlobalSearchHit hit, NormalizedSearchQuery query, int rank)
    {
        var snippet = _highlight.PlainLegacySnippet(hit.Snippet);
        if (string.IsNullOrWhiteSpace(snippet))
        {
            snippet = null;
        }

        return new SearchResult(
            0,
            "Legacy",
            hit.Url,
            "Legacy",
            hit.Url,
            null,
            hit.Source,
            LegacyCategory(hit.Source),
            hit.Title,
            _highlight.Highlight(hit.Title, query.HighlightTerms),
            hit.Extra,
            hit.Url,
            snippet,
            _highlight.Highlight(snippet, query.HighlightTerms),
            null,
            hit.Date,
            hit.FileType,
            null,
            (double)hit.Score,
            rank,
            Array.Empty<SearchRelatedResult>(),
            null);
    }

    /// <summary>
    /// Deny-by-default Legacy authorization through the map shared with the V2
    /// projection builder: a source label without an entry is never shown.
    /// </summary>
    internal static IReadOnlyList<GlobalSearchHit> FilterLegacyByAccess(
        IReadOnlyList<GlobalSearchHit> hits,
        SearchAccessContext access)
    {
        var policies = new HashSet<string>(access.AllowedPolicies, StringComparer.Ordinal);
        return hits.Where(hit => SearchSourcePolicies.IsVisible(hit.Source, policies)).ToArray();
    }

    private static IReadOnlyList<GlobalSearchHit> ApplyLegacyFilters(
        IReadOnlyList<GlobalSearchHit> hits,
        IReadOnlyList<string>? categories,
        IReadOnlyList<string>? sources)
    {
        HashSet<string>? categorySet = categories is { Count: > 0 }
            ? new HashSet<string>(categories.Where(static value => !string.IsNullOrWhiteSpace(value)), StringComparer.OrdinalIgnoreCase)
            : null;
        HashSet<string>? sourceSet = sources is { Count: > 0 }
            ? new HashSet<string>(sources.Where(static value => !string.IsNullOrWhiteSpace(value)), StringComparer.OrdinalIgnoreCase)
            : null;

        if ((categorySet?.Count ?? 0) == 0 && (sourceSet?.Count ?? 0) == 0)
        {
            return hits;
        }

        return hits.Where(hit =>
            ((categorySet?.Count ?? 0) == 0 || categorySet!.Contains(LegacyCategory(hit.Source))) &&
            ((sourceSet?.Count ?? 0) == 0 || sourceSet!.Contains(hit.Source)))
            .ToArray();
    }

    private static IReadOnlyList<SearchFacetValue> BuildLegacyFacets(
        IReadOnlyList<GlobalSearchHit> hits,
        Func<GlobalSearchHit, string> selector) =>
        hits.GroupBy(selector, StringComparer.OrdinalIgnoreCase)
            .Select(group => new SearchFacetValue(group.Key, group.LongCount()))
            .OrderByDescending(value => value.Count)
            .ThenBy(value => value.Value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string LegacyCategory(string source)
    {
        if (source.Contains("Document", StringComparison.OrdinalIgnoreCase))
        {
            return "Documents";
        }

        if (string.Equals(source, "Projects", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("Project portfolio", StringComparison.OrdinalIgnoreCase))
        {
            return "Projects";
        }

        if (source.Contains("Activity", StringComparison.OrdinalIgnoreCase))
        {
            return "Organisation";
        }

        return "Trackers";
    }

    private static string CreateDiagnosticId() =>
        Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

    private async Task SafeQueryLogAsync(
        string query,
        ClaimsPrincipal user,
        long resultCount,
        long latency,
        string engine,
        string? correction,
        CancellationToken cancellationToken)
    {
        try
        {
            await _analytics.LogQueryAsync(query, user, resultCount, latency, engine, correction, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "Search query telemetry write failed.");
        }
    }
}
