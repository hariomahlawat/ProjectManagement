namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed record MediaProcessingRuntimeSnapshot(
    bool WorkerConfigured,
    bool WorkerStarted,
    string State,
    string WorkerId,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? LastHeartbeatUtc,
    DateTimeOffset? LastClaimedAtUtc,
    DateTimeOffset? LastCompletedAtUtc,
    DateTimeOffset? LastFailedAtUtc,
    long? CurrentJobId,
    long? CurrentAssetId,
    int CompletedSinceStart,
    int FailedSinceStart,
    string? LastFailureCode,
    string? LastFailureMessage);

public interface IMediaProcessingRuntimeState
{
    void MarkConfigured(bool configured);
    void MarkStarted(string workerId);
    void Heartbeat(string state);
    void MarkClaimed(long jobId, long assetId);
    void MarkCompleted(long jobId);
    void MarkUnavailable(long jobId);
    void MarkFailed(long jobId, Exception exception);
    void MarkIdle();
    MediaProcessingRuntimeSnapshot GetSnapshot();
}

public sealed class MediaProcessingRuntimeState : IMediaProcessingRuntimeState
{
    private readonly object _sync = new();
    private bool _workerConfigured;
    private bool _workerStarted;
    private string _state = "Not started";
    private string _workerId = string.Empty;
    private DateTimeOffset? _startedAtUtc;
    private DateTimeOffset? _lastHeartbeatUtc;
    private DateTimeOffset? _lastClaimedAtUtc;
    private DateTimeOffset? _lastCompletedAtUtc;
    private DateTimeOffset? _lastFailedAtUtc;
    private long? _currentJobId;
    private long? _currentAssetId;
    private int _completedSinceStart;
    private int _failedSinceStart;
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
            _state = state;
        }
    }

    public void MarkClaimed(long jobId, long assetId)
    {
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            _lastHeartbeatUtc = now;
            _lastClaimedAtUtc = now;
            _currentJobId = jobId;
            _currentAssetId = assetId;
            _state = "Processing";
        }
    }

    public void MarkCompleted(long jobId)
    {
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            _lastHeartbeatUtc = now;
            _lastCompletedAtUtc = now;
            _completedSinceStart++;
            if (_currentJobId == jobId)
            {
                _currentJobId = null;
                _currentAssetId = null;
            }
            _state = "Running";
        }
    }

    public void MarkUnavailable(long jobId)
    {
        lock (_sync)
        {
            _lastHeartbeatUtc = DateTimeOffset.UtcNow;
            if (_currentJobId == jobId)
            {
                _currentJobId = null;
                _currentAssetId = null;
            }
            _state = "Running";
        }
    }

    public void MarkFailed(long jobId, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            _lastHeartbeatUtc = now;
            _lastFailedAtUtc = now;
            _failedSinceStart++;
            _lastFailureCode = exception.GetType().Name;
            _lastFailureMessage = Trim(exception.GetBaseException().Message, 512);
            if (_currentJobId == jobId)
            {
                _currentJobId = null;
                _currentAssetId = null;
            }
            _state = "Running with failures";
        }
    }

    public void MarkIdle()
    {
        lock (_sync)
        {
            _lastHeartbeatUtc = DateTimeOffset.UtcNow;
            _currentJobId = null;
            _currentAssetId = null;
            _state = "Idle";
        }
    }

    public MediaProcessingRuntimeSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new MediaProcessingRuntimeSnapshot(
                _workerConfigured,
                _workerStarted,
                _state,
                _workerId,
                _startedAtUtc,
                _lastHeartbeatUtc,
                _lastClaimedAtUtc,
                _lastCompletedAtUtc,
                _lastFailedAtUtc,
                _currentJobId,
                _currentAssetId,
                _completedSinceStart,
                _failedSinceStart,
                _lastFailureCode,
                _lastFailureMessage);
        }
    }

    private static string Trim(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
