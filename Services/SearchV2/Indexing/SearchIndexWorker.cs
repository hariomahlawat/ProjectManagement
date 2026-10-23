using Microsoft.Extensions.Options;
using ProjectManagement.Services.SearchV2.Models;

namespace ProjectManagement.Services.SearchV2.Indexing;

/// <summary>
/// Keeps the Search V2 index current. Every step of a tick is failure-isolated: the
/// host's default <c>BackgroundServiceExceptionBehavior.StopHost</c> would otherwise
/// turn a database blip in this optional worker into a full PRISM outage. Failed ticks
/// back off exponentially, and failed automatic full rebuilds back off for
/// <see cref="SearchV2Options.FullRebuildRetryMinutes"/> instead of retrying every tick.
///
/// Several processes may run this worker at once (web farm or rolling deployment).
/// A process only rebuilds when the active generation is missing or older than its own
/// projection version; when the active generation is newer it goes passive. Full
/// rebuilds and incremental passes are serialised across processes by
/// <see cref="ISearchIndexCoordinator"/>.
/// </summary>
public sealed class SearchIndexWorker : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxTickBackoff = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan BusyRebuildRequestRetry = TimeSpan.FromMinutes(1);
    private const int MaxIncrementalItemsPerTick = 50;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SearchV2Options _options;
    private readonly ILogger<SearchIndexWorker> _logger;
    private readonly TimeProvider _time;

    private DateTimeOffset _nextAutomaticRebuildUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _nextRebuildRequestRecoveryUtc = DateTimeOffset.MinValue;
    private int _consecutiveRebuildFailures;
    private int? _passiveForVersion;

    public SearchIndexWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<SearchV2Options> options,
        ILogger<SearchIndexWorker> logger)
        : this(scopeFactory, options, logger, TimeProvider.System)
    {
    }

    internal SearchIndexWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<SearchV2Options> options,
        ILogger<SearchIndexWorker> logger,
        TimeProvider time)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    internal bool IsPassive => _passiveForVersion.HasValue;

    internal DateTimeOffset NextAutomaticRebuildUtc => _nextAutomaticRebuildUtc;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Search V2 indexing worker is disabled.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.WorkerIntervalSeconds));
        var delay = InitialDelay;
        var consecutiveFailedTicks = 0;
        while (true)
        {
            try
            {
                await Task.Delay(delay, _time, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            bool healthy;
            try
            {
                healthy = await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // RunOnceAsync isolates each step; this is the last line of defence.
                _logger.LogError(ex, "Search V2 indexing tick failed unexpectedly.");
                healthy = false;
            }

            consecutiveFailedTicks = healthy ? 0 : consecutiveFailedTicks + 1;
            delay = TickDelay(interval, consecutiveFailedTicks);
        }
    }

    internal static TimeSpan TickDelay(TimeSpan interval, int consecutiveFailedTicks)
    {
        if (consecutiveFailedTicks <= 0) return interval;
        var factor = Math.Pow(2, Math.Min(consecutiveFailedTicks, 10));
        var backoff = TimeSpan.FromTicks((long)Math.Min(interval.Ticks * factor, MaxTickBackoff.Ticks));
        return backoff > interval ? backoff : interval;
    }

    /// <summary>
    /// Runs one indexing tick. Returns false when any step failed (the caller backs off).
    /// Never throws except for cancellation.
    /// </summary>
    internal async Task<bool> RunOnceAsync(CancellationToken cancellationToken)
    {
        SearchIndexActiveState state = SearchIndexActiveState.None;
        if (!await TryStepAsync("read index state", async () => state = await ReadStateAsync(cancellationToken), cancellationToken))
        {
            return false;
        }

        var recoveryHealthy = await TryStepAsync("abandoned rebuild request recovery", () => RecoverAbandonedRebuildRequestsIfDueAsync(cancellationToken), cancellationToken);

        if (IsNewerThanOwn(state))
        {
            EnterPassive(state);
            // An administrator's explicit rebuild request is still honoured: it is the
            // recovery path after rolling back to an older build of PRISM.
            return recoveryHealthy
                   & await TryStepAsync("explicit rebuild request", () => ProcessExplicitRebuildRequestAsync(allowDowngrade: true, cancellationToken), cancellationToken);
        }

        LeavePassiveIfNeeded();

        if (!state.HasActiveGeneration || state.IndexVersion < _options.ProjectionVersion)
        {
            // An administrator's request must bypass the automatic-rebuild backoff even
            // while the initial or upgrade rebuild is failing.
            return recoveryHealthy
                   & await TryStepAsync(
                       "initial full rebuild",
                       async () =>
                       {
                           if (await ProcessExplicitRebuildRequestAsync(allowDowngrade: false, cancellationToken)) return;
                           await RunAutomaticFullRebuildAsync(
                               current => !current.HasActiveGeneration || current.IndexVersion < _options.ProjectionVersion,
                               cancellationToken);
                       },
                       cancellationToken);
        }

        var healthy = recoveryHealthy;
        healthy &= await TryStepAsync("stale lease recovery", () => RecoverAbandonedWorkAsync(cancellationToken), cancellationToken);
        healthy &= await TryStepAsync("incremental indexing", () => ProcessIncrementalQueueAsync(cancellationToken), cancellationToken);
        healthy &= await TryStepAsync("scheduled reconciliation", () => ReconcileIfDueAsync(state, cancellationToken), cancellationToken);
        return healthy;
    }

    private async Task<bool> TryStepAsync(string step, Func<Task> action, CancellationToken cancellationToken)
    {
        try
        {
            await action();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Search V2 indexing step '{Step}' failed. The worker will retry with backoff.", step);
            return false;
        }
    }

    private async Task<SearchIndexActiveState> ReadStateAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISearchIndexStore>();
        return await store.GetActiveStateAsync(cancellationToken);
    }

    private bool IsNewerThanOwn(SearchIndexActiveState state) =>
        state.HasActiveGeneration && state.IndexVersion > _options.ProjectionVersion;

    private void EnterPassive(SearchIndexActiveState state)
    {
        if (_passiveForVersion == state.IndexVersion) return;
        _passiveForVersion = state.IndexVersion;
        _logger.LogWarning(
            "Search V2 active index is projection version {ActiveVersion}, newer than this process's {OwnVersion}. Indexing is passive in this process; it serves Legacy search until it is replaced. An administrator rebuild request rebuilds at version {OwnVersion}.",
            state.IndexVersion,
            _options.ProjectionVersion,
            _options.ProjectionVersion);
    }

    private void LeavePassiveIfNeeded()
    {
        if (!_passiveForVersion.HasValue) return;
        _passiveForVersion = null;
        _logger.LogInformation("Search V2 indexing resumed for projection version {OwnVersion}.", _options.ProjectionVersion);
    }

    private async Task RecoverAbandonedWorkAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISearchIndexStore>();
        var recovered = await store.RecoverStaleWorkItemsAsync(LeaseTimeout, cancellationToken);
        if (recovered > 0)
        {
            _logger.LogWarning("Search V2 recovered {Count} abandoned indexing work item(s).", recovered);
        }
    }

    private TimeSpan LeaseTimeout => TimeSpan.FromMinutes(Math.Max(1, _options.WorkItemLeaseMinutes));

    private async Task ProcessIncrementalQueueAsync(CancellationToken cancellationToken)
    {
        SearchIndexWorkItem? rebuildRequest = null;
        using (var lockScope = _scopeFactory.CreateScope())
        {
            var coordinator = lockScope.ServiceProvider.GetRequiredService<ISearchIndexCoordinator>();
            await using var lease = await coordinator.TryAcquireIncrementalAsync(cancellationToken);
            if (lease is null)
            {
                _logger.LogDebug("Search V2 incremental indexing skipped while a full rebuild is running.");
                return;
            }

            for (var processed = 0; processed < MaxIncrementalItemsPerTick && !cancellationToken.IsCancellationRequested; processed++)
            {
                using var scope = _scopeFactory.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<ISearchIndexStore>();
                var item = await store.DequeueAsync(cancellationToken);
                if (item is null) return;

                if (string.Equals(item.EntityType, SearchIndexStore.FullRebuildEntityType, StringComparison.Ordinal))
                {
                    // The rebuild needs the exclusive lock, which this pass's shared lease blocks.
                    rebuildRequest = item;
                    break;
                }

                try
                {
                    var builder = scope.ServiceProvider.GetRequiredService<ISearchProjectionBuilder>();
                    var projections = await builder.BuildEntityAsync(item.EntityType, item.EntityKey, cancellationToken);
                    await store.ReplaceEntityAsync(item.EntityType, item.EntityKey, projections, _options.ProjectionVersion, cancellationToken);
                    await store.CompleteAsync(item.Id, cancellationToken);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Search V2 failed to index {EntityType}/{EntityKey}.", item.EntityType, item.EntityKey);
                    await store.FailAsync(item.Id, ex.Message, cancellationToken);
                }
            }
        }

        if (rebuildRequest is not null)
        {
            await ProcessRebuildRequestAsync(rebuildRequest, allowDowngrade: false, cancellationToken);
        }
    }

    /// <summary>Processes one pending administrator rebuild request. Returns false when none was pending.</summary>
    private async Task<bool> ProcessExplicitRebuildRequestAsync(bool allowDowngrade, CancellationToken cancellationToken)
    {
        SearchIndexWorkItem? item;
        using (var scope = _scopeFactory.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ISearchIndexStore>();
            item = await store.DequeueAsync(cancellationToken, SearchIndexStore.FullRebuildEntityType);
        }

        if (item is null) return false;
        await ProcessRebuildRequestAsync(item, allowDowngrade, cancellationToken);
        return true;
    }

    /// <summary>
    /// A process that dies mid-rebuild leaves its request leased. Ordinary lease recovery
    /// skips rebuild requests, so at most once per lease timeout (in every mode, including
    /// passive) briefly take the exclusive lock — proving nobody is still rebuilding — and
    /// return abandoned rebuild requests to the queue.
    /// </summary>
    private async Task RecoverAbandonedRebuildRequestsIfDueAsync(CancellationToken cancellationToken)
    {
        if (_time.GetUtcNow() < _nextRebuildRequestRecoveryUtc) return;
        _nextRebuildRequestRecoveryUtc = _time.GetUtcNow() + LeaseTimeout;

        using var lockScope = _scopeFactory.CreateScope();
        var coordinator = lockScope.ServiceProvider.GetRequiredService<ISearchIndexCoordinator>();
        await using var lease = await coordinator.TryAcquireFullRebuildAsync(cancellationToken);
        if (lease is null) return;

        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISearchIndexStore>();
        var recovered = await store.RecoverStaleWorkItemsAsync(LeaseTimeout, cancellationToken, includeFullRebuildRequests: true);
        if (recovered > 0)
        {
            _logger.LogWarning("Search V2 recovered {Count} abandoned indexing work item(s), including rebuild requests.", recovered);
        }
    }

    private async Task ProcessRebuildRequestAsync(SearchIndexWorkItem item, bool allowDowngrade, CancellationToken cancellationToken)
    {
        // An explicit request bypasses the automatic-rebuild backoff.
        var outcome = await RunFullRebuildUnderLockAsync(
            _ => true,
            honourBackoff: false,
            allowDowngrade,
            cancellationToken);

        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISearchIndexStore>();
        switch (outcome)
        {
            case RebuildOutcome.Succeeded:
            case RebuildOutcome.Superseded:
                // A newer process's generation satisfies the request.
                await store.CompleteAsync(item.Id, cancellationToken);
                break;
            case RebuildOutcome.Failed:
                await store.FailAsync(item.Id, "Full Search V2 rebuild failed. Review application logs for the underlying exception.", cancellationToken);
                break;
            default:
                // Another process is rebuilding: hand the request back without counting
                // an attempt.
                await store.ReleaseAsync(item.Id, BusyRebuildRequestRetry, cancellationToken);
                break;
        }
    }

    private async Task ReconcileIfDueAsync(SearchIndexActiveState state, CancellationToken cancellationToken)
    {
        if (!IsReconciliationDue(state)) return;
        await RunAutomaticFullRebuildAsync(IsReconciliationDue, cancellationToken);
    }

    private bool IsReconciliationDue(SearchIndexActiveState state)
    {
        if (!state.HasActiveGeneration) return true;
        var last = state.LastFullRebuildUtc ?? DateTimeOffset.MinValue;
        return _time.GetUtcNow() - last >= TimeSpan.FromMinutes(Math.Max(1, _options.FullReconciliationMinutes));
    }

    private Task RunAutomaticFullRebuildAsync(Func<SearchIndexActiveState, bool> stillNeeded, CancellationToken cancellationToken) =>
        RunFullRebuildUnderLockAsync(stillNeeded, honourBackoff: true, allowDowngrade: false, cancellationToken);

    private async Task<RebuildOutcome> RunFullRebuildUnderLockAsync(
        Func<SearchIndexActiveState, bool> stillNeeded,
        bool honourBackoff,
        bool allowDowngrade,
        CancellationToken cancellationToken)
    {
        if (honourBackoff && _time.GetUtcNow() < _nextAutomaticRebuildUtc)
        {
            return RebuildOutcome.Skipped;
        }

        using var lockScope = _scopeFactory.CreateScope();
        var coordinator = lockScope.ServiceProvider.GetRequiredService<ISearchIndexCoordinator>();
        await using var lease = await coordinator.TryAcquireFullRebuildAsync(cancellationToken);
        if (lease is null)
        {
            _logger.LogDebug("Search V2 full rebuild skipped: another process holds the rebuild lock.");
            return RebuildOutcome.Busy;
        }

        // Holding the exclusive lock proves no process is still building, so any
        // abandoned rebuild request lease can safely return to the queue.
        using (var recoveryScope = _scopeFactory.CreateScope())
        {
            var recoveryStore = recoveryScope.ServiceProvider.GetRequiredService<ISearchIndexStore>();
            await recoveryStore.RecoverStaleWorkItemsAsync(LeaseTimeout, cancellationToken, includeFullRebuildRequests: true);
        }

        // Re-read under the lock: another process may have activated a generation
        // (possibly a newer projection version) while this one was waiting.
        var current = await ReadStateAsync(cancellationToken);
        if (!allowDowngrade && IsNewerThanOwn(current))
        {
            EnterPassive(current);
            return RebuildOutcome.Superseded;
        }

        if (!stillNeeded(current))
        {
            return RebuildOutcome.Skipped;
        }

        using var scope = _scopeFactory.CreateScope();
        var outcome = await FullRebuildAsync(scope.ServiceProvider, allowDowngrade, cancellationToken);
        if (outcome != RebuildOutcome.Failed)
        {
            _consecutiveRebuildFailures = 0;
            _nextAutomaticRebuildUtc = DateTimeOffset.MinValue;
            return outcome;
        }

        _consecutiveRebuildFailures++;
        var baseMinutes = Math.Max(1, _options.FullRebuildRetryMinutes);
        var waitMinutes = Math.Min(baseMinutes * Math.Pow(2, _consecutiveRebuildFailures - 1), baseMinutes * 4.0);
        _nextAutomaticRebuildUtc = _time.GetUtcNow().AddMinutes(waitMinutes);
        _logger.LogWarning(
            "Search V2 will not retry an automatic full rebuild before {NextAttemptUtc:O} ({Failures} consecutive failure(s)).",
            _nextAutomaticRebuildUtc,
            _consecutiveRebuildFailures);
        return RebuildOutcome.Failed;
    }

    private async Task<RebuildOutcome> FullRebuildAsync(IServiceProvider services, bool allowDowngrade, CancellationToken cancellationToken)
    {
        var started = _time.GetUtcNow();
        try
        {
            var builder = services.GetRequiredService<ISearchProjectionBuilder>();
            var store = services.GetRequiredService<ISearchIndexStore>();
            var projections = await builder.BuildAllAsync(cancellationToken);
            await store.ReplaceFullGenerationAsync(projections, _options.ProjectionVersion, cancellationToken, allowDowngrade);
            _logger.LogInformation(
                "Search V2 full index rebuild activated {EntryCount} entries for projection version {ProjectionVersion} in {ElapsedMs} ms.",
                projections.Count,
                _options.ProjectionVersion,
                (_time.GetUtcNow() - started).TotalMilliseconds);
            return RebuildOutcome.Succeeded;
        }
        catch (SearchIndexSupersededException)
        {
            // A newer process activated first; the next tick goes passive. Not a failure.
            _logger.LogInformation(
                "Search V2 full rebuild for projection version {ProjectionVersion} was not activated: a newer generation is already active.",
                _options.ProjectionVersion);
            return RebuildOutcome.Superseded;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Search V2 full index rebuild failed. The previous active generation remains available.");
            try
            {
                var store = services.GetRequiredService<ISearchIndexStore>();
                await store.RecordIndexErrorAsync(ex.Message, cancellationToken);
            }
            catch (Exception healthEx) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug(healthEx, "Search V2 could not persist full-rebuild health diagnostics.");
            }
            return RebuildOutcome.Failed;
        }
    }

    private enum RebuildOutcome
    {
        Succeeded,
        Failed,
        Skipped,
        Busy,
        Superseded
    }
}
