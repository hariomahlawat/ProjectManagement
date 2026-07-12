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
    public string ProgressSummary { get; init; } = string.Empty;
    public string? LatestProgressText { get; init; }
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

public sealed record AddConferenceRemarkRequest(
    string OfficerUserId,
    ConferenceItemKind Kind,
    int ItemId,
    string Body);

public sealed record AddConferenceRemarkResult(
    ConferenceDirectionVm Direction,
    string ProgressSummary,
    string? LatestProgressText);

public sealed class OfficerConferenceSectionRenderVm
{
    public string OfficerUserId { get; init; } = string.Empty;
    public OfficerConferenceSectionVm Section { get; init; } = new();
}

public sealed class OfficerConferenceItemRenderVm
{
    public string OfficerUserId { get; init; } = string.Empty;
    public OfficerConferenceItemVm Item { get; init; } = new();
}
