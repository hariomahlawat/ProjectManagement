using System;
using System.Collections.Generic;

namespace ProjectManagement.Models.Stages;

/// <summary>
/// One immutable, project-scoped submission of Actual-date corrections by the
/// assigned Project Officer (Phase D, plan sections C.3 and D.2). At most one
/// revision per project is <see cref="ActualCorrectionRevisionStatus.Pending"/>.
/// After insert only the closure fields change: <see cref="Status"/>,
/// <see cref="ClosedByUserId"/>, <see cref="ClosedOn"/>,
/// <see cref="SupersededByRevisionId"/> and <see cref="DecisionNote"/>.
/// Introduced in D-2; written by <c>ActualCorrectionProposalService</c> from D-3a/b.
/// </summary>
public class ActualCorrectionRevision
{
    public int Id { get; set; }

    public int ProjectId { get; set; }

    /// <summary>Allocated per project under the project gate as max + 1.</summary>
    public int RevisionNo { get; set; }

    public string Status { get; set; } = ActualCorrectionRevisionStatus.Pending;

    /// <summary>The form the revision was submitted through; presentation and evidence only.</summary>
    public string EntryPoint { get; set; } = ActualCorrectionEntryPoint.Editor;

    public string SubmittedByUserId { get; set; } = string.Empty;

    /// <summary>Set once; together with <see cref="Id"/> it is the decision epoch.</summary>
    public DateTimeOffset SubmittedOn { get; set; }

    public string? Note { get; set; }

    /// <summary>SHA-256 (hex) of the canonical item set, for the "unchanged resubmission" check.</summary>
    public string ContentHash { get; set; } = string.Empty;

    public int? SupersedesRevisionId { get; set; }

    public int? SupersededByRevisionId { get; set; }

    public string? ClosedByUserId { get; set; }

    public DateTimeOffset? ClosedOn { get; set; }

    public string? DecisionNote { get; set; }

    public Project? Project { get; set; }

    public ICollection<ActualCorrectionItem> Items { get; set; } = new List<ActualCorrectionItem>();
}

public static class ActualCorrectionRevisionStatus
{
    public const string Pending = "Pending";
    public const string Superseded = "Superseded";
    public const string Withdrawn = "Withdrawn";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

public static class ActualCorrectionEntryPoint
{
    public const string Editor = "Editor";
    public const string Backfill = "Backfill";
}
