using System.Security.Claims;
using ProjectManagement.Services.Search;
using ProjectManagement.Services.SearchV2.Models;
using ProjectManagement.Services.SearchV2.Query;
using ProjectManagement.Services.SearchV2.Security;

namespace ProjectManagement.Services.SearchV2.Analytics;

/// <summary>
/// A sampled engine comparison: the engine that served the user supplied its results,
/// and the dispatcher runs the other engine. Exactly one of the result lists is null.
/// </summary>
public sealed record SearchShadowJob(
    string Query,
    SearchRequest Request,
    ClaimsPrincipal User,
    IReadOnlyList<GlobalSearchHit>? LegacyResults,
    IReadOnlyList<SearchResult>? V2Results);

/// <summary>
/// Runs shadow comparisons off the request path. The served response never waits for
/// the other engine or the comparison insert. Work runs in its own DI scope (the request
/// scope may already be disposed), is bounded to a small number of concurrent jobs, and
/// is dropped rather than queued when the bound is reached.
/// </summary>
public interface ISearchShadowDispatcher
{
    /// <summary>Returns false when the job was dropped because shadow capacity is busy.</summary>
    bool TryDispatch(SearchShadowJob job);
}

public sealed class SearchShadowDispatcher : ISearchShadowDispatcher
{
    private const int MaxConcurrentJobs = 2;
    private static readonly TimeSpan JobTimeout = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SearchShadowDispatcher> _logger;
    private readonly SemaphoreSlim _slots = new(MaxConcurrentJobs, MaxConcurrentJobs);

    public SearchShadowDispatcher(IServiceScopeFactory scopeFactory, ILogger<SearchShadowDispatcher> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool TryDispatch(SearchShadowJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (!_slots.Wait(0))
        {
            _logger.LogDebug("Search shadow comparison skipped: shadow capacity is busy.");
            return false;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await RunAsync(job);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Search shadow comparison failed.");
            }
            finally
            {
                _slots.Release();
            }
        });
        return true;
    }

    internal async Task RunAsync(SearchShadowJob job)
    {
        using var timeout = new CancellationTokenSource(JobTimeout);
        var cancellationToken = timeout.Token;
        await using var scope = _scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var legacy = job.LegacyResults;
        if (legacy is null)
        {
            var access = await services.GetRequiredService<ISearchAccessContextFactory>().CreateAsync(job.User, cancellationToken);
            var hits = await services.GetRequiredService<IGlobalSearchService>().SearchAsync(job.Query, cancellationToken);
            legacy = SearchGateway.FilterLegacyByAccess(hits, access);
        }

        var v2 = job.V2Results;
        if (v2 is null)
        {
            var response = await services.GetRequiredService<ISearchV2Engine>().SearchAsync(job.Request, job.User, cancellationToken);
            if (!response.IsReady) return;
            v2 = response.Results;
        }

        await services.GetRequiredService<ISearchAnalyticsService>().LogShadowAsync(job.Query, legacy, v2, cancellationToken);
    }
}
