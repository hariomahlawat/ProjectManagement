using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Services.Stages;

namespace ProjectManagement.Pages.Projects.Timeline;

/// <summary>
/// User-facing text for Actual correction proposal outcomes, shared by the
/// Actual editor and the backfill form (D-3c). A submission is never described
/// as saved or published: official dates change only after HoD or Admin approval.
/// </summary>
internal static class ActualProposalMessages
{
    internal const string OutOfDate =
        "This page is out of date. Reload to review the current dates before submitting. Nothing was submitted.";

    internal const string NotEditable =
        "This project is closed or legacy, so its actual dates cannot be corrected here. Nothing was submitted.";

    internal static string Describe(ActualCorrectionSubmissionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Outcome switch
        {
            ActualCorrectionSubmissionOutcome.Submitted =>
                $"Submitted for HoD or Admin approval (revision {result.Revision!.RevisionNo}). " +
                "Official dates are unchanged until it is approved; you can revise or withdraw it until then." +
                Warnings(result.Warnings),
            ActualCorrectionSubmissionOutcome.Unchanged =>
                $"No changes since your pending submission (revision {result.Revision!.RevisionNo}). It is still awaiting HoD or Admin approval.",
            ActualCorrectionSubmissionOutcome.Stale => Stale(result),
            ActualCorrectionSubmissionOutcome.Invalid =>
                string.Join(" ", result.Errors ?? Array.Empty<string>()) + " Nothing was submitted.",
            ActualCorrectionSubmissionOutcome.Conflict =>
                $"These stages no longer need a completion date: {Codes(result)}. Reload and review. Nothing was submitted.",
            ActualCorrectionSubmissionOutcome.ProjectNotEditable => NotEditable,
            ActualCorrectionSubmissionOutcome.DirectAuthority => EditActualsModel.AccessChangedMessage,
            ActualCorrectionSubmissionOutcome.NotFound => "The project was not found or has been deleted.",
            _ => "You are not authorised to correct this project's actual dates."
        };
    }

    private static string Stale(ActualCorrectionSubmissionResult result) => result.StaleReason switch
    {
        ActualCorrectionStaleReason.PendingRevisionChanged =>
            "Your pending update changed in another window or was decided. Reload to review the latest values. Nothing was submitted.",
        ActualCorrectionStaleReason.StagesChanged or ActualCorrectionStaleReason.CarriedItemsChanged =>
            $"These stages changed since you opened the page: {Codes(result)}. Reload and review the current dates. Nothing was submitted.",
        _ => OutOfDate
    };

    private static string Codes(ActualCorrectionSubmissionResult result) =>
        string.Join(", ", result.StageCodes ?? Array.Empty<string>());

    private static string Warnings(IReadOnlyList<string>? warnings) =>
        warnings is { Count: > 0 } ? " Note: " + string.Join(" ", warnings) : string.Empty;
}
