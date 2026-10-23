namespace ProjectManagement.Services.SearchV2.Analytics;

public sealed record SearchLatencySnapshot(long Count, double? P50Ms, double? P95Ms)
{
    public static SearchLatencySnapshot Empty { get; } = new(0, null, null);
}

/// <summary>
/// In-process record of Search V2 engine time for served searches. The query log keeps
/// one row per search (gateway latency, engine "V2"); engine-only latency lives here so
/// the health dashboard can still show it without a second log row per search. Samples
/// are per process and reset on restart.
/// </summary>
public interface ISearchEngineLatencyRecorder
{
    void Record(long milliseconds);
    SearchLatencySnapshot Snapshot(TimeSpan window);
}

public sealed class SearchEngineLatencyRecorder : ISearchEngineLatencyRecorder
{
    private const int Capacity = 4096;

    private readonly TimeProvider _time;
    private readonly (DateTimeOffset At, long Ms)[] _samples = new (DateTimeOffset, long)[Capacity];
    private readonly object _gate = new();
    private int _next;
    private int _count;

    public SearchEngineLatencyRecorder()
        : this(TimeProvider.System)
    {
    }

    internal SearchEngineLatencyRecorder(TimeProvider time)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public void Record(long milliseconds)
    {
        var sample = (_time.GetUtcNow(), Math.Max(0, milliseconds));
        lock (_gate)
        {
            _samples[_next] = sample;
            _next = (_next + 1) % Capacity;
            _count = Math.Min(_count + 1, Capacity);
        }
    }

    public SearchLatencySnapshot Snapshot(TimeSpan window)
    {
        var since = _time.GetUtcNow() - window;
        long[] values;
        lock (_gate)
        {
            values = _samples
                .Take(_count)
                .Where(sample => sample.At >= since)
                .Select(sample => sample.Ms)
                .ToArray();
        }

        if (values.Length == 0) return SearchLatencySnapshot.Empty;
        Array.Sort(values);
        return new SearchLatencySnapshot(values.Length, Percentile(values, 0.50), Percentile(values, 0.95));
    }

    // Linear interpolation, matching PostgreSQL percentile_cont used for the query log.
    private static double Percentile(long[] sorted, double fraction)
    {
        var position = fraction * (sorted.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }
}
