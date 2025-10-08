using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;

namespace ProjectManagement.Services.Documents;

public sealed class DocumentDecisionService : IDocumentDecisionService
{
    private readonly ApplicationDbContext _db;
    private readonly IDocumentService _documentService;
    private readonly IClock _clock;
    private readonly IAuditService _audit;

    public DocumentDecisionService(
        ApplicationDbContext db,
        IDocumentService documentService,
        IClock clock,
        IAuditService audit)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _documentService = documentService ?? throw new ArgumentNullException(nameof(documentService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    public async Task<ProjectDocumentRequest> ApproveAsync(
        int requestId,
        string decidedByUserId,
        string? note,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(decidedByUserId))
        {
            throw new ArgumentException("Decided by user id is required.", nameof(decidedByUserId));
        }

        var request = await _db.ProjectDocumentRequests
            .Include(r => r.Document)
            .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);

        if (request == null)
        {
            throw new InvalidOperationException($"Request {requestId} was not found.");
        }

        if (request.Status != ProjectDocumentRequestStatus.Submitted)
        {
            throw new InvalidOperationException("Only submitted requests can be approved.");
        }

        switch (request.RequestType)
        {
            case ProjectDocumentRequestType.Upload:
                await ApproveUploadAsync(request, decidedByUserId, cancellationToken);
                break;
            case ProjectDocumentRequestType.Replace:
                await ApproveReplaceAsync(request, decidedByUserId, cancellationToken);
                break;
            case ProjectDocumentRequestType.Delete:
                await ApproveDeleteAsync(request, decidedByUserId, cancellationToken);
                break;
            default:
                throw new InvalidOperationException($"Unsupported request type {request.RequestType}.");
        }

        request.Status = ProjectDocumentRequestStatus.Approved;
        request.ReviewedByUserId = decidedByUserId;
        request.ReviewedAtUtc = _clock.UtcNow;
        request.ReviewerNote = note;

        await _db.SaveChangesAsync(cancellationToken);

        await Audit.Events.ProjectDocumentApproved(request.ProjectId, request.Id, decidedByUserId, request.RequestType, request.DocumentId)
            .WriteAsync(_audit);

        return request;
    }

    public async Task<ProjectDocumentRequest> RejectAsync(
        int requestId,
        string decidedByUserId,
        string? note,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(decidedByUserId))
        {
            throw new ArgumentException("Decided by user id is required.", nameof(decidedByUserId));
        }

        var request = await _db.ProjectDocumentRequests.FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (request == null)
        {
            throw new InvalidOperationException($"Request {requestId} was not found.");
        }

        if (request.Status != ProjectDocumentRequestStatus.Submitted)
        {
            throw new InvalidOperationException("Only submitted requests can be rejected.");
        }

        if (!string.IsNullOrWhiteSpace(request.TempStorageKey))
        {
            await _documentService.DeleteTempAsync(request.TempStorageKey, cancellationToken);
            request.TempStorageKey = null;
        }

        request.Status = ProjectDocumentRequestStatus.Rejected;
        request.ReviewedByUserId = decidedByUserId;
        request.ReviewedAtUtc = _clock.UtcNow;
        request.ReviewerNote = note;

        await _db.SaveChangesAsync(cancellationToken);

        await Audit.Events.ProjectDocumentRejected(request.ProjectId, request.Id, decidedByUserId, request.RequestType, request.DocumentId)
            .WriteAsync(_audit);

        return request;
    }

    private async Task ApproveUploadAsync(ProjectDocumentRequest request, string decidedByUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TempStorageKey) || request.OriginalFileName is null || request.ContentType is null || request.FileSize is null)
        {
            throw new InvalidOperationException("Upload requests must include a temporary file.");
        }

        var document = await _documentService.PublishNewAsync(
            request.ProjectId,
            request.StageId,
            request.TotId,
            request.Title,
            request.TempStorageKey,
            request.OriginalFileName,
            request.FileSize.Value,
            request.ContentType,
            request.RequestedByUserId,
            decidedByUserId,
            cancellationToken);

        request.DocumentId = document.Id;
        document.RequestId = request.Id;
        request.TempStorageKey = null;
    }

    private async Task ApproveReplaceAsync(ProjectDocumentRequest request, string decidedByUserId, CancellationToken cancellationToken)
    {
        if (request.DocumentId is null)
        {
            throw new InvalidOperationException("Replace requests require a target document.");
        }

        if (string.IsNullOrWhiteSpace(request.TempStorageKey) || request.OriginalFileName is null || request.ContentType is null || request.FileSize is null)
        {
            throw new InvalidOperationException("Replace requests must include a temporary file.");
        }

        var document = await _documentService.OverwriteAsync(
            request.DocumentId.Value,
            request.TempStorageKey,
            request.OriginalFileName,
            request.FileSize.Value,
            request.ContentType,
            request.RequestedByUserId,
            decidedByUserId,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            document.Title = request.Title;
        }

        document.RequestId = request.Id;
        request.TempStorageKey = null;
    }

    private async Task ApproveDeleteAsync(ProjectDocumentRequest request, string decidedByUserId, CancellationToken cancellationToken)
    {
        if (request.DocumentId is null)
        {
            throw new InvalidOperationException("Delete requests require a target document.");
        }

        var document = await _documentService.SoftDeleteAsync(request.DocumentId.Value, decidedByUserId, cancellationToken);
        document.RequestId = request.Id;
    }
}
