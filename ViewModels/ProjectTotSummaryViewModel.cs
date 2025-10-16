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
    public string? LastApprovedBy { get; init; }
    public DateTime? LastApprovedOnUtc { get; init; }
    public TotRequestSummary? PendingRequest { get; init; }
    public TotProgressUpdateSnippet? LatestApprovedUpdate { get; init; }
    public int PendingUpdateCount { get; init; }

    public bool HasPendingRequest => PendingRequest is { State: ProjectTotRequestDecisionState.Pending };
    public bool HasPendingUpdates => PendingUpdateCount > 0;

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
        string? ProposedRemarks,
        string SubmittedBy,
        DateTime SubmittedOnUtc,
        string? DecidedBy,
        DateTime? DecidedOnUtc,
        string? DecisionRemarks);

    public sealed record TotProgressUpdateSnippet(
        string Body,
        DateOnly? EventDate,
        DateTime? PublishedOnUtc);
}
