namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Thread-safe process-local snapshot of expensive unnamed-face grouping. Invalidating a
/// snapshot never discards the last successful result: reviewers may continue using it while
/// the background worker prepares a fresh version. A monotonically increasing generation
/// prevents an invalidation that occurs during a refresh from being accidentally cleared by
/// the older in-flight computation. A lightweight signal wakes the worker promptly instead
/// of waiting for the normal periodic refresh interval.
/// </summary>
public sealed class FaceIdentityGroupingRuntimeState : IFaceIdentityGroupingRuntimeState
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _refreshSignal = new(0, 1);
    private FaceIdentityGroupingRuntimeSnapshot _snapshot = new(null, null, null);

    public FaceIdentityGroupingRuntimeSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return _snapshot;
        }
    }

    public void SetResult(
        FaceIdentityGroupingResult result,
        DateTimeOffset refreshedAtUtc,
        long? refreshGeneration = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        lock (_gate)
        {
            var generation = refreshGeneration ?? _snapshot.RefreshGeneration;
            var superseded = generation != _snapshot.RefreshGeneration;
            _snapshot = new FaceIdentityGroupingRuntimeSnapshot(
                result,
                refreshedAtUtc,
                null,
                IsRefreshPending: superseded,
                InvalidatedAtUtc: superseded ? _snapshot.InvalidatedAtUtc : null,
                RefreshGeneration: _snapshot.RefreshGeneration);

            // If this computation includes the current generation, any already-coalesced wake
            // signal is also satisfied by this result. A newer invalidation cannot race this
            // drain because Invalidate uses the same gate before releasing the signal.
            if (!superseded)
            {
                _refreshSignal.Wait(0);
            }
        }
    }

    public void SetFailure(string failureReason, DateTimeOffset failedAtUtc)
    {
        var normalized = string.IsNullOrWhiteSpace(failureReason)
            ? "Identity grouping failed."
            : failureReason.Trim();
        lock (_gate)
        {
            _snapshot = new FaceIdentityGroupingRuntimeSnapshot(
                _snapshot.Result,
                _snapshot.RefreshedAtUtc ?? failedAtUtc,
                normalized,
                IsRefreshPending: true,
                _snapshot.InvalidatedAtUtc ?? failedAtUtc,
                _snapshot.RefreshGeneration);
        }
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            _snapshot = new FaceIdentityGroupingRuntimeSnapshot(
                _snapshot.Result,
                _snapshot.RefreshedAtUtc,
                null,
                IsRefreshPending: true,
                InvalidatedAtUtc: now,
                RefreshGeneration: checked(_snapshot.RefreshGeneration + 1));
        }

        // Coalesce repeated mutations while a refresh is already pending.
        if (_refreshSignal.CurrentCount == 0)
        {
            _refreshSignal.Release();
        }
    }

    public async Task WaitForRefreshRequestAsync(
        TimeSpan maximumDelay,
        CancellationToken cancellationToken)
    {
        var boundedDelay = maximumDelay <= TimeSpan.Zero
            ? TimeSpan.FromSeconds(1)
            : maximumDelay;
        await _refreshSignal.WaitAsync(boundedDelay, cancellationToken);
    }
}
