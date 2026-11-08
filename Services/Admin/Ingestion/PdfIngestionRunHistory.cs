using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Models;

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
    /// <summary>
    /// Saves a run's summary. Saving the same run again replaces it, so a retry never adds a
    /// second row.
    /// </summary>
    Task RecordAsync(PdfIngestionRunRecord run, CancellationToken cancellationToken = default);

    Task<PdfIngestionRunRecord?> GetAsync(Guid runId, CancellationToken cancellationToken = default);

    Task<PdfIngestionRunRecord?> GetLatestAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PdfIngestionRunRecord>> GetRecentAsync(int count = 10, CancellationToken cancellationToken = default);
}

/// <summary>
/// Run summaries for "Copy PDFs into the repository", kept in the database (PdfIngestionRuns)
/// so every application process shows the same history and it survives a restart. It used to be
/// a per-process list in memory (docs/ux/admin-review-3-fixes.md, C8). Only the newest
/// <see cref="Capacity"/> runs are kept.
/// </summary>
public sealed class PdfIngestionRunHistory : IPdfIngestionRunHistory
{
    public const int Capacity = 50;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ApplicationDbContext _db;
    private readonly ILogger<PdfIngestionRunHistory>? _logger;

    public PdfIngestionRunHistory(ApplicationDbContext db, ILogger<PdfIngestionRunHistory>? logger = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger;
    }

    public async Task RecordAsync(PdfIngestionRunRecord run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);

        var row = await _db.PdfIngestionRuns.SingleOrDefaultAsync(item => item.Id == run.RunId, cancellationToken);
        if (row is null)
        {
            row = new PdfIngestionRun { Id = run.RunId };
            _db.PdfIngestionRuns.Add(row);
        }

        row.StartedAtUtc = run.StartedAtUtc.UtcDateTime;
        row.CompletedAtUtc = run.CompletedAtUtc.UtcDateTime;
        row.InitiatedBy = Truncate(run.InitiatedBy, 256);
        row.Status = Truncate(run.Status, 64) ?? string.Empty;
        row.Discovered = run.Discovered;
        row.IngestedOrLinked = run.IngestedOrLinked;
        row.AlreadyLinked = run.AlreadyLinked;
        row.Missing = run.Missing;
        row.Failed = run.Failed;
        row.SourcesJson = JsonSerializer.Serialize(run.Sources ?? Array.Empty<PdfIngestionSourceSummary>(), JsonOptions);
        row.FailuresJson = JsonSerializer.Serialize(run.Failures ?? Array.Empty<PdfIngestionFailure>(), JsonOptions);
        row.TraceId = Truncate(run.TraceId, 128);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Leave the shared request-scoped context clean for the caller.
            _db.Entry(row).State = EntityState.Detached;
            throw;
        }

        // The summary is saved. Removing old runs is housekeeping: if it fails, the caller must
        // not be told the summary was lost, and the next run trims again.
        try
        {
            await TrimAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "PDF ingestion run {RunId} was saved, but old run summaries could not be removed; the next run will try again.", run.RunId);
        }
    }

    public async Task<PdfIngestionRunRecord?> GetAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var row = await _db.PdfIngestionRuns.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == runId, cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    public async Task<PdfIngestionRunRecord?> GetLatestAsync(CancellationToken cancellationToken = default) =>
        (await GetRecentAsync(1, cancellationToken)).FirstOrDefault();

    public async Task<IReadOnlyList<PdfIngestionRunRecord>> GetRecentAsync(int count = 10, CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(count, 1, Capacity);
        var rows = await _db.PdfIngestionRuns.AsNoTracking()
            .OrderByDescending(item => item.CompletedAtUtc)
            .ThenByDescending(item => item.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
        return rows.Select(ToRecord).ToArray();
    }

    /// <summary>Removes runs older than the newest <see cref="Capacity"/>. Safe to run twice.</summary>
    private async Task TrimAsync(CancellationToken cancellationToken)
    {
        var expired = await _db.PdfIngestionRuns
            .OrderByDescending(item => item.CompletedAtUtc)
            .ThenByDescending(item => item.Id)
            .Skip(Capacity)
            .ToListAsync(cancellationToken);
        if (expired.Count == 0)
        {
            return;
        }

        _db.PdfIngestionRuns.RemoveRange(expired);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another process trimmed the same rows first; nothing is left to do.
            Detach(expired);
        }
        catch
        {
            // Leave the shared request-scoped context clean, then let RecordAsync log it.
            Detach(expired);
            throw;
        }
    }

    private void Detach(IEnumerable<PdfIngestionRun> rows)
    {
        foreach (var row in rows)
        {
            _db.Entry(row).State = EntityState.Detached;
        }
    }

    private static PdfIngestionRunRecord ToRecord(PdfIngestionRun row) =>
        new(
            row.Id,
            AsUtc(row.StartedAtUtc),
            AsUtc(row.CompletedAtUtc),
            row.InitiatedBy,
            row.Status,
            row.Discovered,
            row.IngestedOrLinked,
            row.AlreadyLinked,
            row.Missing,
            row.Failed,
            ReadList<PdfIngestionSourceSummary>(row.SourcesJson),
            ReadList<PdfIngestionFailure>(row.FailuresJson),
            row.TraceId);

    private static DateTimeOffset AsUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static IReadOnlyList<T> ReadList<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<T>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? (IReadOnlyList<T>)Array.Empty<T>();
        }
        catch (JsonException)
        {
            return Array.Empty<T>();
        }
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
