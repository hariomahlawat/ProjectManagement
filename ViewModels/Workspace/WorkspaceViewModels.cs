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
    public int AssignedIdeaCount { get; set; }
    public int AotsUnreadCount { get; set; }
    public string AotsUrl { get; set; } = "/DocumentRepository/Documents?scope=aots";
    public WorkspaceEngagementVm Engagement { get; set; } = new();
    public IReadOnlyList<WorkspaceKpiVm> Kpis { get; set; } = Array.Empty<WorkspaceKpiVm>();
    public IReadOnlyList<WorkspaceRailItemVm> RailItems { get; set; } = Array.Empty<WorkspaceRailItemVm>();
    public IReadOnlyList<WorkspaceAttentionItemVm> PendingWithMe { get; set; } = Array.Empty<WorkspaceAttentionItemVm>();
    public IReadOnlyList<WorkspaceActionQueueItemVm> ActionQueue { get; set; } = Array.Empty<WorkspaceActionQueueItemVm>();
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
    public WorkspaceAttentionItemVm? NextBestAction { get; set; }
    public IReadOnlyList<WorkspaceQuickActionVm> QuickActions { get; set; } = Array.Empty<WorkspaceQuickActionVm>();
    public IReadOnlyList<WorkspaceReminderVm> PersonalReminders { get; set; } = Array.Empty<WorkspaceReminderVm>();
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

public sealed class WorkspaceKpiVm
{
    public string Title { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public string Caption { get; set; } = string.Empty;

    public string Severity { get; set; } = "Info";

    public string Icon { get; set; } = "bi-circle";

    public string? Url { get; set; }
}

public sealed class WorkspaceActionQueueItemVm
{
    public string Type { get; set; } = string.Empty;

    public string BadgeText { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public string Meta { get; set; } = string.Empty;

    public string Severity { get; set; } = "Info";

    public string ActionText { get; set; } = string.Empty;

    public string ActionUrl { get; set; } = string.Empty;

    public DateTime? SortDateUtc { get; set; }
}

public sealed class WorkspaceAttentionItemVm
{
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

    public string UpdateStatus { get; set; } = "Ok";

    public string TimelineStatus { get; set; } = "Ok";

    public string RecordStatus { get; set; } = "Ok";

    public string TaskStatus { get; set; } = "NotApplicable";

    public int RecordHealthPercent { get; set; }

    public int RecordGapCount { get; set; }

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

    public DateTime? DueDateUtc { get; set; }

    public bool IsOverdue { get; set; }

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

public sealed class WorkspaceProjectImprovementVm
{
    public int ProjectId { get; set; }

    public string ProjectName { get; set; } = string.Empty;

    public int FixCount { get; set; }

    public IReadOnlyList<string> FixLabels { get; set; } = Array.Empty<string>();

    public string Url { get; set; } = string.Empty;

    public string Severity { get; set; } = "Warning";
}

public sealed class WorkspaceRecordHealthVm
{
    public int ProjectId { get; set; }

    public string ProjectName { get; set; } = string.Empty;

    public int HealthPercent { get; set; }

    public string HealthLabel { get; set; } = "Good";

    public IReadOnlyList<string> Gaps { get; set; } = Array.Empty<string>();

    public string OpenUrl { get; set; } = string.Empty;
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

    // SECTION: Compact status labels keep dense table cells readable.
    public static string CompactStatusLabel(string status) => status switch
    {
        "ActionRequired" => "Action",
        "NotApplicable" => "N/A",
        "Ok" => "OK",
        "Attention" => "Attention",
        _ => status
    };


    // SECTION: Matrix action labels are shortened for dense enterprise table rows.
    public static string CompactActionLabel(string action) => action switch
    {
        "Complete backfill" => "Backfill",
        "Update current stage" => "Update stage",
        "Update current stage dates" => "Update dates",
        "Add remark" => "Remark",
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

        if (percent >= 60)
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

    // SECTION: Improvement labels turn internal checklist gaps into action wording.
    public static string ImprovementLabel(string gap) => gap switch
    {
        "Basic metadata incomplete" => "Complete basic project details",
        "Category / technical category / project type incomplete" => "Complete project classification",
        "Timeline backfill required" => "Clear timeline backfill",
        "Required current/past stage facts missing" => "Update required stage facts",
        "No PO remark in last 10 days" => "Add a fresh PO remark",
        "No PO remark has been added yet" => "Add first PO remark",
        "No project document uploaded" => "Upload at least one project document",
        "Current stage overdue" => "Review overdue current stage",
        "Current stage actual start missing" => "Update current stage actual start",
        "Current stage planned due missing" => "Update current stage planned due date",
        _ when gap.Contains("completion date missing", StringComparison.OrdinalIgnoreCase) => "Update current stage completion date",
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
        return dueAtUtc.HasValue
            ? $"Due {dueAtUtc.Value.LocalDateTime:dd MMM}"
            : "No due date";
    }
}
