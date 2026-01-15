using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Approvals;
using ProjectManagement.Services.Projects;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Pages.Approvals.Pending;

[Authorize(Roles = "Admin,HoD")]
public class DetailsModel : PageModel
{
    // SECTION: Dependencies
    private readonly IApprovalQueueService _queueService;
    private readonly ApplicationDbContext _db;

    public DetailsModel(IApprovalQueueService queueService, ApplicationDbContext db)
    {
        _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    // SECTION: View model state
    public ApprovalQueueDetailVm Detail { get; private set; } = default!;

    [BindProperty]
    public DecisionInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool AlreadyDecided { get; set; }

    public string? ErrorMessage => TempData["Error"] as string;

    public bool IsPending { get; private set; }

    public string CurrentStatus { get; private set; } = "Pending";

    public string? DecidedByName { get; private set; }

    public DateTime? DecidedAt { get; private set; }

    public string? DecisionRemarks { get; private set; }

    public async Task<IActionResult> OnGetAsync(string type, string id, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ApprovalQueueType>(type, true, out var parsedType))
        {
            return NotFound();
        }

        var detail = await _queueService.GetDetailAsync(parsedType, id, User, cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        Detail = detail;
        Input = new DecisionInput
        {
            ApprovalType = detail.Item.ApprovalType,
            RequestId = detail.Item.RequestId,
            RowVersion = detail.Item.ConcurrencyToken,
            ReturnUrl = ReturnUrl
        };

        await PopulateDecisionStateAsync(detail.Item.ApprovalType, detail.Item.RequestId, cancellationToken);

        return Page();
    }

    // SECTION: Decision input model
    public sealed class DecisionInput
    {
        [Required]
        public ApprovalQueueType ApprovalType { get; set; }

        [Required]
        public string RequestId { get; set; } = string.Empty;

        [Required]
        public string Decision { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string? Remarks { get; set; }

        public string? RowVersion { get; set; }

        public string? ReturnUrl { get; set; }
    }

    // SECTION: Decision state
    private async Task PopulateDecisionStateAsync(
        ApprovalQueueType approvalType,
        string requestId,
        CancellationToken cancellationToken)
    {
        CurrentStatus = Detail.Item.Status;
        IsPending = string.Equals(CurrentStatus, "Pending", StringComparison.OrdinalIgnoreCase);

        switch (approvalType)
        {
            case ApprovalQueueType.StageChange:
                await PopulateStageDecisionAsync(requestId, cancellationToken);
                break;
            case ApprovalQueueType.ProjectMeta:
                await PopulateProjectMetaDecisionAsync(requestId, cancellationToken);
                break;
            case ApprovalQueueType.PlanApproval:
                await PopulatePlanDecisionAsync(requestId, cancellationToken);
                break;
            case ApprovalQueueType.DocRequest:
                await PopulateDocumentDecisionAsync(requestId, cancellationToken);
                break;
            case ApprovalQueueType.TotRequest:
                await PopulateTotDecisionAsync(requestId, cancellationToken);
                break;
            case ApprovalQueueType.ProliferationYearly:
                await PopulateProliferationYearlyDecisionAsync(requestId, cancellationToken);
                break;
            case ApprovalQueueType.ProliferationGranular:
                await PopulateProliferationGranularDecisionAsync(requestId, cancellationToken);
                break;
            default:
                break;
        }
    }

    private async Task PopulateStageDecisionAsync(string requestId, CancellationToken cancellationToken)
    {
        if (!int.TryParse(requestId, out var id))
        {
            return;
        }

        var request = await _db.StageChangeRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (request is null)
        {
            return;
        }

        CurrentStatus = request.DecisionStatus;
        IsPending = string.Equals(request.DecisionStatus, "Pending", StringComparison.OrdinalIgnoreCase);
        DecisionRemarks = request.DecisionNote;
        await PopulateDecisionUserAsync(request.DecidedByUserId, request.DecidedOn?.UtcDateTime, cancellationToken);
    }

    private async Task PopulateProjectMetaDecisionAsync(string requestId, CancellationToken cancellationToken)
    {
        if (!int.TryParse(requestId, out var id))
        {
            return;
        }

        var request = await _db.ProjectMetaChangeRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (request is null)
        {
            return;
        }

        CurrentStatus = request.DecisionStatus;
        IsPending = string.Equals(request.DecisionStatus, ProjectMetaDecisionStatuses.Pending, StringComparison.OrdinalIgnoreCase);
        DecisionRemarks = request.DecisionNote;
        await PopulateDecisionUserAsync(request.DecidedByUserId, request.DecidedOnUtc?.UtcDateTime, cancellationToken);
    }

    private async Task PopulatePlanDecisionAsync(string requestId, CancellationToken cancellationToken)
    {
        if (!int.TryParse(requestId, out var id))
        {
            return;
        }

        var plan = await _db.PlanVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (plan is null)
        {
            return;
        }

        IsPending = plan.Status == PlanVersionStatus.PendingApproval;
        CurrentStatus = IsPending
            ? "Pending"
            : plan.RejectedOn.HasValue
                ? "Rejected"
                : "Approved";

        DecisionRemarks = plan.RejectionNote;

        var decidedByUserId = plan.RejectedOn.HasValue ? plan.RejectedByUserId : plan.ApprovedByUserId;
        var decidedAt = plan.RejectedOn ?? plan.ApprovedOn;
        await PopulateDecisionUserAsync(decidedByUserId, decidedAt?.UtcDateTime, cancellationToken);
    }

    private async Task PopulateDocumentDecisionAsync(string requestId, CancellationToken cancellationToken)
    {
        if (!int.TryParse(requestId, out var id))
        {
            return;
        }

        var request = await _db.ProjectDocumentRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (request is null)
        {
            return;
        }

        CurrentStatus = request.Status.ToString();
        IsPending = request.Status == ProjectDocumentRequestStatus.Submitted;
        DecisionRemarks = request.ReviewerNote;
        await PopulateDecisionUserAsync(request.ReviewedByUserId, request.ReviewedAtUtc?.UtcDateTime, cancellationToken);
    }

    private async Task PopulateTotDecisionAsync(string requestId, CancellationToken cancellationToken)
    {
        if (!int.TryParse(requestId, out var id))
        {
            return;
        }

        var request = await _db.ProjectTotRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (request is null)
        {
            return;
        }

        CurrentStatus = request.DecisionState.ToString();
        IsPending = request.DecisionState == ProjectTotRequestDecisionState.Pending;
        await PopulateDecisionUserAsync(request.DecidedByUserId, request.DecidedOnUtc, cancellationToken);
    }

    private async Task PopulateProliferationYearlyDecisionAsync(string requestId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(requestId, out var id))
        {
            return;
        }

        var record = await _db.ProliferationYearlies
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (record is null)
        {
            return;
        }

        CurrentStatus = record.ApprovalStatus.ToString();
        IsPending = record.ApprovalStatus == Areas.ProjectOfficeReports.Domain.ApprovalStatus.Pending;
        await PopulateDecisionUserAsync(record.ApprovedByUserId, record.ApprovedOnUtc, cancellationToken);
    }

    private async Task PopulateProliferationGranularDecisionAsync(string requestId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(requestId, out var id))
        {
            return;
        }

        var record = await _db.ProliferationGranularEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (record is null)
        {
            return;
        }

        CurrentStatus = record.ApprovalStatus.ToString();
        IsPending = record.ApprovalStatus == Areas.ProjectOfficeReports.Domain.ApprovalStatus.Pending;
        await PopulateDecisionUserAsync(record.ApprovedByUserId, record.ApprovedOnUtc, cancellationToken);
    }

    private async Task PopulateDecisionUserAsync(string? userId, DateTime? decidedAt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            DecidedByName = "Unknown";
            DecidedAt = decidedAt;
            return;
        }

        var user = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.FullName, u.UserName, u.Email })
            .FirstOrDefaultAsync(cancellationToken);

        DecidedByName = ResolveUserDisplayName(user?.FullName, user?.UserName, user?.Email);
        DecidedAt = decidedAt;
    }

    private static string ResolveUserDisplayName(string? fullName, string? userName, string? email)
    {
        if (!string.IsNullOrWhiteSpace(fullName))
        {
            return fullName;
        }

        if (!string.IsNullOrWhiteSpace(userName))
        {
            return userName;
        }

        return !string.IsNullOrWhiteSpace(email) ? email : "Unknown";
    }
}
