using System;

namespace ProjectManagement.Pages.Projects;

// SECTION: Shared right-column summary card view model
public sealed class RightColumnSummaryCardViewModel
{
    public string IconClass { get; init; } = "bi bi-grid";

    public string Title { get; init; } = string.Empty;

    public string Subtitle { get; init; } = string.Empty;

    public string StatusText { get; init; } = string.Empty;

    public string StatusBadgeClass { get; init; } = "text-bg-secondary";

    public string? HeaderActionsPartialName { get; init; }

    public object? HeaderActionsModel { get; init; }

    public string BodyPartialName { get; init; } = string.Empty;

    public object? BodyModel { get; init; }

    public string? FooterPartialName { get; init; }

    public object? FooterModel { get; init; }

    public string CardClass { get; init; } = string.Empty;
}
