using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.ViewModels.Notebook;

public static class NotebookSystemItemKeys
{
    public const string ConferenceDirections = "conference-directions";
}

public sealed class NotebookSystemItemPreferenceVm
{
    public string SystemItemKey { get; set; } = string.Empty;
    public bool ShowInHome { get; set; }
    public bool IsPinned { get; set; }
    public int HomePosition { get; set; }
    public string ColorKey { get; set; } = "white";
    public IReadOnlyList<string> Labels { get; set; } = Array.Empty<string>();
    public Guid Version { get; set; }
}

public sealed class NotebookConferenceDigestCardVm
{
    public required ConferenceDirectionDigestVm Digest { get; init; }
    public required NotebookSystemItemPreferenceVm Preference { get; init; }
    public string View { get; init; } = "shared";
    public bool IsHomePlacement { get; init; }
    public bool IsLabelView { get; init; }
}
