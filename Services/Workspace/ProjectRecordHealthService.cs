using Microsoft.EntityFrameworkCore;
using ProjectManagement.Infrastructure;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
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
        var factCompleteness = await LoadStageFactCompletenessAsync(projectIds, ct);
        var pncApplicability = await LoadPncApplicabilityAsync(projectIds, ct);
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
            score += ScoreBudgetMetrics(project, factCompleteness, pncApplicability, gaps);
            score += ScoreBackfill(project, gaps);
            score += ScoreCurrentStageTimeline(project, gaps);
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

    // SECTION: PNC applicability comes from the latest approved plan and defaults to applicable when unknown.
    private async Task<IReadOnlyDictionary<int, bool>> LoadPncApplicabilityAsync(
        int[] projectIds,
        CancellationToken ct)
    {
        var planRows = await _db.PlanVersions
            .AsNoTracking()
            .Where(plan =>
                projectIds.Contains(plan.ProjectId) &&
                plan.Status == PlanVersionStatus.Approved)
            .Select(plan => new
            {
                plan.ProjectId,
                plan.PncApplicable,
                plan.CreatedOn,
                plan.SubmittedOn,
                plan.ApprovedOn
            })
            .ToListAsync(ct);

        return planRows
            .GroupBy(plan => plan.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(plan => plan.ApprovedOn ?? plan.SubmittedOn ?? plan.CreatedOn)
                    .First()
                    .PncApplicable);
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

    // SECTION: Stage-specific fact completeness validates meaningful budget values.
    private async Task<StageFactCompleteness> LoadStageFactCompletenessAsync(
        int[] projectIds,
        CancellationToken ct)
    {
        var ipa = (await _db.ProjectIpaFacts
            .AsNoTracking()
            .Where(f => projectIds.Contains(f.ProjectId) && f.IpaCost > 0)
            .Select(f => f.ProjectId)
            .ToListAsync(ct)).ToHashSet();

        var aon = (await _db.ProjectAonFacts
            .AsNoTracking()
            .Where(f => projectIds.Contains(f.ProjectId) && f.AonCost > 0)
            .Select(f => f.ProjectId)
            .ToListAsync(ct)).ToHashSet();

        var benchmark = (await _db.ProjectBenchmarkFacts
            .AsNoTracking()
            .Where(f => projectIds.Contains(f.ProjectId) && f.BenchmarkCost > 0)
            .Select(f => f.ProjectId)
            .ToListAsync(ct)).ToHashSet();

        var commercial = (await _db.ProjectCommercialFacts
            .AsNoTracking()
            .Where(f => projectIds.Contains(f.ProjectId) && f.L1Cost > 0)
            .Select(f => f.ProjectId)
            .ToListAsync(ct)).ToHashSet();

        var pnc = (await _db.ProjectPncFacts
            .AsNoTracking()
            .Where(f => projectIds.Contains(f.ProjectId) && f.PncCost > 0)
            .Select(f => f.ProjectId)
            .ToListAsync(ct)).ToHashSet();

        var supplyOrder = (await _db.ProjectSupplyOrderFacts
            .AsNoTracking()
            .Where(f =>
                projectIds.Contains(f.ProjectId) &&
                f.SupplyOrderDate != default)
            .Select(f => f.ProjectId)
            .ToListAsync(ct)).ToHashSet();

        return new StageFactCompleteness(
            ipa,
            aon,
            benchmark,
            commercial,
            pnc,
            supplyOrder);
    }

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
        StageFactCompleteness facts,
        IReadOnlyDictionary<int, bool> pncApplicability,
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

        var isPncApplicable = !pncApplicability.TryGetValue(project.Id, out var applicable) || applicable;

        if (isPncApplicable && HasReachedStage(project, StageCodes.PNC))
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
        List<string> gaps)
    {
        var current = WorkspaceNudgeService.GetCurrentStage(project);

        if (current is null)
        {
            return 10;
        }

        if (_nudges.HasCurrentStageTimelineIssue(current))
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

    private sealed record StageFactCompleteness(
        HashSet<int> Ipa,
        HashSet<int> Aon,
        HashSet<int> Benchmark,
        HashSet<int> Commercial,
        HashSet<int> Pnc,
        HashSet<int> SupplyOrder);
}
