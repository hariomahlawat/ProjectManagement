using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Stages;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Features.Backfill;

public sealed class BackfillViewModel
{
    /// <summary>
    /// Builds the backfill form. The values posted back (Actual start and
    /// completion) and each row's stale token come from the same stored-row
    /// snapshot, the actuals editor's read. Pairing Timeline dates with a token
    /// from a later read could let a backfill post stale dates under a current
    /// token and overwrite a concurrent correction. Display-only fields still come
    /// from the timeline.
    /// </summary>
    public static BackfillViewModel Create(
        int projectId,
        DateOnly today,
        IEnumerable<TimelineItemVm> timelineItems,
        ActualsEditorVm actualsEditor)
    {
        ArgumentNullException.ThrowIfNull(timelineItems);
        ArgumentNullException.ThrowIfNull(actualsEditor);

        var editorRows = actualsEditor.Rows
            .Where(row => !string.IsNullOrWhiteSpace(row.StageToken))
            .GroupBy(row => row.StageCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var stages = timelineItems
            .Where(item => item.RequiresBackfill)
            .Select(item =>
            {
                var snapshot = editorRows.GetValueOrDefault(item.Code);
                return new BackfillStageViewModel
                {
                    StageCode = item.Code,
                    StageName = item.Name,
                    ActualStart = snapshot is null ? item.ActualStart : snapshot.ActualStart,
                    CompletedOn = snapshot is null ? item.CompletedOn : snapshot.CompletedOn,
                    IsAutoCompleted = item.IsAutoCompleted,
                    AutoCompletedFromCode = item.AutoCompletedFromCode,
                    StageToken = snapshot?.StageToken
                };
            })
            .ToArray();

        return new BackfillViewModel
        {
            ProjectId = projectId,
            Today = today,
            // Claim stale protection only when every rendered row carries a token;
            // otherwise a tokenless row would always be refused as stale.
            BaselineVersion = stages.All(stage => !string.IsNullOrWhiteSpace(stage.StageToken))
                ? ActualStageToken.BaselineVersion
                : 0,
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
