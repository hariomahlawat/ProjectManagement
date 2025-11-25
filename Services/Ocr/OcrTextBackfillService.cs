using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Data.Projects;
using ProjectManagement.Models;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Services.Ocr;

// SECTION: OCR text backfill coordinator
public sealed class OcrTextBackfillService
{
    private readonly ApplicationDbContext _db;
    private readonly IDocumentOcrService _docRepoService;
    private readonly IProjectDocumentOcrRunner _projectRunner;
    private readonly ILogger<OcrTextBackfillService> _logger;

    public OcrTextBackfillService(
        ApplicationDbContext db,
        IDocumentOcrService docRepoService,
        IProjectDocumentOcrRunner projectRunner,
        ILogger<OcrTextBackfillService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _docRepoService = docRepoService ?? throw new ArgumentNullException(nameof(docRepoService));
        _projectRunner = projectRunner ?? throw new ArgumentNullException(nameof(projectRunner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<OcrBackfillSummary> RunAsync(CancellationToken ct = default)
    {
        // SECTION: Identify DocRepo banner rows
        var docRepoIds = await _db.DocumentTexts
            .Where(t => t.OcrText != null)
            .Where(t => EF.Functions.ILike(t.OcrText!, "%OCR skipped on page%") || EF.Functions.ILike(t.OcrText!, "Prior OCR%"))
            .Select(t => t.DocumentId)
            .ToListAsync(ct);

        // SECTION: Identify Project document banner rows
        var projectIds = await _db.ProjectDocumentTexts
            .Where(t => t.OcrText != null)
            .Where(t => EF.Functions.ILike(t.OcrText!, "%OCR skipped on page%") || EF.Functions.ILike(t.OcrText!, "Prior OCR%"))
            .Select(t => t.ProjectDocumentId)
            .ToListAsync(ct);

        _logger.LogInformation("Starting OCR backfill for {DocRepoCount} DocRepo and {ProjectCount} project documents.", docRepoIds.Count, projectIds.Count);

        var docRepoProcessed = await ReprocessDocRepoAsync(docRepoIds, ct);
        var projectProcessed = await ReprocessProjectDocsAsync(projectIds, ct);

        _logger.LogInformation("Completed OCR backfill. DocRepo processed: {DocRepoProcessed}. Project processed: {ProjectProcessed}.", docRepoProcessed, projectProcessed);

        return new OcrBackfillSummary(docRepoProcessed, projectProcessed);
    }

    // SECTION: DocRepo pipeline
    private async Task<int> ReprocessDocRepoAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var processed = 0;

        foreach (var id in ids)
        {
            if (await _docRepoService.ReprocessAsync(id, ct))
            {
                processed++;
            }
        }

        return processed;
    }

    // SECTION: Project pipeline
    private async Task<int> ReprocessProjectDocsAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var processed = 0;

        foreach (var id in ids)
        {
            var doc = await _db.ProjectDocuments
                .Include(d => d.DocumentText)
                .FirstOrDefaultAsync(d => d.Id == id, ct);

            if (doc is null)
            {
                continue;
            }

            doc.OcrStatus = ProjectDocumentOcrStatus.Pending;
            doc.OcrFailureReason = null;
            doc.OcrLastTriedUtc = null;
            await _db.SaveChangesAsync(ct);

            var result = await _projectRunner.RunAsync(doc, ct);
            var now = DateTimeOffset.UtcNow;

            if (result.Success)
            {
                doc.OcrStatus = ProjectDocumentOcrStatus.Succeeded;
                doc.OcrFailureReason = null;
                doc.OcrLastTriedUtc = now;

                var text = doc.DocumentText ??= new ProjectDocumentText
                {
                    ProjectDocumentId = doc.Id,
                    UpdatedAtUtc = now
                };

                text.OcrText = OcrTextLimiter.CapExtractedText(result.Text);
                text.UpdatedAtUtc = now;
            }
            else
            {
                doc.OcrStatus = ProjectDocumentOcrStatus.Failed;
                doc.OcrFailureReason = OcrTextLimiter.TrimForFailure(result.Error);
                doc.OcrLastTriedUtc = now;

                if (doc.DocumentText is not null)
                {
                    doc.DocumentText.OcrText = null;
                    doc.DocumentText.UpdatedAtUtc = now;
                }
            }

            await _db.SaveChangesAsync(ct);
            processed++;
        }

        return processed;
    }
}

// SECTION: Backfill summary model
public sealed record OcrBackfillSummary(int DocRepoProcessed, int ProjectDocsProcessed);
