using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Features.MediaLibrary.Hosted;

/// <summary>
/// Durable incremental known-person matcher. New or invalidated face embeddings are
/// discovered from MediaFace candidate-search state and processed outside HTTP requests.
/// The worker is self-healing: stale Processing leases are recovered, bounded searches
/// have an execution timeout, and queue mutations wake the worker immediately.
/// </summary>
public sealed class FaceCandidateRefreshWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFaceCandidateRefreshRuntimeState _runtime;
    private readonly MediaPeopleOptions _options;
    private readonly ILogger<FaceCandidateRefreshWorker> _logger;

    public FaceCandidateRefreshWorker(
        IServiceScopeFactory scopeFactory,
        IFaceCandidateRefreshRuntimeState runtime,
        IOptions<MediaLibraryOptions> options,
        ILogger<FaceCandidateRefreshWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _options = options?.Value.People ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
        _runtime.MarkStarted(workerId);

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        _logger.LogInformation(
            "Known-person candidate worker started. WorkerId={WorkerId}, BatchSize={BatchSize}, IdleDelaySeconds={IdleDelaySeconds}, SearchTimeoutSeconds={SearchTimeoutSeconds}, ProcessingStaleSeconds={ProcessingStaleSeconds}",
            workerId,
            _options.CandidateRefreshBatchSize,
            _options.CandidateRefreshIdleDelaySeconds,
            _options.CandidateSearchTimeoutSeconds,
            _options.CandidateProcessingStaleSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var refreshed = 0;
            var cycleFailed = false;
            try
            {
                _runtime.Heartbeat("Checking queue");
                using var scope = _scopeFactory.CreateScope();
                var queue = scope.ServiceProvider.GetRequiredService<IFaceCandidateRefreshQueueService>();
                var suggestions = scope.ServiceProvider.GetRequiredService<IFaceCandidateSuggestionService>();

                var recovered = await queue.RecoverStaleProcessingAsync(stoppingToken);
                if (recovered > 0)
                {
                    _logger.LogWarning(
                        "Recovered {RecoveredCount} stale known-person matching face(s) back to Pending.",
                        recovered);
                }

                _runtime.MarkBatchStarted();
                using var cycleTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var cycleTimeoutSeconds = Math.Clamp(_options.CandidateSearchTimeoutSeconds + 30, 35, 630);
                cycleTimeout.CancelAfter(TimeSpan.FromSeconds(cycleTimeoutSeconds));
                try
                {
                    refreshed = await suggestions.RefreshUnassignedAsync(
                        Math.Clamp(_options.CandidateRefreshBatchSize, 1, 10_000),
                        cycleTimeout.Token);
                }
                catch (OperationCanceledException exception)
                    when (!stoppingToken.IsCancellationRequested && cycleTimeout.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"Known-person candidate refresh cycle exceeded {cycleTimeoutSeconds} seconds.",
                        exception);
                }
                _runtime.MarkBatchCompleted(refreshed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                cycleFailed = true;
                _runtime.MarkFailed(exception);
                _logger.LogWarning(
                    exception,
                    "Known-person candidate refresh failed. Durable face state will be retried without affecting confirmed identities.");
            }

            try
            {
                if (refreshed > 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
                }
                else
                {
                    if (!cycleFailed)
                    {
                        _runtime.MarkIdle();
                    }
                    await _runtime.WaitForRunRequestAsync(
                        TimeSpan.FromSeconds(Math.Clamp(_options.CandidateRefreshIdleDelaySeconds, 1, 3600)),
                        stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
