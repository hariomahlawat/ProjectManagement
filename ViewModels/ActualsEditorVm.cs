using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.ViewModels;

// SECTION: Actuals editor view models
public sealed class ActualsEditorVm
{
    public static readonly ActualsEditorVm Empty = new()
    {
        Rows = Array.Empty<ActualsEditorRowVm>()
    };

    public int ProjectId { get; init; }

    public DateOnly Today { get; init; }

    public IReadOnlyList<ActualsEditorRowVm> Rows { get; init; } = Array.Empty<ActualsEditorRowVm>();

    // Posted back as Input.BaselineVersion; marks a stale-protected (baseline-aware) form.
    public int BaselineVersion { get; init; }

    // Phase D-3c: the project's pending Actual correction proposal, when the viewer may see it.
    public ActualProposalSummaryVm? PendingProposal { get; init; }

    /// <summary>
    /// The editor for the assigned PO-only user: every pending item is prefilled
    /// with its proposed values (an editor post is the complete proposal), and the
    /// official values stay available for display and for clear-intent detection.
    /// </summary>
    public ActualsEditorVm WithPendingProposal(ActualCorrectionRevision? revision, string? currentUserId, bool prefill = true)
    {
        if (revision is null)
        {
            return this;
        }

        var items = revision.Items.ToDictionary(item => item.StageCode, StringComparer.OrdinalIgnoreCase);
        return new ActualsEditorVm
        {
            ProjectId = ProjectId,
            Today = Today,
            BaselineVersion = BaselineVersion,
            PendingProposal = ActualProposalSummaryVm.From(revision, currentUserId),
            Rows = prefill
                ? Rows.Select(row => items.TryGetValue(row.StageCode, out var item) ? row.WithProposal(item) : row).ToArray()
                : Rows
        };
    }

    /// <summary>Pending stage codes that have no editor row; they cannot be resubmitted from this form.</summary>
    public IReadOnlyList<string> UnrenderedProposalStages(ActualCorrectionRevision? revision) =>
        revision is null
            ? Array.Empty<string>()
            : revision.Items
                .Select(item => item.StageCode)
                .Where(code => !Rows.Any(row => string.Equals(row.StageCode, code, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
}

/// <summary>Identity and status of a pending Actual correction proposal, as rendered in the editors.</summary>
public sealed record ActualProposalSummaryVm(
    int RevisionId,
    int RevisionNo,
    DateTimeOffset SubmittedOn,
    string? Note,
    bool SubmittedByCurrentUser,
    IReadOnlyList<string> StageCodes)
{
    public static ActualProposalSummaryVm From(ActualCorrectionRevision revision, string? currentUserId)
    {
        ArgumentNullException.ThrowIfNull(revision);
        return new ActualProposalSummaryVm(
            revision.Id,
            revision.RevisionNo,
            revision.SubmittedOn,
            revision.Note,
            string.Equals(revision.SubmittedByUserId, currentUserId, StringComparison.OrdinalIgnoreCase),
            revision.Items.Select(item => item.StageCode).OrderBy(code => code, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    /// <summary>The submission epoch posted back as the baseline; round-trips exactly.</summary>
    public string SubmittedOnRoundTrip => SubmittedOn.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Posted by the Actual editor and the backfill form so the server can refuse a
/// post whose intended action no longer matches the user's authority (D-3c).
/// </summary>
public static class ActualsSubmissionMode
{
    public const string Direct = "direct";
    public const string Proposal = "proposal";
}

public sealed class ActualsEditorRowVm
{
    public string StageCode { get; init; } = string.Empty;

    public string StageName { get; init; } = string.Empty;

    public StageStatus Status { get; init; } = StageStatus.NotStarted;

    public bool IsEditable { get; init; }

    public DateOnly? ActualStart { get; init; }

    public DateOnly? CompletedOn { get; init; }

    public DateOnly? EarliestAllowedStartDate { get; init; }

    public string? StartBoundarySourceName { get; init; }

    public bool IsAutoCompleted { get; init; }

    public bool RequiresBackfill { get; init; }

    public bool HasPendingDecision { get; init; }

    public bool IsArppManaged { get; init; }

    public string? ArppSourceLabel { get; init; }

    public DateOnly? ArppCompletionDate { get; init; }

    // Optimistic identity of the stored row (ActualStageToken); null when no row exists.
    public string? StageToken { get; init; }

    // Phase D-3c: a pending proposal item for this stage. ActualStart/CompletedOn above stay official.
    public bool HasProposal { get; init; }

    public DateOnly? ProposedActualStart { get; init; }

    public DateOnly? ProposedCompletedOn { get; init; }

    public bool ProposalClearsStart { get; init; }

    // The values the form is prefilled with: the proposal when one exists, otherwise official.
    public DateOnly? EditStart => HasProposal ? ProposedActualStart : ActualStart;

    public DateOnly? EditCompletedOn => HasProposal ? ProposedCompletedOn : CompletedOn;

    internal ActualsEditorRowVm WithProposal(ActualCorrectionItem item) => new()
    {
        StageCode = StageCode,
        StageName = StageName,
        Status = Status,
        IsEditable = IsEditable,
        ActualStart = ActualStart,
        CompletedOn = CompletedOn,
        EarliestAllowedStartDate = EarliestAllowedStartDate,
        StartBoundarySourceName = StartBoundarySourceName,
        IsAutoCompleted = IsAutoCompleted,
        RequiresBackfill = RequiresBackfill,
        HasPendingDecision = HasPendingDecision,
        IsArppManaged = IsArppManaged,
        ArppSourceLabel = ArppSourceLabel,
        ArppCompletionDate = ArppCompletionDate,
        StageToken = StageToken,
        HasProposal = true,
        ProposedActualStart = item.ProposedActualStart,
        ProposedCompletedOn = item.ProposedCompletedOn,
        ProposalClearsStart = item.ClearsActualStart
    };
}

public sealed class ActualsEditInput
{
    public int ProjectId { get; set; }

    public IList<ActualsEditRowInput> Rows { get; set; } = new List<ActualsEditRowInput>();

    // Absent on forms rendered before stale protection; see ActualStageToken.
    public int? BaselineVersion { get; set; }

    // Phase D-3c (ActualsSubmissionMode). Absent on forms rendered before D-3c.
    public string? SubmissionMode { get; set; }

    // The pending proposal the form was rendered with; both empty for "none".
    public int? BasedOnRevisionId { get; set; }

    public DateTimeOffset? BasedOnSubmittedOn { get; set; }

    // Optional justification for a proposal (D-3a/b Note).
    public string? Note { get; set; }
}

public sealed class ActualsEditRowInput
{
    public string StageCode { get; set; } = string.Empty;

    public string StageName { get; set; } = string.Empty;

    public DateOnly? ActualStart { get; set; }

    // Additive intent: older callers that omit this flag retain blank-as-preserve semantics.
    public bool ClearActualStart { get; set; }

    public DateOnly? CompletedOn { get; set; }

    // The stored row's token when the form was rendered.
    public string? StageToken { get; set; }
}
