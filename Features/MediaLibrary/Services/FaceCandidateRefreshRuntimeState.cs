namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Process-local health and wake-up state for the durable known-person matching worker.
/// Durable work ownership remains on MediaFace candidate-search state; this object only
/// exposes worker liveness/telemetry and provides an immediate wake signal after queuing.
/// </summary>
public sealed class FaceCandidateRefreshRuntimeState : IFaceCandidateRefreshRuntimeState
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _wakeSignal = new(0, 1);

    private bool _workerConfigured;
    private bool _workerStarted;
    private string _state = "Not started";
    private string _workerId = string.Empty;
    private DateTimeOffset? _startedAtUtc;
    private DateTimeOffset? _lastHeartbeatUtc;
    private DateTimeOffset? _currentBatchStartedAtUtc;
    private DateTimeOffset? _lastCompletedAtUtc;
    private DateTimeOffset? _lastFailedAtUtc;
    private int _lastBatchProcessedCount;
    private int _processedSinceStart;
    private int _failureCountSinceStart;
    private int _recoveredStaleSinceStart;
    private string? _lastFailureCode;
    private string? _lastFailureMessage;

    public void MarkConfigured(bool configured)
    {
        lock (_sync)
        {
            _workerConfigured = configured;
            if (!configured)
            {
                _state = "Disabled by configuration";
            }
        }
    }

    public void MarkStarted(string workerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            _workerStarted = true;
            _workerId = workerId;
            _startedAtUtc = now;
            _lastHeartbeatUtc = now;
            _state = "Starting";
        }
    }

    public void Heartbeat(string state)
    {
        lock (_sync)
        {
            _lastHeartbeatUtc = DateTimeOffset.UtcNow;
            _state = string.IsNullOrWhiteSpace(state) ? "Running" : state.Trim();
        }
    }

    public void MarkBatchStarted()
    {
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            _lastHeartbeatUtc = now;
            _currentBatchStartedAtUtc = now;
            _state = "Processing";
        }
    }

    public void MarkBatchCompleted(int processedCount)
    {
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            _lastHeartbeatUtc = now;
            if (processedCount > 0)
            {
                _lastCompletedAtUtc = now;
            }
            _currentBatchStartedAtUtc = null;
            _lastBatchProcessedCount = Math.Max(0, processedCount);
            _processedSinceStart += Math.Max(0, processedCount);
            _state = processedCount > 0 ? "Running" : "Idle";
        }
    }

    public void MarkFailed(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            _lastHeartbeatUtc = now;
            _lastFailedAtUtc = now;
            _currentBatchStartedAtUtc = null;
            _failureCountSinceStart++;
            _lastFailureCode = exception.GetType().Name;
            _lastFailureMessage = Trim(exception.GetBaseException().Message, 512);
            _state = "Running with failures";
        }
    }

    public void MarkRecovered(int recoveredCount)
    {
        if (recoveredCount <= 0)
        {
            return;
        }

        lock (_sync)
        {
            _lastHeartbeatUtc = DateTimeOffset.UtcNow;
            _recoveredStaleSinceStart += recoveredCount;
            _state = "Recovered stale work";
        }
    }

    public void MarkIdle()
    {
        lock (_sync)
        {
            _lastHeartbeatUtc = DateTimeOffset.UtcNow;
            _currentBatchStartedAtUtc = null;
            _state = "Idle";
        }
    }

    public void RequestRun()
    {
        try
        {
            if (_wakeSignal.CurrentCount == 0)
            {
                _wakeSignal.Release();
            }
        }
        catch (SemaphoreFullException)
        {
            // Multiple concurrent queue mutations collapse into one worker wake-up.
        }
    }

    public async Task WaitForRunRequestAsync(
        TimeSpan maximumDelay,
        CancellationToken cancellationToken)
    {
        var delay = maximumDelay <= TimeSpan.Zero
            ? TimeSpan.FromSeconds(1)
            : maximumDelay;

        _ = await _wakeSignal.WaitAsync(delay, cancellationToken);
    }

    public FaceCandidateRefreshRuntimeSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new FaceCandidateRefreshRuntimeSnapshot(
                _workerConfigured,
                _workerStarted,
                _state,
                _workerId,
                _startedAtUtc,
                _lastHeartbeatUtc,
                _currentBatchStartedAtUtc,
                _lastCompletedAtUtc,
                _lastFailedAtUtc,
                _lastBatchProcessedCount,
                _processedSinceStart,
                _failureCountSinceStart,
                _recoveredStaleSinceStart,
                _lastFailureCode,
                _lastFailureMessage);
        }
    }

    private static string Trim(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
