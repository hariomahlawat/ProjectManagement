using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Services.Ocr;

namespace ProjectManagement.Services.DocRepo;

// SECTION: Claim model
/// <summary>A document this worker has claimed; <paramref name="ClaimedAtUtc"/> is the claim token.</summary>
public sealed record DocRepoOcrClaim(Document Document, DateTimeOffset ClaimedAtUtc);

// SECTION: Queue
/// <summary>
/// The OCR queue is the Documents table itself: a document is queued while its OcrStatus is
/// Pending. A worker claims one document at a time by stamping OcrLastTriedUtc with a
/// conditional update, so overlapping app processes (for example during a rolling deploy)
/// never OCR the same document at once. A result is saved only if the claim still holds:
/// if someone re-queued, re-uploaded or deleted the document meanwhile, the stale result is
/// dropped. A claim older than <see cref="ClaimLease"/> is treated as abandoned (the process
/// stopped mid-run) and the document is claimed again, so a PDF that crashes OCR is retried
/// at that pace rather than in a tight loop.
/// </summary>
public sealed class DocRepoOcrQueue
{
    public static readonly TimeSpan ClaimLease = TimeSpan.FromHours(2);
    public const string WorkerActor = "ocr-worker";
    private const int ClaimAttempts = 5;

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;

    public DocRepoOcrQueue(ApplicationDbContext db, IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<DocRepoOcrClaim?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        var claimedAt = ToDatabasePrecision(_clock.UtcNow);
        var staleBefore = claimedAt - ClaimLease;

        for (var attempt = 0; attempt < ClaimAttempts; attempt++)
        {
            var candidate = await _db.Documents
                .AsNoTracking()
                .Where(d => !d.IsDeleted
                    && d.OcrStatus == DocOcrStatus.Pending
                    && (d.OcrLastTriedUtc == null || d.OcrLastTriedUtc < staleBefore))
                .OrderBy(d => d.CreatedAtUtc)
                .ThenBy(d => d.Id)
                .Select(d => new { d.Id, d.OcrLastTriedUtc })
                .FirstOrDefaultAsync(cancellationToken);

            if (candidate is null)
            {
                return null;
            }

            var claimed = await _db.Documents
                .Where(d => d.Id == candidate.Id
                    && d.OcrStatus == DocOcrStatus.Pending
                    && d.OcrLastTriedUtc == candidate.OcrLastTriedUtc)
                .ExecuteUpdateAsync(setters => setters.SetProperty(d => d.OcrLastTriedUtc, claimedAt), cancellationToken);

            if (claimed == 1)
            {
                var document = await _db.Documents.AsNoTracking().FirstAsync(d => d.Id == candidate.Id, cancellationToken);
                return new DocRepoOcrClaim(document, claimedAt);
            }

            // Another worker claimed it first; look for the next one.
        }

        return null;
    }

    /// <summary>Saves the OCR result; returns false when the claim no longer holds and the result was dropped.</summary>
    public async Task<bool> CompleteAsync(DocRepoOcrClaim claim, OcrRunResult result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(result);

        var documentId = claim.Document.Id;
        var claimedAt = claim.ClaimedAtUtc;
        var status = result.Success ? DocOcrStatus.Succeeded : DocOcrStatus.Failed;
        var failureReason = result.Success ? null : OcrTextLimiter.TrimForFailure(result.Error);
        var nowUtc = _clock.UtcNow.UtcDateTime;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var updated = await _db.Documents
            .Where(d => d.Id == documentId
                && !d.IsDeleted
                && d.OcrStatus == DocOcrStatus.Pending
                && d.OcrLastTriedUtc == claimedAt)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(d => d.OcrStatus, status)
                .SetProperty(d => d.OcrFailureReason, failureReason)
                .SetProperty(d => d.UpdatedAtUtc, nowUtc)
                .SetProperty(d => d.UpdatedByUserId, WorkerActor),
                cancellationToken);

        if (updated == 0)
        {
            return false;
        }

        var text = await _db.DocumentTexts.FirstOrDefaultAsync(t => t.DocumentId == documentId, cancellationToken);
        if (result.Success)
        {
            if (text is null)
            {
                text = new DocumentText { DocumentId = documentId };
                _db.DocumentTexts.Add(text);
            }

            text.OcrText = OcrTextLimiter.CapExtractedText(result.Text);
            text.UpdatedAtUtc = nowUtc;
        }
        else if (text is not null)
        {
            text.OcrText = null;
            text.UpdatedAtUtc = nowUtc;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    // PostgreSQL stores timestamps to the microsecond; the claim token must compare equal after a round trip.
    internal static DateTimeOffset ToDatabasePrecision(DateTimeOffset value)
        => new(value.UtcTicks - (value.UtcTicks % 10), TimeSpan.Zero);
}
