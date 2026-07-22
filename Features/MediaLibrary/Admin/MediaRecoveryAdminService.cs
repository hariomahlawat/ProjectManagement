using Microsoft.AspNetCore.Http;
using ProjectManagement.Configuration;
using ProjectManagement.Features.MediaLibrary.Services;
using ProjectManagement.Services.Admin;

namespace ProjectManagement.Features.MediaLibrary.Admin;

public sealed class MediaRecoveryAdminService : IMediaRecoveryAdminService
{
    private const int MaximumBatchSize = 250;

    private readonly IMediaLibraryHealthService _catalogueHealth;
    private readonly IPrismMediaCatalogueSynchronizer _prismSynchronizer;
    private readonly IMediaCatalogueConsistencyService _consistency;
    private readonly IMediaAvailabilityRecoveryService _availabilityRecovery;
    private readonly IMediaAdminAccessService _access;
    private readonly IAdminAuditService _audit;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<MediaRecoveryAdminService> _logger;

    public MediaRecoveryAdminService(
        IMediaLibraryHealthService catalogueHealth,
        IPrismMediaCatalogueSynchronizer prismSynchronizer,
        IMediaCatalogueConsistencyService consistency,
        IMediaAvailabilityRecoveryService availabilityRecovery,
        IMediaAdminAccessService access,
        IAdminAuditService audit,
        IHttpContextAccessor httpContextAccessor,
        ILogger<MediaRecoveryAdminService> logger)
    {
        _catalogueHealth = catalogueHealth ?? throw new ArgumentNullException(nameof(catalogueHealth));
        _prismSynchronizer = prismSynchronizer ?? throw new ArgumentNullException(nameof(prismSynchronizer));
        _consistency = consistency ?? throw new ArgumentNullException(nameof(consistency));
        _availabilityRecovery = availabilityRecovery ?? throw new ArgumentNullException(nameof(availabilityRecovery));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MediaAdminCommandResult> TestCatalogueAsync(CancellationToken cancellationToken)
    {
        if (!await _access.IsAuthorizedAsync(AdminPolicies.MediaView, cancellationToken))
        {
            return Forbidden("view media health");
        }

        try
        {
            var report = await _catalogueHealth.CheckAsync(cancellationToken);
            if (report.IsOperational && report.FacetsHealthy)
            {
                return MediaAdminCommandResult.Success(
                    $"Catalogue test passed. Timeline and facets are healthy; {report.IndexedAssets:N0} assets are indexed.");
            }

            if (report.TimelineQueryHealthy)
            {
                return MediaAdminCommandResult.Failure(
                    "The catalogue timeline is operational, but one or more optional facets are degraded. Review the diagnostics panel.",
                    MediaAdminErrorCodes.CatalogueUnavailable);
            }

            return MediaAdminCommandResult.Failure(
                "The unified catalogue timeline test failed. Review the diagnostics panel and application logs.",
                MediaAdminErrorCodes.CatalogueUnavailable);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Unexpected(ex, "testing media catalogue health");
        }
    }

    public async Task<MediaAdminCommandResult> SynchronizePrismAsync(CancellationToken cancellationToken)
    {
        if (!await _access.IsAuthorizedAsync(AdminPolicies.MediaRecover, cancellationToken))
        {
            return Forbidden("synchronise the PRISM media catalogue");
        }

        try
        {
            var before = await _consistency.CheckAsync(cancellationToken);
            await _prismSynchronizer.SynchronizeAsync(cancellationToken);
            var after = await _consistency.CheckAsync(cancellationToken);

            await AuditBestEffortAsync(new AdminAuditEntry(
                "MediaCatalogueSynchronized",
                "MediaCatalogue",
                Before: before,
                After: after,
                Message: "PRISM media catalogue synchronization completed.",
                Origin: "/Admin/MediaSources"), cancellationToken);

            return MediaAdminCommandResult.Success(
                after.IsConsistent
                    ? "PRISM media catalogue synchronization completed and the catalogue is consistent."
                    : $"PRISM media catalogue synchronization completed. {after.MissingFromCatalogue:N0} missing and {after.OrphanedCatalogueRecords:N0} orphaned record(s) still require review.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Unexpected(ex, "synchronising the PRISM media catalogue");
        }
    }

    public async Task<MediaAdminCommandResult> CheckConsistencyAsync(CancellationToken cancellationToken)
    {
        if (!await _access.IsAuthorizedAsync(AdminPolicies.MediaView, cancellationToken))
        {
            return Forbidden("check media catalogue consistency");
        }

        try
        {
            var report = await _consistency.CheckAsync(cancellationToken);
            return report.IsConsistent
                ? MediaAdminCommandResult.Success(
                    $"Catalogue consistency check passed: {report.PrismSourceRecords:N0} PRISM source records matched {report.CatalogueRecords:N0} catalogue records; {report.AvailableCatalogueRecords:N0} are available and {report.UnavailableCatalogueRecords:N0} are unavailable.")
                : MediaAdminCommandResult.Failure(
                    $"Catalogue consistency check found {report.MissingFromCatalogue:N0} missing and {report.OrphanedCatalogueRecords:N0} orphaned record(s). Run Synchronize PRISM, then check again.",
                    MediaAdminErrorCodes.CatalogueUnavailable);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Unexpected(ex, "checking media catalogue consistency");
        }
    }

    public async Task<MediaAdminCommandResult> ReconcileAvailabilityAsync(
        int maximumItems,
        CancellationToken cancellationToken)
    {
        if (!await _access.IsAuthorizedAsync(AdminPolicies.MediaRecover, cancellationToken))
        {
            return Forbidden("reconcile media availability");
        }

        try
        {
            var result = await _availabilityRecovery.ReconcileHistoricalAsync(
                Math.Clamp(maximumItems, 1, MaximumBatchSize),
                cancellationToken);
            await AuditBestEffortAsync(new AdminAuditEntry(
                "MediaAvailabilityReconciled",
                "MediaAsset",
                After: result,
                Message: "Historical media availability was reconciled.",
                Origin: "/Admin/MediaSources"), cancellationToken);

            var message = result.Examined == 0
                ? "No historical missing-media records required reconciliation."
                : $"Availability reconciliation examined {result.Examined:N0} item(s): {result.Restored:N0} restored, "
                  + $"{result.MarkedUnavailable:N0} confirmed unavailable, {result.TemporarilyUnavailable:N0} temporarily unavailable, "
                  + $"{result.Errors:N0} error(s)."
                  + (result.HasMore ? " Additional historical items remain." : string.Empty);
            return result.Errors == 0
                ? MediaAdminCommandResult.Success(message)
                : MediaAdminCommandResult.Failure(message, MediaAdminErrorCodes.SourceUnavailable);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Unexpected(ex, "reconciling historical media availability");
        }
    }

    public async Task<MediaAdminCommandResult> RecheckUnavailableAsync(
        int maximumItems,
        CancellationToken cancellationToken)
    {
        if (!await _access.IsAuthorizedAsync(AdminPolicies.MediaRecover, cancellationToken))
        {
            return Forbidden("recheck unavailable media");
        }

        try
        {
            var result = await _availabilityRecovery.RecheckAsync(
                assetId: null,
                Math.Clamp(maximumItems, 1, MaximumBatchSize),
                cancellationToken);
            await AuditBestEffortAsync(new AdminAuditEntry(
                "MediaUnavailableBatchRechecked",
                "MediaAsset",
                After: result,
                Message: "Unavailable media was rechecked.",
                Origin: "/Admin/MediaSources"), cancellationToken);

            var message = result.Examined == 0
                ? "No unavailable media required rechecking."
                : $"Rechecked {result.Examined:N0} unavailable item(s): {result.Restored:N0} restored, {result.StillUnavailable:N0} still unavailable, {result.Errors:N0} error(s)."
                  + (result.HasMore ? " More unavailable items remain; run the check again." : string.Empty);
            return result.Errors == 0
                ? MediaAdminCommandResult.Success(message)
                : MediaAdminCommandResult.Failure(message, MediaAdminErrorCodes.SourceUnavailable);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Unexpected(ex, "rechecking unavailable media");
        }
    }

    public async Task<MediaAdminCommandResult> RecheckUnavailableAssetAsync(
        long id,
        CancellationToken cancellationToken)
    {
        if (!await _access.IsAuthorizedAsync(AdminPolicies.MediaRecover, cancellationToken))
        {
            return Forbidden("recheck unavailable media");
        }

        try
        {
            var result = await _availabilityRecovery.RecheckAsync(id, 1, cancellationToken);
            if (result.Examined == 0)
            {
                return MediaAdminCommandResult.Failure(
                    "The requested unavailable media item was not found or has already been restored.",
                    MediaAdminErrorCodes.NotFound);
            }

            await AuditBestEffortAsync(new AdminAuditEntry(
                "MediaUnavailableAssetRechecked",
                "MediaAsset",
                id.ToString(),
                After: result,
                Message: "Unavailable media asset was rechecked.",
                Origin: "/Admin/MediaSources"), cancellationToken);

            if (result.Restored == 1)
            {
                return MediaAdminCommandResult.Success(
                    $"Media asset {id} is available again and was queued for processing.");
            }

            if (result.Errors > 0)
            {
                return MediaAdminCommandResult.Failure(
                    $"Media asset {id} could not be checked. Review application logs using reference {TraceId ?? "unavailable"}.",
                    MediaAdminErrorCodes.UnexpectedFailure,
                    TraceId);
            }

            return MediaAdminCommandResult.Failure(
                $"Media asset {id} is still unavailable. Restore the underlying file before rechecking.",
                MediaAdminErrorCodes.SourceUnavailable);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Unexpected(ex, "rechecking unavailable media asset");
        }
    }

    private async Task AuditBestEffortAsync(AdminAuditEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            await _audit.RecordAsync(entry, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex,
                "Media recovery operation {Action} succeeded but its audit event could not be written.",
                entry.Action);
        }
    }

    private string? TraceId => _httpContextAccessor.HttpContext?.TraceIdentifier;

    private MediaAdminCommandResult Unexpected(Exception ex, string operation)
    {
        _logger.LogError(ex, "Unexpected failure while {Operation}. TraceId={TraceId}", operation, TraceId);
        return MediaAdminCommandResult.Failure(
            $"The recovery operation could not be completed. Reference {TraceId ?? "unavailable"}.",
            MediaAdminErrorCodes.UnexpectedFailure,
            TraceId);
    }

    private static MediaAdminCommandResult Forbidden(string operation) =>
        MediaAdminCommandResult.Failure(
            $"You are not authorised to {operation}.",
            MediaAdminErrorCodes.Forbidden);
}
