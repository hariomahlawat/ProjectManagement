using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.ProjectIdeas;
using ProjectManagement.Models.Remarks;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Services.Workspace;

public sealed class WorkspaceNudgeService
{
    // SECTION: Current-stage helpers
    public static ProjectStage? GetCurrentStage(Project project) => project.ProjectStages
        .OrderBy(s => s.SortOrder)
        .ThenBy(s => s.StageCode)
        .FirstOrDefault(s => s.Status == StageStatus.InProgress)
        ?? project.ProjectStages
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.StageCode)
            .FirstOrDefault(s => s.Status == StageStatus.NotStarted)
        ?? project.ProjectStages
            .Where(s => s.Status == StageStatus.Completed)
            .OrderByDescending(s => s.SortOrder)
            .ThenByDescending(s => s.StageCode)
            .FirstOrDefault();

    public static DateTime? LastPoRemark(Project project, string userId) => project.Remarks()
        .Where(r => !r.IsDeleted && r.AuthorUserId == userId && r.AuthorRole == RemarkActorRole.ProjectOfficer)
        .OrderByDescending(r => r.CreatedAtUtc)
        .Select(r => (DateTime?)r.CreatedAtUtc)
        .FirstOrDefault();

    public string GetUpdateStatus(DateTime? lastRemarkUtc, DateOnly today)
    {
        if (lastRemarkUtc is null) return "ActionRequired";
        var age = today.DayNumber - DateOnly.FromDateTime(lastRemarkUtc.Value).DayNumber;
        return age <= 7 ? "Ok" : age <= 10 ? "Attention" : "ActionRequired";
    }

    public bool HasCurrentStageTimelineIssue(ProjectStage? stage)
    {
        if (stage is null) return false;
        if (stage.Status == StageStatus.InProgress) return stage.ActualStart is null || stage.PlannedDue is null;
        if (stage.Status == StageStatus.Completed) return stage.CompletedOn is null || stage.ActualStart is null;
        return false;
    }

    public bool IsCurrentStageOverdue(ProjectStage? stage, DateOnly today) => stage is not null && stage.Status != StageStatus.Completed && stage.PlannedDue is { } due && due < today;

    public IReadOnlyList<WorkspaceAttentionItemVm> BuildPendingWithMe(IReadOnlyList<Project> projects, IReadOnlyList<WorkspaceTaskVm> tasks, IReadOnlyList<WorkspaceIdeaVm> ideas, string userId, DateOnly today)
    {
        var items = new List<WorkspaceAttentionItemVm>();
        foreach (var project in projects)
        {
            var openUrl = $"/Projects/Overview/{project.Id}";
            var stage = GetCurrentStage(project);
            var lastRemark = LastPoRemark(project, userId);
            var daysSinceRemark = lastRemark is null ? (int?)null : today.DayNumber - DateOnly.FromDateTime(lastRemark.Value).DayNumber;
            if (project.ProjectStages.Any(s => s.RequiresBackfill)) items.Add(ProjectItem(project, "Timeline backfill required", "Danger", "Complete Backfill", $"/Projects/Timeline/EditPlan/{project.Id}"));
            if (IsCurrentStageOverdue(stage, today)) items.Add(ProjectItem(project, $"Current stage overdue by {today.DayNumber - stage!.PlannedDue!.Value.DayNumber} days", "Danger", "Update Timeline", $"/Projects/Timeline/EditPlan/{project.Id}"));
            if (HasCurrentStageTimelineIssue(stage)) items.Add(ProjectItem(project, StageTimelineDetail(stage), "Warning", "Update Current Stage", $"/Projects/Timeline/EditPlan/{project.Id}"));
            if (lastRemark is null || daysSinceRemark > 7) items.Add(ProjectItem(project, lastRemark is null ? "No PO remark has been added yet" : $"No PO remark added in last {daysSinceRemark} days", daysSinceRemark > 10 || lastRemark is null ? "Danger" : "Warning", "Add Remark", $"/Projects/Remarks/Index?projectId={project.Id}"));
        }
        items.AddRange(tasks.Where(t => t.IsOverdue).Select(t => new WorkspaceAttentionItemVm { Type = "Task", Title = t.Title, Detail = $"Overdue by {t.DaysOverdue} days", Severity = "Danger", BadgeText = "Task", ActionText = "Open Task", ActionUrl = t.OpenUrl, DueOrEventDateUtc = t.DueDateUtc }));
        items.AddRange(tasks.Where(t => !t.IsOverdue && t.DueDateUtc is { } due && DateOnly.FromDateTime(due).DayNumber <= today.DayNumber + 7).Select(t => new WorkspaceAttentionItemVm { Type = "Task", Title = t.Title, Detail = DateOnly.FromDateTime(t.DueDateUtc!.Value) == today ? "Due today" : "Due this week", Severity = "Warning", BadgeText = "Task", ActionText = "Open Task", ActionUrl = t.OpenUrl, DueOrEventDateUtc = t.DueDateUtc }));
        items.AddRange(ideas.Where(i => i.NeedsUpdate).Select(i => new WorkspaceAttentionItemVm { Type = "Idea", Title = i.Title, Detail = $"No update in last {Math.Max(0, (DateTime.UtcNow.Date - i.LastActivityAtUtc.Date).Days)} days", Severity = "Warning", BadgeText = "Idea", ActionText = "Open Idea", ActionUrl = i.OpenUrl, DueOrEventDateUtc = i.LastActivityAtUtc }));
        return items.OrderBy(i => i.Severity == "Danger" ? 0 : i.Severity == "Warning" ? 1 : 2).ThenBy(i => i.DueOrEventDateUtc).Take(12).ToList();
    }

    public string GetNextAction(Project project, WorkspaceRecordHealthVm health, string userId, DateOnly today, out string url)
    {
        var stage = GetCurrentStage(project); url = $"/Projects/Overview/{project.Id}";
        if (project.ProjectStages.Any(s => s.RequiresBackfill)) { url = $"/Projects/Timeline/EditPlan/{project.Id}"; return "Complete backfill"; }
        if (IsCurrentStageOverdue(stage, today)) { url = $"/Projects/Timeline/EditPlan/{project.Id}"; return "Update current stage"; }
        if (HasCurrentStageTimelineIssue(stage)) { url = $"/Projects/Timeline/EditPlan/{project.Id}"; return "Update current stage dates"; }
        if (GetUpdateStatus(LastPoRemark(project, userId), today) == "ActionRequired") { url = $"/Projects/Remarks/Index?projectId={project.Id}"; return "Add remark"; }
        if (health.HealthPercent < 80) return "Complete project data";
        return "Review project";
    }

    private static WorkspaceAttentionItemVm ProjectItem(Project project, string detail, string severity, string action, string url) => new() { Type = "Project", Title = project.Name, Detail = detail, Severity = severity, BadgeText = "Project", ActionText = action, ActionUrl = url };
    private static string StageTimelineDetail(ProjectStage? stage) => stage is null ? "Current stage timeline details are incomplete" : stage.Status == StageStatus.InProgress && stage.ActualStart is null ? $"{stage.StageCode} stage actual start date missing" : stage.Status == StageStatus.InProgress && stage.PlannedDue is null ? $"{stage.StageCode} stage planned due date missing" : stage.Status == StageStatus.Completed && stage.CompletedOn is null ? $"{stage.StageCode} stage completion date missing" : "Current stage timeline details are incomplete";
}

internal static class WorkspaceProjectExtensions
{
    public static IEnumerable<Remark> Remarks(this Project project) => project.GetType().GetProperty("Remarks")?.GetValue(project) as IEnumerable<Remark> ?? Array.Empty<Remark>();
}
