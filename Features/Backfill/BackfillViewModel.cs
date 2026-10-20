using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Stages;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Features.Backfill;

public sealed class BackfillViewModel
{
    /// <summary>
    /// Builds the backfill form. Each row's posted values (Actual start and
    /// completion) and its stale token come from the same stored row, read in
    /// one query, so a correction committed between reads can never pair stale
    /// dates with a current token. Display-only fields come from the timeline.
    /// The form always claims baseline protection: a row without a stored row
    /// has no token and is refused by the server rather than disabling the
    /// stale check for every other row.
    /// </summary>
    public static BackfillViewModel Create(
        int projectId,
        DateOnly today,
        IEnumerable<TimelineItemVm> timelineItems,
        IEnumerable<ProjectStage> storedStages)
    {
        ArgumentNullException.ThrowIfNull(timelineItems);
        ArgumentNullException.ThrowIfNull(storedStages);

        var stored = storedStages
            .Where(stage => !string.IsNullOrWhiteSpace(stage.StageCode))
            .GroupBy(stage => stage.StageCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var stages = timelineItems
            .Where(item => item.RequiresBackfill)
            .Select(item =>
            {
                var row = stored.GetValueOrDefault(item.Code);
                return new BackfillStageViewModel
                {
                    StageCode = item.Code,
                    StageName = item.Name,
                    ActualStart = row is null ? item.ActualStart : row.ActualStart,
                    CompletedOn = row is null ? item.CompletedOn : row.CompletedOn,
                    IsAutoCompleted = item.IsAutoCompleted,
                    AutoCompletedFromCode = item.AutoCompletedFromCode,
                    StageToken = row is null ? null : ActualStageToken.Compute(row)
                };
            })
            .ToArray();

        return new BackfillViewModel
        {
            ProjectId = projectId,
            Today = today,
            BaselineVersion = ActualStageToken.BaselineVersion,
            Stages = stages
        };
    }

    public static readonly BackfillViewModel Empty = new()
    {
        ProjectId = 0,
        Today = DateOnly.FromDateTime(DateTime.UtcNow.Date)
    };

    public int ProjectId { get; init; }

    public DateOnly Today { get; init; }

    // Posted back as baselineVersion; marks a stale-protected form.
    public int BaselineVersion { get; init; }

    public IReadOnlyList<BackfillStageViewModel> Stages { get; init; } = Array.Empty<BackfillStageViewModel>();

    public bool HasStages => Stages.Count > 0;

    // Phase D-3c: the pending Actual correction proposal, for the assigned PO-only user.
    public ActualProposalSummaryVm? PendingProposal { get; init; }

    /// <summary>
    /// The pending proposal already supplies a completion date for every stage that
    /// needs one, so nothing is left for the PO to do until a HoD or Admin decides.
    /// The banner then reports the submission instead of "Action needed" (discovery C2.3).
    /// </summary>
    public bool AwaitingApproval => PendingProposal is not null && HasStages &&
        Stages.All(stage => stage.HasProposal && stage.CompletedOn.HasValue);

    /// <summary>
    /// Prefills rows covered by the pending proposal with their proposed values, so
    /// a resubmission does not discard them. Other pending items are carried forward
    /// by the server (backfill entry point).
    /// </summary>
    public BackfillViewModel WithPendingProposal(ActualCorrectionRevision? revision, string? currentUserId)
    {
        if (revision is null)
        {
            return this;
        }

        var items = revision.Items.ToDictionary(item => item.StageCode, StringComparer.OrdinalIgnoreCase);
        return new BackfillViewModel
        {
            ProjectId = ProjectId,
            Today = Today,
            BaselineVersion = BaselineVersion,
            PendingProposal = ActualProposalSummaryVm.From(revision, currentUserId),
            Stages = Stages
                .Select(stage => items.TryGetValue(stage.StageCode, out var item)
                    ? new BackfillStageViewModel
                    {
                        StageCode = stage.StageCode,
                        StageName = stage.StageName,
                        ActualStart = item.ProposedActualStart,
                        CompletedOn = item.ProposedCompletedOn,
                        IsAutoCompleted = stage.IsAutoCompleted,
                        AutoCompletedFromCode = stage.AutoCompletedFromCode,
                        StageToken = stage.StageToken,
                        HasProposal = true,
                        // Backfill cannot clear a start; posting the row would replace the clear.
                        IsCarriedForward = item.ClearsActualStart
                    }
                    : stage)
                .ToArray()
        };
    }
}

public sealed class BackfillStageViewModel
{
    public string StageCode { get; init; } = string.Empty;

    public string StageName { get; init; } = string.Empty;

    public DateOnly? ActualStart { get; init; }

    public DateOnly? CompletedOn { get; init; }

    public bool IsAutoCompleted { get; init; }

    public string? AutoCompletedFromCode { get; init; }

    // Stored row token when rendered (ActualStageToken).
    public string? StageToken { get; init; }

    // Prefilled from the pending proposal rather than from the official row.
    public bool HasProposal { get; init; }

    // Shown read-only and not posted, so the server carries the pending item forward.
    public bool IsCarriedForward { get; init; }

    public string? AutoCompletedFromName =>
        string.IsNullOrWhiteSpace(AutoCompletedFromCode)
            ? null
            : StageCodes.DisplayNameOf(AutoCompletedFromCode);
}
