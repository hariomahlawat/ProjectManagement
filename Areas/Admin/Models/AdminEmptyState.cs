namespace ProjectManagement.Areas.Admin.Models;

public sealed record AdminEmptyStateModel
{
    public string Title { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string Icon { get; init; } = "bi-inbox";

    public string? ActionText { get; init; }

    public string? ActionHref { get; init; }
}
