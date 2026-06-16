using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Remarks;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Services.Workspace;

public sealed class ProjectRecordHealthService
{
    private readonly ApplicationDbContext _db;
    private readonly WorkspaceNudgeService _nudges;
    public ProjectRecordHealthService(ApplicationDbContext db, WorkspaceNudgeService nudges) { _db = db; _nudges = nudges; }

    // SECTION: Batch record-health calculation
    public async Task<IReadOnlyDictionary<int, WorkspaceRecordHealthVm>> CalculateForProjectsAsync(IReadOnlyList<Project> projects, string userId, CancellationToken ct)
    {
        var ids = projects.Select(p => p.Id).ToArray();
        var factProjectIds = await LoadFactProjectIdsAsync(ids, ct);
        var results = new Dictionary<int, WorkspaceRecordHealthVm>();
        foreach (var project in projects)
        {
            var gaps = new List<string>();
            var score = 0;
            if (!string.IsNullOrWhiteSpace(project.Description) && !string.IsNullOrWhiteSpace(project.HodUserId) && !string.IsNullOrWhiteSpace(project.LeadPoUserId)) score += 15; else gaps.Add("Basic metadata incomplete");
            if (project.CategoryId.HasValue && project.TechnicalCategoryId.HasValue && project.ProjectTypeId.HasValue) score += 15; else gaps.Add("Category / technical category / project type incomplete");
            var current = WorkspaceNudgeService.GetCurrentStage(project);
            if (!_nudges.HasCurrentStageTimelineIssue(current) && !_nudges.IsCurrentStageOverdue(current, DateOnly.FromDateTime(DateTime.UtcNow))) score += 15; else gaps.Add("Current stage timeline details are incomplete");
            if (!project.ProjectStages.Any(s => s.RequiresBackfill)) score += 15; else gaps.Add("Timeline backfill required");
            if (HasRequiredFacts(project, factProjectIds, current)) score += 15; else gaps.Add("Required current/past stage facts missing");
            var lastRemark = WorkspaceNudgeService.LastPoRemark(project, userId);
            if (lastRemark.HasValue && (DateTime.UtcNow.Date - lastRemark.Value.Date).Days <= 10) score += 15; else gaps.Add(lastRemark.HasValue ? "No PO remark in last 10 days" : "No PO remark has been added yet");
            if (project.Documents.Any(d => d.Status == ProjectDocumentStatus.Published)) score += 10; else gaps.Add("No supporting document uploaded");
            results[project.Id] = new WorkspaceRecordHealthVm { ProjectId = project.Id, ProjectName = project.Name, HealthPercent = Math.Clamp(score, 0, 100), HealthLabel = Label(score), Gaps = gaps, OpenUrl = $"/Projects/Overview/{project.Id}" };
        }
        return results;
    }

    private async Task<HashSet<int>> LoadFactProjectIdsAsync(int[] projectIds, CancellationToken ct)
    {
        var ids = new HashSet<int>();
        foreach (var id in await _db.ProjectIpaFacts.AsNoTracking().Where(x => projectIds.Contains(x.ProjectId)).Select(x => x.ProjectId).ToListAsync(ct)) ids.Add(id);
        foreach (var id in await _db.ProjectAonFacts.AsNoTracking().Where(x => projectIds.Contains(x.ProjectId)).Select(x => x.ProjectId).ToListAsync(ct)) ids.Add(id);
        foreach (var id in await _db.ProjectBenchmarkFacts.AsNoTracking().Where(x => projectIds.Contains(x.ProjectId)).Select(x => x.ProjectId).ToListAsync(ct)) ids.Add(id);
        foreach (var id in await _db.ProjectPncFacts.AsNoTracking().Where(x => projectIds.Contains(x.ProjectId)).Select(x => x.ProjectId).ToListAsync(ct)) ids.Add(id);
        foreach (var id in await _db.ProjectSupplyOrderFacts.AsNoTracking().Where(x => projectIds.Contains(x.ProjectId)).Select(x => x.ProjectId).ToListAsync(ct)) ids.Add(id);
        return ids;
    }
    private static bool HasRequiredFacts(Project p, HashSet<int> factProjectIds, ProjectStage? current) => current is null || !p.ProjectStages.Where(s => s.Status == StageStatus.Completed || s.Id == current.Id).Any(s => new[] { "IPA", "AON", "BM", "PNC", "SO" }.Contains(s.StageCode, StringComparer.OrdinalIgnoreCase)) || factProjectIds.Contains(p.Id);
    private static string Label(int score) => score >= 80 ? "Good" : score >= 60 ? "Attention" : "Needs Work";
}
