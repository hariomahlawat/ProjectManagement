using Microsoft.Extensions.Options;

namespace ProjectManagement.Services.SearchV2.Indexing;

public sealed class SearchIndexWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SearchV2Options _options;
    private readonly ILogger<SearchIndexWorker> _logger;

    public SearchIndexWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<SearchV2Options> options,
        ILogger<SearchIndexWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Search V2 indexing worker is disabled.");
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        await EnsureIndexAsync(stoppingToken);
        await RecoverAbandonedWorkAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, _options.WorkerIntervalSeconds)));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RecoverAbandonedWorkAsync(stoppingToken);
                await ProcessIncrementalQueueAsync(stoppingToken);
                await ReconcileIfDueAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
    }

    private async Task RecoverAbandonedWorkAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISearchIndexStore>();
        try
        {
            var recovered = await store.RecoverStaleWorkItemsAsync(TimeSpan.FromMinutes(Math.Max(1, _options.WorkItemLeaseMinutes)), cancellationToken);
            if (recovered > 0)
            {
                _logger.LogWarning("Search V2 recovered {Count} abandoned indexing work item(s).", recovered);
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "Search V2 stale indexing lease recovery is not yet available.");
        }
    }

    private async Task EnsureIndexAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISearchIndexStore>();
        if (await store.IsReadyAsync(_options.IndexVersion, cancellationToken)) return;

        await FullRebuildAsync(scope.ServiceProvider, cancellationToken);
    }

    private async Task ProcessIncrementalQueueAsync(CancellationToken cancellationToken)
    {
        for (var processed = 0; processed < 50 && !cancellationToken.IsCancellationRequested; processed++)
        {
            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<ISearchIndexStore>();
            if (!await store.IsReadyAsync(_options.IndexVersion, cancellationToken))
            {
                await FullRebuildAsync(scope.ServiceProvider, cancellationToken);
                return;
            }

            var item = await store.DequeueAsync(cancellationToken);
            if (item is null) return;

            try
            {
                var builder = scope.ServiceProvider.GetRequiredService<ISearchProjectionBuilder>();
                var projections = await builder.BuildEntityAsync(item.EntityType, item.EntityKey, cancellationToken);
                await store.ReplaceEntityAsync(item.EntityType, item.EntityKey, projections, _options.IndexVersion, cancellationToken);
                await store.CompleteAsync(item.Id, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Search V2 failed to index {EntityType}/{EntityKey}.", item.EntityType, item.EntityKey);
                await store.FailAsync(item.Id, ex.Message, cancellationToken);
            }
        }
    }

    private async Task ReconcileIfDueAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISearchIndexStore>();
        var health = await store.GetHealthAsync(cancellationToken);
        if (!health.IsReady) return;

        var last = health.LastFullRebuildUtc ?? DateTimeOffset.MinValue;
        if (DateTimeOffset.UtcNow - last < TimeSpan.FromMinutes(Math.Max(1, _options.FullReconciliationMinutes)))
        {
            return;
        }

        await FullRebuildAsync(scope.ServiceProvider, cancellationToken);
    }

    private async Task FullRebuildAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        try
        {
            var builder = services.GetRequiredService<ISearchProjectionBuilder>();
            var store = services.GetRequiredService<ISearchIndexStore>();
            var projections = await builder.BuildAllAsync(cancellationToken);
            await store.ReplaceFullGenerationAsync(projections, _options.IndexVersion, cancellationToken);
            _logger.LogInformation(
                "Search V2 full index rebuild activated {EntryCount} entries in {ElapsedMs} ms.",
                projections.Count,
                (DateTimeOffset.UtcNow - started).TotalMilliseconds);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Search V2 full index rebuild failed. Legacy search remains available.");
        }
    }
}
