using System;
using System.Collections.Generic;
using ProjectManagement.Models;

namespace ProjectManagement.ViewModels;

public sealed class ProjectTotSummaryViewModel
{
    public static readonly ProjectTotSummaryViewModel Empty = new();

    public bool HasTotRecord { get; init; }
    public ProjectTotStatus Status { get; init; } = ProjectTotStatus.NotStarted;
    public string StatusLabel { get; init; } = "Not started";
    public string Summary { get; init; } = string.Empty;
    public string? Remarks { get; init; }
    public IReadOnlyList<TotFact> Facts { get; init; } = Array.Empty<TotFact>();

    public sealed record TotFact(string Label, string Value);
}
