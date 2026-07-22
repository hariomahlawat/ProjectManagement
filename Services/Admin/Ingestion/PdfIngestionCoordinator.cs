using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Application.Ipr;
using ProjectManagement.Data;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Services.Storage;

namespace ProjectManagement.Services.Admin.Ingestion;

public sealed record PdfIngestionFailure(
    string Source,
    string SourceItemId,
    string FileName,
    string Message);

public sealed record PdfIngestionSourceSummary(
    string Source,
    int Discovered,
    int IngestedOrLinked,
    int AlreadyLinked,
    int Missing,
    int Failed)
{
    public int Completed => IngestedOrLinked + AlreadyLinked;
}

public sealed record PdfIngestionRunResult(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    IReadOnlyList<PdfIngestionSourceSummary> Sources,
    IReadOnlyList<PdfIngestionFailure> Failures,
    string Status)
{
    public Guid RunId { get; init; } = Guid.NewGuid();
    public int Discovered => Sources.Sum(source => source.Discovered);
    public int IngestedOrLinked => Sources.Sum(source => source.IngestedOrLinked);
    public int AlreadyLinked => Sources.Sum(source => source.AlreadyLinked);
    public int Missing => Sources.Sum(source => source.Missing);
    public int Failed => Sources.Sum(source => source.Failed);
}

public interface IPdfIngestionRunGate
{
    bool IsRunning { get; }
    bool TryEnter(out IDisposable? lease);
}

public sealed class PdfIngestionRunGate : IPdfIngestionRunGate
{
    private int _running;

    public bool IsRunning => Volatile.Read(ref _running) == 1;

    public bool TryEnter(out IDisposable? lease)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            lease = null;
            return false;
        }

        lease = new Lease(this);
        return true;
    }

    private sealed class Lease : IDisposable
    {
        private PdfIngestionRunGate? _owner;

        public Lease(PdfIngestionRunGate owner) => _owner = owner;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is not null)
            {
                Volatile.Write(ref owner._running, 0);
            }
        }
    }
}

public interface IPdfIngestionCoordinator
{
    bool IsRunning { get; }

    Task<AdminOperationResult<PdfIngestionRunResult>> RunAsync(
        CancellationToken cancellationToken = default);
}

public sealed class PdfIngestionCoordinator : IPdfIngestionCoordinator
{
    private const int MaximumFailureDetails = 50;

    private readonly ApplicationDbContext _db;
    private readonly IDocRepoIngestionService _ingestionService;
    private readonly IprAttachmentStorage _iprAttachmentStorage;
    private readonly IUploadPathResolver _pathResolver;
    private readonly DocRepoOptions _options;
    private readonly IPdfIngestionRunGate _runGate;
    private readonly IAdminAuditService _audit;
    private readonly IPdfIngestionRunHistory _history;
    private readonly IAdminTimeService _time;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<PdfIngestionCoordinator> _logger;

    public PdfIngestionCoordinator(
        ApplicationDbContext db,
        IDocRepoIngestionService ingestionService,
        IprAttachmentStorage iprAttachmentStorage,
        IUploadPathResolver pathResolver,
        IOptions<DocRepoOptions> options,
        IPdfIngestionRunGate runGate,
        IAdminAuditService audit,
        IPdfIngestionRunHistory history,
        IAdminTimeService time,
        IHttpContextAccessor httpContextAccessor,
        ILogger<PdfIngestionCoordinator> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _ingestionService = ingestionService ?? throw new ArgumentNullException(nameof(ingestionService));
        _iprAttachmentStorage = iprAttachmentStorage ?? throw new ArgumentNullException(nameof(iprAttachmentStorage));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _runGate = runGate ?? throw new ArgumentNullException(nameof(runGate));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsRunning => _runGate.IsRunning;

    public async Task<AdminOperationResult<PdfIngestionRunResult>> RunAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_options.EnableIngestion)
        {
            return AdminOperationResult<PdfIngestionRunResult>.Failure(
                "Document repository ingestion is disabled in configuration.",
                "PdfIngestionDisabled");
        }

        if (!_runGate.TryEnter(out var lease) || lease is null)
        {
            return AdminOperationResult<PdfIngestionRunResult>.Failure(
                "Another PDF ingestion run is already in progress.",
                "PdfIngestionAlreadyRunning");
        }

        using (lease)
        {
            var runId = Guid.NewGuid();
            var startedAt = _time.UtcNow;
            var traceId = _httpContextAccessor.HttpContext?.TraceIdentifier;
            var actor = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
            var failures = new List<PdfIngestionFailure>();
            var summaries = new List<PdfIngestionSourceSummary>();

            var startAuditWritten = await TryRecordAuditAsync(
                new AdminAuditEntry(
                    Action: "PdfIngestionStarted",
                    EntityType: "DocumentRepository",
                    Origin: "Admin.Documents.IngestExternalPdfs",
                    EntityId: runId.ToString("N"),
                    After: new { RunId = runId, StartedAtUtc = startedAt },
                    Message: "External PDF ingestion started."),
                cancellationToken,
                "start",
                traceId);

            if (!startAuditWritten)
            {
                return AdminOperationResult<PdfIngestionRunResult>.Failure(
                    "PDF ingestion was not started because its audit record could not be created. Quote the trace reference to the administrator.",
                    "PdfIngestionAuditUnavailable",
                    traceId);
            }

            try
            {
                summaries.Add(await ProcessFfcAsync(failures, cancellationToken));
                summaries.Add(await ProcessIprAsync(failures, cancellationToken));
                summaries.Add(await ProcessActivitiesAsync(failures, cancellationToken));

                var completedAt = _time.UtcNow;
                var status = summaries.Sum(source => source.Failed) > 0 || summaries.Sum(source => source.Missing) > 0
                    ? "Partially completed"
                    : "Completed";
                var result = new PdfIngestionRunResult(startedAt, completedAt, summaries, failures, status)
                {
                    RunId = runId
                };

                var completionAuditWritten = await TryRecordAuditAsync(
                    new AdminAuditEntry(
                        Action: "PdfIngestionCompleted",
                        EntityType: "DocumentRepository",
                        Origin: "Admin.Documents.IngestExternalPdfs",
                        EntityId: runId.ToString("N"),
                        After: new
                        {
                            result.RunId,
                            result.Status,
                            result.Discovered,
                            result.IngestedOrLinked,
                            result.AlreadyLinked,
                            result.Missing,
                            result.Failed,
                            result.StartedAtUtc,
                            result.CompletedAtUtc
                        },
                        Outcome: status == "Completed" ? "Succeeded" : "PartiallySucceeded",
                        Level: status == "Completed" ? "Info" : "Warning",
                        Message: $"External PDF ingestion {status.ToLowerInvariant()}."),
                    CancellationToken.None,
                    "completion",
                    traceId);

                if (!completionAuditWritten)
                {
                    status = status == "Completed"
                        ? "Completed with audit warning"
                        : "Partially completed with audit warning";
                    result = result with { Status = status };
                }

                _history.Record(new PdfIngestionRunRecord(
                    result.RunId,
                    result.StartedAtUtc,
                    result.CompletedAtUtc,
                    actor,
                    result.Status,
                    result.Discovered,
                    result.IngestedOrLinked,
                    result.AlreadyLinked,
                    result.Missing,
                    result.Failed,
                    result.Sources,
                    result.Failures,
                    traceId));

                return AdminOperationResult<PdfIngestionRunResult>.Success(
                    result,
                    status switch
                    {
                        "Completed" => "PDF ingestion completed.",
                        "Partially completed" => "PDF ingestion completed with missing or failed items.",
                        _ => "PDF ingestion completed, but the completion audit record could not be written. Review the application logs."
                    });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                var completedAt = _time.UtcNow;
                await TryRecordAuditAsync(
                    new AdminAuditEntry(
                        Action: "PdfIngestionCancelled",
                        EntityType: "DocumentRepository",
                        EntityId: runId.ToString("N"),
                        Origin: "Admin.Documents.IngestExternalPdfs",
                        Before: new { RunId = runId, StartedAtUtc = startedAt },
                        After: new { CompletedAtUtc = completedAt },
                        Outcome: "Cancelled",
                        Level: "Warning",
                        Message: "External PDF ingestion was cancelled."),
                    CancellationToken.None,
                    "cancellation",
                    traceId);

                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "External PDF ingestion failed. TraceId={TraceId}",
                    traceId);

                await TryRecordAuditAsync(
                    new AdminAuditEntry(
                        Action: "PdfIngestionFailed",
                        EntityType: "DocumentRepository",
                        EntityId: runId.ToString("N"),
                        Origin: "Admin.Documents.IngestExternalPdfs",
                        Before: new { RunId = runId, StartedAtUtc = startedAt },
                        After: new { CompletedAtUtc = _time.UtcNow, TraceId = traceId },
                        Outcome: "Failed",
                        Level: "Error",
                        Message: "External PDF ingestion failed."),
                    CancellationToken.None,
                    "failure",
                    traceId);

                var failedAt = _time.UtcNow;
                _history.Record(new PdfIngestionRunRecord(
                    runId,
                    startedAt,
                    failedAt,
                    actor,
                    "Failed",
                    0,
                    0,
                    0,
                    0,
                    1,
                    Array.Empty<PdfIngestionSourceSummary>(),
                    Array.Empty<PdfIngestionFailure>(),
                    traceId));

                return AdminOperationResult<PdfIngestionRunResult>.Failure(
                    "PDF ingestion could not be completed. Quote the trace reference to the administrator.",
                    "PdfIngestionFailed",
                    traceId);
            }
        }
    }

    private async Task<PdfIngestionSourceSummary> ProcessFfcAsync(
        ICollection<PdfIngestionFailure> failures,
        CancellationToken cancellationToken)
    {
        const string source = "FFC";
        var rows = await _db.FfcAttachments
            .AsNoTracking()
            .Where(attachment => attachment.ContentType == "application/pdf")
            .Select(attachment => new
            {
                attachment.Id,
                attachment.FilePath
            })
            .ToListAsync(cancellationToken);
        var items = rows
            .Select(row => new SourceFileItem(
                row.Id.ToString(CultureInfo.InvariantCulture),
                row.FilePath,
                row.FilePath))
            .ToList();

        var linked = await GetExistingLinksAsync(source, cancellationToken);
        var counts = new MutableCounts(items.Count);

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (linked.Contains(item.SourceItemId))
            {
                counts.AlreadyLinked++;
                continue;
            }

            var absolutePath = ResolveAbsolutePath(item.StorageKey);
            var fileName = string.IsNullOrWhiteSpace(item.FileName)
                ? (string.IsNullOrWhiteSpace(absolutePath) ? "document.pdf" : Path.GetFileName(absolutePath))
                : Path.GetFileName(item.FileName);
            if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
            {
                counts.Missing++;
                AddFailureDetail(failures, source, item.SourceItemId, fileName, "Source file is missing.");
                continue;
            }

            try
            {
                await using var stream = File.OpenRead(absolutePath);
                var documentId = await _ingestionService.IngestExternalPdfAsync(
                    stream,
                    fileName,
                    source,
                    item.SourceItemId,
                    cancellationToken);

                if (documentId == Guid.Empty)
                {
                    counts.Failed++;
                    AddFailureDetail(failures, source, item.SourceItemId, fileName, "Ingestion service did not create or link a document.");
                }
                else
                {
                    counts.IngestedOrLinked++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                counts.Failed++;
                LogItemFailure(exception, failures, source, item.SourceItemId, fileName);
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
        }

        return counts.ToSummary(source);
    }

    private async Task<PdfIngestionSourceSummary> ProcessIprAsync(
        ICollection<PdfIngestionFailure> failures,
        CancellationToken cancellationToken)
    {
        const string source = "IPR";
        var rows = await _db.IprAttachments
            .AsNoTracking()
            .Where(attachment => !attachment.IsArchived && attachment.ContentType == "application/pdf")
            .Select(attachment => new
            {
                attachment.Id,
                attachment.StorageKey,
                attachment.OriginalFileName
            })
            .ToListAsync(cancellationToken);
        var items = rows
            .Select(row => new SourceFileItem(
                row.Id.ToString(CultureInfo.InvariantCulture),
                row.StorageKey,
                row.OriginalFileName))
            .ToList();

        var linked = await GetExistingLinksAsync(source, cancellationToken);
        var counts = new MutableCounts(items.Count);

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (linked.Contains(item.SourceItemId))
            {
                counts.AlreadyLinked++;
                continue;
            }

            try
            {
                await using var stream = await _iprAttachmentStorage.OpenReadAsync(item.StorageKey, cancellationToken);
                var documentId = await _ingestionService.IngestExternalPdfAsync(
                    stream,
                    item.FileName,
                    source,
                    item.SourceItemId,
                    cancellationToken);

                if (documentId == Guid.Empty)
                {
                    counts.Failed++;
                    AddFailureDetail(failures, source, item.SourceItemId, item.FileName, "Ingestion service did not create or link a document.");
                }
                else
                {
                    counts.IngestedOrLinked++;
                }
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                counts.Missing++;
                AddFailureDetail(failures, source, item.SourceItemId, item.FileName, "Source file is missing.");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                counts.Failed++;
                LogItemFailure(exception, failures, source, item.SourceItemId, item.FileName);
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
        }

        return counts.ToSummary(source);
    }

    private async Task<PdfIngestionSourceSummary> ProcessActivitiesAsync(
        ICollection<PdfIngestionFailure> failures,
        CancellationToken cancellationToken)
    {
        const string source = "Activities";
        var rows = await _db.ActivityAttachments
            .AsNoTracking()
            .Where(attachment => attachment.ContentType == "application/pdf")
            .Select(attachment => new
            {
                attachment.Id,
                attachment.StorageKey,
                attachment.OriginalFileName
            })
            .ToListAsync(cancellationToken);
        var items = rows
            .Select(row => new SourceFileItem(
                row.Id.ToString(CultureInfo.InvariantCulture),
                row.StorageKey,
                row.OriginalFileName))
            .ToList();

        var linked = await GetExistingLinksAsync(source, cancellationToken);
        var counts = new MutableCounts(items.Count);

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (linked.Contains(item.SourceItemId))
            {
                counts.AlreadyLinked++;
                continue;
            }

            var absolutePath = ResolveAbsolutePath(item.StorageKey);
            if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
            {
                counts.Missing++;
                AddFailureDetail(failures, source, item.SourceItemId, item.FileName, "Source file is missing.");
                continue;
            }

            try
            {
                await using var stream = File.OpenRead(absolutePath);
                var documentId = await _ingestionService.IngestExternalPdfAsync(
                    stream,
                    item.FileName,
                    source,
                    item.SourceItemId,
                    cancellationToken);

                if (documentId == Guid.Empty)
                {
                    counts.Failed++;
                    AddFailureDetail(failures, source, item.SourceItemId, item.FileName, "Ingestion service did not create or link a document.");
                }
                else
                {
                    counts.IngestedOrLinked++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                counts.Failed++;
                LogItemFailure(exception, failures, source, item.SourceItemId, item.FileName);
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
        }

        return counts.ToSummary(source);
    }

    private async Task<bool> TryRecordAuditAsync(
        AdminAuditEntry entry,
        CancellationToken cancellationToken,
        string phase,
        string? traceId)
    {
        try
        {
            await _audit.RecordAsync(entry, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(
                exception,
                "Could not write PDF ingestion audit record. Phase={Phase}, TraceId={TraceId}",
                phase,
                traceId);
            return false;
        }
    }

    private async Task<HashSet<string>> GetExistingLinksAsync(
        string source,
        CancellationToken cancellationToken)
    {
        var sourceItemIds = await _db.DocRepoExternalLinks
            .AsNoTracking()
            .Where(link => link.SourceModule == source)
            .Select(link => link.SourceItemId)
            .ToListAsync(cancellationToken);

        return sourceItemIds.ToHashSet(StringComparer.Ordinal);
    }

    private string ResolveAbsolutePath(string? storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            return string.Empty;
        }

        try
        {
            return _pathResolver.ToAbsolute(storageKey);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not resolve upload path for storage key {StorageKey}.", storageKey);
            return storageKey;
        }
    }

    private void LogItemFailure(
        Exception exception,
        ICollection<PdfIngestionFailure> failures,
        string source,
        string sourceItemId,
        string fileName)
    {
        _logger.LogError(
            exception,
            "Failed to ingest external PDF. Source={Source}, SourceItemId={SourceItemId}, FileName={FileName}",
            source,
            sourceItemId,
            fileName);

        AddFailureDetail(
            failures,
            source,
            sourceItemId,
            fileName,
            "Ingestion failed. Review the application logs for details.");
    }

    private static void AddFailureDetail(
        ICollection<PdfIngestionFailure> failures,
        string source,
        string sourceItemId,
        string? fileName,
        string message)
    {
        if (failures.Count >= MaximumFailureDetails)
        {
            return;
        }

        failures.Add(new PdfIngestionFailure(
            source,
            sourceItemId,
            string.IsNullOrWhiteSpace(fileName) ? "document.pdf" : Path.GetFileName(fileName),
            message));
    }


    private sealed record SourceFileItem(
        string SourceItemId,
        string StorageKey,
        string FileName);

    private sealed class MutableCounts
    {
        public MutableCounts(int discovered) => Discovered = discovered;

        public int Discovered { get; }
        public int IngestedOrLinked { get; set; }
        public int AlreadyLinked { get; set; }
        public int Missing { get; set; }
        public int Failed { get; set; }

        public PdfIngestionSourceSummary ToSummary(string source) =>
            new(source, Discovered, IngestedOrLinked, AlreadyLinked, Missing, Failed);
    }
}
