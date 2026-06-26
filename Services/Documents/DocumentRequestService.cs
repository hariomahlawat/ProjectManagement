using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Infrastructure;

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
        int? totId,
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

        ProjectTot? tot = null;
        if (totId.HasValue)
        {
            tot = await _db.ProjectTots
                .FirstOrDefaultAsync(t => t.Id == totId.Value && t.ProjectId == projectId, cancellationToken);

            if (tot is null)
            {
                throw new InvalidOperationException("Selected Transfer of Technology record was not found for this project.");
            }

            if (tot.Status == ProjectTotStatus.NotRequired)
            {
                throw new InvalidOperationException("Transfer of Technology is not required for this project.");
            }
        }

        var request = new ProjectDocumentRequest
        {
            ProjectId = projectId,
            StageId = stageId,
            DocumentId = null,
            Title = nomenclature?.Trim() ?? string.Empty,
            TotId = tot?.Id,
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

    public async Task<IReadOnlyList<ProjectDocumentRequest>> CreateUploadRequestsAsync(
        int projectId,
        int? stageId,
        int? totId,
        IReadOnlyList<DocumentUploadRequestItem> items,
        string requestedByUserId,
        CancellationToken cancellationToken)
    {
        if (items is null || items.Count == 0)
        {
            throw new ArgumentException("At least one document is required.", nameof(items));
        }

        if (string.IsNullOrWhiteSpace(requestedByUserId))
        {
            throw new ArgumentException("Requested by user id is required.", nameof(requestedByUserId));
        }

        if (items.Any(item => item.File is null || string.IsNullOrWhiteSpace(item.Title)))
        {
            throw new InvalidOperationException("Each selected file must have a title.");
        }

        if (stageId.HasValue)
        {
            var stageExists = await _db.ProjectStages
                .AnyAsync(stage => stage.Id == stageId.Value && stage.ProjectId == projectId, cancellationToken);
            if (!stageExists)
            {
                throw new InvalidOperationException("The selected project stage is no longer available.");
            }
        }

        ProjectTot? tot = null;
        if (totId.HasValue)
        {
            tot = await _db.ProjectTots
                .FirstOrDefaultAsync(t => t.Id == totId.Value && t.ProjectId == projectId, cancellationToken);
            if (tot is null || tot.Status == ProjectTotStatus.NotRequired)
            {
                throw new InvalidOperationException("Transfer of Technology is not available for this project.");
            }
        }

        var requestedAt = _clock.UtcNow;
        var requests = items.Select(item => new ProjectDocumentRequest
        {
            ProjectId = projectId,
            StageId = stageId,
            DocumentId = null,
            Title = item.Title.Trim(),
            TotId = tot?.Id,
            RequestType = ProjectDocumentRequestType.Upload,
            Status = ProjectDocumentRequestStatus.Submitted,
            TempStorageKey = item.File.StorageKey,
            OriginalFileName = item.File.OriginalFileName,
            ContentType = item.File.ContentType,
            FileSize = item.File.Length,
            RequestedByUserId = requestedByUserId,
            RequestedAtUtc = requestedAt
        }).ToList();

        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);
        _db.ProjectDocumentRequests.AddRange(requests);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        foreach (var request in requests)
        {
            try
            {
                await Audit.Events.ProjectDocumentRequested(projectId, request.Id, requestedByUserId, request.RequestType, request.DocumentId)
                    .WriteAsync(_audit);
            }
            catch
            {
                // The request batch is already committed. Audit failure must not orphan the staged files.
            }
        }

        return requests;
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
