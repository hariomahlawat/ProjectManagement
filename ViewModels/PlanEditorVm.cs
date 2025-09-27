using System;
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
}
