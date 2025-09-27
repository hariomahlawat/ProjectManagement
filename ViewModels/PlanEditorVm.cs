using System;
using System.Collections.Generic;
using ProjectManagement.Models.Plans;

namespace ProjectManagement.ViewModels;

public static class PlanEditorModes
{
    public const string Exact = "Exact";
    public const string Durations = "Durations";
}

public sealed class PlanEditorVm
{
    public PlanEditVm Exact { get; init; } = new();
    public PlanDurationVm Durations { get; init; } = new();
    public string ActiveMode { get; init; } = PlanEditorModes.Exact;
    public PlanEditorStateVm State { get; init; } = new();
}

public sealed class PlanEditorStateVm
{
    public bool HasDraft { get; init; }
    public bool IsLocked { get; init; }
    public PlanVersionStatus? Status { get; init; }
    public int? VersionNo { get; init; }
    public DateTimeOffset? CreatedOn { get; init; }
    public DateTimeOffset? SubmittedOn { get; init; }
    public string? SubmittedBy { get; init; }
    public DateTimeOffset? RejectedOn { get; init; }
    public string? RejectedBy { get; init; }
    public string? RejectionNote { get; init; }
    public bool HasMyDraft { get; init; }
    public bool HasPendingSubmission { get; init; }
    public bool PendingOwnedByCurrentUser { get; init; }
    public DateTimeOffset? PendingSubmittedOn { get; init; }
    public string? PendingSubmittedBy { get; init; }
    public bool CanSubmit { get; init; } = true;
    public string? SubmissionBlockedReason { get; init; }
    public bool PncApplicable { get; init; } = true;
    public DateTimeOffset? LastSavedOn { get; init; }
    public IReadOnlyList<PlanApprovalHistoryVm> ApprovalHistory { get; init; } = Array.Empty<PlanApprovalHistoryVm>();
}

public sealed class PlanApprovalHistoryVm
{
    public string Action { get; init; } = string.Empty;
    public string? PerformedBy { get; init; }
    public DateTimeOffset PerformedOn { get; init; }
    public string? Note { get; init; }
}
