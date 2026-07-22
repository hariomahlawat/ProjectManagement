using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Outbox;
using ProjectManagement.Features.MediaLibrary.Services;
using ProjectManagement.Services.Admin;

namespace ProjectManagement.Features.MediaLibrary.Admin;

public sealed class MediaQueueAdminService : IMediaQueueAdminService
{
    private const int MaximumBulkItems = 250;

    private readonly MediaLibraryDbContext _db;
    private readonly ApplicationDbContext _applicationDb;
    private readonly IMediaAdminAccessService _access;
    private readonly IAdminAuditService _audit;
    private readonly IAdminTimeService _time;
    private readonly IPrismMediaOutboxSignal _outboxSignal;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<MediaQueueAdminService> _logger;

    public MediaQueueAdminService(
        MediaLibraryDbContext db,
        ApplicationDbContext applicationDb,
        IMediaAdminAccessService access,
        IAdminAuditService audit,
        IAdminTimeService time,
        IPrismMediaOutboxSignal outboxSignal,
        IHttpContextAccessor httpContextAccessor,
        ILogger<MediaQueueAdminService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _applicationDb = applicationDb ?? throw new ArgumentNullException(nameof(applicationDb));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _outboxSignal = outboxSignal ?? throw new ArgumentNullException(nameof(outboxSignal));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MediaAdminCommandResult<int>> RetryRecoverableAsync(
        int maximumItems,
        CancellationToken cancellationToken)
    {
        if (!await CanOperateAsync(cancellationToken))
        {
            return Forbidden<int>();
        }

        try
        {
            var take = Math.Clamp(maximumItems, 1, MaximumBulkItems);
            var ids = await _db.ProcessingJobs.AsNoTracking()
                .Where(job => (job.Status == MediaProcessingJobStatus.Failed
                               || job.Status == MediaProcessingJobStatus.DeadLetter)
                              && job.FailureCode != null
                              && MediaProcessingFailurePolicy.RecoverableFailureCodeNames.Contains(job.FailureCode))
                .OrderBy(job => job.UpdatedAtUtc)
                .ThenBy(job => job.Id)
                .Select(job => job.Id)
                .Take(take)
                .ToArrayAsync(cancellationToken);

            if (ids.Length == 0)
            {
                return MediaAdminCommandResult<int>.Success(
                    0,
                    "No recoverable failed media jobs required retry. Permanent missing-content failures were left unchanged.");
            }

            var now = _time.UtcNow;
            var count = await _db.ProcessingJobs
                .Where(job => ids.Contains(job.Id)
                              && (job.Status == MediaProcessingJobStatus.Failed
                                  || job.Status == MediaProcessingJobStatus.DeadLetter))
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
                    .SetProperty(job => job.UpdatedAtUtc, now),
                    cancellationToken);

            await AuditBestEffortAsync(new AdminAuditEntry(
                "MediaQueueRecoverableRetried",
                "MediaProcessingJob",
                After: new { Count = count, Requested = ids.Length },
                Message: "Recoverable media processing jobs were requeued.",
                Origin: "/Admin/MediaSources"), cancellationToken);

            return MediaAdminCommandResult<int>.Success(
                count,
                $"{count:N0} recoverable media processing job(s) were queued again."
                + (ids.Length == take ? " Additional failed jobs may remain." : string.Empty));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Unexpected<int>(ex, "retrying recoverable media jobs");
        }
    }

    public async Task<MediaAdminCommandResult<int>> RetryPermanentAsync(
        int maximumItems,
        CancellationToken cancellationToken)
    {
        if (!await CanOperateAsync(cancellationToken))
        {
            return Forbidden<int>();
        }

        try
        {
            var take = Math.Clamp(maximumItems, 1, MaximumBulkItems);
            var jobs = await _db.ProcessingJobs
                .Include(job => job.MediaAsset)
                .Where(job => (job.Status == MediaProcessingJobStatus.Failed
                               || job.Status == MediaProcessingJobStatus.DeadLetter)
                              && job.MediaAsset.IsAvailable
                              && job.FailureCode != null
                              && MediaProcessingFailurePolicy.PermanentFailureCodeNames.Contains(job.FailureCode))
                .OrderBy(job => job.UpdatedAtUtc)
                .ThenBy(job => job.Id)
                .Take(take)
                .ToListAsync(cancellationToken);

            var now = _time.UtcNow;
            foreach (var job in jobs)
            {
                ResetJob(job, now);
                RestoreAssetForForcedRetry(job.MediaAsset, now);
            }

            await _db.SaveChangesAsync(cancellationToken);
            await AuditBestEffortAsync(new AdminAuditEntry(
                "MediaQueuePermanentRetried",
                "MediaProcessingJob",
                After: new { Count = jobs.Count, Forced = true },
                Reason: "Underlying files were confirmed restored or corrected by an administrator.",
                Message: "Permanent media failures were force-requeued.",
                Origin: "/Admin/MediaSources"), cancellationToken);

            return MediaAdminCommandResult<int>.Success(
                jobs.Count,
                jobs.Count == 0
                    ? "No permanent media failures were available for forced retry."
                    : $"{jobs.Count:N0} permanent media failure(s) were force-queued. Use this only after restoring or correcting the underlying files."
                      + (jobs.Count == take ? " Additional permanent failures may remain." : string.Empty));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Unexpected<int>(ex, "force-retrying permanent media failures");
        }
    }

    public async Task<MediaAdminCommandResult> RetryJobAsync(
        long id,
        bool forcePermanent,
        CancellationToken cancellationToken)
    {
        if (!await CanOperateAsync(cancellationToken))
        {
            return Forbidden();
        }

        try
        {
            var job = await _db.ProcessingJobs
                .Include(item => item.MediaAsset)
                .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (job is null)
            {
                return MediaAdminCommandResult.Failure(
                    "The media processing job was not found.",
                    MediaAdminErrorCodes.NotFound);
            }

            if (job.Status == MediaProcessingJobStatus.Running)
            {
                return MediaAdminCommandResult.Failure(
                    "This job is currently processing and cannot be retried.",
                    MediaAdminErrorCodes.QueueItemNotRetryable);
            }

            if (job.Status == MediaProcessingJobStatus.Completed)
            {
                return MediaAdminCommandResult.Failure(
                    "Completed jobs cannot be retried from the failure queue.",
                    MediaAdminErrorCodes.QueueItemNotRetryable);
            }

            if (job.Status == MediaProcessingJobStatus.Pending && job.AttemptCount == 0)
            {
                return MediaAdminCommandResult.Failure(
                    "This job is already queued and does not require retrying.",
                    MediaAdminErrorCodes.QueueItemNotRetryable);
            }

            if (!job.MediaAsset.IsAvailable
                || job.MediaAsset.AvailabilityStatus != MediaAvailabilityStatus.Available)
            {
                return MediaAdminCommandResult.Failure(
                    "This media item is unavailable. Restore the source and use Recheck in the Unavailable media section.",
                    MediaAdminErrorCodes.SourceUnavailable);
            }

            var permanent = MediaProcessingFailurePolicy.IsPermanentFailureCode(job.FailureCode);
            if (permanent && !forcePermanent)
            {
                return MediaAdminCommandResult.Failure(
                    "This job represents permanently unsupported content. Use force retry only after correcting the source.",
                    MediaAdminErrorCodes.QueueItemNotRetryable);
            }

            var before = new
            {
                job.Status,
                job.AttemptCount,
                job.FailureCode,
                job.UpdatedAtUtc
            };
            var now = _time.UtcNow;
            ResetJob(job, now);
            if (forcePermanent)
            {
                RestoreAssetForForcedRetry(job.MediaAsset, now);
            }

            await _db.SaveChangesAsync(cancellationToken);
            await AuditBestEffortAsync(new AdminAuditEntry(
                "MediaQueueItemRetried",
                "MediaProcessingJob",
                id.ToString(),
                Before: before,
                After: new { job.Status, job.AttemptCount, job.UpdatedAtUtc, ForcePermanent = forcePermanent },
                Reason: forcePermanent ? "Administrator confirmed that the underlying content was corrected." : null,
                Message: "Media processing job requeued.",
                Origin: "/Admin/MediaSources"), cancellationToken);

            return MediaAdminCommandResult.Success($"Media processing job {id} was queued again.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Unexpected(ex, "retrying media processing job");
        }
    }

    public async Task<MediaAdminCommandResult<int>> RetryIngestionAsync(
        int maximumItems,
        CancellationToken cancellationToken)
    {
        if (!await CanOperateAsync(cancellationToken))
        {
            return Forbidden<int>();
        }

        try
        {
            var take = Math.Clamp(maximumItems, 1, MaximumBulkItems);
            var ids = await _applicationDb.PrismMediaOutboxMessages.AsNoTracking()
                .Where(message => message.Status == PrismMediaOutboxStatus.DeadLetter
                                  || (message.Status == PrismMediaOutboxStatus.Pending
                                      && message.LastError != null))
                .OrderBy(message => message.OccurredAtUtc)
                .ThenBy(message => message.Id)
                .Select(message => message.Id)
                .Take(take)
                .ToArrayAsync(cancellationToken);

            if (ids.Length == 0)
            {
                return MediaAdminCommandResult<int>.Success(
                    0,
                    "No failed PRISM media ingestion events required retrying.");
            }

            var now = _time.UtcNow;
            var count = await _applicationDb.PrismMediaOutboxMessages
                .Where(message => ids.Contains(message.Id)
                                  && (message.Status == PrismMediaOutboxStatus.DeadLetter
                                      || (message.Status == PrismMediaOutboxStatus.Pending
                                          && message.LastError != null)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(message => message.Status, PrismMediaOutboxStatus.Pending)
                    .SetProperty(message => message.AttemptCount, 0)
                    .SetProperty(message => message.AvailableAfterUtc, now)
                    .SetProperty(message => message.ProcessingStartedAtUtc, (DateTimeOffset?)null)
                    .SetProperty(message => message.ProcessedAtUtc, (DateTimeOffset?)null)
                    .SetProperty(message => message.LockedBy, (string?)null)
                    .SetProperty(message => message.LockExpiresAtUtc, (DateTimeOffset?)null)
                    .SetProperty(message => message.LastError, (string?)null),
                    cancellationToken);

            _outboxSignal.Pulse();
            await AuditBestEffortAsync(new AdminAuditEntry(
                "MediaIngestionEventsRetried",
                "PrismMediaOutboxMessage",
                After: new { Count = count, Requested = ids.Length },
                Message: "Failed PRISM media ingestion events were requeued.",
                Origin: "/Admin/MediaSources"), cancellationToken);

            return MediaAdminCommandResult<int>.Success(
                count,
                $"Queued {count:N0} PRISM media ingestion event(s) for immediate retry."
                + (ids.Length == take ? " Additional failed events may remain." : string.Empty));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Unexpected<int>(ex, "retrying PRISM media ingestion events");
        }
    }

    private Task<bool> CanOperateAsync(CancellationToken cancellationToken) =>
        _access.IsAuthorizedAsync(AdminPolicies.MediaOperateQueue, cancellationToken);

    private static void ResetJob(MediaProcessingJob job, DateTimeOffset now)
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
    }

    private static void RestoreAssetForForcedRetry(MediaAsset asset, DateTimeOffset now)
    {
        asset.IsAvailable = true;
        asset.AvailabilityStatus = MediaAvailabilityStatus.Available;
        asset.UnavailableReason = null;
        asset.UnavailableSinceUtc = null;
        asset.LastAvailabilityCheckUtc = now;
        asset.DerivativeStatus = MediaProcessingStatus.Pending;
        asset.AnalysisStatus = MediaProcessingStatus.Pending;
        asset.ProcessingFailureReason = null;
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
                "Media queue operation {Action} succeeded but its audit event could not be written.",
                entry.Action);
        }
    }

    private string? TraceId => _httpContextAccessor.HttpContext?.TraceIdentifier;

    private MediaAdminCommandResult Unexpected(Exception ex, string operation)
    {
        _logger.LogError(ex, "Unexpected failure while {Operation}. TraceId={TraceId}", operation, TraceId);
        return MediaAdminCommandResult.Failure(
            $"The queue operation could not be completed. Reference {TraceId ?? "unavailable"}.",
            MediaAdminErrorCodes.UnexpectedFailure,
            TraceId);
    }

    private MediaAdminCommandResult<T> Unexpected<T>(Exception ex, string operation)
    {
        _logger.LogError(ex, "Unexpected failure while {Operation}. TraceId={TraceId}", operation, TraceId);
        return MediaAdminCommandResult<T>.Failure(
            $"The queue operation could not be completed. Reference {TraceId ?? "unavailable"}.",
            MediaAdminErrorCodes.UnexpectedFailure,
            TraceId);
    }

    private static MediaAdminCommandResult Forbidden() =>
        MediaAdminCommandResult.Failure(
            "You are not authorised to operate the media processing queue.",
            MediaAdminErrorCodes.Forbidden);

    private static MediaAdminCommandResult<T> Forbidden<T>() =>
        MediaAdminCommandResult<T>.Failure(
            "You are not authorised to operate the media processing queue.",
            MediaAdminErrorCodes.Forbidden);
}
