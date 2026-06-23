using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Services.Projects;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Services.Workspace;

public sealed class ProjectRecordHealthService
{
    private const decimal CoreProfileWeight = 15m;
    private const decimal ProcurementWeight = 25m;
    private const decimal HistoricalTimelineWeight = 20m;
    private const decimal CurrentStageTimelineWeight = 15m;
    private const decimal DocumentsWeight = 15m;
    private const decimal SupportingMediaWeight = 10m;

    private const string GapBriefDescription = "Brief description pending";
    private const string GapPhotos = "Add at least 3 project photos";
    private const string GapDocuments = "Upload at least 3 project documents";
    private const string GapVideo = "Add at least 1 project video";

    private readonly ApplicationDbContext _db;
    private readonly ProjectProcurementReadService _procurementRead;

    public ProjectRecordHealthService(
        ApplicationDbContext db,
        ProjectProcurementReadService procurementRead)
    {
        _db = db;
        _procurementRead = procurementRead;
    }

    // SECTION: Batch project-record completeness calculation
    // The percentage answers one question only: of the information that should exist at
    // the project's present stage, how much has actually been recorded?
    public async Task<IReadOnlyDictionary<int, WorkspaceRecordHealthVm>> CalculateForProjectsAsync(
        IReadOnlyList<Project> projects,
        string userId,
        CancellationToken ct)
    {
        _ = userId; // Update freshness is intentionally assessed elsewhere, not in completeness.

        var projectIds = projects.Select(p => p.Id).Distinct().ToArray();
        var mediaCounts = await LoadMediaCountsAsync(projectIds, ct);
        var procurement = await _procurementRead.GetManyAsync(projectIds, ct);
        var pncApplicability = await LoadPncApplicabilityAsync(projectIds, ct);
        var results = new Dictionary<int, WorkspaceRecordHealthVm>();

        foreach (var project in projects)
        {
            var gaps = new List<string>();
            decimal score = 0m;

            score += ScoreCoreProfile(project, gaps);
            score += ScoreProcurement(project, procurement, pncApplicability, gaps);
            score += ScoreHistoricalTimeline(project, gaps);
            score += ScoreCurrentStageTimeline(project, gaps);
            score += ScoreDocuments(project.Id, mediaCounts, gaps);
            score += ScoreSupportingMedia(project.Id, mediaCounts, gaps);

            var roundedScore = Math.Clamp(
                (int)Math.Round(score, MidpointRounding.AwayFromZero),
                0,
                100);

            results[project.Id] = new WorkspaceRecordHealthVm
            {
                ProjectId = project.Id,
                ProjectName = project.Name,
                HealthPercent = roundedScore,
                HealthLabel = Label(roundedScore),
                Gaps = gaps,
                OpenUrl = WorkspaceRouteHelper.ProjectOverview(project.Id)
            };
        }

        return results;
    }

    // SECTION: PNC applicability comes from the latest approved timeline plan.
    // If no approved plan exists, the workflow's PNC stage remains applicable by default,
    // matching the application's existing planning default.
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

    // SECTION: Media and document counts
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

    // SECTION: Core profile — stable project-record information only
    private static decimal ScoreCoreProfile(Project project, List<string> gaps)
    {
        if (!string.IsNullOrWhiteSpace(project.Description) &&
            project.Description.Trim().Length >= 30)
        {
            return CoreProfileWeight;
        }

        gaps.Add(GapBriefDescription);
        return 0m;
    }

    // SECTION: Procurement at a glance — stage-aware, field-level proportional scoring
    private static decimal ScoreProcurement(
        Project project,
        IReadOnlyDictionary<int, ProcurementAtAGlanceVm> procurementByProject,
        IReadOnlyDictionary<int, bool> pncApplicability,
        List<string> gaps)
    {
        var snapshot = procurementByProject.GetValueOrDefault(project.Id) ?? ProcurementAtAGlanceVm.Empty;
        var applicableFields = new List<(bool IsComplete, string Gap)>();

        AddWhenStageCompleted(
            project,
            ProcurementStageRules.StageForIpaCost,
            snapshot.IpaCost is > 0m,
            "IPA Cost pending",
            applicableFields);

        AddWhenStageCompleted(
            project,
            ProcurementStageRules.StageForAonCost,
            snapshot.AonCost is > 0m,
            "AoN Cost pending",
            applicableFields);

        AddWhenStageCompleted(
            project,
            ProcurementStageRules.StageForBenchmarkCost,
            snapshot.BenchmarkCost is > 0m,
            "Benchmark Cost pending",
            applicableFields);

        AddWhenStageCompleted(
            project,
            ProcurementStageRules.StageForL1Cost,
            snapshot.L1Cost is > 0m,
            "L1 Cost pending",
            applicableFields);

        var isPncApplicable = !pncApplicability.TryGetValue(project.Id, out var applicable) || applicable;
        if (isPncApplicable)
        {
            AddWhenStageCompleted(
                project,
                ProcurementStageRules.StageForPncCost,
                snapshot.PncCost is > 0m,
                "PNC Cost pending",
                applicableFields);
        }

        AddWhenStageCompleted(
            project,
            ProcurementStageRules.StageForSupplyOrder,
            snapshot.SupplyOrderDate.HasValue && snapshot.SupplyOrderDate.Value != default,
            "Supply Order Date pending",
            applicableFields);

        return ScoreApplicableFields(ProcurementWeight, applicableFields, gaps);
    }

    private static void AddWhenStageCompleted(
        Project project,
        string stageCode,
        bool isComplete,
        string gap,
        ICollection<(bool IsComplete, string Gap)> applicableFields)
    {
        if (IsStageCompleted(project, stageCode))
        {
            applicableFields.Add((isComplete, gap));
        }
    }

    // SECTION: Historical timeline — only actual dates count
    // Planned dates are deliberately excluded. Every completed historical stage contributes
    // two equally weighted fields: Actual Start and Actual Completion.
    private static decimal ScoreHistoricalTimeline(Project project, List<string> gaps)
    {
        var current = PresentStageHelper.Resolve(project.ProjectStages);
        var hasActiveOrFutureCurrent = current is not null && current.Status != StageStatus.Completed;

        var historicalStages = project.ProjectStages
            .Where(stage => stage.Status == StageStatus.Completed)
            .Where(stage => !hasActiveOrFutureCurrent || stage.SortOrder < current!.SortOrder)
            .OrderBy(stage => stage.SortOrder)
            .ThenBy(stage => stage.StageCode)
            .ToList();

        if (historicalStages.Count == 0)
        {
            return HistoricalTimelineWeight;
        }

        var applicableFields = new List<(bool IsComplete, string Gap)>(historicalStages.Count * 2);

        foreach (var stage in historicalStages)
        {
            applicableFields.Add((
                stage.ActualStart.HasValue,
                $"{stage.StageCode} actual start missing"));
            applicableFields.Add((
                stage.CompletedOn.HasValue,
                $"{stage.StageCode} actual completion missing"));
        }

        return ScoreApplicableFields(HistoricalTimelineWeight, applicableFields, gaps);
    }

    // SECTION: Current-stage timeline (PDC)
    // PDC is the controlling date and therefore carries 60% of this category.
    // Overdue dates do not reduce completeness; they are handled separately as schedule health.
    private static decimal ScoreCurrentStageTimeline(Project project, List<string> gaps)
    {
        var current = PresentStageHelper.Resolve(project.ProjectStages);

        if (current is null || current.Status is StageStatus.Completed or StageStatus.Skipped)
        {
            return CurrentStageTimelineWeight;
        }

        decimal score = 0m;

        if (current.Status == StageStatus.InProgress)
        {
            if (current.ActualStart.HasValue)
            {
                score += CurrentStageTimelineWeight * 0.40m;
            }
            else
            {
                gaps.Add("Current-stage Actual Start pending");
            }
        }
        else
        {
            if (current.PlannedStart.HasValue)
            {
                score += CurrentStageTimelineWeight * 0.40m;
            }
            else
            {
                gaps.Add("Current-stage Planned Start pending");
            }
        }

        if (current.PlannedDue.HasValue)
        {
            score += CurrentStageTimelineWeight * 0.60m;
        }
        else
        {
            gaps.Add("Current-stage timeline (PDC) pending");
        }

        return score;
    }

    // SECTION: Required project documents
    private static decimal ScoreDocuments(
        int projectId,
        IReadOnlyDictionary<int, ProjectDataCounts> counts,
        List<string> gaps)
    {
        var count = counts.TryGetValue(projectId, out var value) ? value.DocumentCount : 0;
        var completed = Math.Min(count, 3);

        if (count < 3)
        {
            gaps.Add(GapDocuments);
        }

        return DocumentsWeight * completed / 3m;
    }

    // SECTION: Supporting media — deliberately lower-weight than substantive records
    private static decimal ScoreSupportingMedia(
        int projectId,
        IReadOnlyDictionary<int, ProjectDataCounts> counts,
        List<string> gaps)
    {
        var value = counts.TryGetValue(projectId, out var found)
            ? found
            : new ProjectDataCounts(0, 0, 0);

        decimal score = 0m;

        var completedPhotoSlots = Math.Min(value.PhotoCount, 3);
        score += 6m * completedPhotoSlots / 3m;
        if (value.PhotoCount < 3)
        {
            gaps.Add(GapPhotos);
        }

        if (value.VideoCount > 0)
        {
            score += 4m;
        }
        else
        {
            gaps.Add(GapVideo);
        }

        return score;
    }

    private static decimal ScoreApplicableFields(
        decimal categoryWeight,
        IReadOnlyCollection<(bool IsComplete, string Gap)> applicableFields,
        ICollection<string> gaps)
    {
        if (applicableFields.Count == 0)
        {
            return categoryWeight;
        }

        var completeCount = 0;
        foreach (var field in applicableFields)
        {
            if (field.IsComplete)
            {
                completeCount++;
            }
            else
            {
                gaps.Add(field.Gap);
            }
        }

        return categoryWeight * completeCount / applicableFields.Count;
    }

    private static bool IsStageCompleted(Project project, string stageCode)
        => project.ProjectStages.Any(stage =>
            string.Equals(stage.StageCode, stageCode, StringComparison.OrdinalIgnoreCase) &&
            stage.Status == StageStatus.Completed);

    private static string Label(int score)
        => score >= 80 ? "Good" : score >= 60 ? "Attention" : "Needs Work";

    private sealed record ProjectDataCounts(int PhotoCount, int DocumentCount, int VideoCount);
}
