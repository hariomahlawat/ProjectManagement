using System.Collections.Concurrent;

namespace ProjectManagement.Services.Admin.Ingestion;

public sealed record PdfIngestionRunRecord(
    Guid RunId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string? InitiatedBy,
    string Status,
    int Discovered,
    int IngestedOrLinked,
    int AlreadyLinked,
    int Missing,
    int Failed,
    IReadOnlyList<PdfIngestionSourceSummary> Sources,
    IReadOnlyList<PdfIngestionFailure> Failures,
    string? TraceId)
{
    public TimeSpan Duration => CompletedAtUtc - StartedAtUtc;
}

public interface IPdfIngestionRunHistory
{
    void Record(PdfIngestionRunRecord run);
    PdfIngestionRunRecord? Get(Guid runId);
    PdfIngestionRunRecord? GetLatest();
    IReadOnlyList<PdfIngestionRunRecord> GetRecent(int count = 10);
}

public sealed class PdfIngestionRunHistory : IPdfIngestionRunHistory
{
    private const int Capacity = 20;
    private readonly ConcurrentDictionary<Guid, PdfIngestionRunRecord> _runs = new();
    private readonly ConcurrentQueue<Guid> _order = new();

    public void Record(PdfIngestionRunRecord run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (!_runs.TryAdd(run.RunId, run))
        {
            _runs[run.RunId] = run;
            return;
        }

        _order.Enqueue(run.RunId);
        while (_order.Count > Capacity && _order.TryDequeue(out var expired))
        {
            _runs.TryRemove(expired, out _);
        }
    }

    public PdfIngestionRunRecord? Get(Guid runId) =>
        _runs.TryGetValue(runId, out var run) ? run : null;

    public PdfIngestionRunRecord? GetLatest() => GetRecent(1).FirstOrDefault();

    public IReadOnlyList<PdfIngestionRunRecord> GetRecent(int count = 10)
    {
        var take = Math.Clamp(count, 1, Capacity);
        return _runs.Values
            .OrderByDescending(run => run.CompletedAtUtc)
            .Take(take)
            .ToArray();
    }
}
