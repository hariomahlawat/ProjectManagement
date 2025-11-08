using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;

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
                .FirstOrDefaultAsync(d => d.Id == documentId, ct);

            if (doc == null)
            {
                return false;
            }

            // reset state
            doc.OcrStatus = DocOcrStatus.Pending;
            doc.OcrFailureReason = null;
            await _db.SaveChangesAsync(ct);

            // run now (sync way)
            var result = await _runner.RunAsync(doc, ct);
            if (result.Success)
            {
                doc.OcrStatus = DocOcrStatus.Succeeded;
                // you currently don't have a place to store result.Text
                // add a column/table later if you want full-text search on OCR
            }
            else
            {
                doc.OcrStatus = DocOcrStatus.Failed;
                doc.OcrFailureReason = result.Error;
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
                .Where(d => d.OcrStatus == DocOcrStatus.Failed && !d.IsDeleted)
                .ToListAsync(ct);

            var processed = 0;

            foreach (var doc in failedDocs)
            {
                // reset
                doc.OcrStatus = DocOcrStatus.Pending;
                doc.OcrFailureReason = null;
                await _db.SaveChangesAsync(ct);

                var result = await _runner.RunAsync(doc, ct);
                if (result.Success)
                {
                    doc.OcrStatus = DocOcrStatus.Succeeded;
                }
                else
                {
                    doc.OcrStatus = DocOcrStatus.Failed;
                    doc.OcrFailureReason = result.Error;
                }

                await _db.SaveChangesAsync(ct);
                processed++;
            }

            return processed;
        }
    }
}
