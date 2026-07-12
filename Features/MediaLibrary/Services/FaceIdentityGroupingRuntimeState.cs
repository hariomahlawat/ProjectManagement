namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Thread-safe process-local snapshot of expensive unnamed-face grouping. HTTP requests
/// read this bounded snapshot and never trigger full-library clustering.
/// </summary>
public sealed class FaceIdentityGroupingRuntimeState : IFaceIdentityGroupingRuntimeState
{
    private readonly object _gate = new();
    private FaceIdentityGroupingRuntimeSnapshot _snapshot = new(null, null, null);

    public FaceIdentityGroupingRuntimeSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return _snapshot;
        }
    }

    public void SetResult(FaceIdentityGroupingResult result, DateTimeOffset refreshedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(result);
        lock (_gate)
        {
            _snapshot = new FaceIdentityGroupingRuntimeSnapshot(result, refreshedAtUtc, null);
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
                normalized);
        }
    }
    public void Invalidate()
    {
        lock (_gate)
        {
            _snapshot = new FaceIdentityGroupingRuntimeSnapshot(null, null, null);
        }
    }

}
