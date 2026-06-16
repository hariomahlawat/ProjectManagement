using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Models.Stages;
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
        var factPresence = await LoadStageFactPresenceAsync(ids, ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var results = new Dictionary<int, WorkspaceRecordHealthVm>();
        foreach (var project in projects)
        {
            var gaps = new List<string>();
            var score = 0;
            if (!string.IsNullOrWhiteSpace(project.Description) && !string.IsNullOrWhiteSpace(project.HodUserId) && !string.IsNullOrWhiteSpace(project.LeadPoUserId)) score += 15; else gaps.Add("Basic metadata incomplete");
            if (project.CategoryId.HasValue && project.TechnicalCategoryId.HasValue && project.ProjectTypeId.HasValue) score += 15; else gaps.Add("Category / technical category / project type incomplete");
            var current = WorkspaceNudgeService.GetCurrentStage(project);
            if (!_nudges.HasCurrentStageTimelineIssue(current) && !_nudges.IsCurrentStageOverdue(current, today)) score += 15; else gaps.Add("Current stage timeline details are incomplete");
            if (!project.ProjectStages.Any(s => s.RequiresBackfill)) score += 15; else gaps.Add("Timeline backfill required");
            if (HasRequiredFacts(project, factPresence, current)) score += 15; else gaps.Add("Required current/past stage facts missing");
            var lastRemark = WorkspaceNudgeService.LastPoRemark(project, userId);
            if (lastRemark.HasValue && (DateTime.UtcNow.Date - lastRemark.Value.Date).Days <= 10) score += 15; else gaps.Add(lastRemark.HasValue ? "No PO remark in last 10 days" : "No PO remark has been added yet");
            if (project.Documents.Any(d => d.Status == ProjectDocumentStatus.Published)) score += 10; else gaps.Add("No project document uploaded");
            results[project.Id] = new WorkspaceRecordHealthVm { ProjectId = project.Id, ProjectName = project.Name, HealthPercent = Math.Clamp(score, 0, 100), HealthLabel = Label(score), Gaps = gaps, OpenUrl = $"/Projects/Overview/{project.Id}" };
        }
        return results;
    }

    // SECTION: Stage-specific fact presence prevents one stage fact from satisfying all stage checks.
    private async Task<StageFactPresence> LoadStageFactPresenceAsync(int[] projectIds, CancellationToken ct)
        => new(
            await LoadIdsAsync(_db.ProjectIpaFacts.Where(x => projectIds.Contains(x.ProjectId)).Select(x => x.ProjectId), ct),
            await LoadIdsAsync(_db.ProjectSowFacts.Where(x => projectIds.Contains(x.ProjectId)).Select(x => x.ProjectId), ct),
            await LoadIdsAsync(_db.ProjectAonFacts.Where(x => projectIds.Contains(x.ProjectId)).Select(x => x.ProjectId), ct),
            await LoadIdsAsync(_db.ProjectBenchmarkFacts.Where(x => projectIds.Contains(x.ProjectId)).Select(x => x.ProjectId), ct),
            await LoadIdsAsync(_db.ProjectCommercialFacts.Where(x => projectIds.Contains(x.ProjectId)).Select(x => x.ProjectId), ct),
            await LoadIdsAsync(_db.ProjectPncFacts.Where(x => projectIds.Contains(x.ProjectId)).Select(x => x.ProjectId), ct),
            await LoadIdsAsync(_db.ProjectSupplyOrderFacts.Where(x => projectIds.Contains(x.ProjectId)).Select(x => x.ProjectId), ct));

    // SECTION: Project ID materialization
    private static async Task<HashSet<int>> LoadIdsAsync(IQueryable<int> query, CancellationToken ct)
        => (await query.ToListAsync(ct)).ToHashSet();

    private static bool HasRequiredFacts(Project project, StageFactPresence facts, ProjectStage? current)
    {
        if (current is null) return true;

        return project.ProjectStages
            .Where(s => s.Status == StageStatus.Completed || s.Id == current.Id)
            .All(s => HasFactForStage(project.Id, s.StageCode, facts));
    }

    private static bool HasFactForStage(int projectId, string stageCode, StageFactPresence facts)
        => stageCode.ToUpperInvariant() switch
        {
            StageCodes.IPA => facts.Ipa.Contains(projectId),
            StageCodes.SOW => facts.Sow.Contains(projectId),
            StageCodes.AON => facts.Aon.Contains(projectId),
            StageCodes.BM => facts.Benchmark.Contains(projectId),
            StageCodes.COB => facts.Commercial.Contains(projectId),
            StageCodes.PNC => facts.Pnc.Contains(projectId),
            StageCodes.SO => facts.SupplyOrder.Contains(projectId),
            _ => true
        };

    private static string Label(int score) => score >= 80 ? "Good" : score >= 60 ? "Attention" : "Needs Work";

    private sealed record StageFactPresence(HashSet<int> Ipa, HashSet<int> Sow, HashSet<int> Aon, HashSet<int> Benchmark, HashSet<int> Commercial, HashSet<int> Pnc, HashSet<int> SupplyOrder);
}
