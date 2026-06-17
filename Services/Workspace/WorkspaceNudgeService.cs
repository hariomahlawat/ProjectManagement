using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.ProjectIdeas;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Projects;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Services.Workspace;

public sealed class WorkspaceNudgeService
{
    // SECTION: Current-stage helpers
    public static ProjectStage? GetCurrentStage(Project project)
        => PresentStageHelper.Resolve(project.ProjectStages);

    public static int? GetCurrentStageAgeDays(Project project, DateOnly today)
    {
        var current = GetCurrentStage(project);
        if (current?.Status == StageStatus.InProgress && current.ActualStart is { } actualStart)
        {
            return Math.Max(0, today.DayNumber - actualStart.DayNumber);
        }

        var lastCompleted = project.ProjectStages
            .Where(s => s.Status == StageStatus.Completed && s.CompletedOn.HasValue)
            .OrderByDescending(s => s.CompletedOn)
            .FirstOrDefault();

        return lastCompleted?.CompletedOn is { } completedOn
            ? Math.Max(0, today.DayNumber - completedOn.DayNumber)
            : null;
    }

    public static DateTime? LastPoRemark(Project project, string userId) => project.Remarks
        .Where(r => !r.IsDeleted && r.AuthorUserId == userId && r.AuthorRole == RemarkActorRole.ProjectOfficer)
        .OrderByDescending(r => r.CreatedAtUtc)
        .Select(r => (DateTime?)r.CreatedAtUtc)
        .FirstOrDefault();

    public string GetUpdateStatus(DateTime? lastRemarkUtc, DateOnly today)
    {
        if (lastRemarkUtc is null) return "ActionRequired";
        var age = today.DayNumber - ToIstDate(lastRemarkUtc.Value).DayNumber;
        return age <= 7 ? "Ok" : age <= 10 ? "Attention" : "ActionRequired";
    }

    public bool HasCurrentStageTimelineIssue(ProjectStage? stage)
    {
        if (stage is null) return false;
        if (stage.Status == StageStatus.InProgress) return stage.ActualStart is null || stage.PlannedDue is null;
        if (stage.Status == StageStatus.Completed) return stage.CompletedOn is null;
        return false;
    }

    public bool IsCurrentStageOverdue(ProjectStage? stage, DateOnly today) => stage is not null && stage.Status != StageStatus.Completed && stage.PlannedDue is { } due && due < today;

    public IReadOnlyList<WorkspaceAttentionItemVm> BuildPendingWithMe(IReadOnlyList<Project> projects, IReadOnlyList<WorkspaceTaskVm> tasks, IReadOnlyList<WorkspaceIdeaVm> ideas, string userId, DateOnly today)
    {
        var items = new List<WorkspaceAttentionItemVm>();
        foreach (var project in projects)
        {
            var stage = GetCurrentStage(project);
            var lastRemark = LastPoRemark(project, userId);
            var daysSinceRemark = lastRemark is null ? (int?)null : today.DayNumber - ToIstDate(lastRemark.Value).DayNumber;

            if (project.ProjectStages.Any(s => s.RequiresBackfill)) items.Add(ProjectItem(project, "Timeline backfill required", "Danger", "Complete Backfill", WorkspaceRouteHelper.ProjectTimeline(project.Id)));
            if (IsCurrentStageOverdue(stage, today)) items.Add(ProjectItem(project, $"Current stage overdue by {today.DayNumber - stage!.PlannedDue!.Value.DayNumber} days", "Danger", "Update Timeline", WorkspaceRouteHelper.ProjectTimeline(project.Id)));
            if (HasCurrentStageTimelineIssue(stage)) items.Add(ProjectItem(project, StageTimelineDetail(stage), "Warning", "Update Current Stage", WorkspaceRouteHelper.ProjectTimeline(project.Id)));
            if (lastRemark is null || daysSinceRemark > 7) items.Add(ProjectItem(project, lastRemark is null ? "No PO remark has been added yet" : $"No PO remark added in last {daysSinceRemark} days", daysSinceRemark > 10 || lastRemark is null ? "Danger" : "Warning", "Add Remark", WorkspaceRouteHelper.ProjectRemarks(project.Id)));
        }

        items.AddRange(tasks
            .Where(t => t.IsOverdue)
            .Select(t => new WorkspaceAttentionItemVm
            {
                Type = "Task",
                Title = t.Title,
                Detail = $"Overdue by {t.DaysOverdue} days",
                Severity = "Danger",
                BadgeText = "Task",
                ActionText = "Open Task",
                ActionUrl = t.OpenUrl,
                DueOrEventDateUtc = t.DueDateUtc
            }));

        items.AddRange(tasks
            .Where(t =>
                !t.IsOverdue &&
                t.DueDateUtc is { } due &&
                ToIstDate(due).DayNumber <= today.DayNumber + 7)
            .Select(t => new WorkspaceAttentionItemVm
            {
                Type = "Task",
                Title = t.Title,
                Detail = ToIstDate(t.DueDateUtc!.Value) == today ? "Due today" : "Due this week",
                Severity = "Warning",
                BadgeText = "Task",
                ActionText = "Open Task",
                ActionUrl = t.OpenUrl,
                DueOrEventDateUtc = t.DueDateUtc
            }));

        items.AddRange(ideas
            .Where(i => i.NeedsUpdate)
            .Select(i => new WorkspaceAttentionItemVm
            {
                Type = "Idea",
                Title = i.Title,
                Detail = $"No update in last {Math.Max(0, today.DayNumber - ToIstDate(i.LastActivityAtUtc).DayNumber)} days",
                Severity = "Warning",
                BadgeText = "Idea",
                ActionText = "Open Idea",
                ActionUrl = i.OpenUrl,
                DueOrEventDateUtc = i.LastActivityAtUtc
            }));
        return PrioritizePendingItems(items).Take(12).ToList();
    }

    public string GetNextAction(Project project, WorkspaceRecordHealthVm health, string userId, DateOnly today, out string url)
    {
        var stage = GetCurrentStage(project); url = WorkspaceRouteHelper.ProjectOverview(project.Id);
        if (project.ProjectStages.Any(s => s.RequiresBackfill)) { url = WorkspaceRouteHelper.ProjectTimeline(project.Id); return "Complete backfill"; }
        if (IsCurrentStageOverdue(stage, today)) { url = WorkspaceRouteHelper.ProjectTimeline(project.Id); return "Update current stage"; }
        if (HasCurrentStageTimelineIssue(stage)) { url = WorkspaceRouteHelper.ProjectTimeline(project.Id); return "Update current stage dates"; }
        if (GetUpdateStatus(LastPoRemark(project, userId), today) == "ActionRequired") { url = WorkspaceRouteHelper.ProjectRemarks(project.Id); return "Add remark"; }
        if (health.HealthPercent < 80) return "Complete project data";
        return "Review project";
    }

    // SECTION: View-model factories
    private static WorkspaceAttentionItemVm ProjectItem(
        Project project,
        string detail,
        string severity,
        string action,
        string url) => new()
        {
            Type = "Project",
            Title = project.Name,
            Detail = detail,
            Severity = severity,
            BadgeText = "Project",
            ActionText = action,
            ActionUrl = url
        };
    private static string StageTimelineDetail(ProjectStage? stage) => stage is null ? "Current stage timeline details are incomplete" : stage.Status == StageStatus.InProgress && stage.ActualStart is null ? "Current stage actual start missing" : stage.Status == StageStatus.InProgress && stage.PlannedDue is null ? "Current stage planned due missing" : stage.Status == StageStatus.Completed && stage.CompletedOn is null ? $"{stage.StageCode} stage completion date missing" : "Current stage timeline details are incomplete";

    // SECTION: Pending item grouping avoids showing multiple lower-priority nudges for the same project.
    private static IEnumerable<WorkspaceAttentionItemVm> PrioritizePendingItems(IEnumerable<WorkspaceAttentionItemVm> items)
        => items
            .GroupBy(i => $"{i.Type}:{i.Title}")
            .Select(g => g.OrderBy(i => SeverityRank(i.Severity)).ThenBy(i => ProjectPendingPriority(i.Detail)).ThenBy(i => i.DueOrEventDateUtc).First())
            .OrderBy(i => SeverityRank(i.Severity))
            .ThenBy(i => ProjectPendingPriority(i.Detail))
            .ThenBy(i => i.DueOrEventDateUtc);

    // SECTION: Current-stage urgency outranks lower-level data warnings in the Pending With Me card.
    private static int ProjectPendingPriority(string detail)
    {
        if (detail.Contains("backfill", StringComparison.OrdinalIgnoreCase)) return 0;
        if (detail.Contains("overdue", StringComparison.OrdinalIgnoreCase)) return 1;
        if (detail.Contains("actual start", StringComparison.OrdinalIgnoreCase)) return 2;
        if (detail.Contains("planned due", StringComparison.OrdinalIgnoreCase)) return 3;
        if (detail.Contains("remark", StringComparison.OrdinalIgnoreCase)) return 4;
        return 5;
    }

    private static int SeverityRank(string severity) => severity == "Danger" ? 0 : severity == "Warning" ? 1 : 2;

    // SECTION: Workspace dates are compared in IST so stale/health states match the user's operating day.
    internal static DateOnly ToIstDate(DateTime utc)
        => DateOnly.FromDateTime(IstClock.ToIst(utc));
}
