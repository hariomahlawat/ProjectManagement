using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Models.Plans;
using ProjectManagement.Services.Approvals;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Pages.Approvals.Pending;

[Authorize(Roles = "Admin,HoD")]
public sealed class DetailsModel : PageModel
{
    private readonly IApprovalQueueService _queueService;
    private readonly ApplicationDbContext _db;

    public DetailsModel(IApprovalQueueService queueService, ApplicationDbContext db)
    {
        _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public ApprovalQueueDetailVm Detail { get; private set; } = default!;

    [BindProperty]
    public DecisionInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    [BindProperty(SupportsGet = true)] public bool AlreadyDecided { get; set; }

    public string? ErrorMessage => TempData["Error"] as string;
    public bool IsPending { get; private set; }
    public bool CanApprove => IsPending && Detail.Item.Readiness == ApprovalReadiness.Ready;
    public bool CanReject => IsPending;
    public string CurrentStatus { get; private set; } = "Pending";
    public string? DecidedByName { get; private set; }
    public DateTimeOffset? DecidedAtUtc { get; private set; }
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

    public sealed class DecisionInput
    {
        [Required] public ApprovalQueueType ApprovalType { get; set; }
        [Required] public string RequestId { get; set; } = string.Empty;
        [Required] public string Decision { get; set; } = string.Empty;
        [MaxLength(2000)] public string? Remarks { get; set; }
        public string? RowVersion { get; set; }
        public string? ReturnUrl { get; set; }
    }

    public string GetTypeLabel(ApprovalQueueType type) => type switch
    {
        ApprovalQueueType.StageChange => "Stage update",
        ApprovalQueueType.ProjectMeta => "Project information change",
        ApprovalQueueType.PlanApproval => "Timeline plan",
        ApprovalQueueType.DocRequest => "Project document request",
        ApprovalQueueType.TotRequest => "Transfer of Technology update",
        ApprovalQueueType.ProliferationYearly => "Yearly proliferation record",
        ApprovalQueueType.ProliferationGranular => "Unit-wise proliferation record",
        ApprovalQueueType.ActivityDelete => "Activity deletion",
        ApprovalQueueType.TrainingDelete => "Training deletion",
        ApprovalQueueType.RepositoryDocumentDelete => "Repository document deletion",
        _ => type.ToString()
    };

    public string GetModuleLabel(ApprovalQueueModule module) => module switch
    {
        ApprovalQueueModule.Projects => "Projects",
        ApprovalQueueModule.ProjectOfficeReports => "Project Office Reports",
        ApprovalQueueModule.Activities => "Institutional Activities",
        ApprovalQueueModule.DocumentRepository => "Document Repository",
        _ => module.ToString()
    };

    public string GetReadinessLabel(ApprovalReadiness readiness) => readiness switch
    {
        ApprovalReadiness.Ready => "Ready for decision",
        ApprovalReadiness.Waiting => "Waiting for an earlier decision",
        ApprovalReadiness.Blocked => "Blocked by missing or invalid information",
        ApprovalReadiness.Superseded => "Superseded by a newer request",
        ApprovalReadiness.Stale => "Project state changed",
        _ => readiness.ToString()
    };

    public string GetReadinessIcon(ApprovalReadiness readiness) => readiness switch
    {
        ApprovalReadiness.Ready => "bi-check2-circle",
        ApprovalReadiness.Waiting => "bi-hourglass-split",
        ApprovalReadiness.Blocked => "bi-exclamation-octagon",
        ApprovalReadiness.Superseded => "bi-files",
        ApprovalReadiness.Stale => "bi-arrow-repeat",
        _ => "bi-circle"
    };

    public string GetCheckIcon(ApprovalCheckState state) => state switch
    {
        ApprovalCheckState.Passed => "bi-check-circle-fill",
        ApprovalCheckState.Waiting => "bi-hourglass-split",
        ApprovalCheckState.Blocked => "bi-x-octagon-fill",
        ApprovalCheckState.Warning => "bi-exclamation-triangle-fill",
        _ => "bi-circle"
    };

    public string FormatDate(DateOnly? value)
        => value?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? "—";

    public string FormatDateTime(DateTimeOffset? value)
        => value.HasValue
            ? IstClock.ToIst(value.Value).ToString("dd MMM yyyy, h:mm tt", CultureInfo.InvariantCulture)
            : "—";

    public string FormatStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "—";
        return value switch
        {
            "NotStarted" => "Not started",
            "InProgress" => "In progress",
            "PendingApproval" => "Pending approval",
            _ => string.Concat(value.Select((character, index) => index > 0 && char.IsUpper(character) ? $" {character}" : character.ToString()))
        };
    }

    public string GetApproveConfirmation()
    {
        var subject = Detail.StageChange is not null
            ? $"{Detail.StageChange.StageName} will be marked {FormatStatus(Detail.StageChange.RequestedStatus).ToLowerInvariant()}"
            : Detail.Item.Summary.TrimEnd('.');
        return $"Approve this request? {subject}. The approved change will be applied immediately.";
    }

    private async Task PopulateDecisionStateAsync(
        ApprovalQueueType approvalType,
        string requestId,
        CancellationToken cancellationToken)
    {
        CurrentStatus = Detail.Item.Status;
        IsPending = IsPendingStatus(approvalType, CurrentStatus);

        switch (approvalType)
        {
            case ApprovalQueueType.StageChange:
                await PopulateStageDecisionAsync(requestId, cancellationToken);
                break;
            case ApprovalQueueType.ProjectMeta:
                await PopulateMetaDecisionAsync(requestId, cancellationToken);
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
                await PopulateYearlyDecisionAsync(requestId, cancellationToken);
                break;
            case ApprovalQueueType.ProliferationGranular:
                await PopulateGranularDecisionAsync(requestId, cancellationToken);
                break;
            case ApprovalQueueType.ActivityDelete:
                await PopulateActivityDecisionAsync(requestId, cancellationToken);
                break;
            case ApprovalQueueType.TrainingDelete:
                await PopulateTrainingDecisionAsync(requestId, cancellationToken);
                break;
            case ApprovalQueueType.RepositoryDocumentDelete:
                await PopulateRepositoryDecisionAsync(requestId, cancellationToken);
                break;
        }
    }

    private static bool IsPendingStatus(ApprovalQueueType type, string? status)
        => type switch
        {
            ApprovalQueueType.PlanApproval => string.Equals(status, PlanVersionStatus.PendingApproval.ToString(), StringComparison.OrdinalIgnoreCase),
            ApprovalQueueType.DocRequest => string.Equals(status, ProjectDocumentRequestStatus.Submitted.ToString(), StringComparison.OrdinalIgnoreCase),
            _ => string.Equals(status, "Pending", StringComparison.OrdinalIgnoreCase)
        };

    private async Task PopulateStageDecisionAsync(string requestId, CancellationToken ct)
    {
        if (!int.TryParse(requestId, out var id)) return;
        var value = await _db.StageChangeRequests.AsNoTracking().Where(item => item.Id == id)
            .Select(item => new { item.DecisionStatus, item.DecisionNote, item.DecidedByUserId, item.DecidedOn })
            .SingleOrDefaultAsync(ct);
        if (value is null) return;
        CurrentStatus = value.DecisionStatus;
        IsPending = string.Equals(value.DecisionStatus, "Pending", StringComparison.OrdinalIgnoreCase);
        DecisionRemarks = value.DecisionNote;
        await SetDecisionUserAsync(value.DecidedByUserId, value.DecidedOn, ct);
    }

    private async Task PopulateMetaDecisionAsync(string requestId, CancellationToken ct)
    {
        if (!int.TryParse(requestId, out var id)) return;
        var value = await _db.ProjectMetaChangeRequests.AsNoTracking().Where(item => item.Id == id)
            .Select(item => new { item.DecisionStatus, item.DecisionNote, item.DecidedByUserId, item.DecidedOnUtc })
            .SingleOrDefaultAsync(ct);
        if (value is null) return;
        CurrentStatus = value.DecisionStatus;
        IsPending = string.Equals(value.DecisionStatus, "Pending", StringComparison.OrdinalIgnoreCase);
        DecisionRemarks = value.DecisionNote;
        await SetDecisionUserAsync(value.DecidedByUserId, value.DecidedOnUtc, ct);
    }

    private async Task PopulatePlanDecisionAsync(string requestId, CancellationToken ct)
    {
        if (!int.TryParse(requestId, out var id)) return;
        var value = await _db.PlanVersions.AsNoTracking().Where(item => item.Id == id)
            .Select(item => new { item.Status, item.RejectionNote, item.RejectedByUserId, item.RejectedOn, item.ApprovedByUserId, item.ApprovedOn })
            .SingleOrDefaultAsync(ct);
        if (value is null) return;
        CurrentStatus = value.Status.ToString();
        IsPending = value.Status == PlanVersionStatus.PendingApproval;
        DecisionRemarks = value.RejectionNote;
        await SetDecisionUserAsync(value.RejectedOn.HasValue ? value.RejectedByUserId : value.ApprovedByUserId, value.RejectedOn ?? value.ApprovedOn, ct);
    }

    private async Task PopulateDocumentDecisionAsync(string requestId, CancellationToken ct)
    {
        if (!int.TryParse(requestId, out var id)) return;
        var value = await _db.ProjectDocumentRequests.AsNoTracking().Where(item => item.Id == id)
            .Select(item => new { item.Status, item.ReviewerNote, item.ReviewedByUserId, item.ReviewedAtUtc })
            .SingleOrDefaultAsync(ct);
        if (value is null) return;
        CurrentStatus = value.Status.ToString();
        IsPending = value.Status == ProjectDocumentRequestStatus.Submitted;
        DecisionRemarks = value.ReviewerNote;
        await SetDecisionUserAsync(value.ReviewedByUserId, value.ReviewedAtUtc, ct);
    }

    private async Task PopulateTotDecisionAsync(string requestId, CancellationToken ct)
    {
        if (!int.TryParse(requestId, out var id)) return;
        var value = await _db.ProjectTotRequests.AsNoTracking().Where(item => item.Id == id)
            .Select(item => new { item.DecisionState, item.DecidedByUserId, item.DecidedOnUtc })
            .SingleOrDefaultAsync(ct);
        if (value is null) return;
        CurrentStatus = value.DecisionState.ToString();
        IsPending = string.Equals(CurrentStatus, "Pending", StringComparison.OrdinalIgnoreCase);
        await SetDecisionUserAsync(value.DecidedByUserId, ToOffset(value.DecidedOnUtc), ct);
    }

    private async Task PopulateYearlyDecisionAsync(string requestId, CancellationToken ct)
    {
        if (!Guid.TryParse(requestId, out var id)) return;
        var value = await _db.ProliferationYearlies.AsNoTracking().Where(item => item.Id == id)
            .Select(item => new { item.ApprovalStatus, item.ApprovedByUserId, item.ApprovedOnUtc })
            .SingleOrDefaultAsync(ct);
        if (value is null) return;
        CurrentStatus = value.ApprovalStatus.ToString();
        IsPending = value.ApprovalStatus == ApprovalStatus.Pending;
        await SetDecisionUserAsync(value.ApprovedByUserId, ToOffset(value.ApprovedOnUtc), ct);
    }

    private async Task PopulateGranularDecisionAsync(string requestId, CancellationToken ct)
    {
        if (!Guid.TryParse(requestId, out var id)) return;
        var value = await _db.ProliferationGranularEntries.AsNoTracking().Where(item => item.Id == id)
            .Select(item => new { item.ApprovalStatus, item.ApprovedByUserId, item.ApprovedOnUtc })
            .SingleOrDefaultAsync(ct);
        if (value is null) return;
        CurrentStatus = value.ApprovalStatus.ToString();
        IsPending = value.ApprovalStatus == ApprovalStatus.Pending;
        await SetDecisionUserAsync(value.ApprovedByUserId, ToOffset(value.ApprovedOnUtc), ct);
    }

    private async Task PopulateActivityDecisionAsync(string requestId, CancellationToken ct)
    {
        if (!int.TryParse(requestId, out var id)) return;
        var value = await _db.ActivityDeleteRequests.AsNoTracking().Where(item => item.Id == id)
            .Select(item => new { item.ApprovedAtUtc, item.ApprovedByUserId, item.RejectedAtUtc, item.RejectedByUserId })
            .SingleOrDefaultAsync(ct);
        if (value is null) return;
        CurrentStatus = value.ApprovedAtUtc.HasValue ? "Approved" : value.RejectedAtUtc.HasValue ? "Rejected" : "Pending";
        IsPending = !value.ApprovedAtUtc.HasValue && !value.RejectedAtUtc.HasValue;
        await SetDecisionUserAsync(value.ApprovedAtUtc.HasValue ? value.ApprovedByUserId : value.RejectedByUserId, value.ApprovedAtUtc ?? value.RejectedAtUtc, ct);
    }

    private async Task PopulateTrainingDecisionAsync(string requestId, CancellationToken ct)
    {
        if (!Guid.TryParse(requestId, out var id)) return;
        var value = await _db.TrainingDeleteRequests.AsNoTracking().Where(item => item.Id == id)
            .Select(item => new { item.Status, item.DecisionNotes, item.DecidedByUserId, item.DecidedAtUtc })
            .SingleOrDefaultAsync(ct);
        if (value is null) return;
        CurrentStatus = value.Status.ToString();
        IsPending = value.Status == TrainingDeleteRequestStatus.Pending;
        DecisionRemarks = value.DecisionNotes;
        await SetDecisionUserAsync(value.DecidedByUserId, value.DecidedAtUtc, ct);
    }

    private async Task PopulateRepositoryDecisionAsync(string requestId, CancellationToken ct)
    {
        if (!long.TryParse(requestId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) return;
        var value = await _db.DocumentDeleteRequests.AsNoTracking().Where(item => item.Id == id)
            .Select(item => new { item.ApprovedAtUtc, item.ApprovedByUserId })
            .SingleOrDefaultAsync(ct);
        if (value is null) return;
        CurrentStatus = value.ApprovedAtUtc.HasValue ? "Approved" : "Pending";
        IsPending = !value.ApprovedAtUtc.HasValue;
        await SetDecisionUserAsync(value.ApprovedByUserId, value.ApprovedAtUtc, ct);
    }

    private async Task SetDecisionUserAsync(string? userId, DateTimeOffset? decidedAt, CancellationToken ct)
    {
        DecidedAtUtc = decidedAt;
        if (string.IsNullOrWhiteSpace(userId)) return;
        DecidedByName = await _db.Users.AsNoTracking().Where(user => user.Id == userId)
            .Select(user => user.FullName ?? user.UserName ?? user.Email)
            .SingleOrDefaultAsync(ct);
    }

    private static DateTimeOffset? ToOffset(DateTime? value)
        => value.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc))
            : null;
}
