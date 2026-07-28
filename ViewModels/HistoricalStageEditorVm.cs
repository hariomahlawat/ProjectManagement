using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;

namespace ProjectManagement.ViewModels;

/// <summary>
/// Outcomes that can be established from documentary evidence for a legacy,
/// terminal project. "Ceased" is represented by an unfinished standard
/// ProjectStage row and is available only for cancelled projects.
/// </summary>
public enum HistoricalStageOutcome
{
    NotRecorded = 0,
    Completed = 1,
    Skipped = 2,
    Ceased = 3
}

public sealed class HistoricalStageEditorVm
{
    public static readonly HistoricalStageEditorVm Empty = new();

    public int ProjectId { get; init; }

    public string ProjectName { get; init; } = string.Empty;

    public ProjectLifecycleStatus LifecycleStatus { get; init; } = ProjectLifecycleStatus.Active;

    public bool IsLegacy { get; init; }

    public bool IsDeleted { get; init; }

    public DateOnly LatestPermittedDate { get; init; }

    public IReadOnlyList<HistoricalStageEditorRowVm> Rows { get; init; } =
        Array.Empty<HistoricalStageEditorRowVm>();

    public bool IsCancelled => LifecycleStatus == ProjectLifecycleStatus.Cancelled;

    public bool IsAvailable =>
        ProjectId > 0 &&
        IsLegacy &&
        !IsDeleted &&
        (LifecycleStatus is ProjectLifecycleStatus.Completed or ProjectLifecycleStatus.Cancelled);
}

public sealed class HistoricalStageEditorRowVm
{
    public string StageCode { get; init; } = string.Empty;

    public string StageName { get; init; } = string.Empty;

    public int SortOrder { get; init; }

    public StageStatus ExistingStatus { get; init; } = StageStatus.NotStarted;

    public HistoricalStageOutcome Outcome { get; init; }

    public DateOnly? ActualStart { get; init; }

    public DateOnly? CompletedOn { get; init; }

    public bool HasRecordedData { get; init; }

    public bool IsArppManaged { get; init; }

    public string? ArppSourceLabel { get; init; }

    public DateOnly? ArppCompletionDate { get; init; }
}

public sealed class HistoricalStageRecordInput
{
    public const int EvidenceNoteMaxLength = 800;

    public int ProjectId { get; set; }

    [Required(ErrorMessage = "Describe the documentary source used for this historical update.")]
    [StringLength(
        EvidenceNoteMaxLength,
        MinimumLength = 5,
        ErrorMessage = "The evidence note must be between 5 and 800 characters.")]
    public string EvidenceNote { get; set; } = string.Empty;

    public IList<HistoricalStageRecordRowInput> Rows { get; set; } =
        new List<HistoricalStageRecordRowInput>();
}

public sealed class HistoricalStageRecordRowInput
{
    [Required]
    [StringLength(16)]
    public string StageCode { get; set; } = string.Empty;

    public HistoricalStageOutcome Outcome { get; set; }

    public DateOnly? ActualStart { get; set; }

    public DateOnly? CompletedOn { get; set; }
}
