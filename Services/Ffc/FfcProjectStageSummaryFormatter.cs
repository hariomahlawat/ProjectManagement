using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Ffc;

public sealed record FfcProjectStageSnapshot(
    int ProjectId,
    string StageCode,
    int SortOrder,
    StageStatus Status,
    DateOnly? CompletedOn);

public static class FfcProjectStageSummaryFormatter
{
    public static string? Format(IEnumerable<FfcProjectStageSnapshot>? projectStages)
    {
        static string FormatDate(DateOnly? date) => date.HasValue
            ? date.Value.ToString("d MMM yyyy", CultureInfo.InvariantCulture)
            : string.Empty;

        var stages = projectStages?
            .Where(stage => !StageCodes.IsTot(stage.StageCode))
            .OrderBy(stage => stage.SortOrder)
            .ToList() ?? new List<FfcProjectStageSnapshot>();

        if (stages.Count == 0)
        {
            return null;
        }

        var paymentStage = stages.FirstOrDefault(stage => StageCodes.IsPayment(stage.StageCode));
        if (paymentStage is not null)
        {
            stages = stages
                .Where(stage => stage.SortOrder <= paymentStage.SortOrder)
                .ToList();
        }

        var topCompleted = stages
            .Where(stage => stage.Status == StageStatus.Completed)
            .OrderByDescending(stage => stage.SortOrder)
            .ThenByDescending(stage => stage.CompletedOn ?? DateOnly.MinValue)
            .FirstOrDefault();

        var started = stages.FirstOrDefault(stage =>
            stage.Status is StageStatus.InProgress or StageStatus.Blocked);

        var missed = topCompleted is null
            ? Array.Empty<string>()
            : stages
                .Where(stage => stage.SortOrder < topCompleted.SortOrder &&
                                stage.Status != StageStatus.Completed)
                .Select(stage => StageCodes.DisplayNameOf(stage.StageCode))
                .ToArray();

        if (started is not null)
        {
            var previous = stages.LastOrDefault(stage =>
                stage.SortOrder < started.SortOrder &&
                stage.Status == StageStatus.Completed);

            var previousLabel = previous is null
                ? null
                : StageCodes.DisplayNameOf(previous.StageCode);
            var previousDate = previous is null ? string.Empty : FormatDate(previous.CompletedOn);
            var currentLabel = StageCodes.DisplayNameOf(started.StageCode);
            var currentState = started.Status == StageStatus.Blocked ? "Blocked" : "In progress";
            var missedPart = missed.Length > 0
                ? $" — missed: {string.Join(", ", missed)}"
                : string.Empty;

            if (previousLabel is null)
            {
                return $"Now: {currentLabel} ({currentState}){missedPart}";
            }

            var previousPart = string.IsNullOrEmpty(previousDate)
                ? previousLabel
                : $"{previousLabel} ({previousDate})";

            return $"Last completed: {previousPart} · Now: {currentLabel} ({currentState}){missedPart}";
        }

        if (topCompleted is not null)
        {
            var topLabel = StageCodes.DisplayNameOf(topCompleted.StageCode);
            var topDate = FormatDate(topCompleted.CompletedOn);
            var topPart = string.IsNullOrEmpty(topDate)
                ? topLabel
                : $"{topLabel} ({topDate})";

            var next = stages.FirstOrDefault(stage => stage.SortOrder > topCompleted.SortOrder);
            var nextPart = string.Empty;
            if (next is not null)
            {
                var nextLabel = StageCodes.DisplayNameOf(next.StageCode);
                var suffix = next.Status switch
                {
                    StageStatus.InProgress => " (Started)",
                    StageStatus.Blocked => " (Blocked)",
                    _ => " (Not started)"
                };

                nextPart = $" · Next: {nextLabel}{suffix}";
            }

            var missedPart = missed.Length > 0
                ? $" — missed: {string.Join(", ", missed)}"
                : string.Empty;

            return $"Last completed: {topPart}{nextPart}{missedPart}";
        }

        var firstDefined = stages.FirstOrDefault();
        return firstDefined is null
            ? null
            : $"Not started · First stage: {StageCodes.DisplayNameOf(firstDefined.StageCode)}";
    }
}
