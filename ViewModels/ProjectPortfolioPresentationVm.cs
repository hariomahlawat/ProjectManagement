using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;

namespace ProjectManagement.ViewModels;

public sealed class ProjectPortfolioPresentationVm
{
    public string PageTitle { get; init; } = "Project";
    public TimelineItemVm? CurrentStage { get; init; }
    public TimelineItemVm? NextStage { get; init; }
    public bool IsWorkflowConcluded { get; init; }
    public int CompletedStages { get; init; }
    public int FullyRecordedCompletedStages { get; init; }
    public int CompletedStagesRequiringBackfill { get; init; }
    public int SkippedStages { get; init; }
    public int ResolvedStages { get; init; }
    public int TotalStages { get; init; }
    public int ProgressMaximum => TotalStages == 0 ? 1 : TotalStages;
    public int ProgressPercent { get; init; }
    public int DelayedStageCount { get; init; }
    public int CompletedLateStageCount { get; init; }
    public int? CurrentStageOverdueDays { get; init; }
    public int BackfillStageCount { get; init; }
    public int CompletenessPercent { get; init; }
    public IReadOnlyList<string> MissingProfileFacts { get; init; } = Array.Empty<string>();
    public string PlanStatus { get; init; } = "Not approved";
    public string PlanHealth { get; init; } = "Current-stage plan not approved";
    public string ScheduleStatus { get; init; } = "Not assessed";
    public string ScheduleDetail { get; init; } = "Set the current-stage planned completion date";
    public string CurrentStageDisplay => IsWorkflowConcluded ? "Lifecycle concluded" : CurrentStage?.Name ?? "Not started";
    public string CurrentStageDetail => IsWorkflowConcluded ? "All applicable stages are complete or skipped" : CurrentStage?.Code ?? "No active stage";
    public string NextAction { get; init; } = "Review project status";
    public string NextActionDetail { get; init; } = "Operational follow-up";
    public string ProfileCompletenessDetail => CompletenessPercent == 100
        ? "Core profile complete"
        : $"{MissingProfileFacts.Count} recommended detail{(MissingProfileFacts.Count == 1 ? string.Empty : "s")} missing";

    public static ProjectPortfolioPresentationVm Create(Project? project, TimelineVm timeline, bool hasBackfill)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        var ordered = timeline.Items.OrderBy(item => item.SortOrder).ToArray();
        var isWorkflowConcluded = ordered.Length > 0 && ordered.All(item => item.Status is StageStatus.Completed or StageStatus.Skipped);
        var current = isWorkflowConcluded
            ? null
            : ordered.FirstOrDefault(item => item.Status == StageStatus.InProgress)
                ?? ordered.FirstOrDefault(item => item.Status is not StageStatus.Completed and not StageStatus.Skipped);
        var next = current is null
            ? null
            : ordered.FirstOrDefault(item =>
                item.SortOrder > current.SortOrder &&
                item.Status is StageStatus.NotStarted or StageStatus.Blocked);

        var completedCount = ordered.Count(item => item.Status == StageStatus.Completed);
        var completedBackfillCount = ordered.Count(item => item.Status == StageStatus.Completed && item.RequiresBackfill);
        var fullyRecordedCompletedCount = completedCount - completedBackfillCount;
        var skippedCount = ordered.Count(item => item.Status == StageStatus.Skipped);
        var resolvedCount = completedCount + skippedCount;
        var completedLateCount = ordered.Count(item =>
            item.Status == StageStatus.Completed &&
            item.ShowFinishVariance &&
            (item.FinishVarianceDays ?? 0) > 0);
        var currentOverdueDays = current is { IsOverdue: true, DaysRemaining: int remaining }
            ? Math.Abs(remaining)
            : (int?)null;
        var delayed = completedLateCount + (currentOverdueDays.HasValue ? 1 : 0);
        var backfillCount = ordered.Count(item => item.RequiresBackfill);

        var profileChecks = new (string Label, bool IsPresent)[]
        {
            ("Project name", !string.IsNullOrWhiteSpace(project?.Name)),
            ("Category", project?.CategoryId is not null),
            ("Technical category", project?.TechnicalCategoryId is not null),
            ("Head of Department", project?.HodUserId is not null),
            ("Project Officer", project?.LeadPoUserId is not null),
            ("Sponsoring unit", project?.SponsoringUnitId is not null),
            ("Sponsoring line directorate", project?.SponsoringLineDirectorateId is not null),
            ("Project description", !string.IsNullOrWhiteSpace(project?.Description))
        };
        var missingProfileFacts = profileChecks
            .Where(item => !item.IsPresent)
            .Select(item => item.Label)
            .ToArray();

        var nextAction = BuildNextAction(current, isWorkflowConcluded, backfillCount);
        var schedule = BuildScheduleStatus(current, completedLateCount);

        return new ProjectPortfolioPresentationVm
        {
            PageTitle = project?.Name ?? "Project",
            CurrentStage = current,
            NextStage = next,
            IsWorkflowConcluded = isWorkflowConcluded,
            CompletedStages = completedCount,
            FullyRecordedCompletedStages = fullyRecordedCompletedCount,
            CompletedStagesRequiringBackfill = completedBackfillCount,
            SkippedStages = skippedCount,
            ResolvedStages = resolvedCount,
            TotalStages = timeline.TotalStages,
            ProgressPercent = timeline.TotalStages == 0 ? 0 : (int)Math.Round(resolvedCount * 100d / timeline.TotalStages),
            DelayedStageCount = delayed,
            CompletedLateStageCount = completedLateCount,
            CurrentStageOverdueDays = currentOverdueDays,
            BackfillStageCount = backfillCount,
            CompletenessPercent = (int)Math.Round(profileChecks.Count(item => item.IsPresent) * 100d / profileChecks.Length),
            MissingProfileFacts = missingProfileFacts,
            PlanStatus = project?.PlanApprovedAt.HasValue == true ? "Approved" : timeline.PlanPendingApproval ? "Pending" : "Not approved",
            PlanHealth = project?.PlanApprovedAt.HasValue == true
                ? "Current-stage deadline monitored"
                : timeline.PlanPendingApproval
                    ? "Timeline approval pending"
                    : current?.NeedsPlannedCompletion == true
                        ? "Current-stage planned completion not set"
                        : "Current-stage plan not approved",
            ScheduleStatus = schedule.Status,
            ScheduleDetail = schedule.Detail,
            NextAction = nextAction.Action,
            NextActionDetail = nextAction.Detail
        };
    }

    private static (string Action, string Detail) BuildNextAction(
        TimelineItemVm? current,
        bool isWorkflowConcluded,
        int backfillCount)
    {
        if (backfillCount > 0)
        {
            return (
                $"Complete missing details for {backfillCount} stage{(backfillCount == 1 ? string.Empty : "s")}",
                "Completion dates or mandatory stage facts require attention");
        }

        if (current is null)
        {
            return isWorkflowConcluded
                ? ("No further lifecycle action", "Lifecycle sequence complete")
                : ("Start the first applicable stage", "No stage is currently in progress");
        }

        if (current.Status == StageStatus.Blocked)
        {
            return ($"Resolve {current.Name}", "The current stage is blocked");
        }

        if (current.Status == StageStatus.InProgress && current.NeedsPlannedCompletion)
        {
            return ("Set planned completion", $"Add the target completion date for {current.Name}");
        }

        if (current.IsOverdue && current.DaysRemaining is int overdue)
        {
            var days = Math.Abs(overdue);
            return ($"Recover {current.Name}", $"Current stage is {days} day{(days == 1 ? string.Empty : "s")} overdue");
        }

        if (current.Status == StageStatus.InProgress && current.DaysRemaining is int remaining)
        {
            return (
                $"Progress {current.Name}",
                remaining == 0
                    ? "Planned completion is due today"
                    : $"{remaining} day{(remaining == 1 ? string.Empty : "s")} remain to planned completion");
        }

        return ($"Progress {current.Name}", "Operational follow-up");
    }

    private static (string Status, string Detail) BuildScheduleStatus(
        TimelineItemVm? current,
        int completedLateCount)
    {
        if (current is { IsOverdue: true, DaysRemaining: int overdue })
        {
            var days = Math.Abs(overdue);
            return ("Current stage overdue", $"{days} day{(days == 1 ? string.Empty : "s")} beyond planned completion");
        }

        if (current is { Status: StageStatus.InProgress, NeedsPlannedCompletion: true })
        {
            return ("Not assessed", "Set the current-stage planned completion date");
        }

        if (current is { Status: StageStatus.InProgress, DaysRemaining: int remaining })
        {
            return remaining switch
            {
                0 => ("Due today", "Current stage reaches planned completion today"),
                <= 7 => ("Due soon", $"{remaining} day{(remaining == 1 ? string.Empty : "s")} remaining"),
                _ => ("On schedule", $"{remaining} day{(remaining == 1 ? string.Empty : "s")} remaining")
            };
        }

        if (completedLateCount > 0)
        {
            return (
                $"{completedLateCount} completed late",
                "Completed after the recorded planned completion date");
        }

        return ("No variance", "No current overdue stage or recorded late completion");
    }

    public static string StageStatusLabel(StageStatus status) => status switch
    {
        StageStatus.Completed => "Completed",
        StageStatus.InProgress => "In progress",
        StageStatus.Blocked => "Blocked",
        StageStatus.Skipped => "Skipped",
        StageStatus.NotStarted => "Not started",
        _ => "Unknown"
    };
}

public sealed class ProjectOverviewAccessVm
{
    public bool IsAdmin { get; init; }
    public bool IsHoD { get; init; }
    public bool IsAssignedProjectOfficer { get; init; }
    public bool IsAssignedHoD { get; init; }
    public bool CanAssignRoles => IsAdmin || IsHoD;
    public bool CanEditTimeline { get; init; }
    public bool CanReviewPlan => IsAdmin || IsHoD;
    public bool CanRequestStageChange => IsAssignedProjectOfficer || IsHoD || IsAdmin;
    public bool CanApplyStageChangeDirectly => IsAdmin || IsHoD;
}

public sealed class ProjectTimelinePanelVm
{
    public TimelineVm Timeline { get; init; } = new();
    public ProjectOverviewAccessVm Access { get; init; } = new();
}
