using System;
using System.Collections.Generic;
using ProjectManagement.Models;
using ProjectManagement.Models.Remarks;

namespace ProjectManagement.ViewModels;

public sealed class ProjectTotSummaryViewModel
{
    public static readonly ProjectTotSummaryViewModel Empty = new();

    public bool HasTotRecord { get; init; }
    public ProjectTotStatus Status { get; init; } = ProjectTotStatus.NotStarted;
    public string StatusLabel { get; init; } = "Not started";
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<TotFact> Facts { get; init; } = Array.Empty<TotFact>();
    public string? LastApprovedBy { get; init; }
    public DateTime? LastApprovedOnUtc { get; init; }
    public TotRequestSummary? PendingRequest { get; init; }
    public TotRemarkSnippet? LatestRemark { get; init; }

    public bool HasPendingRequest => PendingRequest is { State: ProjectTotRequestDecisionState.Pending };

    public sealed record TotFact(string Label, string Value);

    public sealed record TotRequestSummary(
        ProjectTotRequestDecisionState State,
        string StateLabel,
        ProjectTotStatus ProposedStatus,
        string ProposedStatusLabel,
        DateOnly? ProposedStartedOn,
        DateOnly? ProposedCompletedOn,
        string? ProposedMetDetails,
        DateOnly? ProposedMetCompletedOn,
        bool? ProposedFirstProductionModelManufactured,
        DateOnly? ProposedFirstProductionModelManufacturedOn,
        string SubmittedBy,
        DateTime SubmittedOnUtc,
        string? DecidedBy,
        DateTime? DecidedOnUtc);

    public sealed record TotRemarkSnippet(
        RemarkType Type,
        string TypeLabel,
        string Body,
        DateOnly? EventDate,
        DateTime CreatedAtUtc,
        string? AuthorDisplayName);
}
