using ProjectManagement.Features.MediaLibrary.Outbox;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Compatibility facade used by source-domain services after a successful commit. Durable
/// ingestion is created transactionally by the ApplicationDbContext interceptor; this facade
/// only wakes the background consumer so the new event is processed without waiting for polling.
/// </summary>
public sealed class PrismMediaIngestionCoordinator : IPrismMediaIngestionCoordinator
{
    private readonly IPrismMediaOutboxSignal _signal;

    public PrismMediaIngestionCoordinator(IPrismMediaOutboxSignal signal)
    {
        _signal = signal ?? throw new ArgumentNullException(nameof(signal));
    }

    public Task<PrismMediaIngestionResult> ReconcileAfterSourceChangeAsync(
        string reason,
        CancellationToken cancellationToken)
    {
        // The source transaction has already committed. Never turn a client disconnect
        // into a false failure response; durable outbox polling remains the safety net.
        _signal.Pulse();
        return Task.FromResult(new PrismMediaIngestionResult(true, "Queued"));
    }
}

/// <summary>
/// Fast in-process gate retained to avoid redundant work inside one host. Full reconciliation
/// and targeted ingestion additionally use a PostgreSQL advisory transaction lock, which is the
/// authoritative multi-instance concurrency control.
/// </summary>
public sealed class PrismMediaSynchronizationGate : IPrismMediaSynchronizationGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async ValueTask<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        return new Releaser(_gate);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _gate;

        public Releaser(SemaphoreSlim gate) => _gate = gate;

        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
