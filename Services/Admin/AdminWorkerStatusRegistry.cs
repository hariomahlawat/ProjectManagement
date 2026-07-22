using System.Collections.Concurrent;

namespace ProjectManagement.Services.Admin;

public enum AdminWorkerState
{
    Registered = 0,
    Running = 1,
    Healthy = 2,
    Failed = 3
}

public sealed record AdminWorkerStatus(
    string Key,
    string Label,
    AdminWorkerState State,
    DateTimeOffset RegisteredUtc,
    DateTimeOffset? LastStartedUtc,
    DateTimeOffset? LastSucceededUtc,
    DateTimeOffset? LastFailedUtc,
    TimeSpan? ExpectedInterval,
    string? Detail);

public interface IAdminWorkerStatusRegistry
{
    void Register(string key, string label, TimeSpan? expectedInterval = null);
    void MarkStarted(string key);
    void MarkSucceeded(string key, string? detail = null);
    void MarkFailed(string key, Exception exception);
    IReadOnlyList<AdminWorkerStatus> GetSnapshot();
}

/// <summary>
/// Maintains a process-local operational snapshot for background services. The
/// registry intentionally stores only safe summaries; full exception details
/// remain in structured application logs.
/// </summary>
public sealed class AdminWorkerStatusRegistry : IAdminWorkerStatusRegistry
{
    private readonly ConcurrentDictionary<string, WorkerState> _workers =
        new(StringComparer.OrdinalIgnoreCase);

    public void Register(string key, string label, TimeSpan? expectedInterval = null)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Worker key is required.", nameof(key));
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Worker label is required.", nameof(label));

        var normalizedKey = key.Trim();
        TimeSpan? normalizedInterval = expectedInterval is { } interval && interval > TimeSpan.Zero
            ? interval
            : null;

        _workers.AddOrUpdate(
            normalizedKey,
            _ => new WorkerState(normalizedKey, label.Trim(), DateTimeOffset.UtcNow, normalizedInterval),
            (_, existing) =>
            {
                lock (existing.Sync)
                {
                    existing.Label = label.Trim();
                    existing.ExpectedInterval = normalizedInterval;
                    return existing;
                }
            });
    }

    public void MarkStarted(string key)
    {
        if (!_workers.TryGetValue(key, out var state)) return;
        lock (state.Sync)
        {
            state.State = AdminWorkerState.Running;
            state.LastStartedUtc = DateTimeOffset.UtcNow;
            state.Detail = null;
        }
    }

    public void MarkSucceeded(string key, string? detail = null)
    {
        if (!_workers.TryGetValue(key, out var state)) return;
        lock (state.Sync)
        {
            state.State = AdminWorkerState.Healthy;
            state.LastSucceededUtc = DateTimeOffset.UtcNow;
            state.Detail = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim();
        }
    }

    public void MarkFailed(string key, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (!_workers.TryGetValue(key, out var state)) return;
        lock (state.Sync)
        {
            state.State = AdminWorkerState.Failed;
            state.LastFailedUtc = DateTimeOffset.UtcNow;
            // Never expose the exception message through the administrative UI.
            state.Detail = exception.GetType().Name;
        }
    }

    public IReadOnlyList<AdminWorkerStatus> GetSnapshot() => _workers.Values
        .Select(state =>
        {
            lock (state.Sync)
            {
                return new AdminWorkerStatus(
                    state.Key,
                    state.Label,
                    state.State,
                    state.RegisteredUtc,
                    state.LastStartedUtc,
                    state.LastSucceededUtc,
                    state.LastFailedUtc,
                    state.ExpectedInterval,
                    state.Detail);
            }
        })
        .OrderBy(status => status.Label, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private sealed class WorkerState
    {
        public WorkerState(
            string key,
            string label,
            DateTimeOffset registeredUtc,
            TimeSpan? expectedInterval)
        {
            Key = key;
            Label = label;
            RegisteredUtc = registeredUtc;
            ExpectedInterval = expectedInterval;
        }

        public object Sync { get; } = new();
        public string Key { get; }
        public string Label { get; set; }
        public DateTimeOffset RegisteredUtc { get; }
        public AdminWorkerState State { get; set; } = AdminWorkerState.Registered;
        public DateTimeOffset? LastStartedUtc { get; set; }
        public DateTimeOffset? LastSucceededUtc { get; set; }
        public DateTimeOffset? LastFailedUtc { get; set; }
        public TimeSpan? ExpectedInterval { get; set; }
        public string? Detail { get; set; }
    }
}
