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
    /// <summary>
    /// Admin-facing helper to re-run OCR for failed documents.
    /// </summary>
    public sealed class DocumentOcrService
    {
        private readonly ApplicationDbContext _db;
        private readonly IDocumentOcrRunner _runner;

        public DocumentOcrService(ApplicationDbContext db, IDocumentOcrRunner runner)
        {
            _db = db;
            _runner = runner;
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

        /// <summary>
        /// Reprocess all documents that are currently in Failed state.
        /// </summary>
        public async Task<int> ReprocessAllFailedAsync(CancellationToken ct = default)
        {
            var failedDocs = await _db.Documents
                .Include(d => d.DocumentText)
                .Where(d => d.OcrStatus == DocOcrStatus.Failed && !d.IsDeleted)
                .ToListAsync(ct);

            var processed = 0;

            foreach (var doc in failedDocs)
            {
                // reset
                doc.OcrStatus = DocOcrStatus.Pending;
                doc.OcrFailureReason = null;
                doc.OcrLastTriedUtc = null;
                await _db.SaveChangesAsync(ct);

                var result = await _runner.RunAsync(doc, ct);
                if (result.Success)
                {
                    doc.OcrStatus = DocOcrStatus.Succeeded;
                    var text = doc.DocumentText ??= new DocumentText
                    {
                        DocumentId = doc.Id,
                        UpdatedAtUtc = DateTime.UtcNow
                    };

                    text.OcrText = OcrTextLimiter.CapExtractedText(result.Text);
                    text.UpdatedAtUtc = DateTime.UtcNow;
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
                processed++;
            }

            return processed;
        }

    }
}
