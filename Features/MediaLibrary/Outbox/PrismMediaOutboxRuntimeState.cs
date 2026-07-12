namespace ProjectManagement.Features.MediaLibrary.Outbox;

public sealed record PrismMediaOutboxRuntimeSnapshot(
    bool IsRunning,
    DateTimeOffset? LastHeartbeatUtc,
    DateTimeOffset? LastClaimedAtUtc,
    DateTimeOffset? LastCompletedAtUtc,
    DateTimeOffset? LastFailedAtUtc,
    string BackfillStatus,
    DateTimeOffset? BackfillLastAttemptedAtUtc,
    DateTimeOffset? BackfillCompletedAtUtc,
    string? LastError);

public interface IPrismMediaOutboxRuntimeState
{
    PrismMediaOutboxRuntimeSnapshot GetSnapshot();
    void MarkStarted();
    void MarkStopped();
    void Heartbeat();
    void MarkClaimed();
    void MarkCompleted();
    void MarkFailed(string error);
    void MarkBackfillAttempt(string status);
    void MarkBackfillCompleted();
}

/// <summary>
/// Process-local operational telemetry for the durable outbox consumer. Correctness is stored
/// in the outbox rows; this state exists only to make worker liveness and startup-backfill
/// progress visible to administrators.
/// </summary>
public sealed class PrismMediaOutboxRuntimeState : IPrismMediaOutboxRuntimeState
{
    private readonly object _gate = new();
    private bool _isRunning;
    private DateTimeOffset? _lastHeartbeatUtc;
    private DateTimeOffset? _lastClaimedAtUtc;
    private DateTimeOffset? _lastCompletedAtUtc;
    private DateTimeOffset? _lastFailedAtUtc;
    private string _backfillStatus = "Pending";
    private DateTimeOffset? _backfillLastAttemptedAtUtc;
    private DateTimeOffset? _backfillCompletedAtUtc;
    private string? _lastError;

    public PrismMediaOutboxRuntimeSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return new PrismMediaOutboxRuntimeSnapshot(
                _isRunning,
                _lastHeartbeatUtc,
                _lastClaimedAtUtc,
                _lastCompletedAtUtc,
                _lastFailedAtUtc,
                _backfillStatus,
                _backfillLastAttemptedAtUtc,
                _backfillCompletedAtUtc,
                _lastError);
        }
    }

    public void MarkStarted()
    {
        lock (_gate)
        {
            _isRunning = true;
            _lastHeartbeatUtc = DateTimeOffset.UtcNow;
        }
    }

    public void MarkStopped()
    {
        lock (_gate)
        {
            _isRunning = false;
            _lastHeartbeatUtc = DateTimeOffset.UtcNow;
        }
    }

    public void Heartbeat()
    {
        lock (_gate)
        {
            _lastHeartbeatUtc = DateTimeOffset.UtcNow;
        }
    }

    public void MarkClaimed()
    {
        lock (_gate)
        {
            _lastClaimedAtUtc = DateTimeOffset.UtcNow;
            _lastHeartbeatUtc = _lastClaimedAtUtc;
        }
    }

    public void MarkCompleted()
    {
        lock (_gate)
        {
            _lastCompletedAtUtc = DateTimeOffset.UtcNow;
            _lastHeartbeatUtc = _lastCompletedAtUtc;
            _lastError = null;
        }
    }

    public void MarkFailed(string error)
    {
        lock (_gate)
        {
            _lastFailedAtUtc = DateTimeOffset.UtcNow;
            _lastHeartbeatUtc = _lastFailedAtUtc;
            _lastError = string.IsNullOrWhiteSpace(error) ? "Unknown outbox worker failure." : error;
        }
    }

    public void MarkBackfillAttempt(string status)
    {
        lock (_gate)
        {
            _backfillStatus = string.IsNullOrWhiteSpace(status) ? "Running" : status;
            _backfillLastAttemptedAtUtc = DateTimeOffset.UtcNow;
            _lastHeartbeatUtc = _backfillLastAttemptedAtUtc;
        }
    }

    public void MarkBackfillCompleted()
    {
        lock (_gate)
        {
            _backfillStatus = "Completed";
            _backfillCompletedAtUtc = DateTimeOffset.UtcNow;
            _lastHeartbeatUtc = _backfillCompletedAtUtc;
            _lastError = null;
        }
    }
}
