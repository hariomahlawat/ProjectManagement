using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace ProjectManagement.Services.Search;

// SECTION: Global search contract
public interface IGlobalSearchService
{
    Task<IReadOnlyList<GlobalSearchHit>> SearchAsync(string query, CancellationToken cancellationToken);
}

// SECTION: Legacy global search implementation
// Search V2 is the primary long-term engine. This service remains deliberately
// robust while it is retained for warm-up, shadow comparison and rollback.
public sealed class GlobalSearchService : IGlobalSearchService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GlobalSearchService> _logger;

    public GlobalSearchService(IServiceScopeFactory scopeFactory, ILogger<GlobalSearchService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<GlobalSearchHit>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<GlobalSearchHit>();
        }

        // Each provider keeps its own DI scope/DbContext. EF DbContext is not
        // thread-safe, so this is what makes the fan-out genuinely concurrent.
        using var docScope = _scopeFactory.CreateScope();
        using var ffcScope = _scopeFactory.CreateScope();
        using var iprScope = _scopeFactory.CreateScope();
        using var actScope = _scopeFactory.CreateScope();
        using var projectScope = _scopeFactory.CreateScope();
        using var projectDocumentScope = _scopeFactory.CreateScope();
        using var reportsScope = _scopeFactory.CreateScope();

        var docService = docScope.ServiceProvider.GetRequiredService<DocRepo.IGlobalDocRepoSearchService>();
        var ffcService = ffcScope.ServiceProvider.GetRequiredService<IGlobalFfcSearchService>();
        var iprService = iprScope.ServiceProvider.GetRequiredService<IGlobalIprSearchService>();
        var actService = actScope.ServiceProvider.GetRequiredService<IGlobalActivitiesSearchService>();
        var projectService = projectScope.ServiceProvider.GetRequiredService<IGlobalProjectSearchService>();
        var projectDocumentService = projectDocumentScope.ServiceProvider.GetRequiredService<IGlobalProjectDocumentSearchService>();
        var reportsService = reportsScope.ServiceProvider.GetRequiredService<IGlobalProjectReportsSearchService>();

        var tasks = new[]
        {
            SafeSearchAsync("Document Repository", () => docService.SearchAsync(query, 30, cancellationToken), cancellationToken),
            SafeSearchAsync("FFC", () => ffcService.SearchAsync(query, 20, cancellationToken), cancellationToken),
            SafeSearchAsync("IPR", () => iprService.SearchAsync(query, 20, cancellationToken), cancellationToken),
            SafeSearchAsync("Activities", () => actService.SearchAsync(query, 20, cancellationToken), cancellationToken),
            SafeSearchAsync("Projects", () => projectService.SearchAsync(query, 20, cancellationToken), cancellationToken),
            SafeSearchAsync("Project documents", () => projectDocumentService.SearchAsync(query, 20, cancellationToken), cancellationToken),
            SafeSearchAsync("Project Office trackers", () => reportsService.SearchAsync(query, 20, cancellationToken), cancellationToken)
        };

        var providerResults = await Task.WhenAll(tasks);
        var combined = providerResults.SelectMany(result => result).ToList();
        if (combined.Count == 0)
        {
            return Array.Empty<GlobalSearchHit>();
        }

        // URL deduplication is retained only as a legacy safety mechanism.
        // Search V2 performs canonical-entity clustering before pagination.
        return combined
            .GroupBy(hit => hit.Url, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(hit => hit.Score)
                .ThenByDescending(hit => hit.Date)
                .First())
            .OrderByDescending(hit => hit.Score)
            .ThenByDescending(hit => hit.Date)
            .ToList();
    }

    private async Task<IReadOnlyList<GlobalSearchHit>> SafeSearchAsync(
        string provider,
        Func<Task<IReadOnlyList<GlobalSearchHit>>> search,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var results = await search();
            stopwatch.Stop();
            _logger.LogDebug(
                "Legacy search provider {Provider} returned {Count} candidate(s) in {ElapsedMilliseconds} ms.",
                provider,
                results.Count,
                stopwatch.ElapsedMilliseconds);
            return results;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                ex,
                "Legacy search provider {Provider} failed after {ElapsedMilliseconds} ms. Other providers will continue.",
                provider,
                stopwatch.ElapsedMilliseconds);
            return Array.Empty<GlobalSearchHit>();
        }
    }
}
