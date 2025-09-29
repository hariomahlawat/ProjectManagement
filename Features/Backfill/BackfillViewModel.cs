using System;
using System.Collections.Generic;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Features.Backfill;

public sealed class BackfillViewModel
{
    public static readonly BackfillViewModel Empty = new()
    {
        ProjectId = 0,
        Today = DateOnly.FromDateTime(DateTime.UtcNow.Date)
    };

    public int ProjectId { get; init; }

    public DateOnly Today { get; init; }

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

    public string? AutoCompletedFromName =>
        string.IsNullOrWhiteSpace(AutoCompletedFromCode)
            ? null
            : StageCodes.DisplayNameOf(AutoCompletedFromCode);
}
