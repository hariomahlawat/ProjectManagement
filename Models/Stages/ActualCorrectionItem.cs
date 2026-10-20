using System;
using ProjectManagement.Models.Execution;

namespace ProjectManagement.Models.Stages;

/// <summary>
/// One stage's proposed Actual facts within an <see cref="ActualCorrectionRevision"/>,
/// with the official values it was based on (plan section D.2). Items are never
/// updated after insert. Introduced in D-2; written by <c>ActualCorrectionProposalService</c> from D-3a/b.
/// </summary>
public class ActualCorrectionItem
{
    public int Id { get; set; }

    public int RevisionId { get; set; }

    public string StageCode { get; set; } = string.Empty;

    /// <summary>Derived on the server: <see cref="ActualCorrectionItemKind.Backfill"/> or <see cref="ActualCorrectionItemKind.Correction"/>.</summary>
    public string Kind { get; set; } = ActualCorrectionItemKind.Correction;

    // Official snapshot at submission, used for the approval-time baseline check.
    public StageStatus BeforeStatus { get; set; }

    public DateOnly? BeforeActualStart { get; set; }

    public DateOnly? BeforeCompletedOn { get; set; }

    public bool BeforeRequiresBackfill { get; set; }

    // Final proposed values, already normalized.
    public DateOnly? ProposedActualStart { get; set; }

    public DateOnly? ProposedCompletedOn { get; set; }

    /// <summary>Explicit intent to clear the start, distinct from "null because unknown".</summary>
    public bool ClearsActualStart { get; set; }

    public string? Note { get; set; }

    public ActualCorrectionRevision? Revision { get; set; }
}

public static class ActualCorrectionItemKind
{
    public const string Correction = "Correction";
    public const string Backfill = "Backfill";
}
