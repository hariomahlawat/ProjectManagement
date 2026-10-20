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

    /// <summary>The server's messages for the last refused post; shown inside the editor.</summary>
    public IReadOnlyList<string> SubmissionErrors { get; init; } = Array.Empty<string>();

    /// <summary>The editor shows the user's entries from that refused post, not the saved plan.</summary>
    public bool RestoredSubmission { get; init; }

    /// <summary>
    /// Messages that name this stage, so its fields can be marked. The planner's messages
    /// either start with the stage name or say "For {name}," / "Duration for {name} ".
    /// </summary>
    public IReadOnlyList<string> ErrorsFor(string stageName) =>
        string.IsNullOrWhiteSpace(stageName)
            ? Array.Empty<string>()
            : SubmissionErrors
                .Where(message =>
                    message.StartsWith(stageName + " ", StringComparison.Ordinal) ||
                    message.StartsWith(stageName + ":", StringComparison.Ordinal) ||
                    message.Contains($"For {stageName},", StringComparison.Ordinal) ||
                    message.Contains($"Duration for {stageName} ", StringComparison.Ordinal))
                .ToArray();
}

public sealed class PlanEditorStateVm
{
    public bool HasDraft { get; init; }
    public string EditToken { get; init; } = "none";
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
    public bool CanDiscardDraft { get; init; }
    public bool HasPendingSubmission { get; init; }
    public int? PendingPlanVersionId { get; init; }
    public bool PendingOwnedByCurrentUser { get; init; }
    public DateTimeOffset? PendingSubmittedOn { get; init; }
    public string? PendingSubmittedBy { get; init; }
    public bool CanSubmit { get; init; } = true;
    public string? SubmissionBlockedReason { get; init; }
    public bool PncApplicable { get; init; } = true;
    /// <summary>
    /// When the user's draft (or pending submission) was created or submitted. PlanVersion
    /// does not record later saves, so the UI presents this as the draft's start, not its last save.
    /// </summary>
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
