using System;
using ProjectManagement.Models;

namespace ProjectManagement.ViewModels;

public sealed class ProjectLifecycleActionsViewModel
{
    public static readonly ProjectLifecycleActionsViewModel Empty = new();

    public ProjectLifecycleStatus Status { get; init; } = ProjectLifecycleStatus.Active;

    public bool CanManageLifecycle { get; init; }

    /// <summary>
    /// True when an active project can be marked completed or a completed project's
    /// completion information can be improved.
    /// </summary>
    public bool CanMarkCompleted { get; init; }

    /// <summary>
    /// Retained for compatibility with older views. The unified completion form now
    /// handles exact-date updates directly.
    /// </summary>
    public bool CanEndorseCompletedDate { get; init; }

    public bool CanCancel { get; init; }

    public bool CanReactivate { get; init; }

    public int? CompletedYear { get; init; }

    public short? CompletedMonth { get; init; }

    public DateOnly? CompletedOn { get; init; }

    public ProjectCompletionPrecision CompletionPrecision { get; init; } = ProjectCompletionPrecision.NotKnown;

    public string CompletionDisplay { get; init; } = "Not recorded";

    public DateOnly TodayLocal { get; init; }

    public DateOnly? CancelledOn { get; init; }

    public string? CancelReason { get; init; }

    public bool HasActions => CanMarkCompleted || CanEndorseCompletedDate || CanCancel || CanReactivate;
}
