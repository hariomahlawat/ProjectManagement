namespace ProjectManagement.ViewModels.Workspace;

public sealed class ProjectOfficerWorkspaceVm
{
    public string UserDisplayName { get; set; } = string.Empty;
    public string RoleTitle { get; set; } = "Project Officer Workspace";
    public string MyProjectsUrl { get; set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public int PortfolioHealthPercent { get; set; }
    public string PortfolioHealthLabel { get; set; } = "Good";
    public int AssignedProjectCount { get; set; }
    public int PendingWithMeCount { get; set; }
    public int OverdueTaskCount { get; set; }
    public int RecordGapCount { get; set; }
    public int AssignedIdeaCount { get; set; }
    public WorkspaceEngagementVm Engagement { get; set; } = new();
    public IReadOnlyList<WorkspaceKpiVm> Kpis { get; set; } = Array.Empty<WorkspaceKpiVm>();
    public IReadOnlyList<WorkspaceAttentionItemVm> PendingWithMe { get; set; } = Array.Empty<WorkspaceAttentionItemVm>();
    public IReadOnlyList<WorkspaceAttentionItemVm> WaitingOnOthers { get; set; } = Array.Empty<WorkspaceAttentionItemVm>();
    public IReadOnlyList<WorkspaceProjectMatrixRowVm> ProjectMatrix { get; set; } = Array.Empty<WorkspaceProjectMatrixRowVm>();
    public IReadOnlyList<WorkspaceTaskVm> OfficialTasks { get; set; } = Array.Empty<WorkspaceTaskVm>();
    public IReadOnlyList<WorkspaceIdeaVm> Ideas { get; set; } = Array.Empty<WorkspaceIdeaVm>();
    public IReadOnlyList<WorkspaceRecordHealthVm> RecordHealth { get; set; } = Array.Empty<WorkspaceRecordHealthVm>();
    public IReadOnlyList<WorkspaceQuickActionVm> QuickActions { get; set; } = Array.Empty<WorkspaceQuickActionVm>();
    public IReadOnlyList<WorkspaceReminderVm> PersonalReminders { get; set; } = Array.Empty<WorkspaceReminderVm>();
}

public sealed class WorkspaceKpiVm { public string Title { get; set; } = string.Empty; public string Value { get; set; } = string.Empty; public string Caption { get; set; } = string.Empty; public string Severity { get; set; } = "Info"; public string Icon { get; set; } = "bi-circle"; public string? Url { get; set; } }
public sealed class WorkspaceAttentionItemVm { public string Type { get; set; } = string.Empty; public string Title { get; set; } = string.Empty; public string Detail { get; set; } = string.Empty; public string Severity { get; set; } = "Info"; public string BadgeText { get; set; } = string.Empty; public string ActionText { get; set; } = string.Empty; public string ActionUrl { get; set; } = string.Empty; public DateTime? DueOrEventDateUtc { get; set; } }
public sealed class WorkspaceProjectMatrixRowVm { public int ProjectId { get; set; } public string ProjectName { get; set; } = string.Empty; public string CurrentStageCode { get; set; } = string.Empty; public string CurrentStageName { get; set; } = string.Empty; public int? DaysInCurrentStage { get; set; } public string UpdateStatus { get; set; } = "Ok"; public string TimelineStatus { get; set; } = "Ok"; public string RecordStatus { get; set; } = "Ok"; public string TaskStatus { get; set; } = "NotApplicable"; public int RecordHealthPercent { get; set; } public int RecordGapCount { get; set; } public DateTime? LastPoRemarkAtUtc { get; set; } public bool HasBackfill { get; set; } public bool HasCurrentStageIssue { get; set; } public bool HasOverdueCurrentStage { get; set; } public string NextActionText { get; set; } = "Open"; public string NextActionUrl { get; set; } = string.Empty; public string OpenUrl { get; set; } = string.Empty; public string AddRemarkUrl { get; set; } = string.Empty; public string TimelineUrl { get; set; } = string.Empty; }
public sealed class WorkspaceTaskVm { public int TaskId { get; set; } public string Title { get; set; } = string.Empty; public string ContextLabel { get; set; } = "Miscellaneous"; public string Priority { get; set; } = string.Empty; public string Status { get; set; } = string.Empty; public DateTime? DueDateUtc { get; set; } public bool IsOverdue { get; set; } public int? DaysOverdue { get; set; } public string OpenUrl { get; set; } = string.Empty; }
public sealed class WorkspaceIdeaVm { public int IdeaId { get; set; } public string Title { get; set; } = string.Empty; public string Status { get; set; } = string.Empty; public DateTime LastActivityAtUtc { get; set; } public bool NeedsUpdate { get; set; } public int CommentCount { get; set; } public int DocumentCount { get; set; } public string OpenUrl { get; set; } = string.Empty; }
public sealed class WorkspaceRecordHealthVm { public int ProjectId { get; set; } public string ProjectName { get; set; } = string.Empty; public int HealthPercent { get; set; } public string HealthLabel { get; set; } = "Good"; public IReadOnlyList<string> Gaps { get; set; } = Array.Empty<string>(); public string OpenUrl { get; set; } = string.Empty; }
public sealed class WorkspaceEngagementVm { public DateTime? LastLoginUtc { get; set; } public DateTime? LastActivityUtc { get; set; } public int LoginsThisMonth { get; set; } public int ActiveDaysThisMonth { get; set; } public int ActionsRecordedThisMonth { get; set; } public int RemarksPostedThisMonth { get; set; } public int TasksUpdatedThisMonth { get; set; } public int DocumentsUploadedThisMonth { get; set; } public string EngagementLabel { get; set; } = "Active"; }
public sealed class WorkspaceQuickActionVm { public string Text { get; set; } = string.Empty; public string Url { get; set; } = string.Empty; public string Icon { get; set; } = "bi-arrow-right"; }
public sealed class WorkspaceReminderVm { public Guid ReminderId { get; set; } public string Title { get; set; } = string.Empty; public string Priority { get; set; } = string.Empty; public DateTimeOffset? DueAtUtc { get; set; } public bool IsPinned { get; set; } public string OpenUrl { get; set; } = string.Empty; }

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

    // SECTION: Record-health color classes map scores to human-readable health bands.
    public static string HealthCss(int percent)
    {
        if (percent >= 80) return "good";
        if (percent >= 60) return "attention";
        return "danger";
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
}
