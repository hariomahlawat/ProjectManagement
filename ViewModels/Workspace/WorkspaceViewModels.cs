using System;
using System.Linq;

namespace ProjectManagement.ViewModels.Workspace;

public sealed class ProjectOfficerWorkspaceVm
{
    public string UserDisplayName { get; set; } = string.Empty;
    public string RoleTitle { get; set; } = "Project Officer Workspace";
    public string MyProjectsUrl { get; set; } = "/Projects/Ongoing";
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public int PortfolioHealthPercent { get; set; }
    public string PortfolioHealthLabel { get; set; } = "Good";
    public string RecordHealthSummaryLabel { get; set; } = "Good";
    public int AssignedProjectCount { get; set; }
    public int PendingWithMeCount { get; set; }
    public int DailyActionCount { get; set; }
    public int OverdueTaskCount { get; set; }
    public int RecordGapCount { get; set; }
    public int ProjectsNeedingAttentionCount { get; set; }
    public int ProjectTimelineIssueCount { get; set; }
    public int AssignedIdeaCount { get; set; }
    public int PendingConferenceDirectionCount { get; set; }
    public int AotsUnreadCount { get; set; }
    public int? NavigationActionCount { get; set; }
    public int? NavigationFollowUpCount { get; set; }
    public string AotsUrl { get; set; } = "/DocumentRepository/Documents?scope=aots";
    public WorkspaceEngagementVm Engagement { get; set; } = new();
    public ErpActivityStripVm ActivityStrip { get; set; } = new();
    public ErpActivityYearVm ActivityYear { get; set; } = new();
    public WorkspaceDocumentHubVm DocumentHub { get; set; } = new();
    public IReadOnlyList<WorkspaceCommandChipVm> CommandChips { get; set; } = Array.Empty<WorkspaceCommandChipVm>();
    public WorkspaceDataCompletenessInsightVm DataCompletenessInsight { get; set; } = new();
    public IReadOnlyList<WorkspaceRailItemVm> RailItems { get; set; } = Array.Empty<WorkspaceRailItemVm>();
    public IReadOnlyList<WorkspaceAttentionItemVm> PendingWithMe { get; set; } = Array.Empty<WorkspaceAttentionItemVm>();
    public IReadOnlyList<WorkspaceActionQueueItemVm> ActionQueue { get; set; } = Array.Empty<WorkspaceActionQueueItemVm>();
    public IReadOnlyList<WorkspaceActionQueueGroupVm> ActionQueueGroups { get; set; } = Array.Empty<WorkspaceActionQueueGroupVm>();
    public IReadOnlyList<WorkspaceActionQueueItemVm> AllActionQueue { get; set; } = Array.Empty<WorkspaceActionQueueItemVm>();
    public IReadOnlyList<WorkspaceActionQueueGroupVm> AllActionQueueGroups { get; set; } = Array.Empty<WorkspaceActionQueueGroupVm>();
    public int ActionQueueTotalCount { get; set; }
    public int ActionProjectCount { get; set; }
    public int ActionIdeaCount { get; set; }
    public int ActionTaskCount { get; set; }
    public WorkspaceActionQueueSummaryVm ActionSummary { get; set; } = new();

    public string ActionHeadline => ActionQueueTotalCount switch
    {
        0 when RecordGapCount == 1 => "1 record gap requires attention",
        0 when RecordGapCount > 1 => $"{RecordGapCount} record gaps require attention",
        0 => "Your workspace is clear",
        1 => "1 action requires attention",
        _ => $"{ActionQueueTotalCount} actions require attention"
    };

    public int FollowUpCount => NavigationFollowUpCount
        ?? PersonalReminders.Count + Ideas.Count(idea => idea.NeedsUpdate);

    public int? ActionQueueBadgeCount
    {
        get
        {
            var count = NavigationActionCount ?? ActionQueueTotalCount;
            return count > 0 ? count : null;
        }
    }

    public string OperationalSummary
    {
        get
        {
            var scope = new List<string>();
            var projectCount = ActionSummary.ProjectCount > 0 ? ActionSummary.ProjectCount : ActionProjectCount;
            var ideaCount = ActionSummary.IdeaCount > 0 ? ActionSummary.IdeaCount : ActionIdeaCount;
            var taskCount = ActionSummary.TaskCount > 0 ? ActionSummary.TaskCount : ActionTaskCount;

            if (projectCount > 0)
            {
                scope.Add($"{projectCount} project{(projectCount == 1 ? string.Empty : "s")}");
            }

            if (ideaCount > 0)
            {
                scope.Add($"{ideaCount} idea{(ideaCount == 1 ? string.Empty : "s")}");
            }

            if (taskCount > 0)
            {
                scope.Add($"{taskCount} task{(taskCount == 1 ? string.Empty : "s")}");
            }

            var parts = new List<string>();
            if (scope.Count > 0)
            {
                parts.Add($"Across {JoinHumanReadable(scope)}");
            }
            else if (AssignedProjectCount > 0 && ActionQueueTotalCount == 0)
            {
                parts.Add($"Across {AssignedProjectCount} project{(AssignedProjectCount == 1 ? string.Empty : "s")}");
            }

            AddCount(parts, ActionSummary.ReturnedCount, "returned item", "returned items");
            AddCount(parts, ActionSummary.OverdueTaskCount, "overdue task", "overdue tasks");
            AddCount(parts,
                ActionSummary.ConferenceDirectionCount > 0 ? ActionSummary.ConferenceDirectionCount : PendingConferenceDirectionCount,
                "conference direction",
                "conference directions");
            AddCount(parts,
                ActionSummary.TimelineCount > 0 ? ActionSummary.TimelineCount : ProjectTimelineIssueCount,
                "timeline action",
                "timeline actions");
            AddCount(parts, ActionSummary.ProjectUpdateCount, "overdue update", "overdue updates");
            AddCount(parts, ActionSummary.IdeaUpdateCount, "idea update", "idea updates");
            AddCount(parts, ActionSummary.DueSoonTaskCount, "task due soon", "tasks due soon");
            AddCount(parts, ActionSummary.AotsCount, "AOTS document", "AOTS documents");

            if (RecordGapCount > 0)
            {
                AddCount(parts, RecordGapCount, "record gap", "record gaps");
            }

            return parts.Count == 0 ? "No immediate action required" : string.Join(" · ", parts);
        }
    }

    private static void AddCount(ICollection<string> parts, int count, string singular, string plural)
    {
        if (count > 0)
        {
            parts.Add($"{count} {(count == 1 ? singular : plural)}");
        }
    }

    private static string JoinHumanReadable(IReadOnlyList<string> values) => values.Count switch
    {
        0 => string.Empty,
        1 => values[0],
        2 => $"{values[0]} and {values[1]}",
        _ => $"{string.Join(", ", values.Take(values.Count - 1))} and {values[^1]}"
    };

    public int ActionQueueHiddenCount => Math.Max(0, ActionQueueTotalCount - ActionQueue.Sum(item => item.RepresentedActionCount));
    public IReadOnlyList<WorkspaceAttentionItemVm> RemarksDue { get; set; } = Array.Empty<WorkspaceAttentionItemVm>();
    public IReadOnlyList<WorkspaceTaskVm> OfficialTasksDue { get; set; } = Array.Empty<WorkspaceTaskVm>();
    public IReadOnlyList<WorkspaceIdeaVm> IdeasNeedingUpdate { get; set; } = Array.Empty<WorkspaceIdeaVm>();
    public IReadOnlyList<WorkspaceAotsDocumentVm> AotsDocuments { get; set; } = Array.Empty<WorkspaceAotsDocumentVm>();
    public IReadOnlyList<WorkspaceAttentionItemVm> ReturnedItems { get; set; } = Array.Empty<WorkspaceAttentionItemVm>();
    public IReadOnlyList<WorkspaceAttentionItemVm> TimelineAlerts { get; set; } = Array.Empty<WorkspaceAttentionItemVm>();
    public int OfficialTaskCount { get; set; }
    public int RemarksDueCount { get; set; }
    public int IdeasNeedingUpdateCount { get; set; }
    public IReadOnlyList<WorkspaceAttentionItemVm> WaitingOnOthers { get; set; } = Array.Empty<WorkspaceAttentionItemVm>();
    public IReadOnlyList<WorkspaceProjectMatrixRowVm> ProjectMatrix { get; set; } = Array.Empty<WorkspaceProjectMatrixRowVm>();
    public IReadOnlyList<WorkspaceTaskVm> OfficialTasks { get; set; } = Array.Empty<WorkspaceTaskVm>();
    public IReadOnlyList<WorkspaceIdeaVm> Ideas { get; set; } = Array.Empty<WorkspaceIdeaVm>();
    public IReadOnlyList<WorkspaceRecordHealthVm> RecordHealth { get; set; } = Array.Empty<WorkspaceRecordHealthVm>();
    public IReadOnlyList<WorkspaceImprovementVm> ImproveScoreItems { get; set; } = Array.Empty<WorkspaceImprovementVm>();
    public IReadOnlyList<WorkspaceProjectImprovementVm> ImproveProjects { get; set; } = Array.Empty<WorkspaceProjectImprovementVm>();
    public int ImproveProjectsTotalCount { get; set; }
    public int ImproveProjectsHiddenCount => Math.Max(0, ImproveProjectsTotalCount - ImproveProjects.Count);
    public WorkspaceAttentionItemVm? NextBestAction { get; set; }
    public IReadOnlyList<WorkspaceQuickActionVm> QuickActions { get; set; } = Array.Empty<WorkspaceQuickActionVm>();
    public IReadOnlyList<WorkspaceReminderVm> PersonalReminders { get; set; } = Array.Empty<WorkspaceReminderVm>();
    public CommandOfficerWorkloadVm? CommandWorkloadCard { get; set; }
    public IReadOnlyList<WorkspaceUpcomingEventVm> UpcomingEvents { get; set; } = Array.Empty<WorkspaceUpcomingEventVm>();
}

public sealed class WorkspaceDocumentHubVm
{
    public int FavouriteCount { get; set; }
    public int AotsUnreadCount { get; set; }
    public int RecentCount { get; set; }
    public int UploadedByMeCount { get; set; }
    public IReadOnlyList<WorkspaceDocumentVm> Favourites { get; set; } = Array.Empty<WorkspaceDocumentVm>();
    public IReadOnlyList<WorkspaceDocumentVm> Aots { get; set; } = Array.Empty<WorkspaceDocumentVm>();
    public IReadOnlyList<WorkspaceDocumentVm> Recent { get; set; } = Array.Empty<WorkspaceDocumentVm>();
    public IReadOnlyList<WorkspaceDocumentVm> UploadedByMe { get; set; } = Array.Empty<WorkspaceDocumentVm>();
}

public sealed class WorkspaceDocumentVm
{
    public Guid DocumentId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Office { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public DateOnly? DocumentDate { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTimeOffset? LastViewedAtUtc { get; set; }
    public bool IsAots { get; set; }
    public bool IsFavourite { get; set; }
    public bool IsUnreadAots { get; set; }
    public string OpenUrl { get; set; } = string.Empty;
}

public sealed class ProjectOfficerDocumentListRenderVm
{
    public IReadOnlyList<WorkspaceDocumentVm> Documents { get; init; } = Array.Empty<WorkspaceDocumentVm>();
    public string EmptyTitle { get; init; } = "No documents";
    public string EmptyText { get; init; } = "Documents will appear here when available.";
    public string DateMode { get; init; } = "document";
}

public sealed class ProjectOfficerActionGroupsRenderVm
{
    public IReadOnlyList<WorkspaceActionQueueGroupVm> Groups { get; init; } = Array.Empty<WorkspaceActionQueueGroupVm>();
    public bool ShowRecommended { get; init; } = true;
}

public sealed class ProjectOfficerProjectTableRenderVm
{
    public IReadOnlyList<WorkspaceProjectMatrixRowVm> Rows { get; init; } = Array.Empty<WorkspaceProjectMatrixRowVm>();
    public bool Compact { get; init; }
}

public sealed class WorkspaceUpcomingEventVm
{
    public string InstanceId { get; set; } = string.Empty;

    public Guid SeriesId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string CategoryLabel { get; set; } = string.Empty;

    public string GroupLabel { get; set; } = string.Empty;

    public string DateLabel { get; set; } = string.Empty;

    public string TimeLabel { get; set; } = string.Empty;

    public string? Location { get; set; }

    public string Icon { get; set; } = "bi-calendar-event";

    public string Tone { get; set; } = "event";

    public DateOnly LocalDate { get; set; }

    public DateTimeOffset StartUtc { get; set; }

    public DateTimeOffset EndUtc { get; set; }

    public bool IsAllDay { get; set; }

    public bool IsCelebration { get; set; }

    public string OpenUrl { get; set; } = "/Calendar";
}

public sealed class WorkspaceCommandChipVm
{
    public string Label { get; set; } = string.Empty;

    public int Count { get; set; }

    public string State { get; set; } = "Neutral";

    public string Icon { get; set; } = "bi-dot";
}

public sealed class WorkspaceDataCompletenessInsightVm
{
    public int AverageCompletenessPercent { get; set; }

    public int ProjectsWithGapsCount { get; set; }

    public int AssignedProjectsCount { get; set; }

    public string MostCommonGapLabel { get; set; } = "None";

    public string? BestProjectName { get; set; }

    public int? BestProjectScore { get; set; }

    public string? NeedsMostAttentionProjectName { get; set; }

    public int? NeedsMostAttentionProjectScore { get; set; }

    public IReadOnlyList<WorkspaceGapFrequencyVm> GapFrequencies { get; set; } = Array.Empty<WorkspaceGapFrequencyVm>();
}

public sealed class WorkspaceGapFrequencyVm
{
    public string Label { get; set; } = string.Empty;

    public int Count { get; set; }

    public int PercentOfMax { get; set; }
}

public sealed class WorkspaceRailItemVm
{
    public string Label { get; set; } = string.Empty;

    public string Icon { get; set; } = "bi-circle";

    public int Count { get; set; }

    public string Anchor { get; set; } = "#";

    public bool IsPrimary { get; set; }
}

public sealed class WorkspaceAotsDocumentVm
{
    public Guid DocumentId { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Office { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public string OpenUrl { get; set; } = string.Empty;
}

public sealed class WorkspaceConferenceDirectionActionVm
{
    public ConferenceItemKind Kind { get; set; }

    public int ItemId { get; set; }

    public int? ProjectId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string DirectionText { get; set; } = string.Empty;

    public DateTime IssuedAtUtc { get; set; }

    public string ActionUrl { get; set; } = string.Empty;
}

public sealed class WorkspaceActionQueueItemVm
{
    public int? ProjectId { get; set; }

    public string WorkItemKey { get; set; } = string.Empty;

    public int RepresentedActionCount { get; set; } = 1;

    public string Type { get; set; } = string.Empty;

    public string BadgeText { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public string Meta { get; set; } = string.Empty;

    public string PriorityReason { get; set; } = string.Empty;

    public string Severity { get; set; } = "Info";

    public string ActionText { get; set; } = string.Empty;

    public string ActionUrl { get; set; } = string.Empty;

    public DateTime? SortDateUtc { get; set; }

    // Lower values appear first in the unified action queue.
    public int PriorityRank { get; set; } = 100;
}

public sealed class WorkspaceActionQueueGroupVm
{
    public string Key { get; set; } = string.Empty;

    public int? ProjectId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string PrimaryUrl { get; set; } = string.Empty;

    public string Severity { get; set; } = "Info";

    public bool IsRecommended { get; set; }

    public IReadOnlyList<WorkspaceActionQueueItemVm> Actions { get; set; } = Array.Empty<WorkspaceActionQueueItemVm>();

    public int ActionCount => Actions.Sum(action => action.RepresentedActionCount);
}

public sealed class WorkspaceActionQueueSummaryVm
{
    public int TotalCount { get; set; }
    public int ProjectCount { get; set; }
    public int IdeaCount { get; set; }
    public int TaskCount { get; set; }
    public int ReturnedCount { get; set; }
    public int OverdueTaskCount { get; set; }
    public int ConferenceDirectionCount { get; set; }
    public int TimelineCount { get; set; }
    public int ProjectUpdateCount { get; set; }
    public int IdeaUpdateCount { get; set; }
    public int AotsCount { get; set; }
    public int DueSoonTaskCount { get; set; }
}

public sealed class WorkspaceAttentionItemVm
{
    public int? ProjectId { get; set; }

    public string WorkItemKey { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public string Severity { get; set; } = "Info";

    public string BadgeText { get; set; } = string.Empty;

    public string ActionText { get; set; } = string.Empty;

    public string ActionUrl { get; set; } = string.Empty;

    public DateTime? DueOrEventDateUtc { get; set; }
}

public sealed class WorkspaceProjectMatrixRowVm
{
    public int ProjectId { get; set; }

    public string ProjectName { get; set; } = string.Empty;

    public string CurrentStageCode { get; set; } = string.Empty;

    public string CurrentStageName { get; set; } = string.Empty;

    public int? DaysInCurrentStage { get; set; }

    public DateOnly? CurrentStagePdc { get; set; }

    public int? DaysUntilCurrentStagePdc { get; set; }

    public int? DaysSinceLastPoRemark { get; set; }

    public bool IsCurrentStageStartMissing { get; set; }

    public bool IsCurrentStagePdcMissing { get; set; }

    public bool IsCurrentStageNotStarted { get; set; }

    public string UpdateStatus { get; set; } = "Ok";

    public string TimelineStatus { get; set; } = "Ok";

    public string RecordStatus { get; set; } = "Ok";

    public string TaskStatus { get; set; } = "NotApplicable";

    public int RecordHealthPercent { get; set; }

    public int RecordGapCount { get; set; }

    public WorkspaceRecordHealthVm RecordHealth { get; set; } = new();

    public DateTime? LastPoRemarkAtUtc { get; set; }

    public bool HasBackfill { get; set; }

    public bool HasCurrentStageIssue { get; set; }

    public bool HasOverdueCurrentStage { get; set; }

    public string NextActionText { get; set; } = "Open";

    public string NextActionUrl { get; set; } = string.Empty;

    public string OpenUrl { get; set; } = string.Empty;

    public string AddRemarkUrl { get; set; } = string.Empty;

    public string TimelineUrl { get; set; } = string.Empty;
}

public sealed class WorkspaceTaskVm
{
    public int TaskId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string ContextLabel { get; set; } = "Miscellaneous";

    public string Priority { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateOnly DueDate { get; set; }

    public bool IsOverdue { get; set; }

    public bool IsDueSoon { get; set; }

    public int? DaysOverdue { get; set; }

    public string OpenUrl { get; set; } = string.Empty;
}

public sealed class WorkspaceIdeaVm
{
    public int IdeaId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTime LastActivityAtUtc { get; set; }

    public bool NeedsUpdate { get; set; }

    public int CommentCount { get; set; }

    public int DocumentCount { get; set; }

    public string OpenUrl { get; set; } = string.Empty;
}

public sealed class WorkspaceImprovementVm
{
    public int ProjectId { get; set; }

    public string ProjectName { get; set; } = string.Empty;

    public string Gap { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string Severity { get; set; } = "Warning";
}

public sealed class WorkspaceProjectGapDetailVm
{
    public string Label { get; set; } = string.Empty;

    public string ActionText { get; set; } = string.Empty;

    public string ActionUrl { get; set; } = string.Empty;

    public string Icon { get; set; } = "bi-dot";

    public string Severity { get; set; } = "Warning";
}

public sealed class WorkspaceProjectImprovementVm
{
    public int ProjectId { get; set; }

    public string ProjectName { get; set; } = string.Empty;

    public int FixCount { get; set; }

    public IReadOnlyList<string> FixLabels { get; set; } = Array.Empty<string>();

    public IReadOnlyList<WorkspaceProjectGapDetailVm> GapDetails { get; set; } = Array.Empty<WorkspaceProjectGapDetailVm>();

    public int HealthPercent { get; set; }

    public WorkspaceRecordHealthVm RecordHealth { get; set; } = new();

    public string HealthLabel { get; set; } = string.Empty;

    public string HealthCss { get; set; } = string.Empty;

    public IReadOnlyList<WorkspaceProjectGapDetailVm> PreviewGapDetails => GapDetails.Take(3).ToList();

    public int HiddenGapCount => Math.Max(0, GapDetails.Count - PreviewGapDetails.Count);

    public string Url { get; set; } = string.Empty;

    public string Severity { get; set; } = "Warning";
}

public sealed class WorkspaceRecordHealthVm
{
    public int ProjectId { get; set; }

    public string ProjectName { get; set; } = string.Empty;

    public int HealthPercent { get; set; }

    public string HealthLabel { get; set; } = "Good";

    // Legacy text list retained for compatibility with existing consumers and tests.
    public IReadOnlyList<string> Gaps { get; set; } = Array.Empty<string>();

    public IReadOnlyList<WorkspaceRecordGapVm> GapDetails { get; set; } = Array.Empty<WorkspaceRecordGapVm>();

    public IReadOnlyList<WorkspaceRecordComponentScoreVm> Components { get; set; } = Array.Empty<WorkspaceRecordComponentScoreVm>();

    public int RecordGapCount => GapDetails.Count;

    // Backward-compatible alias for older consumers.
    public int PendingFieldCount => RecordGapCount;

    public string OpenUrl { get; set; } = string.Empty;
}

public sealed class WorkspaceRecordGapVm
{
    public string Code { get; set; } = string.Empty;

    public string Component { get; set; } = string.Empty;

    public string FieldLabel { get; set; } = string.Empty;

    public string? StageCode { get; set; }

    public string Status { get; set; } = "Pending";

    public string Reason { get; set; } = string.Empty;

    public decimal EarnedPoints { get; set; }

    public decimal MaximumPoints { get; set; }

    public string ActionText { get; set; } = string.Empty;

    public string ActionUrl { get; set; } = string.Empty;

    public string Icon { get; set; } = "bi-exclamation-circle";

    public int Priority { get; set; }
}

public sealed class WorkspaceRecordComponentScoreVm
{
    public string Code { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public decimal EarnedPoints { get; set; }

    public decimal MaximumPoints { get; set; }

    public string Status { get; set; } = "Complete";

    public string Explanation { get; set; } = string.Empty;

    public IReadOnlyList<WorkspaceRecordGapVm> Gaps { get; set; } = Array.Empty<WorkspaceRecordGapVm>();
}

public sealed class WorkspaceEngagementVm
{
    public DateTime? LastLoginUtc { get; set; }

    public DateTime? LastActivityUtc { get; set; }

    public int LoginsThisMonth { get; set; }

    public int ActiveDaysThisMonth { get; set; }

    public int ActionsRecordedThisMonth { get; set; }

    public int RemarksPostedThisMonth { get; set; }

    public int TasksUpdatedThisMonth { get; set; }

    public int DocumentsUploadedThisMonth { get; set; }

    public string EngagementLabel { get; set; } = "Active";
}

public sealed class WorkspaceQuickActionVm
{
    public string Text { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string Icon { get; set; } = "bi-arrow-right";
}

public sealed class WorkspaceReminderVm
{
    public Guid ReminderId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Priority { get; set; } = string.Empty;

    public DateTimeOffset? DueAtUtc { get; set; }

    public bool IsPinned { get; set; }

    public string OpenUrl { get; set; } = string.Empty;
}

public static class WorkspaceDisplayHelpers
{
    // SECTION: Compact status labels keep matrix chips readable.
    public static string StatusLabel(string status) => status switch
    {
        "ActionRequired" => "Action Required",
        "NotApplicable" => "N/A",
        "Ok" => "OK",
        "Attention" => "Attention",
        _ => status
    };

    // SECTION: Project update labels separate the current state from the supporting age detail.
    public static string ProjectUpdateStatusLabel(WorkspaceProjectMatrixRowVm row)
    {
        if (!row.LastPoRemarkAtUtc.HasValue)
        {
            return "No update recorded";
        }

        return row.UpdateStatus switch
        {
            "ActionRequired" => "Update overdue",
            "Attention" => "Update due",
            "Ok" => "Current",
            _ => StatusLabel(row.UpdateStatus)
        };
    }

    public static string ProjectUpdateStatusDetail(WorkspaceProjectMatrixRowVm row)
    {
        if (!row.DaysSinceLastPoRemark.HasValue)
        {
            return "Add the first project update";
        }

        return row.DaysSinceLastPoRemark.Value switch
        {
            0 => "Updated today",
            1 => "Updated yesterday",
            var days => $"{days} days since update"
        };
    }

    public static string CurrentStageDurationLabel(WorkspaceProjectMatrixRowVm row)
    {
        if (row.IsCurrentStageNotStarted)
        {
            return "Not started";
        }

        if (!row.DaysInCurrentStage.HasValue)
        {
            return "Start date not recorded";
        }

        return row.DaysInCurrentStage.Value == 1
            ? "1 day in stage"
            : $"{row.DaysInCurrentStage.Value} days in stage";
    }

    // SECTION: Timeline labels distinguish current-stage issues from historical record gaps.
    public static string TimelineStatusLabel(WorkspaceProjectMatrixRowVm row)
    {
        if (row.HasOverdueCurrentStage)
        {
            var overdueDays = Math.Abs(row.DaysUntilCurrentStagePdc ?? 0);
            return overdueDays == 1 ? "Overdue by 1 day" : $"Overdue by {overdueDays} days";
        }

        if (row.IsCurrentStagePdcMissing)
        {
            return "Current-stage PDC missing";
        }

        if (row.IsCurrentStageStartMissing)
        {
            return "Current-stage start missing";
        }

        if (row.IsCurrentStageNotStarted)
        {
            return "Stage not started";
        }

        if (row.HasBackfill)
        {
            return "Historical dates incomplete";
        }

        if (row.DaysUntilCurrentStagePdc is 0)
        {
            return "Due today";
        }

        if (row.DaysUntilCurrentStagePdc is > 0 and <= 7)
        {
            return row.DaysUntilCurrentStagePdc == 1
                ? "Due in 1 day"
                : $"Due in {row.DaysUntilCurrentStagePdc} days";
        }

        return "On track";
    }

    public static string TimelineStatusDetail(WorkspaceProjectMatrixRowVm row)
    {
        if (row.HasOverdueCurrentStage && row.CurrentStagePdc.HasValue)
        {
            return $"PDC {row.CurrentStagePdc.Value:dd MMM yyyy}";
        }

        if (row.IsCurrentStagePdcMissing)
        {
            return "Set the PDC for the current stage";
        }

        if (row.IsCurrentStageStartMissing)
        {
            return "Record the current-stage start date";
        }

        if (row.IsCurrentStageNotStarted)
        {
            return "Timeline not yet active";
        }

        if (row.HasBackfill)
        {
            return "Complete missing historical stage dates";
        }

        if (row.CurrentStagePdc.HasValue)
        {
            return $"PDC {row.CurrentStagePdc.Value:dd MMM yyyy}";
        }

        return "Current-stage dates complete";
    }

    public static string TimelineStatusCss(WorkspaceProjectMatrixRowVm row)
    {
        if (row.HasOverdueCurrentStage)
        {
            return "danger";
        }

        if (row.HasCurrentStageIssue || row.HasBackfill)
        {
            return "warning";
        }

        if (row.IsCurrentStageNotStarted)
        {
            return "neutral";
        }

        return "good";
    }

    public static string TimelineActionLabel(WorkspaceProjectMatrixRowVm row)
    {
        if (row.HasOverdueCurrentStage)
        {
            return "Review timeline";
        }

        if (row.IsCurrentStagePdcMissing)
        {
            return "Set current-stage PDC";
        }

        if (row.IsCurrentStageStartMissing)
        {
            return "Set stage start";
        }

        if (row.HasBackfill)
        {
            return "Complete timeline";
        }

        return "Open timeline";
    }

    public static string ProjectNextActionLabel(WorkspaceProjectMatrixRowVm row)
    {
        if (row.HasOverdueCurrentStage || row.HasCurrentStageIssue || row.HasBackfill)
        {
            return TimelineActionLabel(row);
        }

        if (row.UpdateStatus is "ActionRequired" or "Attention")
        {
            return "Add Project Officer update";
        }

        return CompactActionLabel(row.NextActionText);
    }

    // SECTION: Matrix action labels are shortened for dense enterprise table rows.
    public static string CompactActionLabel(string action) => action switch
    {
        "Complete backfill" => "Complete timeline",
        "Update current stage" => "Update stage",
        "Update current stage dates" => "Set current-stage PDC",
        "Add remark" => "Add remark",
        "Complete project data" => "Complete data",
        _ => action
    };

    // SECTION: Record-health color classes map scores to human-readable health bands.
    public static string HealthCss(int percent)
    {
        if (percent >= 80)
        {
            return "good";
        }

        // Incomplete records need attention, but only very low completeness is critical.
        // This keeps red reserved for genuinely severe conditions.
        if (percent >= 26)
        {
            return "attention";
        }

        return "danger";
    }


    // SECTION: Record-health labels translate percentages into user-friendly bands.
    public static string HealthBandLabel(int percent)
    {
        if (percent >= 80)
        {
            return "Good";
        }

        if (percent >= 60)
        {
            return "Review";
        }

        return "Needs Work";
    }

    // SECTION: Improvement labels turn completeness gaps into direct action wording.
    public static string ImprovementLabel(string gap) => gap switch
    {
        "Brief description pending" => "Add brief description",
        "Add at least 3 project photos" => "Add project photos",
        "Upload at least 3 project documents" => "Upload project documents",
        "Add at least 1 project video" => "Add project video",
        _ when gap.Contains("Cost pending", StringComparison.OrdinalIgnoreCase) => gap.Replace(" pending", string.Empty, StringComparison.OrdinalIgnoreCase),
        "Supply Order Date pending" => "Add Supply Order Date",
        _ when gap.Contains("actual start missing", StringComparison.OrdinalIgnoreCase) => gap,
        _ when gap.Contains("actual completion missing", StringComparison.OrdinalIgnoreCase) => gap,
        "Current-stage Actual Start pending" => "Add current-stage Actual Start",
        "Current-stage Planned Start pending" => "Add current-stage Planned Start",
        "Current-stage timeline (PDC) pending" => "Add current-stage PDC",
        _ => gap
    };

    // SECTION: Short gap labels keep right-rail project summaries scannable.
    public static string ShortGapLabel(string gap) => gap switch
    {
        "Brief description pending" => "Description",
        "Add at least 3 project photos" => "Photos",
        "Upload at least 3 project documents" => "Documents",
        "Add at least 1 project video" => "Video",
        "IPA Cost pending" => "IPA Cost",
        "AoN Cost pending" => "AoN Cost",
        "Benchmark Cost pending" => "Benchmark",
        "L1 Cost pending" => "L1 Cost",
        "PNC Cost pending" => "PNC Cost",
        "Supply Order Date pending" => "SO Date",
        "Current-stage Actual Start pending" => "Actual Start",
        "Current-stage Planned Start pending" => "Planned Start",
        "Current-stage timeline (PDC) pending" => "PDC",
        _ when gap.Contains("actual start missing", StringComparison.OrdinalIgnoreCase) => "Historical start",
        _ when gap.Contains("actual completion missing", StringComparison.OrdinalIgnoreCase) => "Historical completion",
        _ => gap
    };

    // SECTION: Urgency labels translate pending item details into short visual badges.
    public static string UrgencyLabel(WorkspaceAttentionItemVm item)
    {
        if (item.Detail.Contains("backfill", StringComparison.OrdinalIgnoreCase))
        {
            return "Critical";
        }

        if (item.Detail.Contains("overdue", StringComparison.OrdinalIgnoreCase))
        {
            return "Overdue";
        }

        return item.Severity switch
        {
            "Danger" => "Action",
            "Warning" => "Attention",
            _ => "Info"
        };
    }

    // SECTION: Reminder due-date display is shared by workspace reminder cards.
    public static string FormatReminderDate(DateTimeOffset? dueAtUtc)
    {
        if (!dueAtUtc.HasValue)
        {
            return "No due date";
        }

        var localDue = dueAtUtc.Value.LocalDateTime;
        var today = DateTime.Now.Date;
        var dueDate = localDue.Date;

        if (dueDate < today)
        {
            var days = (today - dueDate).Days;
            return $"Overdue by {days} day{(days == 1 ? string.Empty : "s")}";
        }

        if (dueDate == today)
        {
            return "Due today";
        }

        return localDue.Year == DateTime.Now.Year
            ? $"Due {localDue:dd MMM}"
            : $"Due {localDue:dd MMM yyyy}";
    }
}
