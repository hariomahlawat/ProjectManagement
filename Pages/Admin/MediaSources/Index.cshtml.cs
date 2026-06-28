using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Pages.Admin.MediaSources;

[Authorize(Roles = "Admin,HoD")]
public sealed class IndexModel : PageModel
{
    private readonly MediaLibraryDbContext _db;
    private readonly MediaLibraryOptions _options;
    private readonly IFileSystemSourceHealthService _healthService;
    private readonly IFileSystemPathResolver _pathResolver;
    private readonly IMediaLibrarySchemaService _schemaService;
    private readonly IMediaLibraryHealthService _catalogueHealthService;
    private readonly IMediaLibraryDiagnostics _catalogueDiagnostics;
    private readonly IMediaCatalogueConsistencyService _consistencyService;
    private readonly IPrismMediaCatalogueSynchronizer _prismSynchronizer;
    private readonly IMediaProcessingRuntimeState _processingRuntime;
    private readonly IMediaCacheHealthService _cacheHealthService;
    private readonly IMediaAvailabilityRecoveryService _availabilityRecoveryService;

    public IndexModel(
        MediaLibraryDbContext db,
        IOptions<MediaLibraryOptions> options,
        IFileSystemSourceHealthService healthService,
        IFileSystemPathResolver pathResolver,
        IMediaLibrarySchemaService schemaService,
        IMediaLibraryHealthService catalogueHealthService,
        IMediaLibraryDiagnostics catalogueDiagnostics,
        IMediaCatalogueConsistencyService consistencyService,
        IPrismMediaCatalogueSynchronizer prismSynchronizer,
        IMediaProcessingRuntimeState processingRuntime,
        IMediaCacheHealthService cacheHealthService,
        IMediaAvailabilityRecoveryService availabilityRecoveryService)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _healthService = healthService ?? throw new ArgumentNullException(nameof(healthService));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _schemaService = schemaService ?? throw new ArgumentNullException(nameof(schemaService));
        _catalogueHealthService = catalogueHealthService ?? throw new ArgumentNullException(nameof(catalogueHealthService));
        _catalogueDiagnostics = catalogueDiagnostics ?? throw new ArgumentNullException(nameof(catalogueDiagnostics));
        _consistencyService = consistencyService ?? throw new ArgumentNullException(nameof(consistencyService));
        _prismSynchronizer = prismSynchronizer ?? throw new ArgumentNullException(nameof(prismSynchronizer));
        _processingRuntime = processingRuntime ?? throw new ArgumentNullException(nameof(processingRuntime));
        _cacheHealthService = cacheHealthService ?? throw new ArgumentNullException(nameof(cacheHealthService));
        _availabilityRecoveryService = availabilityRecoveryService ?? throw new ArgumentNullException(nameof(availabilityRecoveryService));
    }

    [BindProperty]
    public SourceInput Input { get; set; } = new();

    public IReadOnlyList<SourceRow> Sources { get; private set; } = Array.Empty<SourceRow>();
    public int PendingJobs { get; private set; }
    public int RunningJobs { get; private set; }
    public int RetryingJobs { get; private set; }
    public int CompletedJobs { get; private set; }
    public int FailedJobs { get; private set; }
    public int DeadLetterJobs { get; private set; }
    public int UnavailableAssetCount { get; private set; }
    public int HistoricalAvailabilityCandidates { get; private set; }
    public DateTimeOffset? LastAvailabilityCheckUtc { get; private set; }
    public DateTimeOffset? OldestPendingAtUtc { get; private set; }
    public MediaProcessingRuntimeSnapshot ProcessingRuntime { get; private set; } = new(false, false, "Unknown", string.Empty, null, null, null, null, null, null, null, 0, 0, null, null);
    public MediaCacheHealthResult? CacheHealth { get; private set; }
    public IReadOnlyList<ProcessingJobRow> RecentProblemJobs { get; private set; } = Array.Empty<ProcessingJobRow>();
    public IReadOnlyList<UnavailableAssetRow> UnavailableAssets { get; private set; } = Array.Empty<UnavailableAssetRow>();
    public bool CatalogueAvailable { get; private set; } = true;
    public bool CatalogueSchemaCurrent { get; private set; } = true;
    public bool CatalogueMigrationHistoryConsistent { get; private set; } = true;
    public string? CatalogueDiagnosticReference { get; private set; }
    public bool ExternalSourcesEnabled => _options.IsExternalSourceFeatureEnabled;
    public bool IsEditing => Input.Id.HasValue;
    public IReadOnlyList<string> PendingMigrations { get; private set; } = Array.Empty<string>();
    public string? CatalogueError { get; private set; }
    public MediaLibraryHealthReport? CatalogueHealth { get; private set; }
    public IReadOnlyList<MediaLibraryDiagnosticEvent> CatalogueDiagnostics { get; private set; } = Array.Empty<MediaLibraryDiagnosticEvent>();
    public int ExternalSourceCount => Sources.Count(source => source.Type == MediaLibrarySourceType.FileSystem);
    public long PrismAssetCount { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? WarningMessage { get; set; }

    public async Task OnGetAsync(Guid? edit, CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        if (!CatalogueAvailable || !edit.HasValue)
        {
            return;
        }

        var source = await _db.Sources.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == edit.Value, cancellationToken);
        if (source is null || source.SourceType != MediaLibrarySourceType.FileSystem)
        {
            WarningMessage = "The requested external source was not found.";
            return;
        }

        Input = SourceInput.FromEntity(source);
    }


    public async Task<IActionResult> OnPostInitializeCatalogueAsync(CancellationToken cancellationToken)
    {
        var result = await _schemaService.MigrateAsync(cancellationToken);
        if (!result.IsOperational)
        {
            WarningMessage = null;
            return RedirectToPage();
        }

        StatusMessage = result.IsCurrent
            ? "The media catalogue schema is current. PRISM reconciliation and background processing can run."
            : "The media catalogue is operational. One or more migration metadata items still require administrative attention.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestCatalogueAsync(CancellationToken cancellationToken)
    {
        var report = await _catalogueHealthService.CheckAsync(cancellationToken);
        if (report.IsOperational && report.FacetsHealthy)
        {
            StatusMessage = $"Catalogue test passed. Timeline and facets are healthy; {report.IndexedAssets:N0} assets are indexed.";
        }
        else if (report.TimelineQueryHealthy)
        {
            WarningMessage = "The catalogue timeline is operational, but one or more optional facets are degraded. Review the diagnostics panel.";
        }
        else
        {
            WarningMessage = "The unified catalogue timeline test failed. Review the diagnostics panel and application logs before enabling further media-intelligence features.";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSynchronizePrismAsync(CancellationToken cancellationToken)
    {
        await _prismSynchronizer.SynchronizeAsync(cancellationToken);
        StatusMessage = "PRISM media catalogue synchronization completed.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCheckConsistencyAsync(CancellationToken cancellationToken)
    {
        var report = await _consistencyService.CheckAsync(cancellationToken);
        if (report.IsConsistent)
        {
            StatusMessage = $"Catalogue consistency check passed: {report.PrismSourceRecords:N0} PRISM source records and {report.CatalogueRecords:N0} available catalogue records.";
        }
        else
        {
            WarningMessage = $"Catalogue consistency check found {report.MissingFromCatalogue:N0} missing and {report.OrphanedCatalogueRecords:N0} orphaned record(s). Run Synchronize PRISM now, then check again.";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        if (!ExternalSourcesEnabled)
        {
            WarningMessage = "External folders are disabled in production configuration.";
            return RedirectToPage();
        }

        NormalizeInput(Input);
        ValidateInput(Input);
        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        var normalizedKey = MediaSourceBootstrapper.NormalizeKey(
            string.IsNullOrWhiteSpace(Input.Key) ? Input.Name : Input.Key);
        var duplicate = await _db.Sources.AnyAsync(source =>
            source.Key == normalizedKey
            && (!Input.Id.HasValue || source.Id != Input.Id.Value), cancellationToken);
        if (duplicate)
        {
            ModelState.AddModelError("Input.Key", "Another media source already uses this key.");
            await LoadAsync(cancellationToken);
            return Page();
        }

        var existingRoots = await _db.Sources
            .AsNoTracking()
            .Where(source => source.SourceType == MediaLibrarySourceType.FileSystem
                             && !source.IsDeleted
                             && source.RootPath != null
                             && (!Input.Id.HasValue || source.Id != Input.Id.Value))
            .Select(source => source.RootPath!)
            .ToListAsync(cancellationToken);
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (existingRoots.Any(root => string.Equals(root, Input.RootPath, pathComparison)))
        {
            ModelState.AddModelError("Input.RootPath", "This folder is already connected as another media source.");
            await LoadAsync(cancellationToken);
            return Page();
        }

        MediaLibrarySource entity;
        if (Input.Id.HasValue)
        {
            entity = await _db.Sources.SingleOrDefaultAsync(source => source.Id == Input.Id.Value, cancellationToken)
                     ?? throw new InvalidOperationException("The media source no longer exists.");
            if (entity.SourceType != MediaLibrarySourceType.FileSystem || entity.IsConfigurationManaged)
            {
                return Forbid();
            }
        }
        else
        {
            entity = new MediaLibrarySource
            {
                Id = Guid.NewGuid(),
                SourceType = MediaLibrarySourceType.FileSystem,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                ScanStatus = "Never",
                HealthStatus = "Unknown"
            };
            _db.Sources.Add(entity);
        }

        var extensions = ParseExtensions(Input.AllowedExtensions);
        entity.Key = normalizedKey;
        entity.Name = Input.Name.Trim();
        entity.RootPath = Input.RootPath;
        entity.IsEnabled = Input.IsEnabled;
        entity.IsVisibleInLibrary = Input.IsVisibleInLibrary;
        entity.IsReadOnly = true;
        entity.IncludeSubfolders = Input.IncludeSubfolders;
        entity.ScanIntervalMinutes = Input.ScanIntervalMinutes;
        entity.AllowedExtensionsJson = JsonSerializer.Serialize(extensions);
        entity.IsConfigurationManaged = false;
        entity.IsDeleted = false;
        entity.DisconnectedAtUtc = null;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;

        var health = await _healthService.TestAsync(
            entity.RootPath,
            entity.IncludeSubfolders,
            extensions,
            cancellationToken);
        ApplyHealth(entity, health);

        if (entity.IsEnabled)
        {
            entity.ScanRequestedAtUtc = DateTimeOffset.UtcNow;
            entity.ScanStatus = health.IsReachable ? "Queued" : "Waiting for source";
        }

        await _db.SaveChangesAsync(cancellationToken);
        StatusMessage = health.IsReachable
            ? $"{entity.Name} was saved and a scan was queued."
            : $"{entity.Name} was saved. The folder is currently unavailable and will be retried.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestAsync(Guid? id, CancellationToken cancellationToken)
    {
        if (!ExternalSourcesEnabled)
        {
            WarningMessage = "External folders are disabled in production configuration.";
            return RedirectToPage();
        }

        string path;
        bool includeSubfolders;
        string[] extensions;
        MediaLibrarySource? source = null;

        if (id.HasValue)
        {
            source = await _db.Sources.SingleOrDefaultAsync(item => item.Id == id.Value, cancellationToken);
            if (source is null || source.SourceType != MediaLibrarySourceType.FileSystem)
            {
                return NotFound();
            }

            path = source.RootPath ?? string.Empty;
            includeSubfolders = source.IncludeSubfolders;
            extensions = ParseExtensions(source.AllowedExtensionsJson, json: true);
        }
        else
        {
            NormalizeInput(Input);
            ValidateInput(Input, validateName: false);
            if (!ModelState.IsValid)
            {
                await LoadAsync(cancellationToken);
                return Page();
            }

            path = Input.RootPath;
            includeSubfolders = Input.IncludeSubfolders;
            extensions = ParseExtensions(Input.AllowedExtensions);
        }

        var health = await _healthService.TestAsync(path, includeSubfolders, extensions, cancellationToken);
        if (source is not null)
        {
            ApplyHealth(source, health);
            source.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        if (health.IsReachable)
        {
            StatusMessage = $"Connection successful ({health.PathKind}). {health.Message}";
        }
        else
        {
            WarningMessage = $"Connection failed ({health.PathKind}). {health.Message}";
        }

        if (id.HasValue)
        {
            return RedirectToPage(new { edit = id.Value });
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostScanAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!ExternalSourcesEnabled)
        {
            WarningMessage = "External folders are disabled in production configuration.";
            return RedirectToPage();
        }

        var source = await _db.Sources.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (source is null)
        {
            return NotFound();
        }

        if (source.SourceType != MediaLibrarySourceType.FileSystem || source.IsDeleted)
        {
            return BadRequest();
        }

        if (!source.IsEnabled)
        {
            WarningMessage = "Enable the source before requesting a scan.";
            return RedirectToPage();
        }

        source.ScanRequestedAtUtc = DateTimeOffset.UtcNow;
        source.ScanStatus = "Queued";
        source.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        StatusMessage = $"A scan has been queued for {source.Name}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSetStateAsync(
        Guid id,
        bool enabled,
        bool visible,
        CancellationToken cancellationToken)
    {
        if (!ExternalSourcesEnabled)
        {
            WarningMessage = "External folders are disabled in production configuration.";
            return RedirectToPage();
        }

        var source = await _db.Sources.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (source is null)
        {
            return NotFound();
        }

        if (source.SourceType != MediaLibrarySourceType.FileSystem || source.IsConfigurationManaged)
        {
            return Forbid();
        }

        source.IsEnabled = enabled;
        source.IsVisibleInLibrary = visible;
        source.ScanStatus = enabled ? "Queued" : "Disabled";
        source.ScanRequestedAtUtc = enabled ? DateTimeOffset.UtcNow : null;
        source.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        StatusMessage = $"{source.Name} was updated.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDisconnectAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!ExternalSourcesEnabled)
        {
            WarningMessage = "External folders are disabled in production configuration.";
            return RedirectToPage();
        }

        var source = await _db.Sources.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (source is null)
        {
            return NotFound();
        }

        if (source.SourceType != MediaLibrarySourceType.FileSystem || source.IsConfigurationManaged)
        {
            return Forbid();
        }

        source.IsEnabled = false;
        source.IsVisibleInLibrary = false;
        source.IsDeleted = true;
        source.DisconnectedAtUtc = DateTimeOffset.UtcNow;
        source.ScanRequestedAtUtc = null;
        source.ScanStatus = "Disconnected";
        source.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        StatusMessage = $"{source.Name} was disconnected. No original file was changed or deleted.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRetryFailedAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var count = await _db.ProcessingJobs
            .Where(job => (job.Status == MediaProcessingJobStatus.Failed
                           || job.Status == MediaProcessingJobStatus.DeadLetter)
                          && job.FailureCode != null
                          && MediaProcessingFailurePolicy.RecoverableFailureCodeNames.Contains(job.FailureCode))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.Status, MediaProcessingJobStatus.Pending)
                .SetProperty(job => job.AttemptCount, 0)
                .SetProperty(job => job.AvailableAfterUtc, now)
                .SetProperty(job => job.StartedAtUtc, (DateTimeOffset?)null)
                .SetProperty(job => job.CompletedAtUtc, (DateTimeOffset?)null)
                .SetProperty(job => job.LockedBy, (string?)null)
                .SetProperty(job => job.LockExpiresAtUtc, (DateTimeOffset?)null)
                .SetProperty(job => job.FailureCode, (string?)null)
                .SetProperty(job => job.FailureMessage, (string?)null)
                .SetProperty(job => job.UpdatedAtUtc, now), cancellationToken);

        StatusMessage = count == 0
            ? "No recoverable failed media jobs required retry. Permanent missing-content failures were left unchanged."
            : $"{count} recoverable media processing job(s) were queued again.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRetryPermanentAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var jobs = await _db.ProcessingJobs
            .Include(job => job.MediaAsset)
            .Where(job => (job.Status == MediaProcessingJobStatus.Failed
                           || job.Status == MediaProcessingJobStatus.DeadLetter)
                          && job.MediaAsset.IsAvailable
                          && job.FailureCode != null
                          && MediaProcessingFailurePolicy.PermanentFailureCodeNames.Contains(job.FailureCode))
            .ToListAsync(cancellationToken);

        foreach (var job in jobs)
        {
            job.Status = MediaProcessingJobStatus.Pending;
            job.AttemptCount = 0;
            job.AvailableAfterUtc = now;
            job.StartedAtUtc = null;
            job.CompletedAtUtc = null;
            job.LockedBy = null;
            job.LockExpiresAtUtc = null;
            job.FailureCode = null;
            job.FailureMessage = null;
            job.UpdatedAtUtc = now;

            job.MediaAsset.IsAvailable = true;
            job.MediaAsset.AvailabilityStatus = MediaAvailabilityStatus.Available;
            job.MediaAsset.UnavailableReason = null;
            job.MediaAsset.UnavailableSinceUtc = null;
            job.MediaAsset.LastAvailabilityCheckUtc = now;
            job.MediaAsset.DerivativeStatus = MediaProcessingStatus.Pending;
            job.MediaAsset.AnalysisStatus = MediaProcessingStatus.Pending;
            job.MediaAsset.ProcessingFailureReason = null;
        }

        await _db.SaveChangesAsync(cancellationToken);
        StatusMessage = jobs.Count == 0
            ? "No permanent media failures were available for forced retry."
            : $"{jobs.Count} permanent media failure(s) were force-queued. Use this only after restoring the underlying files.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRetryJobAsync(long id, bool forcePermanent = false, CancellationToken cancellationToken = default)
    {
        var job = await _db.ProcessingJobs
            .Include(item => item.MediaAsset)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (job is null) return NotFound();

        if (!job.MediaAsset.IsAvailable)
        {
            WarningMessage = "This media item is marked unavailable. Restore the underlying source and use Recheck in the Unavailable media section.";
            return RedirectToPage();
        }

        if (MediaProcessingFailurePolicy.IsPermanentFailureCode(job.FailureCode) && !forcePermanent)
        {
            WarningMessage = "This job represents permanently unsupported content. Use force retry only after correcting the source.";
            return RedirectToPage();
        }

        var now = DateTimeOffset.UtcNow;
        job.Status = MediaProcessingJobStatus.Pending;
        job.AttemptCount = 0;
        job.AvailableAfterUtc = now;
        job.StartedAtUtc = null;
        job.CompletedAtUtc = null;
        job.LockedBy = null;
        job.LockExpiresAtUtc = null;
        job.FailureCode = null;
        job.FailureMessage = null;
        job.UpdatedAtUtc = now;

        if (forcePermanent)
        {
            job.MediaAsset.IsAvailable = true;
            job.MediaAsset.AvailabilityStatus = MediaAvailabilityStatus.Available;
            job.MediaAsset.UnavailableReason = null;
            job.MediaAsset.UnavailableSinceUtc = null;
            job.MediaAsset.LastAvailabilityCheckUtc = now;
            job.MediaAsset.DerivativeStatus = MediaProcessingStatus.Pending;
            job.MediaAsset.AnalysisStatus = MediaProcessingStatus.Pending;
            job.MediaAsset.ProcessingFailureReason = null;
        }

        await _db.SaveChangesAsync(cancellationToken);
        StatusMessage = $"Media processing job {id} was queued again.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostReconcileAvailabilityAsync(CancellationToken cancellationToken)
    {
        var result = await _availabilityRecoveryService.ReconcileHistoricalAsync(250, cancellationToken);
        StatusMessage = result.Examined == 0
            ? "No historical missing-media records required reconciliation."
            : $"Availability reconciliation examined {result.Examined:N0} item(s): {result.Restored:N0} restored, "
              + $"{result.MarkedUnavailable:N0} confirmed unavailable, {result.TemporarilyUnavailable:N0} temporarily unavailable, "
              + $"{result.Errors:N0} error(s).";

        if (result.HasMore)
        {
            WarningMessage = "Additional historical items remain. Background reconciliation will continue automatically.";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRecheckUnavailableAsync(CancellationToken cancellationToken)
    {
        var result = await _availabilityRecoveryService.RecheckAsync(null, 100, cancellationToken);
        StatusMessage = result.Examined == 0
            ? "No unavailable media required rechecking."
            : $"Rechecked {result.Examined:N0} unavailable item(s): {result.Restored:N0} restored, {result.StillUnavailable:N0} still unavailable, {result.Errors:N0} error(s)."
              + (result.HasMore ? " More unavailable items remain; run the check again." : string.Empty);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRecheckUnavailableAssetAsync(long id, CancellationToken cancellationToken)
    {
        var result = await _availabilityRecoveryService.RecheckAsync(id, 1, cancellationToken);
        if (result.Examined == 0)
        {
            WarningMessage = "The requested unavailable media item was not found or has already been restored.";
        }
        else if (result.Restored == 1)
        {
            StatusMessage = $"Media asset {id} is available again and was queued for processing.";
        }
        else if (result.Errors > 0)
        {
            WarningMessage = $"Media asset {id} could not be checked. Review application logs for details.";
        }
        else
        {
            WarningMessage = $"Media asset {id} is still unavailable. Restore the underlying file before rechecking.";
        }

        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var schema = await _schemaService.GetStatusAsync(cancellationToken);
        PendingMigrations = schema.PendingMigrations;
        CatalogueError = schema.Error;
        CatalogueDiagnosticReference = schema.DiagnosticReference;
        CatalogueSchemaCurrent = schema.IsCurrent;
        CatalogueMigrationHistoryConsistent = schema.MigrationHistoryConsistent;
        CatalogueAvailable = schema.IsAvailable && schema.IsOperational;
        if (!CatalogueAvailable)
        {
            Sources = Array.Empty<SourceRow>();
            PendingJobs = 0;
            RunningJobs = 0;
            RetryingJobs = 0;
            CompletedJobs = 0;
            FailedJobs = 0;
            DeadLetterJobs = 0;
            PrismAssetCount = 0;
            UnavailableAssetCount = 0;
            HistoricalAvailabilityCandidates = 0;
            LastAvailabilityCheckUtc = null;
            UnavailableAssets = Array.Empty<UnavailableAssetRow>();
            ProcessingRuntime = _processingRuntime.GetSnapshot();
            CatalogueDiagnostics = _catalogueDiagnostics.GetLatest();
            return;
        }

        try
        {
            Sources = await _db.Sources
                .AsNoTracking()
                .Where(source => !source.IsDeleted)
                .OrderBy(source => source.SourceType)
                .ThenBy(source => source.Name)
                .Select(source => new SourceRow(
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
                    source.LastError))
                .ToListAsync(cancellationToken);

            PrismAssetCount = await _db.Assets.LongCountAsync(
                asset => asset.Source.SourceType == MediaLibrarySourceType.Prism
                         && asset.IsAvailable
                         && asset.AvailabilityStatus == MediaAvailabilityStatus.Available
                         && !asset.IsDeleted,
                cancellationToken);

            var now = DateTimeOffset.UtcNow;
            PendingJobs = await _db.ProcessingJobs.CountAsync(
                job => job.Status == MediaProcessingJobStatus.Pending
                       && job.AttemptCount == 0,
                cancellationToken);
            RetryingJobs = await _db.ProcessingJobs.CountAsync(
                job => job.Status == MediaProcessingJobStatus.Pending
                       && job.AttemptCount > 0,
                cancellationToken);
            RunningJobs = await _db.ProcessingJobs.CountAsync(
                job => job.Status == MediaProcessingJobStatus.Running,
                cancellationToken);
            CompletedJobs = await _db.ProcessingJobs.CountAsync(
                job => job.Status == MediaProcessingJobStatus.Completed,
                cancellationToken);
            FailedJobs = await _db.ProcessingJobs.CountAsync(
                job => job.Status == MediaProcessingJobStatus.Failed,
                cancellationToken);
            var unavailableFailureCodes = new[]
            {
                nameof(MediaContentUnavailableException),
                nameof(FileNotFoundException),
                nameof(DirectoryNotFoundException)
            };
            DeadLetterJobs = await _db.ProcessingJobs.CountAsync(
                job => job.Status == MediaProcessingJobStatus.DeadLetter
                       && job.MediaAsset.IsAvailable
                       && job.MediaAsset.AvailabilityStatus == MediaAvailabilityStatus.Available
                       && (job.FailureCode == null || !unavailableFailureCodes.Contains(job.FailureCode)),
                cancellationToken);
            var availabilityStatus = await _availabilityRecoveryService.GetStatusAsync(cancellationToken);
            UnavailableAssetCount = availabilityStatus.UnavailableAssets;
            HistoricalAvailabilityCandidates = availabilityStatus.HistoricalCandidates;
            LastAvailabilityCheckUtc = availabilityStatus.LastAvailabilityCheckUtc;
            var unavailableRows = await _db.Assets
                .AsNoTracking()
                .Where(asset => !asset.IsDeleted
                                && asset.AvailabilityStatus != MediaAvailabilityStatus.Available)
                .OrderByDescending(asset => asset.LastSeenAtUtc)
                .ThenBy(asset => asset.Id)
                .Take(50)
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
            UnavailableAssets = unavailableRows
                .Select(asset => new UnavailableAssetRow(
                    asset.Id,
                    asset.Origin,
                    asset.ContextTitle,
                    asset.OriginalFileName,
                    asset.SourceLabel,
                    asset.LastSeenAtUtc,
                    asset.AvailabilityStatus,
                    asset.UnavailableSinceUtc,
                    asset.LastAvailabilityCheckUtc,
                    asset.UnavailableReason ?? "The source media is unavailable."))
                .ToList();
            OldestPendingAtUtc = await _db.ProcessingJobs
                .Where(job => job.Status == MediaProcessingJobStatus.Pending)
                .OrderBy(job => job.CreatedAtUtc)
                .Select(job => (DateTimeOffset?)job.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            RecentProblemJobs = await _db.ProcessingJobs
                .AsNoTracking()
                .Where(job => job.MediaAsset.IsAvailable
                              && (job.FailureCode == null || !unavailableFailureCodes.Contains(job.FailureCode))
                              && job.MediaAsset.AvailabilityStatus == MediaAvailabilityStatus.Available
                              && (job.Status == MediaProcessingJobStatus.DeadLetter
                                  || job.Status == MediaProcessingJobStatus.Failed
                                  || (job.Status == MediaProcessingJobStatus.Pending && job.AttemptCount > 0)))
                .OrderByDescending(job => job.UpdatedAtUtc)
                .Take(20)
                .Select(job => new ProcessingJobRow(
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

            ProcessingRuntime = _processingRuntime.GetSnapshot();
            CacheHealth = await _cacheHealthService.CheckAsync(cancellationToken);
            CatalogueHealth = await _catalogueHealthService.CheckAsync(cancellationToken);
            CatalogueDiagnostics = _catalogueDiagnostics.GetLatest();
        }
        catch (Exception ex) when (ex is NpgsqlException or DbUpdateException or InvalidOperationException or TimeoutException)
        {
            CatalogueAvailable = false;
            Sources = Array.Empty<SourceRow>();
            CatalogueError = "The media catalogue could not be loaded. Review application logs and verify that all media migrations have been applied.";
        }
    }

    private void ValidateInput(SourceInput input, bool validateName = true)
    {
        if (validateName && string.IsNullOrWhiteSpace(input.Name))
        {
            ModelState.AddModelError("Input.Name", "Name is required.");
        }

        if (string.IsNullOrWhiteSpace(input.RootPath))
        {
            ModelState.AddModelError("Input.RootPath", "Enter a fully-qualified local or UNC folder path.");
        }
        else
        {
            try
            {
                input.RootPath = _pathResolver.ResolveRoot(input.RootPath);
            }
            catch (Exception ex) when (ex is InvalidOperationException
                                       or ArgumentException
                                       or NotSupportedException
                                       or PathTooLongException)
            {
                ModelState.AddModelError("Input.RootPath", ex.Message);
            }
        }

        if (input.ScanIntervalMinutes is < 1 or > 10080)
        {
            ModelState.AddModelError("Input.ScanIntervalMinutes", "Scan interval must be between 1 and 10080 minutes.");
        }

        if (ParseExtensions(input.AllowedExtensions).Length == 0)
        {
            ModelState.AddModelError("Input.AllowedExtensions", "Provide at least one file extension.");
        }
    }

    private static void NormalizeInput(SourceInput input)
    {
        input.Name = input.Name?.Trim() ?? string.Empty;
        input.Key = input.Key?.Trim() ?? string.Empty;
        input.RootPath = input.RootPath?.Trim() ?? string.Empty;
        input.AllowedExtensions = input.AllowedExtensions?.Trim() ?? string.Empty;
    }

    private static string[] ParseExtensions(string value, bool json = false)
    {
        IEnumerable<string> values;
        if (json)
        {
            try
            {
                values = JsonSerializer.Deserialize<string[]>(value) ?? Array.Empty<string>();
            }
            catch (JsonException)
            {
                values = Array.Empty<string>();
            }
        }
        else
        {
            values = value.Split(new[] { ',', ';', '\r', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        return MediaSourceBootstrapper.NormalizeExtensions(values);
    }

    private static void ApplyHealth(MediaLibrarySource entity, FileSystemSourceHealth health)
    {
        entity.LastHealthCheckedAtUtc = health.CheckedAtUtc;
        entity.HealthStatus = health.IsReachable ? "Reachable" : "Unavailable";
        entity.HealthMessage = health.Message.Length <= 2048 ? health.Message : health.Message[..2048];
        if (health.IsReachable)
        {
            entity.LastError = null;
        }
    }

    public sealed record ProcessingJobRow(
        long Id,
        long MediaAssetId,
        MediaProcessingJobStatus Status,
        int AttemptCount,
        int MaxAttempts,
        string? FailureCode,
        string? FailureMessage,
        DateTimeOffset AvailableAfterUtc,
        DateTimeOffset UpdatedAtUtc);

    public sealed record UnavailableAssetRow(
        long Id,
        MediaAssetOrigin Origin,
        string ContextTitle,
        string OriginalFileName,
        string SourceLabel,
        DateTimeOffset LastSeenAtUtc,
        MediaAvailabilityStatus Status,
        DateTimeOffset? UnavailableSinceUtc,
        DateTimeOffset? LastCheckedUtc,
        string Reason);

    public sealed class SourceInput
    {
        public Guid? Id { get; set; }

        [MaxLength(160)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(64)]
        public string Key { get; set; } = string.Empty;

        [MaxLength(2048)]
        public string RootPath { get; set; } = string.Empty;

        public bool IsEnabled { get; set; } = true;
        public bool IsVisibleInLibrary { get; set; } = true;
        public bool IncludeSubfolders { get; set; } = true;
        public int ScanIntervalMinutes { get; set; } = 30;
        public string AllowedExtensions { get; set; } = string.Join(", ", MediaSourceDefaults.AllowedExtensions);

        public static SourceInput FromEntity(MediaLibrarySource source)
            => new()
            {
                Id = source.Id,
                Name = source.Name,
                Key = source.Key,
                RootPath = source.RootPath ?? string.Empty,
                IsEnabled = source.IsEnabled,
                IsVisibleInLibrary = source.IsVisibleInLibrary,
                IncludeSubfolders = source.IncludeSubfolders,
                ScanIntervalMinutes = source.ScanIntervalMinutes,
                AllowedExtensions = string.Join(", ", IndexModel.ParseExtensions(source.AllowedExtensionsJson, json: true))
            };
    }

    public sealed record SourceRow(
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
        string? LastError);
}
