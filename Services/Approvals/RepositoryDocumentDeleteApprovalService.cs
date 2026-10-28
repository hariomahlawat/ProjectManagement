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

        // Requesting and approving a deletion are separate duties, as for other approval types.
        if (string.Equals(request.RequestedByUserId, actorUserId, StringComparison.Ordinal))
        {
            return ApprovalDecisionResult.Forbidden("You requested this deletion, so another approver must decide it.");
        }

        var document = request.Document;
        var now = _clock.UtcNow;

        // The request row is claimed in the database, so when two approvers act at the same
        // moment only the first decision is applied.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var pending = _db.DocumentDeleteRequests.Where(item => item.Id == requestId && item.ApprovedAtUtc == null);

        if (action == ApprovalDecisionAction.Reject)
        {
            if (await pending.ExecuteDeleteAsync(cancellationToken) == 0)
            {
                return ApprovalDecisionResult.AlreadyDecided("This repository document request has already been decided.");
            }

            _audit.Stage(
                document.Id,
                actorUserId,
                "DeleteRejected",
                new { request.Id, Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim() });
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ApprovalDecisionResult.Success("Repository document delete request rejected.");
        }

        var claimed = await pending.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(item => item.ApprovedAtUtc, now)
                .SetProperty(item => item.ApprovedByUserId, actorUserId),
            cancellationToken);
        if (claimed == 0)
        {
            return ApprovalDecisionResult.AlreadyDecided("This repository document request has already been decided.");
        }

        document.IsDeleted = true;
        document.IsActive = false;
        document.DeletedAtUtc = now.UtcDateTime;
        document.DeletedByUserId = actorUserId;
        document.DeleteReason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        document.UpdatedAtUtc = now.UtcDateTime;
        document.UpdatedByUserId = actorUserId;

        _audit.Stage(document.Id, actorUserId, "SoftDeleted", new { request.Id, request.Reason });
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ApprovalDecisionResult.Success("Document moved to trash.");
    }
}
