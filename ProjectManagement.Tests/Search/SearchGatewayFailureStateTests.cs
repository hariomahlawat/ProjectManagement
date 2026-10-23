using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Services.Search;
using ProjectManagement.Services.SearchV2;
using ProjectManagement.Services.SearchV2.Analytics;
using ProjectManagement.Services.SearchV2.Models;
using ProjectManagement.Services.SearchV2.Query;
using ProjectManagement.Services.SearchV2.Security;
using Xunit;

namespace ProjectManagement.Tests.Search;

/// <summary>
/// The Search page must tell "nothing matched" apart from "search could not run":
/// a failed engine is reported as unavailable, a V2 fallback as degraded.
/// </summary>
public sealed class SearchGatewayFailureStateTests
{
    private static readonly ClaimsPrincipal User = new(new ClaimsIdentity(
        new[] { new Claim(ClaimTypes.NameIdentifier, "user-1"), new Claim(ClaimTypes.Name, "officer") },
        "Test"));

    [Fact]
    public async Task LegacyFailure_WhenV2CannotServe_IsUnavailableNotEmpty()
    {
        var gateway = CreateGateway(
            v2: SearchResponse.NotReady("aura"),
            legacy: () => throw new InvalidOperationException("database down"),
            serveV2: true);

        var response = await gateway.SearchAsync(new SearchRequest("aura"), User, CancellationToken.None);

        Assert.True(response.IsUnavailable);
        Assert.False(response.IsDegraded);
        Assert.Empty(response.Results);
    }

    [Fact]
    public async Task V2NotReady_WithWorkingLegacy_IsDegraded()
    {
        var gateway = CreateGateway(
            v2: SearchResponse.NotReady("aura"),
            legacy: () => new[] { new GlobalSearchHit("Projects", "AURA", null, "/Projects/Overview/1", null, 1m, null, null) },
            serveV2: true);

        var response = await gateway.SearchAsync(new SearchRequest("aura"), User, CancellationToken.None);

        Assert.True(response.FellBackToLegacy);
        Assert.True(response.IsDegraded);
        Assert.False(response.IsUnavailable);
        Assert.Single(response.Results);
    }

    [Fact]
    public async Task LegacyAsTheConfiguredEngine_IsNeitherDegradedNorUnavailable()
    {
        var gateway = CreateGateway(
            v2: SearchResponse.NotReady("aura"),
            legacy: () => Array.Empty<GlobalSearchHit>(),
            serveV2: false);

        var response = await gateway.SearchAsync(new SearchRequest("aura"), User, CancellationToken.None);

        Assert.False(response.IsDegraded);
        Assert.False(response.IsUnavailable);
        Assert.Empty(response.Results);
    }

    private static SearchGateway CreateGateway(SearchResponse v2, Func<IReadOnlyList<GlobalSearchHit>> legacy, bool serveV2)
    {
        var options = Options.Create(new SearchV2Options { Enabled = true, ServeV2 = serveV2, ShadowMode = false });
        return new SearchGateway(
            new StubEngine(v2),
            new StubLegacy(legacy),
            new SearchQueryNormalizer(),
            new SearchHighlightService(options),
            new NoopAnalytics(),
            new StubAccess(),
            options,
            NullLogger<SearchGateway>.Instance);
    }

    private sealed class StubEngine(SearchResponse response) : ISearchV2Engine
    {
        public Task<SearchResponse> SearchAsync(SearchRequest request, ClaimsPrincipal user, CancellationToken cancellationToken)
            => Task.FromResult(response);

        public Task<IReadOnlyList<SearchSuggestion>> SuggestAsync(string query, ClaimsPrincipal user, int? limit, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<SearchSuggestion>>(Array.Empty<SearchSuggestion>());
    }

    private sealed class StubLegacy(Func<IReadOnlyList<GlobalSearchHit>> search) : IGlobalSearchService
    {
        public Task<IReadOnlyList<GlobalSearchHit>> SearchAsync(string query, CancellationToken cancellationToken)
        {
            try
            {
                return Task.FromResult(search());
            }
            catch (Exception ex)
            {
                return Task.FromException<IReadOnlyList<GlobalSearchHit>>(ex);
            }
        }
    }

    private sealed class StubAccess : ISearchAccessContextFactory
    {
        public Task<SearchAccessContext> CreateAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
            => Task.FromResult(new SearchAccessContext("user-1", Array.Empty<string>(), Array.Empty<string>()));
    }

    private sealed class NoopAnalytics : ISearchAnalyticsService
    {
        public Task LogQueryAsync(string query, ClaimsPrincipal user, long resultCount, long latencyMs, string engine, string? correction, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LogShadowAsync(string query, IReadOnlyList<GlobalSearchHit> legacy, IReadOnlyList<SearchResult> v2, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LogClickAsync(string query, string entityType, string entityKey, int rank, string sourceModule, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PruneAsync(int retentionDays, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
