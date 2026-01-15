namespace ProjectManagement.ViewModels.Navigation;

public sealed record PendingApprovalsBadgeViewModel
{
    // SECTION: Badge state
    public bool ShowBadge { get; init; }

    public string DisplayText { get; init; } = string.Empty;

    public string? AriaLabel { get; init; }
}
