using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Outbox;
using ProjectManagement.Features.MediaLibrary.Services;
using ProjectManagement.Services.Admin;

namespace ProjectManagement.Features.MediaLibrary.Admin;

public sealed class MediaSourcesAdminQueryService : IMediaSourcesAdminQueryService
{
    private const int MaximumPageSize = 100;

    private static readonly string[] UnavailableFailureCodes =
    {
        nameof(MediaContentUnavailableException),
        nameof(FileNotFoundException),
        nameof(DirectoryNotFoundException)
    };

    private readonly MediaLibraryDbContext _db;
    private readonly ApplicationDbContext _applicationDb;
    private readonly MediaLibraryOptions _options;
    private readonly IMediaLibrarySchemaService _schemaService;
    private readonly IMediaLibraryHealthService _catalogueHealthService;
    private readonly IMediaLibraryDiagnostics _catalogueDiagnostics;
    private readonly IMediaCatalogueConsistencyService _consistencyService;
    private readonly IMediaProcessingRuntimeState _processingRuntime;
    private readonly IMediaCacheHealthService _cacheHealthService;
    private readonly IMediaAvailabilityRecoveryService _availabilityRecoveryService;
    private readonly IPrismMediaSourceSnapshotService _sourceSnapshot;
    private readonly IPrismMediaOutboxRuntimeState _outboxRuntimeState;
    private readonly IMediaAdminAccessService _access;
    private readonly IAdminTimeService _time;
    private readonly ILogger<MediaSourcesAdminQueryService> _logger;

    public MediaSourcesAdminQueryService(
        MediaLibraryDbContext db,
        ApplicationDbContext applicationDb,
        IOptions<MediaLibraryOptions> options,
        IMediaLibrarySchemaService schemaService,
        IMediaLibraryHealthService catalogueHealthService,
        IMediaLibraryDiagnostics catalogueDiagnostics,
        IMediaCatalogueConsistencyService consistencyService,
        IMediaProcessingRuntimeState processingRuntime,
        IMediaCacheHealthService cacheHealthService,
        IMediaAvailabilityRecoveryService availabilityRecoveryService,
        IPrismMediaSourceSnapshotService sourceSnapshot,
        IPrismMediaOutboxRuntimeState outboxRuntimeState,
        IMediaAdminAccessService access,
        IAdminTimeService time,
        ILogger<MediaSourcesAdminQueryService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _applicationDb = applicationDb ?? throw new ArgumentNullException(nameof(applicationDb));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _schemaService = schemaService ?? throw new ArgumentNullException(nameof(schemaService));
        _catalogueHealthService = catalogueHealthService ?? throw new ArgumentNullException(nameof(catalogueHealthService));
        _catalogueDiagnostics = catalogueDiagnostics ?? throw new ArgumentNullException(nameof(catalogueDiagnostics));
        _consistencyService = consistencyService ?? throw new ArgumentNullException(nameof(consistencyService));
        _processingRuntime = processingRuntime ?? throw new ArgumentNullException(nameof(processingRuntime));
        _cacheHealthService = cacheHealthService ?? throw new ArgumentNullException(nameof(cacheHealthService));
        _availabilityRecoveryService = availabilityRecoveryService ?? throw new ArgumentNullException(nameof(availabilityRecoveryService));
        _sourceSnapshot = sourceSnapshot ?? throw new ArgumentNullException(nameof(sourceSnapshot));
        _outboxRuntimeState = outboxRuntimeState ?? throw new ArgumentNullException(nameof(outboxRuntimeState));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MediaSourcesAdminPage> GetPageAsync(
        MediaSourcesAdminQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await _access.IsAuthorizedAsync(AdminPolicies.MediaView, cancellationToken))
        {
            throw new UnauthorizedAccessException("The current user is not authorised to view media administration.");
        }

        var ingestion = await LoadIngestionStatusAsync(cancellationToken);
        var schema = await _schemaService.GetStatusAsync(cancellationToken);
        var catalogueAvailable = schema.IsAvailable && schema.IsOperational;
        var pageSize = Math.Clamp(query.UnavailablePageSize, 1, MaximumPageSize);

        if (!catalogueAvailable)
        {
            return new MediaSourcesAdminPage
            {
                ExternalSourcesEnabled = _options.IsExternalSourceFeatureEnabled,
                CatalogueAvailable = false,
                CatalogueSchemaCurrent = schema.IsCurrent,
                CatalogueMigrationHistoryConsistent = schema.MigrationHistoryConsistent,
                CatalogueDiagnosticReference = schema.DiagnosticReference,
                PendingMigrations = schema.PendingMigrations,
                CatalogueError = schema.Error,
                MissingFromCatalogue = ingestion.PrismSourceRecordCount,
                ProcessingRuntime = _processingRuntime.GetSnapshot(),
                CatalogueDiagnostics = _catalogueDiagnostics.GetLatest(),
                UnavailablePage = 1,
                UnavailablePageSize = pageSize,
                PrismSourceRecordCount = ingestion.PrismSourceRecordCount,
                ActivitySourcePhotoCount = ingestion.ActivitySourcePhotoCount,
                PendingIngestionEvents = ingestion.Pending,
                ProcessingIngestionEvents = ingestion.Processing,
                DeadLetterIngestionEvents = ingestion.DeadLetter,
                RetryableIngestionEvents = ingestion.Retryable,
                OutboxSchemaAvailable = ingestion.SchemaAvailable,
                OutboxSchemaWarning = ingestion.SchemaWarning,
                OldestPendingIngestionAtUtc = ingestion.OldestPendingAtUtc,
                LastIngestionError = ingestion.LastError,
                OutboxRuntime = ingestion.Runtime
            };
        }

        try
        {
            var sourceRows = await _db.Sources
                .AsNoTracking()
                .Where(source => !source.IsDeleted)
                .OrderBy(source => source.SourceType)
                .ThenBy(source => source.Name)
                .ToListAsync(cancellationToken);

            var sources = sourceRows
                .Select(source => new MediaSourceAdminRow(
                    source.Id,
                    source.Name,
                    source.Key,
                    source.SourceType,
                    source.IsEnabled,
                    source.IsVisibleInLibrary,
                    source.IsReadOnly,
                    source.IsConfigurationManaged,
                    source.RootPath,
                    source.ScanStatus,
                    source.HealthStatus,
                    source.HealthMessage,
                    source.IndexedAssetCount,
                    source.LastSuccessfulScanAtUtc,
                    source.LastHealthCheckedAtUtc,
                    source.LastError,
                    MediaSourceAdminConcurrency.Create(source)))
                .ToList();

            var consistency = await _consistencyService.CheckAsync(cancellationToken);
            var prismAssetCount = (long)consistency.AvailableCatalogueRecords;
            sources = sources
                .Select(source => source.Key == MediaSourceBootstrapper.PrismSourceKey
                    ? source with { AssetCount = prismAssetCount }
                    : source)
                .ToList();

            var activityCatalogueRepresentationCount = await _db.Assets.LongCountAsync(
                asset => asset.Source.SourceType == MediaLibrarySourceType.Prism
                         && asset.Origin == MediaAssetOrigin.ActivityPhoto
                         && !asset.IsDeleted,
                cancellationToken);
            var activityCataloguePhotoCount = await _db.Assets.LongCountAsync(
                asset => asset.Source.SourceType == MediaLibrarySourceType.Prism
                         && asset.Origin == MediaAssetOrigin.ActivityPhoto
                         && asset.IsAvailable
                         && !asset.IsDeleted,
                cancellationToken);

            var jobCounts = await _db.ProcessingJobs
                .AsNoTracking()
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    Pending = group.Count(job => job.Status == MediaProcessingJobStatus.Pending && job.AttemptCount == 0),
                    Retrying = group.Count(job => job.Status == MediaProcessingJobStatus.Pending && job.AttemptCount > 0),
                    Running = group.Count(job => job.Status == MediaProcessingJobStatus.Running),
                    Completed = group.Count(job => job.Status == MediaProcessingJobStatus.Completed && job.FailureCode != "SourceUnavailable"),
                    SourceUnavailable = group.Count(job => job.Status == MediaProcessingJobStatus.Completed && job.FailureCode == "SourceUnavailable"),
                    Failed = group.Count(job => job.Status == MediaProcessingJobStatus.Failed)
                })
                .SingleOrDefaultAsync(cancellationToken);

            var deadLetterJobs = await _db.ProcessingJobs.CountAsync(
                job => job.Status == MediaProcessingJobStatus.DeadLetter
                       && job.MediaAsset.IsAvailable
                       && job.MediaAsset.AvailabilityStatus == MediaAvailabilityStatus.Available
                       && (job.FailureCode == null || !UnavailableFailureCodes.Contains(job.FailureCode)),
                cancellationToken);

            var availabilityStatus = await _availabilityRecoveryService.GetStatusAsync(cancellationToken);
            var unavailableBaseQuery = _db.Assets
                .AsNoTracking()
                .Where(asset => !asset.IsDeleted
                                && asset.AvailabilityStatus != MediaAvailabilityStatus.Available);

            var statusCounts = await unavailableBaseQuery
                .GroupBy(asset => asset.AvailabilityStatus)
                .Select(group => new { Status = group.Key, Count = group.Count() })
                .ToDictionaryAsync(item => item.Status, item => item.Count, cancellationToken);

            if (query.UnavailableStatus.HasValue
                && query.UnavailableStatus.Value != MediaAvailabilityStatus.Available)
            {
                unavailableBaseQuery = unavailableBaseQuery
                    .Where(asset => asset.AvailabilityStatus == query.UnavailableStatus.Value);
            }

            var unavailableSearch = query.UnavailableQuery?.Trim();
            if (!string.IsNullOrWhiteSpace(unavailableSearch))
            {
                var pattern = $"%{unavailableSearch}%";
                unavailableBaseQuery = unavailableBaseQuery.Where(asset =>
                    EF.Functions.ILike(asset.ContextTitle, pattern)
                    || EF.Functions.ILike(asset.OriginalFileName, pattern)
                    || EF.Functions.ILike(asset.SourceLabel, pattern));
            }

            var unavailableFilteredCount = await unavailableBaseQuery.CountAsync(cancellationToken);
            var unavailableTotalPages = unavailableFilteredCount == 0
                ? 0
                : (int)Math.Ceiling(unavailableFilteredCount / (double)pageSize);
            var unavailablePage = Math.Clamp(query.UnavailablePage, 1, Math.Max(1, unavailableTotalPages));

            var unavailableRows = await unavailableBaseQuery
                .OrderByDescending(asset => asset.LastAvailabilityCheckUtc ?? asset.UnavailableSinceUtc ?? asset.LastSeenAtUtc)
                .ThenBy(asset => asset.ContextTitle)
                .ThenBy(asset => asset.Id)
                .Skip((unavailablePage - 1) * pageSize)
                .Take(pageSize)
                .Select(asset => new
                {
                    asset.Id,
                    asset.Origin,
                    asset.ContextTitle,
                    asset.OriginalFileName,
                    asset.SourceLabel,
                    asset.LastSeenAtUtc,
                    asset.AvailabilityStatus,
                    asset.UnavailableSinceUtc,
                    asset.LastAvailabilityCheckUtc,
                    asset.UnavailableReason
                })
                .ToListAsync(cancellationToken);

            var unavailableAssets = unavailableRows
                .Select(asset => new UnavailableMediaAdminRow(
                    asset.Id,
                    asset.Origin,
                    asset.ContextTitle,
                    asset.OriginalFileName,
                    asset.SourceLabel,
                    asset.LastSeenAtUtc,
                    asset.AvailabilityStatus,
                    MediaAdminDisplay.AvailabilityStatusLabel(asset.AvailabilityStatus),
                    asset.UnavailableSinceUtc,
                    _time.FormatIst(asset.UnavailableSinceUtc, "Not checked"),
                    asset.LastAvailabilityCheckUtc,
                    _time.FormatIst(asset.LastAvailabilityCheckUtc, "Not checked"),
                    MediaAdminDisplay.SummarizeUnavailableReason(asset.UnavailableReason),
                    asset.UnavailableReason ?? "The source media is unavailable."))
                .ToList();

            var oldestPendingAtUtc = await _db.ProcessingJobs
                .AsNoTracking()
                .Where(job => job.Status == MediaProcessingJobStatus.Pending)
                .OrderBy(job => job.CreatedAtUtc)
                .Select(job => (DateTimeOffset?)job.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            var recentProblemJobs = await _db.ProcessingJobs
                .AsNoTracking()
                .Where(job => job.MediaAsset.IsAvailable
                              && (job.FailureCode == null || !UnavailableFailureCodes.Contains(job.FailureCode))
                              && job.MediaAsset.AvailabilityStatus == MediaAvailabilityStatus.Available
                              && (job.Status == MediaProcessingJobStatus.DeadLetter
                                  || job.Status == MediaProcessingJobStatus.Failed
                                  || (job.Status == MediaProcessingJobStatus.Pending && job.AttemptCount > 0)))
                .OrderByDescending(job => job.UpdatedAtUtc)
                .Take(20)
                .Select(job => new MediaProcessingJobAdminRow(
                    job.Id,
                    job.MediaAssetId,
                    job.Status,
                    job.AttemptCount,
                    job.MaxAttempts,
                    job.FailureCode,
                    job.FailureMessage,
                    job.AvailableAfterUtc,
                    job.UpdatedAtUtc))
                .ToListAsync(cancellationToken);

            return new MediaSourcesAdminPage
            {
                Sources = sources,
                PendingJobs = jobCounts?.Pending ?? 0,
                RetryingJobs = jobCounts?.Retrying ?? 0,
                RunningJobs = jobCounts?.Running ?? 0,
                CompletedJobs = jobCounts?.Completed ?? 0,
                SourceUnavailableJobs = jobCounts?.SourceUnavailable ?? 0,
                FailedJobs = jobCounts?.Failed ?? 0,
                DeadLetterJobs = deadLetterJobs,
                UnavailableAssetCount = availabilityStatus.UnavailableAssets,
                HistoricalAvailabilityCandidates = availabilityStatus.HistoricalCandidates,
                LastAvailabilityCheckUtc = availabilityStatus.LastAvailabilityCheckUtc,
                OldestPendingAtUtc = oldestPendingAtUtc,
                ProcessingRuntime = _processingRuntime.GetSnapshot(),
                CacheHealth = await _cacheHealthService.CheckAsync(cancellationToken),
                RecentProblemJobs = recentProblemJobs,
                UnavailableAssets = unavailableAssets,
                UnavailableStatusCounts = statusCounts,
                UnavailablePage = unavailablePage,
                UnavailableTotalPages = unavailableTotalPages,
                UnavailablePageSize = pageSize,
                CatalogueAvailable = true,
                CatalogueSchemaCurrent = schema.IsCurrent,
                CatalogueMigrationHistoryConsistent = schema.MigrationHistoryConsistent,
                CatalogueDiagnosticReference = schema.DiagnosticReference,
                ExternalSourcesEnabled = _options.IsExternalSourceFeatureEnabled,
                PendingMigrations = schema.PendingMigrations,
                CatalogueError = schema.Error,
                CatalogueHealth = await _catalogueHealthService.CheckAsync(cancellationToken),
                CatalogueDiagnostics = _catalogueDiagnostics.GetLatest(),
                PrismAssetCount = prismAssetCount,
                PrismCatalogueRecordCount = consistency.CatalogueRecords,
                PrismUnavailableCatalogueCount = consistency.UnavailableCatalogueRecords,
                PrismOrphanedCatalogueCount = consistency.OrphanedCatalogueRecords,
                PrismSourceRecordCount = consistency.PrismSourceRecords,
                ActivitySourcePhotoCount = ingestion.ActivitySourcePhotoCount,
                ActivityCataloguePhotoCount = activityCataloguePhotoCount,
                ActivityCatalogueRepresentationCount = activityCatalogueRepresentationCount,
                ActivityUnavailableCataloguePhotoCount = activityCatalogueRepresentationCount - activityCataloguePhotoCount,
                PendingIngestionEvents = ingestion.Pending,
                ProcessingIngestionEvents = ingestion.Processing,
                DeadLetterIngestionEvents = ingestion.DeadLetter,
                RetryableIngestionEvents = ingestion.Retryable,
                OutboxSchemaAvailable = ingestion.SchemaAvailable,
                OutboxSchemaWarning = ingestion.SchemaWarning,
                OldestPendingIngestionAtUtc = ingestion.OldestPendingAtUtc,
                LastIngestionError = ingestion.LastError,
                OutboxRuntime = ingestion.Runtime,
                MissingFromCatalogue = consistency.MissingFromCatalogue
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is NpgsqlException or DbUpdateException or InvalidOperationException or TimeoutException)
        {
            var reference = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
            _logger.LogError(ex, "Media Admin page load failed. Reference {Reference}.", reference);
            return new MediaSourcesAdminPage
            {
                ExternalSourcesEnabled = _options.IsExternalSourceFeatureEnabled,
                CatalogueAvailable = false,
                CatalogueSchemaCurrent = schema.IsCurrent,
                CatalogueMigrationHistoryConsistent = schema.MigrationHistoryConsistent,
                CatalogueDiagnosticReference = reference,
                PendingMigrations = schema.PendingMigrations,
                CatalogueError = $"The media catalogue could not be loaded. Reference {reference}.",
                ProcessingRuntime = _processingRuntime.GetSnapshot(),
                CatalogueDiagnostics = _catalogueDiagnostics.GetLatest(),
                UnavailablePage = 1,
                UnavailablePageSize = pageSize,
                PrismSourceRecordCount = ingestion.PrismSourceRecordCount,
                ActivitySourcePhotoCount = ingestion.ActivitySourcePhotoCount,
                PendingIngestionEvents = ingestion.Pending,
                ProcessingIngestionEvents = ingestion.Processing,
                DeadLetterIngestionEvents = ingestion.DeadLetter,
                RetryableIngestionEvents = ingestion.Retryable,
                OutboxSchemaAvailable = ingestion.SchemaAvailable,
                OutboxSchemaWarning = ingestion.SchemaWarning,
                OldestPendingIngestionAtUtc = ingestion.OldestPendingAtUtc,
                LastIngestionError = ingestion.LastError,
                OutboxRuntime = ingestion.Runtime
            };
        }
    }

    private async Task<IngestionStatus> LoadIngestionStatusAsync(CancellationToken cancellationToken)
    {
        var runtime = _outboxRuntimeState.GetSnapshot();
        var snapshot = await _sourceSnapshot.GetSnapshotAsync(cancellationToken);

        try
        {
            var aggregate = await _applicationDb.PrismMediaOutboxMessages
                .AsNoTracking()
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    Pending = group.Count(message => message.Status == PrismMediaOutboxStatus.Pending),
                    Processing = group.Count(message => message.Status == PrismMediaOutboxStatus.Processing),
                    DeadLetter = group.Count(message => message.Status == PrismMediaOutboxStatus.DeadLetter),
                    Retryable = group.Count(message => message.Status == PrismMediaOutboxStatus.DeadLetter
                                                       || (message.Status == PrismMediaOutboxStatus.Pending && message.LastError != null)),
                    OldestPending = group.Where(message => message.Status == PrismMediaOutboxStatus.Pending)
                        .Min(message => (DateTimeOffset?)message.OccurredAtUtc)
                })
                .SingleOrDefaultAsync(cancellationToken);

            var lastError = await _applicationDb.PrismMediaOutboxMessages
                .AsNoTracking()
                .Where(message => message.LastError != null)
                .OrderByDescending(message => message.ProcessingStartedAtUtc ?? message.OccurredAtUtc)
                .Select(message => message.LastError)
                .FirstOrDefaultAsync(cancellationToken);

            return new IngestionStatus(
                snapshot.TotalCount,
                snapshot.ActivityPhotoCount,
                aggregate?.Pending ?? 0,
                aggregate?.Processing ?? 0,
                aggregate?.DeadLetter ?? 0,
                aggregate?.Retryable ?? 0,
                true,
                null,
                aggregate?.OldestPending,
                lastError,
                runtime);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return new IngestionStatus(
                snapshot.TotalCount,
                snapshot.ActivityPhotoCount,
                0,
                0,
                0,
                0,
                false,
                "The PRISM media outbox table is missing. Apply the Phase 9G application migration before enabling Activity media ingestion.",
                null,
                ex.MessageText,
                runtime);
        }
    }

    private sealed record IngestionStatus(
        int PrismSourceRecordCount,
        int ActivitySourcePhotoCount,
        int Pending,
        int Processing,
        int DeadLetter,
        int Retryable,
        bool SchemaAvailable,
        string? SchemaWarning,
        DateTimeOffset? OldestPendingAtUtc,
        string? LastError,
        PrismMediaOutboxRuntimeSnapshot Runtime);
}
