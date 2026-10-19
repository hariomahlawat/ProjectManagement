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

    public string? AutoCompletedFromName =>
        string.IsNullOrWhiteSpace(AutoCompletedFromCode)
            ? null
            : StageCodes.DisplayNameOf(AutoCompletedFromCode);
}
