using Microsoft.EntityFrameworkCore;
using ProjectManagement.Infrastructure;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Services.Workspace;

public sealed class ProjectRecordHealthService
{
    private const string GapBriefDescription = "Brief description pending";
    private const string GapPhotos = "Add at least 3 project photos";
    private const string GapDocuments = "Upload at least 3 project documents";
    private const string GapVideo = "Add at least 1 project video";
    private const string GapBudget = "Update applicable budget details";
    private const string GapBackfill = "Clear timeline backfill";
    private const string GapCurrentStageTimeline = "Update current stage timeline";
    private const string GapRecentRemark = "Add recent project remark";

    private readonly ApplicationDbContext _db;
    private readonly WorkspaceNudgeService _nudges;

    public ProjectRecordHealthService(
        ApplicationDbContext db,
        WorkspaceNudgeService nudges)
    {
        _db = db;
        _nudges = nudges;
    }

    // SECTION: Batch project-data completeness calculation
    public async Task<IReadOnlyDictionary<int, WorkspaceRecordHealthVm>> CalculateForProjectsAsync(
        IReadOnlyList<Project> projects,
        string userId,
        CancellationToken ct)
    {
        var projectIds = projects.Select(p => p.Id).ToArray();
        var mediaCounts = await LoadMediaCountsAsync(projectIds, ct);
        var factPresence = await LoadStageFactPresenceAsync(projectIds, ct);
        var today = DateOnly.FromDateTime(IstClock.ToIst(DateTime.UtcNow));
        var results = new Dictionary<int, WorkspaceRecordHealthVm>();

        foreach (var project in projects)
        {
            var gaps = new List<string>();
            var score = 0;

            score += ScoreBriefDescription(project, gaps);
            score += ScorePhotos(project.Id, mediaCounts, gaps);
            score += ScoreDocuments(project.Id, mediaCounts, gaps);
            score += ScoreVideo(project.Id, mediaCounts, gaps);
            score += ScoreBudgetMetrics(project, factPresence, gaps);
            score += ScoreBackfill(project, gaps);
            score += ScoreCurrentStageTimeline(project, today, gaps);
            score += ScoreRecentPoRemark(project, userId, today, gaps);

            results[project.Id] = new WorkspaceRecordHealthVm
            {
                ProjectId = project.Id,
                ProjectName = project.Name,
                HealthPercent = Math.Clamp(score, 0, 100),
                HealthLabel = Label(score),
                Gaps = gaps,
                OpenUrl = WorkspaceRouteHelper.ProjectOverview(project.Id)
            };
        }

        return results;
    }

    // SECTION: Media and document count loading keeps the assigned-project query lightweight.
    private async Task<IReadOnlyDictionary<int, ProjectDataCounts>> LoadMediaCountsAsync(
        int[] projectIds,
        CancellationToken ct)
    {
        var photoCounts = await _db.ProjectPhotos
            .AsNoTracking()
            .Where(p => projectIds.Contains(p.ProjectId))
            .GroupBy(p => p.ProjectId)
            .Select(g => new { ProjectId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProjectId, x => x.Count, ct);

        var documentCounts = await _db.ProjectDocuments
            .AsNoTracking()
            .Where(d =>
                projectIds.Contains(d.ProjectId) &&
                d.Status == ProjectDocumentStatus.Published &&
                !d.IsArchived)
            .GroupBy(d => d.ProjectId)
            .Select(g => new { ProjectId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProjectId, x => x.Count, ct);

        var videoCounts = await _db.ProjectVideos
            .AsNoTracking()
            .Where(v => projectIds.Contains(v.ProjectId))
            .GroupBy(v => v.ProjectId)
            .Select(g => new { ProjectId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProjectId, x => x.Count, ct);

        return projectIds.ToDictionary(
            id => id,
            id => new ProjectDataCounts(
                photoCounts.GetValueOrDefault(id),
                documentCounts.GetValueOrDefault(id),
                videoCounts.GetValueOrDefault(id)));
    }

    // SECTION: Stage-specific fact presence supports stage-aware budget completeness.
    private async Task<StageFactPresence> LoadStageFactPresenceAsync(int[] projectIds, CancellationToken ct)
        => new(
            await LoadIdsAsync(_db.ProjectIpaFacts.Where(x => projectIds.Contains(x.ProjectId)).Select(x => x.ProjectId), ct),
            await LoadIdsAsync(_db.ProjectAonFacts.Where(x => projectIds.Contains(x.ProjectId)).Select(x => x.ProjectId), ct),
            await LoadIdsAsync(_db.ProjectBenchmarkFacts.Where(x => projectIds.Contains(x.ProjectId)).Select(x => x.ProjectId), ct),
            await LoadIdsAsync(_db.ProjectCommercialFacts.Where(x => projectIds.Contains(x.ProjectId)).Select(x => x.ProjectId), ct),
            await LoadIdsAsync(_db.ProjectPncFacts.Where(x => projectIds.Contains(x.ProjectId)).Select(x => x.ProjectId), ct),
            await LoadIdsAsync(_db.ProjectSupplyOrderFacts.Where(x => projectIds.Contains(x.ProjectId)).Select(x => x.ProjectId), ct));

    // SECTION: Project ID materialization
    private static async Task<HashSet<int>> LoadIdsAsync(IQueryable<int> query, CancellationToken ct)
        => (await query.ToListAsync(ct)).ToHashSet();

    // SECTION: Completeness scoring metrics
    private static int ScoreBriefDescription(Project project, List<string> gaps)
    {
        if (!string.IsNullOrWhiteSpace(project.Description) &&
            project.Description.Trim().Length >= 30)
        {
            return 15;
        }

        gaps.Add(GapBriefDescription);
        return 0;
    }

    private static int ScorePhotos(
        int projectId,
        IReadOnlyDictionary<int, ProjectDataCounts> counts,
        List<string> gaps)
    {
        var photoCount = counts.TryGetValue(projectId, out var value)
            ? value.PhotoCount
            : 0;

        if (photoCount >= 3)
        {
            return 10;
        }

        gaps.Add(GapPhotos);

        return photoCount switch
        {
            2 => 7,
            1 => 4,
            _ => 0
        };
    }

    private static int ScoreDocuments(
        int projectId,
        IReadOnlyDictionary<int, ProjectDataCounts> counts,
        List<string> gaps)
    {
        var documentCount = counts.TryGetValue(projectId, out var value)
            ? value.DocumentCount
            : 0;

        if (documentCount >= 3)
        {
            return 15;
        }

        gaps.Add(GapDocuments);

        return documentCount switch
        {
            2 => 10,
            1 => 5,
            _ => 0
        };
    }

    private static int ScoreVideo(
        int projectId,
        IReadOnlyDictionary<int, ProjectDataCounts> counts,
        List<string> gaps)
    {
        var videoCount = counts.TryGetValue(projectId, out var value)
            ? value.VideoCount
            : 0;

        if (videoCount >= 1)
        {
            return 10;
        }

        gaps.Add(GapVideo);
        return 0;
    }

    private static int ScoreBudgetMetrics(
        Project project,
        StageFactPresence facts,
        List<string> gaps)
    {
        var expected = new List<bool>();

        if (HasReachedStage(project, StageCodes.IPA))
        {
            expected.Add(facts.Ipa.Contains(project.Id));
        }

        if (HasReachedStage(project, StageCodes.AON))
        {
            expected.Add(facts.Aon.Contains(project.Id));
        }

        if (HasReachedStage(project, StageCodes.BM))
        {
            expected.Add(facts.Benchmark.Contains(project.Id));
        }

        if (HasReachedStage(project, StageCodes.COB))
        {
            expected.Add(facts.Commercial.Contains(project.Id));
        }

        if (HasReachedStage(project, StageCodes.PNC))
        {
            expected.Add(facts.Pnc.Contains(project.Id));
        }

        if (HasReachedStage(project, StageCodes.SO))
        {
            expected.Add(facts.SupplyOrder.Contains(project.Id));
        }

        if (expected.Count == 0)
        {
            return 15;
        }

        var completed = expected.Count(x => x);

        if (completed == expected.Count)
        {
            return 15;
        }

        gaps.Add(GapBudget);
        return (int)Math.Round(15m * completed / expected.Count);
    }

    private static int ScoreBackfill(Project project, List<string> gaps)
    {
        if (!project.ProjectStages.Any(s => s.RequiresBackfill))
        {
            return 15;
        }

        gaps.Add(GapBackfill);
        return 0;
    }

    private int ScoreCurrentStageTimeline(
        Project project,
        DateOnly today,
        List<string> gaps)
    {
        var current = WorkspaceNudgeService.GetCurrentStage(project);

        if (current is null)
        {
            return 10;
        }

        if (_nudges.IsCurrentStageOverdue(current, today) ||
            _nudges.HasCurrentStageTimelineIssue(current))
        {
            gaps.Add(GapCurrentStageTimeline);
            return 0;
        }

        return 10;
    }

    private static int ScoreRecentPoRemark(
        Project project,
        string userId,
        DateOnly today,
        List<string> gaps)
    {
        var lastRemark = WorkspaceNudgeService.LastPoRemark(project, userId);

        if (lastRemark.HasValue &&
            today.DayNumber - WorkspaceNudgeService.ToIstDate(lastRemark.Value).DayNumber <= 10)
        {
            return 10;
        }

        gaps.Add(GapRecentRemark);
        return 0;
    }

    // SECTION: Stage applicability helpers
    private static bool HasReachedStage(Project project, string stageCode)
    {
        var targetOrder = ProcurementWorkflow.OrderOf(project.WorkflowVersion, stageCode);

        if (targetOrder == int.MaxValue)
        {
            return false;
        }

        return project.ProjectStages.Any(stage =>
            ProcurementWorkflow.OrderOf(project.WorkflowVersion, stage.StageCode) >= targetOrder &&
            stage.Status != StageStatus.NotStarted);
    }

    private static string Label(int score) => score >= 80 ? "Good" : score >= 60 ? "Attention" : "Needs Work";

    private sealed record ProjectDataCounts(int PhotoCount, int DocumentCount, int VideoCount);

    private sealed record StageFactPresence(
        HashSet<int> Ipa,
        HashSet<int> Aon,
        HashSet<int> Benchmark,
        HashSet<int> Commercial,
        HashSet<int> Pnc,
        HashSet<int> SupplyOrder);
}
