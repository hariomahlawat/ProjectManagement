using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Search;
using ProjectManagement.Services.SearchV2;
using ProjectManagement.Services.SearchV2.Analytics;
using ProjectManagement.Services.SearchV2.Models;
using ProjectManagement.Services.SearchV2.Query;
using ProjectManagement.Services.SearchV2.Security;
using Xunit;

namespace ProjectManagement.Tests.Search;

public sealed class SearchGatewayTests
{
    [Fact]
    public async Task ServingV2_DoesNotRunLegacyOrShadow_ByDefault_AndLogsOneRow()
    {
        var harness = new Harness(new SearchV2Options { ServeV2 = true, ShadowMode = true });

        var response = await harness.Gateway.SearchAsync(new SearchRequest("radar"), User(), CancellationToken.None);

        Assert.True(response.UsedSearchV2);
        Assert.Equal(0, harness.Legacy.Calls);
        Assert.Empty(harness.Shadow.Jobs);
        var row = Assert.Single(harness.Analytics.QueryEngines);
        Assert.Equal("V2", row);
        Assert.Equal(new long[] { 40 }, harness.Latency.Recorded);
    }

    [Fact]
    public async Task ServingV2_WithSampling_DispatchesShadowOffTheRequestPath()
    {
        var harness = new Harness(new SearchV2Options { ServeV2 = true, ShadowMode = true, ShadowSampleRate = 1 });

        await harness.Gateway.SearchAsync(new SearchRequest("radar"), User(), CancellationToken.None);

        Assert.Equal(0, harness.Legacy.Calls);
        var job = Assert.Single(harness.Shadow.Jobs);
        Assert.Null(job.LegacyResults);
        Assert.NotNull(job.V2Results);
        Assert.Equal("radar", job.Query);
    }

    [Fact]
    public async Task CursorPages_AreNeverShadowed()
    {
        var harness = new Harness(new SearchV2Options { ServeV2 = true, ShadowMode = true, ShadowSampleRate = 1 });

        await harness.Gateway.SearchAsync(new SearchRequest("radar", Cursor: "next-page"), User(), CancellationToken.None);

        Assert.Empty(harness.Shadow.Jobs);
    }

    [Fact]
    public async Task ServingLegacy_DispatchesV2Shadow_WithoutWaitingForV2()
    {
        var harness = new Harness(new SearchV2Options { ServeV2 = false, ShadowMode = true });

        var response = await harness.Gateway.SearchAsync(new SearchRequest("radar"), User(), CancellationToken.None);

        Assert.False(response.UsedSearchV2);
        Assert.Equal(0, harness.Engine.SearchCalls);
        Assert.Equal(1, harness.Legacy.Calls);
        var job = Assert.Single(harness.Shadow.Jobs);
        Assert.NotNull(job.LegacyResults);
        Assert.Null(job.V2Results);
        Assert.Equal(new[] { "Legacy" }, harness.Analytics.QueryEngines);
    }

    [Fact]
    public async Task V2Failure_FallsBackToLegacy_WithoutShadow()
    {
        var harness = new Harness(new SearchV2Options { ServeV2 = true, ShadowMode = true, ShadowSampleRate = 1 });
        harness.Engine.Response = SearchResponse.NotReady("radar", SearchV2ExecutionStatus.QueryFailed, "ABC");

        var response = await harness.Gateway.SearchAsync(new SearchRequest("radar"), User(), CancellationToken.None);

        Assert.True(response.FellBackToLegacy);
        Assert.Equal(1, harness.Legacy.Calls);
        Assert.Empty(harness.Shadow.Jobs);
        Assert.Equal(new[] { "Legacy-Fallback" }, harness.Analytics.QueryEngines);
    }

    [Fact]
    public async Task LegacyResults_AreDenyByDefault_ForUnmappedAndUnauthorisedSources()
    {
        var harness = new Harness(new SearchV2Options { ServeV2 = false, ShadowMode = false });
        harness.Access.Policies = new[] { Policies.Ipr.View };
        harness.Legacy.Hits = new[]
        {
            Hit("Projects"),
            Hit("IPR"),
            Hit("FFC"),
            Hit("Visits tracker"),
            Hit("Proliferation survey"),
            Hit("Some new provider")
        };

        var response = await harness.Gateway.SearchAsync(new SearchRequest("radar"), User(), CancellationToken.None);

        Assert.Equal(
            new[] { "Projects", "IPR", "FFC" },
            response.Results.Select(result => result.SourceModule).ToArray());
    }

    [Theory]
    [InlineData(true, null, 0.0)]
    [InlineData(false, null, 1.0)]
    [InlineData(true, 0.25, 0.25)]
    [InlineData(false, 0.0, 0.0)]
    public void ShadowSampleRate_DefaultsToZeroWhileV2Serves(bool servedByV2, double? configured, double expected)
    {
        var harness = new Harness(new SearchV2Options { ShadowSampleRate = configured });

        Assert.Equal(expected, harness.Gateway.EffectiveShadowSampleRate(servedByV2));
    }

    private static GlobalSearchHit Hit(string source) =>
        new(source, $"{source} hit", null, $"/{source.Replace(' ', '-')}", null, 1m, null, null);

    private static ClaimsPrincipal User() =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "u1") }, "Test"));

    private sealed class Harness
    {
        public Harness(SearchV2Options options)
        {
            Gateway = new SearchGateway(
                Engine,
                Legacy,
                new SearchQueryNormalizer(),
                new SearchHighlightService(Options.Create(options)),
                Analytics,
                Access,
                Shadow,
                Latency,
                Options.Create(options),
                NullLogger<SearchGateway>.Instance);
        }

        public FakeEngine Engine { get; } = new();
        public FakeLegacy Legacy { get; } = new();
        public FakeAnalytics Analytics { get; } = new();
        public FakeAccess Access { get; } = new();
        public FakeShadow Shadow { get; } = new();
        public FakeLatency Latency { get; } = new();
        public SearchGateway Gateway { get; }
    }

    private sealed class FakeEngine : ISearchV2Engine
    {
        public int SearchCalls { get; private set; }

        public SearchResponse Response { get; set; } = new(
            "radar",
            Array.Empty<SearchResult>(),
            0,
            0,
            SearchFacets.Empty,
            null,
            40,
            true);

        public Task<SearchResponse> SearchAsync(SearchRequest request, ClaimsPrincipal user, CancellationToken cancellationToken)
        {
            SearchCalls++;
            return Task.FromResult(Response);
        }

        public Task<IReadOnlyList<SearchSuggestion>> SuggestAsync(string query, ClaimsPrincipal user, int? limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SearchSuggestion>>(Array.Empty<SearchSuggestion>());
    }

    private sealed class FakeLegacy : IGlobalSearchService
    {
        public int Calls { get; private set; }
        public IReadOnlyList<GlobalSearchHit> Hits { get; set; } = new[] { Hit("Projects") };

        public Task<IReadOnlyList<GlobalSearchHit>> SearchAsync(string query, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Hits);
        }
    }

    private sealed class FakeAccess : ISearchAccessContextFactory
    {
        public IReadOnlyList<string> Policies { get; set; } = SearchSourcePolicies.AllPolicies;

        public Task<SearchAccessContext> CreateAsync(ClaimsPrincipal user, CancellationToken cancellationToken) =>
            Task.FromResult(new SearchAccessContext("u1", Array.Empty<string>(), Policies));
    }

    private sealed class FakeAnalytics : ISearchAnalyticsService
    {
        public List<string> QueryEngines { get; } = new();

        public Task LogQueryAsync(string query, ClaimsPrincipal user, long resultCount, long latencyMs, string engine, string? correction, CancellationToken cancellationToken)
        {
            QueryEngines.Add(engine);
            return Task.CompletedTask;
        }

        public Task LogShadowAsync(string query, IReadOnlyList<GlobalSearchHit> legacy, IReadOnlyList<SearchResult> v2, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Shadow comparisons must go through the dispatcher.");

        public Task LogClickAsync(string query, string entityType, string entityKey, int rank, string sourceModule, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task PruneAsync(int retentionDays, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeShadow : ISearchShadowDispatcher
    {
        public List<SearchShadowJob> Jobs { get; } = new();

        public bool TryDispatch(SearchShadowJob job)
        {
            Jobs.Add(job);
            return true;
        }
    }

    private sealed class FakeLatency : ISearchEngineLatencyRecorder
    {
        public List<long> Recorded { get; } = new();

        public void Record(long milliseconds) => Recorded.Add(milliseconds);

        public SearchLatencySnapshot Snapshot(TimeSpan window) => SearchLatencySnapshot.Empty;
    }
}

public sealed class SearchSourcePoliciesTests
{
    [Fact]
    public void AllPolicies_MatchTheSearchAccessPolicySet()
    {
        Assert.Equal(
            new[]
            {
                Policies.Documents.View,
                Policies.Ipr.View,
                ProjectOfficeReportsPolicies.ViewVisits,
                ProjectOfficeReportsPolicies.ViewTotTracker,
                ProjectOfficeReportsPolicies.ViewTrainingTracker,
                ProjectOfficeReportsPolicies.ViewProliferationTracker,
                ProjectOfficeReportsPolicies.ViewArpp
            }.OrderBy(value => value, StringComparer.Ordinal),
            SearchSourcePolicies.AllPolicies.OrderBy(value => value, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("Document repository", Policies.Documents.View)]
    [InlineData("Document Repository", Policies.Documents.View)]
    [InlineData("IPR", Policies.Ipr.View)]
    [InlineData("Visits", ProjectOfficeReportsPolicies.ViewVisits)]
    [InlineData("Visits tracker", ProjectOfficeReportsPolicies.ViewVisits)]
    [InlineData("ToT", ProjectOfficeReportsPolicies.ViewTotTracker)]
    [InlineData("TOT tracker", ProjectOfficeReportsPolicies.ViewTotTracker)]
    [InlineData("Proliferation", ProjectOfficeReportsPolicies.ViewProliferationTracker)]
    [InlineData("Proliferation survey", ProjectOfficeReportsPolicies.ViewProliferationTracker)]
    [InlineData("Training tracker", ProjectOfficeReportsPolicies.ViewTrainingTracker)]
    [InlineData("ARPP", ProjectOfficeReportsPolicies.ViewArpp)]
    [InlineData("Projects", null)]
    [InlineData("Project documents", null)]
    [InlineData("FFC", null)]
    [InlineData("Activities", null)]
    [InlineData("Social media", null)]
    [InlineData("Social media tracker", null)]
    public void KnownSources_MapToTheirModulePolicy(string source, string? expectedPolicy)
    {
        Assert.True(SearchSourcePolicies.TryGetRequiredPolicy(source, out var policy));
        Assert.Equal(expectedPolicy, policy);
    }

    [Fact]
    public void UnknownSources_AreHiddenAndCannotBeIndexed()
    {
        var everyPolicy = new HashSet<string>(SearchSourcePolicies.AllPolicies, StringComparer.Ordinal);

        Assert.False(SearchSourcePolicies.IsVisible("Brand new module", everyPolicy));
        Assert.False(SearchSourcePolicies.IsVisible(null, everyPolicy));
        Assert.Throws<InvalidOperationException>(() => SearchSourcePolicies.RequiredPolicyFor("Brand new module"));
    }
}
