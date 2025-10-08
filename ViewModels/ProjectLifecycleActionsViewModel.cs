using System;
using ProjectManagement.Models;

namespace ProjectManagement.ViewModels;

public sealed class ProjectLifecycleActionsViewModel
{
    public static readonly ProjectLifecycleActionsViewModel Empty = new();

    public ProjectLifecycleStatus Status { get; init; } = ProjectLifecycleStatus.Active;

    public bool CanMarkCompleted { get; init; }

    public bool CanEndorseCompletedDate { get; init; }

    public bool CanCancel { get; init; }

    public int? CompletedYear { get; init; }

    public DateOnly? CompletedOn { get; init; }

    public DateOnly? CancelledOn { get; init; }

    public string? CancelReason { get; init; }

    public bool HasActions => CanMarkCompleted || CanEndorseCompletedDate || CanCancel;
}
