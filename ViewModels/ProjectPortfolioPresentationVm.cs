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
    public int CompletedStages { get; init; }
    public int TotalStages { get; init; }
    public int ProgressMaximum => TotalStages == 0 ? 1 : TotalStages;
    public int ProgressPercent { get; init; }
    public int DelayedStageCount { get; init; }
    public int CompletenessPercent { get; init; }
    public string PlanStatus { get; init; } = "Not approved";
    public string PlanHealth { get; init; } = "Stage records aligned";

    public static ProjectPortfolioPresentationVm Create(Project? project, TimelineVm timeline, bool hasBackfill)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        var ordered = timeline.Items.OrderBy(item => item.SortOrder).ToArray();
        var current = ordered.FirstOrDefault(item => item.Status == StageStatus.InProgress)
            ?? ordered.FirstOrDefault(item => item.Status is not StageStatus.Completed and not StageStatus.Skipped)
            ?? ordered.LastOrDefault();
        var next = current is null
            ? null
            : ordered.FirstOrDefault(item => item.SortOrder > current.SortOrder && item.Status != StageStatus.Skipped);
        var delayed = ordered.Count(item => item.IsOverdue || (item.StartVarianceDays ?? 0) > 0 || (item.FinishVarianceDays ?? 0) > 0);
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

        return new ProjectPortfolioPresentationVm
        {
            PageTitle = project?.Name ?? "Project",
            CurrentStage = current,
            NextStage = next,
            CompletedStages = timeline.CompletedCount,
            TotalStages = timeline.TotalStages,
            ProgressPercent = timeline.TotalStages == 0 ? 0 : (int)Math.Round(timeline.CompletedCount * 100d / timeline.TotalStages),
            DelayedStageCount = delayed,
            CompletenessPercent = (int)Math.Round(completeness.Count(value => value) * 100d / completeness.Length),
            PlanStatus = project?.PlanApprovedAt.HasValue == true ? "Approved" : timeline.PlanPendingApproval ? "Pending" : "Not approved",
            PlanHealth = hasBackfill ? "Backfill required" : "Stage records aligned"
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
    public bool CanReviewPlan => IsHoD;
    public bool CanRequestStageChange => IsAssignedProjectOfficer || IsHoD;
    public bool CanApplyStageChangeDirectly => IsHoD;
}

public sealed class ProjectTimelinePanelVm
{
    public TimelineVm Timeline { get; init; } = new();
    public ProjectOverviewAccessVm Access { get; init; } = new();
}
