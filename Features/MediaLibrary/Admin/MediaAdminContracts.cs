using System.ComponentModel.DataAnnotations;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Outbox;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Features.MediaLibrary.Admin;

public static class MediaAdminErrorCodes
{
    public const string Forbidden = "Forbidden";
    public const string NotFound = "NotFound";
    public const string InvalidInput = "InvalidInput";
    public const string DuplicateSource = "DuplicateSource";
    public const string ConcurrencyConflict = "ConcurrencyConflict";
    public const string SourceUnavailable = "SourceUnavailable";
    public const string SourceDisabled = "SourceDisabled";
    public const string SourceManagedByConfiguration = "SourceManagedByConfiguration";
    public const string QueueItemNotRetryable = "QueueItemNotRetryable";
    public const string CatalogueUnavailable = "CatalogueUnavailable";
    public const string UnexpectedFailure = "UnexpectedFailure";
}

public sealed record MediaAdminCommandResult<T>(
    bool Succeeded,
    T? Value = default,
    string? UserMessage = null,
    string? ErrorCode = null,
    string? TraceId = null,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null)
{
    public static MediaAdminCommandResult<T> Success(T value, string? message = null) =>
        new(true, value, message);

    public static MediaAdminCommandResult<T> Failure(
        string message,
        string? errorCode = null,
        string? traceId = null,
        IReadOnlyDictionary<string, string[]>? fieldErrors = null) =>
        new(false, default, message, errorCode, traceId, fieldErrors);
}

public sealed record MediaAdminCommandResult(
    bool Succeeded,
    string? UserMessage = null,
    string? ErrorCode = null,
    string? TraceId = null,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null)
{
    public static MediaAdminCommandResult Success(string? message = null) => new(true, message);

    public static MediaAdminCommandResult Failure(
        string message,
        string? errorCode = null,
        string? traceId = null,
        IReadOnlyDictionary<string, string[]>? fieldErrors = null) =>
        new(false, message, errorCode, traceId, fieldErrors);
}

public sealed class MediaSourceAdminInput
{
    public Guid? Id { get; set; }

    /// <summary>
    /// Optimistic concurrency token derived from administrator-controlled source configuration.
    /// </summary>
    [MaxLength(64)]
    public string? ConcurrencyToken { get; set; }

    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(64)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(2048)]
    public string RootPath { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;
    public bool IsVisibleInLibrary { get; set; } = true;
    public bool IncludeSubfolders { get; set; } = true;

    [Range(1, 10080)]
    public int ScanIntervalMinutes { get; set; } = 30;

    public string AllowedExtensions { get; set; } = string.Join(", ", MediaSourceDefaults.AllowedExtensions);
}

public sealed record MediaSourceTestResult(
    bool IsReachable,
    string PathKind,
    string Message,
    Guid? SourceId,
    string? ConcurrencyToken);

public sealed record MediaSourceAdminRow(
    Guid Id,
    string Name,
    string Key,
    MediaLibrarySourceType Type,
    bool IsEnabled,
    bool IsVisibleInLibrary,
    bool IsReadOnly,
    bool IsConfigurationManaged,
    string? RootPath,
    string Status,
    string HealthStatus,
    string? HealthMessage,
    long AssetCount,
    DateTimeOffset? LastSuccessfulScanAtUtc,
    DateTimeOffset? LastHealthCheckedAtUtc,
    string? LastError,
    string ConcurrencyToken);

public sealed record MediaProcessingJobAdminRow(
    long Id,
    long MediaAssetId,
    MediaProcessingJobStatus Status,
    int AttemptCount,
    int MaxAttempts,
    string? FailureCode,
    string? FailureMessage,
    DateTimeOffset AvailableAfterUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record UnavailableMediaAdminRow(
    long Id,
    MediaAssetOrigin Origin,
    string ContextTitle,
    string OriginalFileName,
    string SourceLabel,
    DateTimeOffset LastSeenAtUtc,
    MediaAvailabilityStatus Status,
    string StatusLabel,
    DateTimeOffset? UnavailableSinceUtc,
    string UnavailableSinceDisplay,
    DateTimeOffset? LastCheckedUtc,
    string LastCheckedDisplay,
    string ReasonSummary,
    string ReasonDetail);

public sealed record MediaSourcesAdminQuery(
    string? UnavailableQuery,
    MediaAvailabilityStatus? UnavailableStatus,
    int UnavailablePage,
    int UnavailablePageSize = 25);

public sealed record MediaSourcesAdminPage
{
    public IReadOnlyList<MediaSourceAdminRow> Sources { get; init; } = Array.Empty<MediaSourceAdminRow>();
    public int PendingJobs { get; init; }
    public int RunningJobs { get; init; }
    public int RetryingJobs { get; init; }
    public int CompletedJobs { get; init; }
    public int FailedJobs { get; init; }
    public int DeadLetterJobs { get; init; }
    public int SourceUnavailableJobs { get; init; }
    public int UnavailableAssetCount { get; init; }
    public int HistoricalAvailabilityCandidates { get; init; }
    public DateTimeOffset? LastAvailabilityCheckUtc { get; init; }
    public DateTimeOffset? OldestPendingAtUtc { get; init; }
    public MediaProcessingRuntimeSnapshot ProcessingRuntime { get; init; }
        = new(false, false, "Unknown", string.Empty, null, null, null, null, null, null, null, 0, 0, null, null);
    public MediaCacheHealthResult? CacheHealth { get; init; }
    public IReadOnlyList<MediaProcessingJobAdminRow> RecentProblemJobs { get; init; } = Array.Empty<MediaProcessingJobAdminRow>();
    public IReadOnlyList<UnavailableMediaAdminRow> UnavailableAssets { get; init; } = Array.Empty<UnavailableMediaAdminRow>();
    public IReadOnlyDictionary<MediaAvailabilityStatus, int> UnavailableStatusCounts { get; init; }
        = new Dictionary<MediaAvailabilityStatus, int>();
    public int UnavailablePage { get; init; } = 1;
    public int UnavailableTotalPages { get; init; }
    public int UnavailablePageSize { get; init; } = 25;
    public bool CatalogueAvailable { get; init; } = true;
    public bool CatalogueSchemaCurrent { get; init; } = true;
    public bool CatalogueMigrationHistoryConsistent { get; init; } = true;
    public string? CatalogueDiagnosticReference { get; init; }
    public bool ExternalSourcesEnabled { get; init; }
    public IReadOnlyList<string> PendingMigrations { get; init; } = Array.Empty<string>();
    public string? CatalogueError { get; init; }
    public MediaLibraryHealthReport? CatalogueHealth { get; init; }
    public IReadOnlyList<MediaLibraryDiagnosticEvent> CatalogueDiagnostics { get; init; } = Array.Empty<MediaLibraryDiagnosticEvent>();
    public long PrismAssetCount { get; init; }
    public int PrismCatalogueRecordCount { get; init; }
    public int PrismUnavailableCatalogueCount { get; init; }
    public int PrismOrphanedCatalogueCount { get; init; }
    public int PrismSourceRecordCount { get; init; }
    public int ActivitySourcePhotoCount { get; init; }
    public long ActivityCataloguePhotoCount { get; init; }
    public long ActivityCatalogueRepresentationCount { get; init; }
    public long ActivityUnavailableCataloguePhotoCount { get; init; }
    public int PendingIngestionEvents { get; init; }
    public int ProcessingIngestionEvents { get; init; }
    public int DeadLetterIngestionEvents { get; init; }
    public int RetryableIngestionEvents { get; init; }
    public bool OutboxSchemaAvailable { get; init; } = true;
    public string? OutboxSchemaWarning { get; init; }
    public DateTimeOffset? OldestPendingIngestionAtUtc { get; init; }
    public string? LastIngestionError { get; init; }
    public PrismMediaOutboxRuntimeSnapshot OutboxRuntime { get; init; }
        = new(false, null, null, null, null, "Pending", null, null, null);
    public int MissingFromCatalogue { get; init; }
}

public interface IMediaAdminAccessService
{
    Task<bool> IsAuthorizedAsync(string policy, CancellationToken cancellationToken = default);
}

public interface IMediaSourcesAdminQueryService
{
    Task<MediaSourcesAdminPage> GetPageAsync(MediaSourcesAdminQuery query, CancellationToken cancellationToken);
}

public interface IMediaSourceAdminService
{
    Task<MediaAdminCommandResult<MediaSourceAdminInput>> GetForEditAsync(Guid id, CancellationToken cancellationToken);
    Task<MediaAdminCommandResult<Guid>> SaveAsync(MediaSourceAdminInput input, CancellationToken cancellationToken);
    Task<MediaAdminCommandResult<MediaSourceTestResult>> TestAsync(
        Guid? sourceId,
        string? concurrencyToken,
        MediaSourceAdminInput? input,
        CancellationToken cancellationToken);
    Task<MediaAdminCommandResult> RequestScanAsync(Guid id, string concurrencyToken, CancellationToken cancellationToken);
    Task<MediaAdminCommandResult> SetStateAsync(
        Guid id,
        string concurrencyToken,
        bool enabled,
        bool visible,
        CancellationToken cancellationToken);
    Task<MediaAdminCommandResult> DisconnectAsync(Guid id, string concurrencyToken, CancellationToken cancellationToken);
}

public interface IMediaQueueAdminService
{
    Task<MediaAdminCommandResult<int>> RetryRecoverableAsync(int maximumItems, CancellationToken cancellationToken);
    Task<MediaAdminCommandResult<int>> RetryPermanentAsync(int maximumItems, CancellationToken cancellationToken);
    Task<MediaAdminCommandResult> RetryJobAsync(long id, bool forcePermanent, CancellationToken cancellationToken);
    Task<MediaAdminCommandResult<int>> RetryIngestionAsync(int maximumItems, CancellationToken cancellationToken);
}

public interface IMediaRecoveryAdminService
{
    Task<MediaAdminCommandResult> TestCatalogueAsync(CancellationToken cancellationToken);
    Task<MediaAdminCommandResult> SynchronizePrismAsync(CancellationToken cancellationToken);
    Task<MediaAdminCommandResult> CheckConsistencyAsync(CancellationToken cancellationToken);
    Task<MediaAdminCommandResult> ReconcileAvailabilityAsync(int maximumItems, CancellationToken cancellationToken);
    Task<MediaAdminCommandResult> RecheckUnavailableAsync(int maximumItems, CancellationToken cancellationToken);
    Task<MediaAdminCommandResult> RecheckUnavailableAssetAsync(long id, CancellationToken cancellationToken);
}
