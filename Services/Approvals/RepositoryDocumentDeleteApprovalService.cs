using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Services.Approvals;

public sealed class RepositoryDocumentDeleteApprovalService
{
    private readonly ApplicationDbContext _db;
    private readonly IDocRepoAuditService _audit;
    private readonly IClock _clock;

    public RepositoryDocumentDeleteApprovalService(
        ApplicationDbContext db,
        IDocRepoAuditService audit,
        IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<ApprovalDecisionResult> DecideAsync(
        long requestId,
        ApprovalDecisionAction action,
        string actorUserId,
        string? remarks,
        CancellationToken cancellationToken = default)
    {
        var request = await _db.DocumentDeleteRequests
            .Include(item => item.Document)
            .SingleOrDefaultAsync(item => item.Id == requestId, cancellationToken);

        if (request is null)
        {
            return ApprovalDecisionResult.NotFound("Repository document delete request not found.");
        }

        if (request.ApprovedAtUtc.HasValue)
        {
            return ApprovalDecisionResult.AlreadyDecided("This repository document request has already been approved.");
        }

        if (request.Document is null || request.Document.IsDeleted)
        {
            return ApprovalDecisionResult.ValidationFailed("The document is no longer available.");
        }

        var document = request.Document;
        var now = _clock.UtcNow;

        if (action == ApprovalDecisionAction.Reject)
        {
            _db.DocumentDeleteRequests.Remove(request);
            await _db.SaveChangesAsync(cancellationToken);

            await _audit.WriteAsync(
                document.Id,
                actorUserId,
                "DeleteRejected",
                new { request.Id, Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim() },
                cancellationToken);

            return ApprovalDecisionResult.Success("Repository document delete request rejected.");
        }

        request.ApprovedAtUtc = now;
        request.ApprovedByUserId = actorUserId;

        document.IsDeleted = true;
        document.IsActive = false;
        document.DeletedAtUtc = now.UtcDateTime;
        document.DeletedByUserId = actorUserId;
        document.DeleteReason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        document.UpdatedAtUtc = now.UtcDateTime;
        document.UpdatedByUserId = actorUserId;

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync(
            document.Id,
            actorUserId,
            "SoftDeleted",
            new { request.Id, request.Reason },
            cancellationToken);

        return ApprovalDecisionResult.Success("Document moved to trash.");
    }
}
