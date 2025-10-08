using System;
using System.Collections.Generic;
using ProjectManagement.Models;

namespace ProjectManagement.ViewModels;

public sealed class ProjectLifecycleSummaryViewModel
{
    public static readonly ProjectLifecycleSummaryViewModel Empty = new();

    public bool ShowPostCompletionView { get; init; }
    public ProjectLifecycleStatus Status { get; init; } = ProjectLifecycleStatus.Active;
    public string StatusLabel { get; init; } = "Active";
    public bool IsLegacy { get; init; }
    public string? PrimaryDetail { get; init; }
    public string? SecondaryDetail { get; init; }
    public string? BadgeText { get; init; }
    public IReadOnlyList<LifecycleFact> Facts { get; init; } = Array.Empty<LifecycleFact>();

    public sealed record LifecycleFact(string Label, string Value);
}
