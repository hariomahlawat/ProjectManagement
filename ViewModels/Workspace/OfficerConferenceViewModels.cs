using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.ViewModels.Workspace;

public enum ConferenceItemKind
{
    Project = 1,
    ProjectIdea = 2,
    ActionTask = 3
}

public sealed class OfficerConferenceVm
{
    public string OfficerUserId { get; init; } = string.Empty;
    public string OfficerName { get; init; } = string.Empty;
    public string OfficerRank { get; init; } = string.Empty;
    public string OfficerDisplayName => string.IsNullOrWhiteSpace(OfficerRank)
        ? OfficerName
        : $"{OfficerRank} {OfficerName}";

    public string OfficerInitial { get; init; } = "P";
    public int ProjectCount { get; init; }
    public int IdeaCount { get; init; }
    public int OtherTaskCount { get; init; }

    public string? PreviousOfficerUserId { get; init; }
    public string? NextOfficerUserId { get; init; }
    public IReadOnlyList<OfficerConferenceOfficerOptionVm> OfficerOptions { get; init; }
        = Array.Empty<OfficerConferenceOfficerOptionVm>();

    public IReadOnlyList<OfficerConferenceSectionVm> Sections { get; init; }
        = Array.Empty<OfficerConferenceSectionVm>();
}

public sealed record OfficerConferenceOfficerOptionVm(
    string UserId,
    string DisplayName,
    bool IsSelected);

public sealed class OfficerConferenceSectionVm
{
    public ConferenceItemKind Kind { get; init; }
    public string Title { get; init; } = string.Empty;
    public string IconClass { get; init; } = string.Empty;
    public IReadOnlyList<OfficerConferenceItemVm> Items { get; init; }
        = Array.Empty<OfficerConferenceItemVm>();
}

public sealed class OfficerConferenceItemVm
{
    public ConferenceItemKind Kind { get; init; }
    public int ItemId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string OpenUrl { get; init; } = string.Empty;

    public string CurrentStateCode { get; init; } = string.Empty;
    public string CurrentStateName { get; init; } = string.Empty;
    public string? CurrentContext { get; init; }
    public string? AttentionText { get; init; }
    public bool RequiresAttention { get; init; }

    public ConferenceDirectionVm? LatestDirection { get; init; }
    public IReadOnlyList<ConferenceProgressEntryVm> ProgressEntries { get; init; }
        = Array.Empty<ConferenceProgressEntryVm>();
    public string? EmptyProgressText { get; init; }

    // Retained for Action Tasks until their progress semantics are reviewed separately.
    public string ProgressSummary { get; init; } = string.Empty;
    public string? LatestProgressText { get; init; }
}

public sealed class ConferenceProgressEntryVm
{
    public string Label { get; init; } = string.Empty;
    public string? Title { get; init; }
    public string? Body { get; init; }
    public string? AuthorName { get; init; }
    public DateTime? ActivityAtUtc { get; init; }
    public string? EmptyText { get; init; }
}

public sealed class ConferenceDirectionVm
{
    public int Id { get; init; }
    public string Body { get; init; } = string.Empty;
    public string AuthorName { get; init; } = string.Empty;
    public string AuthorRole { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
    public string SnapshotLabel { get; init; } = string.Empty;
    public string SnapshotValue { get; init; } = string.Empty;
}


public sealed class AddConferenceDirectionInput
{
    [Required]
    [StringLength(450)]
    public string OfficerUserId { get; set; } = string.Empty;

    [EnumDataType(typeof(ConferenceItemKind))]
    public ConferenceItemKind Kind { get; set; }

    [Range(1, int.MaxValue)]
    public int ItemId { get; set; }

    [Required]
    [StringLength(4000)]
    public string Body { get; set; } = string.Empty;
}

public sealed record AddConferenceRemarkRequest(
    string OfficerUserId,
    ConferenceItemKind Kind,
    int ItemId,
    string Body);

public sealed record AddConferenceRemarkResult(
    ConferenceDirectionVm Direction,
    IReadOnlyList<ConferenceProgressEntryVm> ProgressEntries,
    string? EmptyProgressText,
    string ProgressSummary,
    string? LatestProgressText);


public sealed class CreateConferenceTaskInput
{
    [Display(Name = "Task title")]
    [Required]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "Task brief / expected outcome")]
    [Required]
    [StringLength(4000)]
    public string Description { get; set; } = string.Empty;

    [Display(Name = "Due date")]
    [Required]
    [DataType(DataType.Date)]
    public DateTime? DueDate { get; set; }

    [Required]
    [RegularExpression("^(Low|Normal|High|Critical)$", ErrorMessage = "Select a valid priority.")]
    public string Priority { get; set; } = "Normal";
}

public sealed record CreateConferenceTaskRequest(
    string OfficerUserId,
    string Title,
    string Description,
    DateTime DueDate,
    string Priority);

public sealed record CreateConferenceTaskResult(OfficerConferenceItemVm Task);

public sealed class OfficerConferenceSectionRenderVm
{
    public string OfficerUserId { get; init; } = string.Empty;
    public string OfficerDisplayName { get; init; } = string.Empty;
    public DateTime MinimumTaskDueDate { get; init; }
    public DateTime DefaultTaskDueDate { get; init; }
    public OfficerConferenceSectionVm Section { get; init; } = new();
}

public sealed class OfficerConferenceItemRenderVm
{
    public string OfficerUserId { get; init; } = string.Empty;
    public OfficerConferenceItemVm Item { get; init; } = new();
}
