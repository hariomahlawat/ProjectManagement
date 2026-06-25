using System;
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
    public int SkippedStages { get; init; }
    public int ResolvedStages { get; init; }
    public int TotalStages { get; init; }
    public int ProgressMaximum => TotalStages == 0 ? 1 : TotalStages;
    public int ProgressPercent { get; init; }
    public int DelayedStageCount { get; init; }
    public int BackfillStageCount { get; init; }
    public int CompletenessPercent { get; init; }
    public string PlanStatus { get; init; } = "Not approved";
    public string PlanHealth { get; init; } = "Stage records aligned";
    public string CurrentStageDisplay => IsWorkflowConcluded ? "Lifecycle concluded" : CurrentStage?.Name ?? "Not started";
    public string CurrentStageDetail => IsWorkflowConcluded ? "All applicable stages are complete or skipped" : CurrentStage?.Code ?? "No active stage";
    public string NextAction { get; init; } = "Review project status";

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
        var skippedCount = ordered.Count(item => item.Status == StageStatus.Skipped);
        var resolvedCount = completedCount + skippedCount;
        var delayed = ordered.Count(item =>
            item.IsOverdue ||
            (item.Status == StageStatus.Completed && item.ShowFinishVariance && (item.FinishVarianceDays ?? 0) > 0));
        var backfillCount = ordered.Count(item => item.RequiresBackfill);
        var completeness = new[]
        {
            !string.IsNullOrWhiteSpace(project?.Name),
            project?.CategoryId is not null,
            project?.TechnicalCategoryId is not null,
            project?.HodUserId is not null,
            project?.LeadPoUserId is not null,
            project?.SponsoringUnitId is not null,
            project?.SponsoringLineDirectorateId is not null,
            !string.IsNullOrWhiteSpace(project?.Description)
        };

        var nextAction = backfillCount > 0
            ? $"Complete missing details for {backfillCount} stage{(backfillCount == 1 ? string.Empty : "s")}" 
            : current is not null
                ? $"Progress {current.Name}"
                : isWorkflowConcluded
                    ? "No further lifecycle action"
                    : "Start the first applicable stage";

        return new ProjectPortfolioPresentationVm
        {
            PageTitle = project?.Name ?? "Project",
            CurrentStage = current,
            NextStage = next,
            IsWorkflowConcluded = isWorkflowConcluded,
            CompletedStages = completedCount,
            SkippedStages = skippedCount,
            ResolvedStages = resolvedCount,
            TotalStages = timeline.TotalStages,
            ProgressPercent = timeline.TotalStages == 0 ? 0 : (int)Math.Round(resolvedCount * 100d / timeline.TotalStages),
            DelayedStageCount = delayed,
            BackfillStageCount = backfillCount,
            CompletenessPercent = (int)Math.Round(completeness.Count(value => value) * 100d / completeness.Length),
            PlanStatus = project?.PlanApprovedAt.HasValue == true ? "Approved" : timeline.PlanPendingApproval ? "Pending" : "Not approved",
            PlanHealth = project?.PlanApprovedAt.HasValue == true
                ? "Current-stage deadline monitored"
                : timeline.PlanPendingApproval
                    ? "Timeline approval pending"
                    : "Current-stage planned completion not approved",
            NextAction = nextAction
        };
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
