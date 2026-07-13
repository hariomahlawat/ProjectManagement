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
    string? Detail);

public interface IAdminWorkerStatusRegistry
{
    void Register(string key, string label);
    void MarkStarted(string key);
    void MarkSucceeded(string key, string? detail = null);
    void MarkFailed(string key, Exception exception);
    IReadOnlyList<AdminWorkerStatus> GetSnapshot();
}

public sealed class AdminWorkerStatusRegistry : IAdminWorkerStatusRegistry
{
    private readonly ConcurrentDictionary<string, WorkerState> _workers =
        new(StringComparer.OrdinalIgnoreCase);

    public void Register(string key, string label)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Worker key is required.", nameof(key));
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Worker label is required.", nameof(label));

        _workers.TryAdd(key.Trim(), new WorkerState(key.Trim(), label.Trim(), DateTimeOffset.UtcNow));
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
                    state.Detail);
            }
        })
        .OrderBy(status => status.Label, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private sealed class WorkerState
    {
        public WorkerState(string key, string label, DateTimeOffset registeredUtc)
        {
            Key = key;
            Label = label;
            RegisteredUtc = registeredUtc;
        }

        public object Sync { get; } = new();
        public string Key { get; }
        public string Label { get; }
        public DateTimeOffset RegisteredUtc { get; }
        public AdminWorkerState State { get; set; } = AdminWorkerState.Registered;
        public DateTimeOffset? LastStartedUtc { get; set; }
        public DateTimeOffset? LastSucceededUtc { get; set; }
        public DateTimeOffset? LastFailedUtc { get; set; }
        public string? Detail { get; set; }
    }
}
