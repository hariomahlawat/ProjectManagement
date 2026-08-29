using System.Diagnostics;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Configuration;
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
/// Search V2 is deliberately shadow-first: while ServeV2 is false the legacy
/// result remains authoritative, but both engines are launched concurrently so
/// shadow measurement does not add serial query latency.
/// </summary>
public sealed class SearchGateway : ISearchGateway
{
    private readonly ISearchV2Engine _v2;
    private readonly IGlobalSearchService _legacy;
    private readonly ISearchQueryNormalizer _normalizer;
    private readonly ISearchHighlightService _highlight;
    private readonly ISearchAnalyticsService _analytics;
    private readonly ISearchAccessContextFactory _accessFactory;
    private readonly SearchV2Options _options;
    private readonly ILogger<SearchGateway> _logger;

    public SearchGateway(
        ISearchV2Engine v2,
        IGlobalSearchService legacy,
        ISearchQueryNormalizer normalizer,
        ISearchHighlightService highlight,
        ISearchAnalyticsService analytics,
        ISearchAccessContextFactory accessFactory,
        IOptions<SearchV2Options> options,
        ILogger<SearchGateway> logger)
    {
        _v2 = v2 ?? throw new ArgumentNullException(nameof(v2));
        _legacy = legacy ?? throw new ArgumentNullException(nameof(legacy));
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
        _highlight = highlight ?? throw new ArgumentNullException(nameof(highlight));
        _analytics = analytics ?? throw new ArgumentNullException(nameof(analytics));
        _accessFactory = accessFactory ?? throw new ArgumentNullException(nameof(accessFactory));
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

        // Run V2 only when it can serve this user or when shadow comparison is explicitly enabled.
        // When both paths are needed they are still started before either is awaited.
        var runV2 = _options.Enabled && (serveV2ToUser || _options.ShadowMode);
        var v2Task = runV2
            ? _v2.SearchAsync(request, user, cancellationToken)
            : null;

        var legacyRequired = !_options.Enabled || !serveV2ToUser || _options.ShadowMode;
        var legacyTask = legacyRequired
            ? ReadLegacyAuthorizedAsync(normalized.Original, user, cancellationToken)
            : null;

        SearchResponse? v2Response = null;
        if (v2Task is not null)
        {
            v2Response = await v2Task;
        }

        IReadOnlyList<GlobalSearchHit>? legacyResults = null;
        if (legacyTask is not null)
        {
            legacyResults = await legacyTask;
        }

        if (_options.ShadowMode && v2Response is { IsReady: true } && legacyResults is not null)
        {
            await SafeShadowLogAsync(normalized.Original, legacyResults, v2Response.Results, cancellationToken);
        }

        if (serveV2ToUser && v2Response is { IsReady: true })
        {
            stopwatch.Stop();
            var gatewayLatencyMs = stopwatch.ElapsedMilliseconds;
            await SafeQueryLogAsync(
                normalized.Original,
                user,
                v2Response.TotalHits,
                v2Response.QueryTimeMilliseconds,
                "V2-Engine",
                v2Response.CorrectedQuery,
                cancellationToken);
            await SafeQueryLogAsync(
                normalized.Original,
                user,
                v2Response.TotalHits,
                gatewayLatencyMs,
                "V2",
                v2Response.CorrectedQuery,
                cancellationToken);

            return new SearchGatewayResponse(
                v2Response.Query,
                v2Response.Results,
                v2Response.TotalHits,
                v2Response.Facets,
                v2Response.NextCursor,
                gatewayLatencyMs,
                true,
                v2Response.IsPartial,
                v2Response.CorrectedQuery);
        }

        // V2 may be enabled without Legacy having been started (for example,
        // ServeV2=true but the index is temporarily unavailable). Fall back
        // safely and apply source-module authorization before adapting results.
        legacyResults ??= await ReadLegacyAuthorizedAsync(normalized.Original, user, cancellationToken);

        var categoryFacets = BuildLegacyFacets(legacyResults, static hit => LegacyCategory(hit.Source));
        var sourceFacets = BuildLegacyFacets(legacyResults, static hit => hit.Source);
        var filtered = ApplyLegacyFilters(legacyResults, request.Categories, request.Sources);
        var adapted = filtered.Select((hit, index) => AdaptLegacy(hit, normalized, index + 1)).ToArray();

        stopwatch.Stop();
        await SafeQueryLogAsync(
            normalized.Original,
            user,
            adapted.Length,
            stopwatch.ElapsedMilliseconds,
            "Legacy",
            null,
            cancellationToken);

        // Legacy providers return bounded candidate sets rather than a true
        // corpus count. IsPartial=true ensures the page labels these as Top
        // results instead of presenting adapted.Length as an authoritative corpus total.
        return new SearchGatewayResponse(
            normalized.Original,
            adapted,
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
            null);
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

    private static IReadOnlyList<GlobalSearchHit> FilterLegacyByAccess(
        IReadOnlyList<GlobalSearchHit> hits,
        SearchAccessContext access)
    {
        var policies = new HashSet<string>(access.AllowedPolicies, StringComparer.Ordinal);
        return hits.Where(hit => hit.Source switch
        {
            "Document Repository" => policies.Contains(Policies.Documents.View),
            "IPR" => policies.Contains(Policies.Ipr.View),
            "Visits tracker" => policies.Contains(ProjectOfficeReportsPolicies.ViewVisits),
            "Training tracker" => policies.Contains(ProjectOfficeReportsPolicies.ViewTrainingTracker),
            "TOT tracker" => policies.Contains(ProjectOfficeReportsPolicies.ViewTotTracker),
            "Proliferation survey" => policies.Contains(ProjectOfficeReportsPolicies.ViewProliferationTracker),
            _ => true
        }).ToArray();
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

    private async Task SafeShadowLogAsync(
        string query,
        IReadOnlyList<GlobalSearchHit> legacy,
        IReadOnlyList<SearchResult> v2,
        CancellationToken cancellationToken)
    {
        try
        {
            await _analytics.LogShadowAsync(query, legacy, v2, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "Search shadow telemetry write failed.");
        }
    }
}
