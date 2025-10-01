using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;

namespace ProjectManagement.Services.Documents;

public sealed class DocumentRequestService : IDocumentRequestService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditService _audit;

    public DocumentRequestService(ApplicationDbContext db, IClock clock, IAuditService audit)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    public async Task<ProjectDocumentRequest> CreateUploadRequestAsync(
        int projectId,
        int? stageId,
        string nomenclature,
        DocumentFileDescriptor file,
        string requestedByUserId,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            throw new ArgumentNullException(nameof(file));
        }

        if (string.IsNullOrWhiteSpace(requestedByUserId))
        {
            throw new ArgumentException("Requested by user id is required.", nameof(requestedByUserId));
        }

        var request = new ProjectDocumentRequest
        {
            ProjectId = projectId,
            StageId = stageId,
            DocumentId = null,
            Title = nomenclature?.Trim() ?? string.Empty,
            RequestType = ProjectDocumentRequestType.Upload,
            Status = ProjectDocumentRequestStatus.Submitted,
            TempStorageKey = file.StorageKey,
            OriginalFileName = file.OriginalFileName,
            ContentType = file.ContentType,
            FileSize = file.Length,
            RequestedByUserId = requestedByUserId,
            RequestedAtUtc = _clock.UtcNow,
        };

        _db.ProjectDocumentRequests.Add(request);
        await _db.SaveChangesAsync(cancellationToken);

        await Audit.Events.ProjectDocumentRequested(projectId, request.Id, requestedByUserId, request.RequestType, request.DocumentId)
            .WriteAsync(_audit);

        return request;
    }

    public async Task<ProjectDocumentRequest> CreateReplaceRequestAsync(
        int documentId,
        string? newTitle,
        DocumentFileDescriptor file,
        string requestedByUserId,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            throw new ArgumentNullException(nameof(file));
        }

        if (string.IsNullOrWhiteSpace(requestedByUserId))
        {
            throw new ArgumentException("Requested by user id is required.", nameof(requestedByUserId));
        }

        var document = await _db.ProjectDocuments.FirstOrDefaultAsync(x => x.Id == documentId, cancellationToken);
        if (document == null)
        {
            throw new InvalidOperationException($"Document {documentId} was not found.");
        }

        await EnsureNoPendingForDocumentAsync(documentId, cancellationToken);

        var title = string.IsNullOrWhiteSpace(newTitle)
            ? document.Title
            : newTitle.Trim();

        var request = new ProjectDocumentRequest
        {
            ProjectId = document.ProjectId,
            StageId = document.StageId,
            DocumentId = document.Id,
            Title = title,
            RequestType = ProjectDocumentRequestType.Replace,
            Status = ProjectDocumentRequestStatus.Submitted,
            TempStorageKey = file.StorageKey,
            OriginalFileName = file.OriginalFileName,
            ContentType = file.ContentType,
            FileSize = file.Length,
            RequestedByUserId = requestedByUserId,
            RequestedAtUtc = _clock.UtcNow,
        };

        _db.ProjectDocumentRequests.Add(request);
        await _db.SaveChangesAsync(cancellationToken);

        await Audit.Events.ProjectDocumentRequested(document.ProjectId, request.Id, requestedByUserId, request.RequestType, request.DocumentId)
            .WriteAsync(_audit);

        return request;
    }

    public async Task<ProjectDocumentRequest> CreateDeleteRequestAsync(
        int documentId,
        string? reason,
        string requestedByUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(requestedByUserId))
        {
            throw new ArgumentException("Requested by user id is required.", nameof(requestedByUserId));
        }

        var document = await _db.ProjectDocuments.FirstOrDefaultAsync(x => x.Id == documentId, cancellationToken);
        if (document == null)
        {
            throw new InvalidOperationException($"Document {documentId} was not found.");
        }

        await EnsureNoPendingForDocumentAsync(documentId, cancellationToken);

        var request = new ProjectDocumentRequest
        {
            ProjectId = document.ProjectId,
            StageId = document.StageId,
            DocumentId = document.Id,
            Title = document.Title,
            RequestType = ProjectDocumentRequestType.Delete,
            Status = ProjectDocumentRequestStatus.Submitted,
            TempStorageKey = null,
            OriginalFileName = document.OriginalFileName,
            ContentType = document.ContentType,
            FileSize = document.FileSize,
            RequestedByUserId = requestedByUserId,
            RequestedAtUtc = _clock.UtcNow,
            Description = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
        };

        _db.ProjectDocumentRequests.Add(request);
        await _db.SaveChangesAsync(cancellationToken);

        await Audit.Events.ProjectDocumentRequested(document.ProjectId, request.Id, requestedByUserId, request.RequestType, request.DocumentId)
            .WriteAsync(_audit);

        return request;
    }

    private async Task EnsureNoPendingForDocumentAsync(int documentId, CancellationToken cancellationToken)
    {
        var hasPending = await _db.ProjectDocumentRequests
            .AsNoTracking()
            .Where(x => x.DocumentId == documentId)
            .AnyAsync(x => x.Status == ProjectDocumentRequestStatus.Draft || x.Status == ProjectDocumentRequestStatus.Submitted, cancellationToken);

        if (hasPending)
        {
            throw new InvalidOperationException("A pending request already exists for this document.");
        }
    }
}
