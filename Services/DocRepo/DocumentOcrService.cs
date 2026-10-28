using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Services.Ocr;

namespace ProjectManagement.Services.DocRepo
{
    // SECTION: Contract
    public interface IDocumentOcrService
    {
        /// <summary>Runs OCR now, inside the caller's request (used by the OCR text backfill).</summary>
        Task<bool> ReprocessAsync(Guid documentId, CancellationToken ct = default);

        /// <summary>Marks one document for the OCR worker; returns false if it does not exist.</summary>
        Task<bool> RequeueAsync(Guid documentId, string actorUserId, CancellationToken ct = default);

        /// <summary>Marks every failed document for the OCR worker and returns how many were queued.</summary>
        Task<int> RequeueAllFailedAsync(string actorUserId, CancellationToken ct = default);
    }

    /// <summary>
    /// Admin-facing helper to re-run OCR for failed documents.
    /// </summary>
    public sealed class DocumentOcrService : IDocumentOcrService
    {
        private readonly ApplicationDbContext _db;
        private readonly IDocumentOcrRunner _runner;
        private readonly IDocRepoAuditService _audit;

        public DocumentOcrService(ApplicationDbContext db, IDocumentOcrRunner runner, IDocRepoAuditService audit)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        }

        /// <summary>
        /// Reprocess a single document immediately.
        /// </summary>
        public async Task<bool> ReprocessAsync(Guid documentId, CancellationToken ct = default)
        {
            var doc = await _db.Documents
                .Include(d => d.DocumentText)
                .FirstOrDefaultAsync(d => d.Id == documentId, ct);

            if (doc == null)
            {
                return false;
            }

            // reset state
            doc.OcrStatus = DocOcrStatus.Pending;
            doc.OcrFailureReason = null;
            doc.OcrLastTriedUtc = null;
            await _db.SaveChangesAsync(ct);

            // run now (sync way)
            var result = await _runner.RunAsync(doc, ct);
            if (result.Success)
            {
                doc.OcrStatus = DocOcrStatus.Succeeded;
                var documentText = doc.DocumentText ??= new DocumentText
                {
                    DocumentId = doc.Id,
                    UpdatedAtUtc = DateTime.UtcNow
                };

                documentText.OcrText = OcrTextLimiter.CapExtractedText(result.Text);
                documentText.UpdatedAtUtc = DateTime.UtcNow;
                doc.OcrFailureReason = null;
            }
            else
            {
                doc.OcrStatus = DocOcrStatus.Failed;
                doc.OcrFailureReason = OcrTextLimiter.TrimForFailure(result.Error);

                if (doc.DocumentText is not null)
                {
                    doc.DocumentText.OcrText = null;
                    doc.DocumentText.UpdatedAtUtc = DateTime.UtcNow;
                }
            }

            await _db.SaveChangesAsync(ct);
            return result.Success;
        }

        public async Task<bool> RequeueAsync(Guid documentId, string actorUserId, CancellationToken ct = default)
        {
            var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == documentId && !d.IsDeleted, ct);
            if (doc == null)
            {
                return false;
            }

            MarkPending(doc, actorUserId);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<int> RequeueAllFailedAsync(string actorUserId, CancellationToken ct = default)
        {
            var failedDocs = await _db.Documents
                .Where(d => d.OcrStatus == DocOcrStatus.Failed && !d.IsDeleted)
                .ToListAsync(ct);

            foreach (var doc in failedDocs)
            {
                MarkPending(doc, actorUserId);
            }

            await _db.SaveChangesAsync(ct);
            return failedDocs.Count;
        }

        // The worker picks up pending documents in order; clearing the last-tried time releases
        // any claim a stopped worker left behind.
        private void MarkPending(Document doc, string actorUserId)
        {
            doc.OcrStatus = DocOcrStatus.Pending;
            doc.OcrFailureReason = null;
            doc.OcrLastTriedUtc = null;
            doc.UpdatedAtUtc = DateTime.UtcNow;
            doc.UpdatedByUserId = actorUserId;
            _audit.Stage(doc.Id, actorUserId, DocRepoAuditEventTypes.OcrRequeued, new { doc.Id });
        }
    }
}
