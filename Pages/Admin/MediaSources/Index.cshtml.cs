using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Features.MediaLibrary.Admin;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Outbox;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Pages.Admin.MediaSources;

[Authorize(Policy = AdminPolicies.MediaView)]
public sealed class IndexModel : PageModel
{
    private readonly IMediaSourcesAdminQueryService _queryService;
    private readonly IMediaSourceAdminService _sourceService;
    private readonly IMediaQueueAdminService _queueService;
    private readonly IMediaRecoveryAdminService _recoveryService;

    public IndexModel(
        IMediaSourcesAdminQueryService queryService,
        IMediaSourceAdminService sourceService,
        IMediaQueueAdminService queueService,
        IMediaRecoveryAdminService recoveryService)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _sourceService = sourceService ?? throw new ArgumentNullException(nameof(sourceService));
        _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
        _recoveryService = recoveryService ?? throw new ArgumentNullException(nameof(recoveryService));
    }

    [BindProperty]
    public MediaSourceAdminInput Input { get; set; } = new();

    public IReadOnlyList<MediaSourceAdminRow> Sources { get; private set; } = Array.Empty<MediaSourceAdminRow>();
    public int PendingJobs { get; private set; }
    public int RunningJobs { get; private set; }
    public int RetryingJobs { get; private set; }
    public int CompletedJobs { get; private set; }
    public int FailedJobs { get; private set; }
    public int DeadLetterJobs { get; private set; }
    public int SourceUnavailableJobs { get; private set; }
    public int UnavailableAssetCount { get; private set; }
    public int HistoricalAvailabilityCandidates { get; private set; }
    public DateTimeOffset? LastAvailabilityCheckUtc { get; private set; }
    public DateTimeOffset? OldestPendingAtUtc { get; private set; }
    public MediaProcessingRuntimeSnapshot ProcessingRuntime { get; private set; }
        = new(false, false, "Unknown", string.Empty, null, null, null, null, null, null, null, 0, 0, null, null);
    public MediaCacheHealthResult? CacheHealth { get; private set; }
    public IReadOnlyList<MediaProcessingJobAdminRow> RecentProblemJobs { get; private set; }
        = Array.Empty<MediaProcessingJobAdminRow>();
    public IReadOnlyList<UnavailableMediaAdminRow> UnavailableAssets { get; private set; }
        = Array.Empty<UnavailableMediaAdminRow>();
    public IReadOnlyDictionary<MediaAvailabilityStatus, int> UnavailableStatusCounts { get; private set; }
        = new Dictionary<MediaAvailabilityStatus, int>();
    public int UnavailableTotalPages { get; private set; }
    public int UnavailablePageSize { get; private set; } = 25;

    [BindProperty(SupportsGet = true, Name = "uq")]
    public string? UnavailableQuery { get; set; }

    [BindProperty(SupportsGet = true, Name = "us")]
    public MediaAvailabilityStatus? UnavailableStatusFilter { get; set; }

    [BindProperty(SupportsGet = true, Name = "up")]
    public int UnavailablePage { get; set; } = 1;

    public bool CatalogueAvailable { get; private set; } = true;
    public bool CatalogueSchemaCurrent { get; private set; } = true;
    public bool CatalogueMigrationHistoryConsistent { get; private set; } = true;
    public string? CatalogueDiagnosticReference { get; private set; }
    public bool ExternalSourcesEnabled { get; private set; }
    public bool IsEditing => Input.Id.HasValue;
    public IReadOnlyList<string> PendingMigrations { get; private set; } = Array.Empty<string>();
    public string? CatalogueError { get; private set; }
    public MediaLibraryHealthReport? CatalogueHealth { get; private set; }
    public IReadOnlyList<MediaLibraryDiagnosticEvent> CatalogueDiagnostics { get; private set; }
        = Array.Empty<MediaLibraryDiagnosticEvent>();
    public int ExternalSourceCount => Sources.Count(source => source.Type == MediaLibrarySourceType.FileSystem);
    public long PrismAssetCount { get; private set; }
    public int PrismCatalogueRecordCount { get; private set; }
    public int PrismUnavailableCatalogueCount { get; private set; }
    public int PrismOrphanedCatalogueCount { get; private set; }
    public int PrismSourceRecordCount { get; private set; }
    public int ActivitySourcePhotoCount { get; private set; }
    public long ActivityCataloguePhotoCount { get; private set; }
    public long ActivityCatalogueRepresentationCount { get; private set; }
    public long ActivityUnavailableCataloguePhotoCount { get; private set; }
    public int PendingIngestionEvents { get; private set; }
    public int ProcessingIngestionEvents { get; private set; }
    public int DeadLetterIngestionEvents { get; private set; }
    public int RetryableIngestionEvents { get; private set; }
    public bool OutboxSchemaAvailable { get; private set; } = true;
    public string? OutboxSchemaWarning { get; private set; }
    public DateTimeOffset? OldestPendingIngestionAtUtc { get; private set; }
    public string? LastIngestionError { get; private set; }
    public PrismMediaOutboxRuntimeSnapshot OutboxRuntime { get; private set; }
        = new(false, null, null, null, null, "Pending", null, null, null);
    public int MissingFromCatalogue { get; private set; }

    [TempData(Key = "Admin.MediaSources.Status")]
    public string? StatusMessage { get; set; }

    [TempData(Key = "Admin.MediaSources.Warning")]
    public string? WarningMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid? edit, CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        if (!CatalogueAvailable || !edit.HasValue)
        {
            return Page();
        }

        var result = await _sourceService.GetForEditAsync(edit.Value, cancellationToken);
        if (result.Succeeded && result.Value is not null)
        {
            Input = result.Value;
            return Page();
        }

        if (result.ErrorCode == MediaAdminErrorCodes.Forbidden)
        {
            return Forbid();
        }

        WarningMessage = result.UserMessage;
        return Page();
    }

    public async Task<IActionResult> OnPostTestCatalogueAsync(CancellationToken cancellationToken)
    {
        Apply(await _recoveryService.TestCatalogueAsync(cancellationToken));
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSynchronizePrismAsync(CancellationToken cancellationToken)
    {
        var result = await _recoveryService.SynchronizePrismAsync(cancellationToken);
        if (result.ErrorCode == MediaAdminErrorCodes.Forbidden) return Forbid();
        Apply(result);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCheckConsistencyAsync(CancellationToken cancellationToken)
    {
        Apply(await _recoveryService.CheckConsistencyAsync(cancellationToken));
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        var result = await _sourceService.SaveAsync(Input, cancellationToken);
        if (result.Succeeded)
        {
            StatusMessage = result.UserMessage;
            return RedirectToPage();
        }

        if (result.ErrorCode == MediaAdminErrorCodes.Forbidden) return Forbid();
        AddFieldErrors(result.FieldErrors);
        WarningMessage = result.UserMessage;

        if (result.ErrorCode is MediaAdminErrorCodes.InvalidInput or MediaAdminErrorCodes.DuplicateSource)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        return Input.Id.HasValue
            ? RedirectToPage(new { edit = Input.Id.Value })
            : RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestAsync(
        Guid? id,
        string? version,
        CancellationToken cancellationToken)
    {
        var result = await _sourceService.TestAsync(
            id,
            version,
            id.HasValue ? null : Input,
            cancellationToken);

        if (result.ErrorCode == MediaAdminErrorCodes.Forbidden) return Forbid();
        if (result.ErrorCode == MediaAdminErrorCodes.NotFound) return NotFound();

        if (result.Succeeded && result.Value is not null)
        {
            if (result.Value.IsReachable)
            {
                StatusMessage = result.UserMessage;
            }
            else
            {
                WarningMessage = result.UserMessage;
            }

            if (id.HasValue)
            {
                return RedirectToPage(new { edit = id.Value });
            }

            await LoadAsync(cancellationToken);
            return Page();
        }

        AddFieldErrors(result.FieldErrors);
        WarningMessage = result.UserMessage;
        if (id.HasValue)
        {
            return RedirectToPage(new { edit = id.Value });
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostScanAsync(
        Guid id,
        string version,
        CancellationToken cancellationToken)
    {
        return CompleteSourceCommand(
            await _sourceService.RequestScanAsync(id, version, cancellationToken),
            id);
    }

    public async Task<IActionResult> OnPostSetStateAsync(
        Guid id,
        string version,
        bool enabled,
        bool visible,
        CancellationToken cancellationToken)
    {
        return CompleteSourceCommand(
            await _sourceService.SetStateAsync(id, version, enabled, visible, cancellationToken),
            id);
    }

    public async Task<IActionResult> OnPostDisconnectAsync(
        Guid id,
        string version,
        CancellationToken cancellationToken)
    {
        return CompleteSourceCommand(
            await _sourceService.DisconnectAsync(id, version, cancellationToken),
            id);
    }

    public async Task<IActionResult> OnPostRetryFailedAsync(CancellationToken cancellationToken)
    {
        var result = await _queueService.RetryRecoverableAsync(250, cancellationToken);
        if (result.ErrorCode == MediaAdminErrorCodes.Forbidden) return Forbid();
        Apply(result);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRetryPermanentAsync(CancellationToken cancellationToken)
    {
        var result = await _queueService.RetryPermanentAsync(100, cancellationToken);
        if (result.ErrorCode == MediaAdminErrorCodes.Forbidden) return Forbid();
        Apply(result);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRetryJobAsync(
        long id,
        bool forcePermanent = false,
        CancellationToken cancellationToken = default)
    {
        var result = await _queueService.RetryJobAsync(id, forcePermanent, cancellationToken);
        if (result.ErrorCode == MediaAdminErrorCodes.Forbidden) return Forbid();
        if (result.ErrorCode == MediaAdminErrorCodes.NotFound) return NotFound();
        Apply(result);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostReconcileAvailabilityAsync(CancellationToken cancellationToken)
    {
        var result = await _recoveryService.ReconcileAvailabilityAsync(250, cancellationToken);
        if (result.ErrorCode == MediaAdminErrorCodes.Forbidden) return Forbid();
        Apply(result);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRecheckUnavailableAsync(CancellationToken cancellationToken)
    {
        var result = await _recoveryService.RecheckUnavailableAsync(100, cancellationToken);
        if (result.ErrorCode == MediaAdminErrorCodes.Forbidden) return Forbid();
        Apply(result);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRecheckUnavailableAssetAsync(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await _recoveryService.RecheckUnavailableAssetAsync(id, cancellationToken);
        if (result.ErrorCode == MediaAdminErrorCodes.Forbidden) return Forbid();
        Apply(result);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRetryIngestionAsync(CancellationToken cancellationToken)
    {
        var result = await _queueService.RetryIngestionAsync(250, cancellationToken);
        if (result.ErrorCode == MediaAdminErrorCodes.Forbidden) return Forbid();
        Apply(result);
        return RedirectToPage();
    }

    private IActionResult CompleteSourceCommand(
        MediaAdminCommandResult result,
        Guid sourceId)
    {
        if (result.ErrorCode == MediaAdminErrorCodes.Forbidden) return Forbid();
        if (result.ErrorCode == MediaAdminErrorCodes.NotFound) return NotFound();
        Apply(result);
        return result.ErrorCode == MediaAdminErrorCodes.ConcurrencyConflict
            ? RedirectToPage(new { edit = sourceId })
            : RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var state = await _queryService.GetPageAsync(
            new MediaSourcesAdminQuery(
                UnavailableQuery,
                UnavailableStatusFilter,
                UnavailablePage,
                25),
            cancellationToken);
        ApplyState(state);
    }

    private void ApplyState(MediaSourcesAdminPage state)
    {
        Sources = state.Sources;
        PendingJobs = state.PendingJobs;
        RunningJobs = state.RunningJobs;
        RetryingJobs = state.RetryingJobs;
        CompletedJobs = state.CompletedJobs;
        FailedJobs = state.FailedJobs;
        DeadLetterJobs = state.DeadLetterJobs;
        SourceUnavailableJobs = state.SourceUnavailableJobs;
        UnavailableAssetCount = state.UnavailableAssetCount;
        HistoricalAvailabilityCandidates = state.HistoricalAvailabilityCandidates;
        LastAvailabilityCheckUtc = state.LastAvailabilityCheckUtc;
        OldestPendingAtUtc = state.OldestPendingAtUtc;
        ProcessingRuntime = state.ProcessingRuntime;
        CacheHealth = state.CacheHealth;
        RecentProblemJobs = state.RecentProblemJobs;
        UnavailableAssets = state.UnavailableAssets;
        UnavailableStatusCounts = state.UnavailableStatusCounts;
        UnavailablePage = state.UnavailablePage;
        UnavailableTotalPages = state.UnavailableTotalPages;
        UnavailablePageSize = state.UnavailablePageSize;
        CatalogueAvailable = state.CatalogueAvailable;
        CatalogueSchemaCurrent = state.CatalogueSchemaCurrent;
        CatalogueMigrationHistoryConsistent = state.CatalogueMigrationHistoryConsistent;
        CatalogueDiagnosticReference = state.CatalogueDiagnosticReference;
        ExternalSourcesEnabled = state.ExternalSourcesEnabled;
        PendingMigrations = state.PendingMigrations;
        CatalogueError = state.CatalogueError;
        CatalogueHealth = state.CatalogueHealth;
        CatalogueDiagnostics = state.CatalogueDiagnostics;
        PrismAssetCount = state.PrismAssetCount;
        PrismCatalogueRecordCount = state.PrismCatalogueRecordCount;
        PrismUnavailableCatalogueCount = state.PrismUnavailableCatalogueCount;
        PrismOrphanedCatalogueCount = state.PrismOrphanedCatalogueCount;
        PrismSourceRecordCount = state.PrismSourceRecordCount;
        ActivitySourcePhotoCount = state.ActivitySourcePhotoCount;
        ActivityCataloguePhotoCount = state.ActivityCataloguePhotoCount;
        ActivityCatalogueRepresentationCount = state.ActivityCatalogueRepresentationCount;
        ActivityUnavailableCataloguePhotoCount = state.ActivityUnavailableCataloguePhotoCount;
        PendingIngestionEvents = state.PendingIngestionEvents;
        ProcessingIngestionEvents = state.ProcessingIngestionEvents;
        DeadLetterIngestionEvents = state.DeadLetterIngestionEvents;
        RetryableIngestionEvents = state.RetryableIngestionEvents;
        OutboxSchemaAvailable = state.OutboxSchemaAvailable;
        OutboxSchemaWarning = state.OutboxSchemaWarning;
        OldestPendingIngestionAtUtc = state.OldestPendingIngestionAtUtc;
        LastIngestionError = state.LastIngestionError;
        OutboxRuntime = state.OutboxRuntime;
        MissingFromCatalogue = state.MissingFromCatalogue;
    }

    private void Apply(MediaAdminCommandResult result)
    {
        if (result.Succeeded)
        {
            StatusMessage = result.UserMessage;
        }
        else
        {
            WarningMessage = result.UserMessage;
        }
    }

    private void Apply<T>(MediaAdminCommandResult<T> result)
    {
        if (result.Succeeded)
        {
            StatusMessage = result.UserMessage;
        }
        else
        {
            WarningMessage = result.UserMessage;
        }
    }

    private void AddFieldErrors(IReadOnlyDictionary<string, string[]>? errors)
    {
        if (errors is null) return;
        foreach (var (field, messages) in errors)
        {
            foreach (var message in messages)
            {
                ModelState.AddModelError(field, message);
            }
        }
    }

    public static string GetAvailabilityStatusLabel(MediaAvailabilityStatus status) =>
        MediaAdminDisplay.AvailabilityStatusLabel(status);

    public int GetUnavailableStatusCount(MediaAvailabilityStatus status) =>
        UnavailableStatusCounts.TryGetValue(status, out var count) ? count : 0;
}
