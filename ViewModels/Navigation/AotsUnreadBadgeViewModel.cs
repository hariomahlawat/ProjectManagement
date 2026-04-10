namespace ProjectManagement.ViewModels.Navigation;

public sealed class AotsUnreadBadgeViewModel
{
    // SECTION: Visibility
    public bool ShowBadge { get; init; }

    // SECTION: Render text
    public string DisplayText { get; init; } = string.Empty;

    // SECTION: Accessibility
    public string AriaLabel { get; init; } = "Unread AOTS documents";

    // SECTION: Visual variant selector
    public string Variant { get; init; } = "default";
}
