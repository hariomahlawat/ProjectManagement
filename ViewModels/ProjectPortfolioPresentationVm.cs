using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.ViewModels;

public sealed class ProjectPortfolioPresentationVm
{
    public string PageTitle { get; init; } = "Project";
    public ProjectLifecycleStatus LifecycleStatus { get; init; } = ProjectLifecycleStatus.Active;
    public TimelineItemVm? CurrentStage { get; init; }
    public TimelineItemVm? NextStage { get; init; }
    public bool IsWorkflowConcluded { get; init; }
    public bool IsTerminalLifecycle =>
        LifecycleStatus is ProjectLifecycleStatus.Completed or ProjectLifecycleStatus.Cancelled;
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
    public int ProfileCompletedCount { get; init; }
    public int ProfileTotalCount { get; init; }
    public IReadOnlyList<string> MissingProfileFacts { get; init; } = Array.Empty<string>();
    public string PlanStatus { get; init; } = "Not approved";
    public string PlanHealth { get; init; } = "Current-stage plan not approved";
    public string ScheduleStatus { get; init; } = "Not assessed";
    public string ScheduleDetail { get; init; } = "Set the current-stage planned completion date";
    public string LifecycleSummarySubtitle => IsTerminalLifecycle
        ? "Historical plan, schedule and record status at lifecycle closure."
        : "Timeline plan health, schedule status and the next operational action.";
    public string TimelinePlanLabel => IsTerminalLifecycle ? "Historical plan" : "Timeline plan";
    public string ScheduleStatusLabel => IsTerminalLifecycle ? "Historical schedule" : "Schedule status";
    public string NextActionLabel => IsTerminalLifecycle
        ? (BackfillStageCount > 0 ? "Record action" : "Lifecycle action")
        : "Next action";
    public string CurrentStageDisplay => LifecycleStatus switch
    {
        ProjectLifecycleStatus.Completed => "Project completed",
        ProjectLifecycleStatus.Cancelled => "Project cancelled",
        _ => IsWorkflowConcluded ? "Lifecycle concluded" : CurrentStage?.Name ?? "Not started"
    };
    public string CurrentStageDetail
    {
        get
        {
            if (LifecycleStatus == ProjectLifecycleStatus.Completed)
            {
                return "Project lifecycle completed";
            }

            if (LifecycleStatus == ProjectLifecycleStatus.Cancelled)
            {
                return "Stage progression ceased at cancellation";
            }

            if (IsWorkflowConcluded)
            {
                return BackfillStageCount > 0
                    ? $"Workflow concluded · {BackfillStageCount} stage{(BackfillStageCount == 1 ? string.Empty : "s")} require backfill"
                    : "All applicable stages are complete or skipped";
            }

            var currentDetail = CurrentStage?.HasPendingRequest == true
                ? $"{PendingActionLabel(CurrentStage.PendingStatus)} · Awaiting HoD approval"
                : CurrentStage?.Code ?? "No active stage";

            if (NextStage is not null)
            {
                return $"{currentDetail} · Next: {NextStage.Name}";
            }

            if (BackfillStageCount > 0)
            {
                return $"{currentDetail} · {BackfillStageCount} stage{(BackfillStageCount == 1 ? string.Empty : "s")} require backfill";
            }

            return currentDetail;
        }
    }
    public string NextAction { get; init; } = "Review project status";
    public string NextActionDetail { get; init; } = "Operational follow-up";
    public string ProfileCompletenessDetail => CompletenessPercent == 100
        ? "Core profile complete"
        : $"{MissingProfileFacts.Count} recommended detail{(MissingProfileFacts.Count == 1 ? string.Empty : "s")} missing";

    public static ProjectPortfolioPresentationVm Create(Project? project, TimelineVm timeline, bool hasBackfill)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        var ordered = timeline.Items.OrderBy(item => item.SortOrder).ToArray();
        var lifecyclePosition = ProjectLifecyclePositionResolver.Resolve(
            ordered
                .Where(item => !StageCodes.IsTot(item.Code))
                .Select(item => new ProjectLifecycleStageSnapshot(
                    item.Code,
                    item.Name,
                    item.Status,
                    item.SortOrder,
                    item.CompletedOn,
                    item.Status != StageStatus.NotStarted ||
                    item.HasPlanDates ||
                    item.HasActualDates)));

        var isWorkflowConcluded = lifecyclePosition.IsConcluded;
        var current = FindTimelineItem(ordered, lifecyclePosition.CurrentStage);
        var next = FindTimelineItem(ordered, lifecyclePosition.NextStage);
        var lifecycleStatus = project?.LifecycleStatus ?? ProjectLifecycleStatus.Active;
        var isTerminalLifecycle = IsTerminalLifecycleStatus(lifecycleStatus);

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
            ("project name", !string.IsNullOrWhiteSpace(project?.Name)),
            ("category", project?.CategoryId is not null),
            ("technical category", project?.TechnicalCategoryId is not null),
            ("project type", project?.ProjectTypeId is not null),
            ("Head of Department", project?.HodUserId is not null),
            ("Project Officer", project?.LeadPoUserId is not null),
            ("sponsoring unit", project?.SponsoringUnitId is not null),
            ("sponsoring line directorate", project?.SponsoringLineDirectorateId is not null),
            ("project description", !string.IsNullOrWhiteSpace(project?.Description))
        };
        var missingProfileFacts = profileChecks
            .Where(item => !item.IsPresent)
            .Select(item => item.Label)
            .ToArray();

        var nextAction = BuildNextAction(
            current,
            isWorkflowConcluded,
            backfillCount,
            lifecycleStatus);
        var schedule = BuildScheduleStatus(
            current,
            completedLateCount,
            lifecycleStatus);
        var planStatus = BuildPlanStatus(project, timeline, isTerminalLifecycle);
        var planHealth = lifecycleStatus switch
        {
            ProjectLifecycleStatus.Completed => "Project lifecycle completed; planning is read-only",
            ProjectLifecycleStatus.Cancelled => "Project lifecycle cancelled; planning is read-only",
            _ => project?.PlanApprovedAt.HasValue == true
                ? "Current-stage deadline monitored"
                : timeline.PlanPendingApproval
                    ? "Timeline approval pending"
                    : current?.HasPendingCompletion == true
                        ? "Completion update awaiting HoD approval"
                        : current?.NeedsPlannedCompletion == true
                            ? "Current-stage planned completion not set"
                            : "Current-stage plan not approved"
        };

        return new ProjectPortfolioPresentationVm
        {
            PageTitle = project?.Name ?? "Project",
            LifecycleStatus = lifecycleStatus,
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
            DelayedStageCount = isTerminalLifecycle ? completedLateCount : delayed,
            CompletedLateStageCount = completedLateCount,
            CurrentStageOverdueDays = isTerminalLifecycle ? null : currentOverdueDays,
            BackfillStageCount = backfillCount,
            CompletenessPercent = (int)Math.Round(profileChecks.Count(item => item.IsPresent) * 100d / profileChecks.Length),
            ProfileCompletedCount = profileChecks.Count(item => item.IsPresent),
            ProfileTotalCount = profileChecks.Length,
            MissingProfileFacts = missingProfileFacts,
            PlanStatus = planStatus,
            PlanHealth = planHealth,
            ScheduleStatus = schedule.Status,
            ScheduleDetail = schedule.Detail,
            NextAction = nextAction.Action,
            NextActionDetail = nextAction.Detail
        };
    }

    private static TimelineItemVm? FindTimelineItem(
        IReadOnlyList<TimelineItemVm> timeline,
        ProjectLifecycleStageSnapshot? stage)
    {
        if (stage is null)
        {
            return null;
        }

        return timeline.FirstOrDefault(item =>
            item.SortOrder == stage.SortOrder &&
            string.Equals(item.Code, stage.Code, StringComparison.OrdinalIgnoreCase))
            ?? timeline.FirstOrDefault(item =>
                string.Equals(item.Code, stage.Code, StringComparison.OrdinalIgnoreCase));
    }

    private static (string Action, string Detail) BuildNextAction(
        TimelineItemVm? current,
        bool isWorkflowConcluded,
        int backfillCount,
        ProjectLifecycleStatus lifecycleStatus)
    {
        if (IsTerminalLifecycleStatus(lifecycleStatus))
        {
            if (backfillCount > 0)
            {
                return (
                    "Complete historical record backfill",
                    "Add missing completion details without reopening the lifecycle");
            }

            return lifecycleStatus == ProjectLifecycleStatus.Cancelled
                ? ("No further lifecycle action", "Stage progression ceased at cancellation")
                : ("No further lifecycle action", "Project lifecycle is complete");
        }

        if (backfillCount > 0)
        {
            return (
                $"Complete missing details for {backfillCount} stage{(backfillCount == 1 ? string.Empty : "s")}",
                "Completion dates or mandatory stage facts require attention");
        }

        if (current?.HasPendingRequest == true)
        {
            return (
                "Await HoD approval",
                $"{PendingActionLabel(current.PendingStatus)} and visible on the timeline");
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

        if (current.Status == StageStatus.NotStarted)
        {
            return ($"Start {current.Name}", "The current stage has not been started");
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
        int completedLateCount,
        ProjectLifecycleStatus lifecycleStatus)
    {
        if (IsTerminalLifecycleStatus(lifecycleStatus))
        {
            if (completedLateCount > 0)
            {
                return (
                    $"{completedLateCount} stage{(completedLateCount == 1 ? string.Empty : "s")} completed late",
                    "Historical variance retained at lifecycle closure");
            }

            return lifecycleStatus == ProjectLifecycleStatus.Cancelled
                ? ("Lifecycle cancelled", "No active schedule remains after cancellation")
                : ("Lifecycle completed", "No active schedule remains");
        }

        if (current?.HasPendingRequest == true)
        {
            return (
                current.PendingStatus?.Equals("Completed", StringComparison.OrdinalIgnoreCase) == true
                    ? "Completion awaiting approval"
                    : "Stage update awaiting approval",
                "Official lifecycle status will change after the HoD decision");
        }

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

        if (current is { Status: StageStatus.NotStarted })
        {
            return (
                "Current stage not started",
                "Schedule tracking begins when the current stage starts");
        }

        if (completedLateCount > 0)
        {
            return (
                $"{completedLateCount} completed late",
                "Completed after the recorded planned completion date");
        }

        return ("No variance", "No current overdue stage or recorded late completion");
    }

    private static bool IsTerminalLifecycleStatus(ProjectLifecycleStatus lifecycleStatus) =>
        lifecycleStatus is ProjectLifecycleStatus.Completed or ProjectLifecycleStatus.Cancelled;

    private static string BuildPlanStatus(
        Project? project,
        TimelineVm timeline,
        bool isTerminalLifecycle)
    {
        if (project?.PlanApprovedAt.HasValue == true)
        {
            return "Approved";
        }

        if (isTerminalLifecycle)
        {
            return "Not recorded";
        }

        return timeline.PlanPendingApproval ? "Pending" : "Not approved";
    }

    public static string PendingActionLabel(string? pendingStatus) => pendingStatus?.Trim().ToLowerInvariant() switch
    {
        "completed" => "Completion submitted",
        "inprogress" => "Start submitted",
        "blocked" => "Blocked status submitted",
        "skipped" => "Skip submitted",
        "notstarted" => "Reopen submitted",
        _ => "Stage update submitted"
    };

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
    public bool CanAssignRoles => IsAdmin || IsHoD;
    public bool CanEditTimeline { get; init; }
    public bool CanReviewPlan => IsAdmin || IsHoD;
    public bool CanSubmitStageUpdate => IsAssignedProjectOfficer && !IsHoD;
    public bool CanApplyStageChangeDirectly => IsHoD;
}

public sealed class ProjectTimelinePanelVm
{
    public TimelineVm Timeline { get; init; } = new();
    public ProjectOverviewAccessVm Access { get; init; } = new();
    public ProjectLifecycleStatus LifecycleStatus { get; init; } = ProjectLifecycleStatus.Active;
    public bool IsLegacy { get; init; }

    public bool HasRecordedStageHistory =>
        Timeline.Items.Any(item =>
            item.Status != StageStatus.NotStarted ||
            item.HasPlanDates ||
            item.HasActualDates);

    public bool ShowTimeline =>
        LifecycleStatus is ProjectLifecycleStatus.Active
            or ProjectLifecycleStatus.Completed
            or ProjectLifecycleStatus.Cancelled;
}
