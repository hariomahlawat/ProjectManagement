using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.ViewModels.Workspace;

public enum ConferenceItemKind
{
    Project = 1,
    ProjectIdea = 2,
    ActionTask = 3,

    /// <summary>
    /// A project of the IT projects engine: an IT project, reviewed under the ITO, or a MOLTI or
    /// SRE project, reviewed under the Project Officer assigned to it. The ids are one sequence,
    /// so the kind and the id still name one project.
    /// </summary>
    ItProject = 4
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
    public int ActiveProjectCount { get; init; }
    public int RecentlyCompletedProjectCount { get; init; }
    public int CompletedProjectRetentionDays { get; init; }
    public int IdeaCount { get; init; }
    public int OtherTaskCount { get; init; }

    /// <summary>
    /// Live projects of the IT projects engine under this officer: IT projects for an ITO, MOLTI
    /// and SRE projects for their assigned officer.
    /// </summary>
    public int ItProjectCount { get; init; }

    /// <summary>The same projects counted per module, for the header ("2 IT projects", "1 MOLTI project").</summary>
    public IReadOnlyList<ConferenceModuleCountVm> ModuleProjectCounts { get; init; } = Array.Empty<ConferenceModuleCountVm>();

    public string? PreviousOfficerUserId { get; init; }
    public string? NextOfficerUserId { get; init; }
    public IReadOnlyList<OfficerConferenceOfficerOptionVm> OfficerOptions { get; init; }
        = Array.Empty<OfficerConferenceOfficerOptionVm>();

    public ErpActivityStripVm ActivityStrip { get; init; } = new();

    public IReadOnlyList<OfficerConferenceSectionVm> Sections { get; init; }
        = Array.Empty<OfficerConferenceSectionVm>();
}

public sealed record OfficerConferenceOfficerOptionVm(
    string UserId,
    string DisplayName,
    bool IsSelected,
    int ProjectCount = 0,
    int IdeaCount = 0,
    int OtherTaskCount = 0,
    int ActiveProjectCount = 0,
    int RecentlyCompletedProjectCount = 0,
    int ItProjectCount = 0)
{
    /// <summary>The officer's rank alone, or empty. <see cref="DisplayName"/> stays "rank name".</summary>
    public string Rank { get; init; } = string.Empty;

    /// <summary>The officer's name without the rank; the display name when the two are not known apart.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Up to two letters from the name (never the rank), for an officer with no picture.</summary>
    public string Initials
    {
        get
        {
            var source = string.IsNullOrWhiteSpace(Name) ? DisplayName : Name;
            var parts = (source ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length switch
            {
                0 => "?",
                1 => char.ToUpperInvariant(parts[0][0]).ToString(),
                _ => $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}"
            };
        }
    }

    /// <summary>The engine projects counted per module, for the officer picker.</summary>
    public IReadOnlyList<ConferenceModuleCountVm> ModuleProjectCounts { get; init; } = Array.Empty<ConferenceModuleCountVm>();
}

/// <summary>How many live projects of one module sit under an officer, with the words to say it.</summary>
public sealed record ConferenceModuleCountVm(int Count, string Singular, string Plural)
{
    public string Label => Count == 1 ? Singular : Plural;
}

public sealed class OfficerConferenceSectionVm
{
    public ConferenceItemKind Kind { get; init; }
    public string Title { get; init; } = string.Empty;
    public string IconClass { get; init; } = string.Empty;

    /// <summary>
    /// For a section of the IT projects engine: the module it lists ("IT", "MOLTI", "SRE"). An
    /// officer can have a section per module, so the kind alone does not name the section.
    /// </summary>
    public string? ModuleCode { get; init; }

    /// <summary>One item of the section, for the column heading and the empty text: "IT project", "MOLTI project".</summary>
    public string? ItemLabel { get; init; }
    public IReadOnlyList<OfficerConferenceItemVm> Items { get; init; }
        = Array.Empty<OfficerConferenceItemVm>();
}

public sealed class OfficerConferenceItemVm
{
    public ConferenceItemKind Kind { get; init; }
    public int ItemId { get; init; }

    /// <summary>What the item is called when the kind does not say it: "MOLTI project". Null for the kind's own name.</summary>
    public string? ItemLabel { get; init; }
    public string Title { get; init; } = string.Empty;
    public string OpenUrl { get; init; } = string.Empty;

    public string CurrentStateCode { get; init; } = string.Empty;
    public string CurrentStateName { get; init; } = string.Empty;
    public string? CurrentContext { get; init; }
    public string? AttentionText { get; init; }
    public bool RequiresAttention { get; init; }
    public bool IsRecentlyCompleted { get; init; }
    public WorkspaceRecordHealthVm? RecordHealth { get; init; }

    public ConferenceDirectionVm? LatestDirection { get; init; }
    public int DirectionCount { get; init; }
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

/// <summary>
/// Live, command-only digest of the latest conference direction for each in-scope
/// project, idea and task. The digest is derived from conference source records and is
/// never persisted as a Notebook item.
/// </summary>
public sealed class ConferenceDirectionDigestVm
{
    public int TotalDirectionCount { get; init; }
    public int OfficerCount => OfficerGroups.Count;
    public IReadOnlyList<ConferenceDirectionDigestOfficerVm> OfficerGroups { get; init; }
        = Array.Empty<ConferenceDirectionDigestOfficerVm>();
}

public sealed class ConferenceDirectionDigestOfficerVm
{
    public string OfficerUserId { get; init; } = string.Empty;
    public string OfficerDisplayName { get; init; } = string.Empty;
    public string ConferenceReviewUrl { get; init; } = string.Empty;
    public DateTime LatestDirectionAtUtc { get; init; }
    public IReadOnlyList<ConferenceDirectionDigestItemVm> Directions { get; init; }
        = Array.Empty<ConferenceDirectionDigestItemVm>();
}

public sealed class ConferenceDirectionDigestItemVm
{
    public ConferenceItemKind Kind { get; init; }
    public int ItemId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string DirectionText { get; init; } = string.Empty;
    public DateTime IssuedAtUtc { get; init; }
    public string OpenUrl { get; init; } = string.Empty;

    public string KindLabel => Kind switch
    {
        ConferenceItemKind.Project => "Project",
        ConferenceItemKind.ProjectIdea => "Idea",
        ConferenceItemKind.ActionTask => "Task",
        ConferenceItemKind.ItProject => "IT project",
        _ => "Item"
    };
}


public sealed class ConferenceDirectionCycleVm
{
    public ConferenceDirectionVm Direction { get; init; } = new();
    public IReadOnlyList<ConferenceProgressEntryVm> ProgressEntries { get; init; }
        = Array.Empty<ConferenceProgressEntryVm>();
    public string? EmptyProgressText { get; init; }
    public int SequenceNumber { get; init; }
    public int TotalDirections { get; init; }
    public bool IsLatest { get; init; }
}

public sealed class ConferenceDirectionHistoryVm
{
    public ConferenceItemKind Kind { get; init; }
    public int ItemId { get; init; }
    public IReadOnlyList<ConferenceDirectionCycleVm> Cycles { get; init; }
        = Array.Empty<ConferenceDirectionCycleVm>();
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




public sealed class CreateConferenceIdeaInput
{
    [Display(Name = "Idea title")]
    [Required]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "Concept / problem statement")]
    [Required]
    [StringLength(2000)]
    public string Description { get; set; } = string.Empty;

    [Display(Name = "HoD oversight")]
    [StringLength(450)]
    public string? AssignedHodUserId { get; set; }
}

public sealed record CreateConferenceIdeaRequest(
    string OfficerUserId,
    string Title,
    string Description,
    string? AssignedHodUserId);

public sealed record CreateConferenceIdeaResult(OfficerConferenceItemVm Idea);

public sealed class ConferenceIdeaCreationOptionsVm
{
    public bool CanCreate { get; init; }
    public bool RequiresHodSelection { get; init; }
    public string? FixedHodUserId { get; init; }
    public string? FixedHodDisplayName { get; init; }
    public string? UnavailableReason { get; init; }
    public IReadOnlyList<ConferenceHodOptionVm> HodOptions { get; init; }
        = Array.Empty<ConferenceHodOptionVm>();
}

public sealed record ConferenceHodOptionVm(
    string UserId,
    string DisplayName,
    bool IsSelected);

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
    public bool CanManageConference { get; init; }
    public string OfficerDisplayName { get; init; } = string.Empty;
    public DateTime MinimumTaskDueDate { get; init; }
    public DateTime DefaultTaskDueDate { get; init; }
    public ConferenceIdeaCreationOptionsVm IdeaCreation { get; init; } = new();
    public int CompletedProjectRetentionDays { get; init; }
    public OfficerConferenceSectionVm Section { get; init; } = new();
}

public sealed class OfficerConferenceItemRenderVm
{
    public string OfficerUserId { get; init; } = string.Empty;
    public bool CanManageConference { get; init; }
    public OfficerConferenceItemVm Item { get; init; } = new();
}
