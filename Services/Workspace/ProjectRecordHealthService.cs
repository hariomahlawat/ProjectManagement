using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
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

    private readonly ApplicationDbContext _db;
    private readonly ProjectProcurementReadService _procurementRead;

    public ProjectRecordHealthService(
        ApplicationDbContext db,
        ProjectProcurementReadService procurementRead)
    {
        _db = db;
        _procurementRead = procurementRead;
    }

    // The score combines fixed project-record requirements (description, documents,
    // photographs and video) with procurement and timeline fields that become applicable
    // as the project progresses. The weights and thresholds are intentionally stable.
    public async Task<IReadOnlyDictionary<int, WorkspaceRecordHealthVm>> CalculateForProjectsAsync(
        IReadOnlyList<Project> projects,
        string userId,
        CancellationToken ct)
    {
        var projectIds = projects.Select(p => p.Id).Distinct().ToArray();
        var mediaCounts = await LoadMediaCountsAsync(projectIds, ct);
        var procurement = await _procurementRead.GetManyAsync(projectIds, ct);
        var pncApplicability = await LoadPncApplicabilityAsync(projectIds, ct);
        var results = new Dictionary<int, WorkspaceRecordHealthVm>();

        foreach (var project in projects)
        {
            var components = new List<WorkspaceRecordComponentScoreVm>
            {
                ScoreCoreProfile(project, userId),
                ScoreProcurement(project, procurement, pncApplicability),
                ScoreHistoricalTimeline(project),
                ScoreCurrentStageTimeline(project),
                ScoreDocuments(project.Id, mediaCounts),
                ScoreSupportingMedia(project.Id, mediaCounts)
            };

            var gapDetails = components
                .SelectMany(component => component.Gaps)
                .OrderBy(gap => gap.Priority)
                .ThenBy(gap => gap.Component)
                .ThenBy(gap => gap.FieldLabel)
                .ToList();

            var roundedScore = Math.Clamp(
                (int)Math.Round(components.Sum(component => component.EarnedPoints), MidpointRounding.AwayFromZero),
                0,
                100);

            results[project.Id] = new WorkspaceRecordHealthVm
            {
                ProjectId = project.Id,
                ProjectName = project.Name,
                HealthPercent = roundedScore,
                HealthLabel = Label(roundedScore),
                Components = components,
                GapDetails = gapDetails,
                // Retained for compatibility with existing consumers and tests.
                Gaps = gapDetails.Select(gap => gap.FieldLabel).ToList(),
                OpenUrl = WorkspaceRouteHelper.ProjectOverview(project.Id)
            };
        }

        return results;
    }

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

    private static WorkspaceRecordComponentScoreVm ScoreCoreProfile(Project project, string userId)
    {
        var gaps = new List<WorkspaceRecordGapVm>();
        var descriptionComplete = !string.IsNullOrWhiteSpace(project.Description) &&
                                  project.Description.Trim().Length >= 30;

        if (!descriptionComplete)
        {
            var isAssignedProjectOfficer = string.Equals(
                project.LeadPoUserId,
                userId,
                StringComparison.OrdinalIgnoreCase);
            var actionUrl = isAssignedProjectOfficer
                ? WorkspaceRouteHelper.ProjectMetaRequest(project.Id)
                : WorkspaceRouteHelper.ProjectMetaEdit(project.Id);
            var actionText = isAssignedProjectOfficer
                ? "Update project details"
                : "Edit project details";

            gaps.Add(Gap(
                code: "PROJECT_DESCRIPTION",
                component: "Core project profile",
                fieldLabel: "Project description",
                reason: "Required for every project and must contain a meaningful description.",
                earned: 0m,
                maximum: CoreProfileWeight,
                actionText: actionText,
                actionUrl: actionUrl,
                icon: "bi-card-text",
                priority: 40));
        }

        return Component(
            "CORE_PROFILE",
            "Core project profile",
            descriptionComplete ? CoreProfileWeight : 0m,
            CoreProfileWeight,
            gaps);
    }

    private static WorkspaceRecordComponentScoreVm ScoreProcurement(
        Project project,
        IReadOnlyDictionary<int, ProcurementAtAGlanceVm> procurementByProject,
        IReadOnlyDictionary<int, bool> pncApplicability)
    {
        var snapshot = procurementByProject.GetValueOrDefault(project.Id) ?? ProcurementAtAGlanceVm.Empty;
        var fields = new List<ApplicableField>();
        var actionUrl = WorkspaceRouteHelper.ProjectProcurementEdit(project.Id);

        AddWhenStageCompleted(project, ProcurementStageRules.StageForIpaCost,
            snapshot.IpaCost is > 0m, "IPA_COST", "IPA Cost", actionUrl, fields);
        AddWhenStageCompleted(project, ProcurementStageRules.StageForAonCost,
            snapshot.AonCost is > 0m, "AON_COST", "AoN Cost", actionUrl, fields);
        AddWhenStageCompleted(project, ProcurementStageRules.StageForBenchmarkCost,
            snapshot.BenchmarkCost is > 0m, "BENCHMARK_COST", "Benchmark Cost", actionUrl, fields);
        AddWhenStageCompleted(project, ProcurementStageRules.StageForL1Cost,
            snapshot.L1Cost is > 0m, "L1_COST", "L1 Cost", actionUrl, fields);

        var isPncApplicable = !pncApplicability.TryGetValue(project.Id, out var applicable) || applicable;
        if (isPncApplicable)
        {
            AddWhenStageCompleted(project, ProcurementStageRules.StageForPncCost,
                snapshot.PncCost is > 0m, "PNC_COST", "PNC Cost", actionUrl, fields);
        }

        AddWhenStageCompleted(project, ProcurementStageRules.StageForSupplyOrder,
            snapshot.SupplyOrderDate.HasValue && snapshot.SupplyOrderDate.Value != default,
            "SUPPLY_ORDER_DATE", "Supply Order Date", actionUrl, fields);

        return ScoreApplicableFields(
            code: "PROCUREMENT",
            label: "Procurement at a glance",
            weight: ProcurementWeight,
            fields: fields,
            componentReason: "Applicable procurement fields are determined by completed project stages.",
            actionText: "Update procurement",
            icon: "bi-currency-rupee",
            priority: 20);
    }

    private static void AddWhenStageCompleted(
        Project project,
        string stageCode,
        bool isComplete,
        string code,
        string fieldLabel,
        string actionUrl,
        ICollection<ApplicableField> fields)
    {
        if (!IsStageCompleted(project, stageCode))
        {
            return;
        }

        fields.Add(new ApplicableField(
            isComplete,
            code,
            fieldLabel,
            stageCode,
            $"Required because the {stageCode} stage is completed.",
            actionUrl));
    }

    // Historical stages are completion-driven. Actual start is optional and may be inferred.
    private static WorkspaceRecordComponentScoreVm ScoreHistoricalTimeline(Project project)
    {
        var current = PresentStageHelper.Resolve(project);
        var hasActiveOrFutureCurrent = current is not null && current.Status != StageStatus.Completed;

        var currentOrder = current is null
            ? int.MaxValue
            : ProcurementWorkflow.OrderOf(project.WorkflowVersion, current.StageCode);

        var historicalStages = project.ProjectStages
            .Where(stage => stage.Status == StageStatus.Completed)
            .Where(stage => !hasActiveOrFutureCurrent ||
                ProcurementWorkflow.OrderOf(project.WorkflowVersion, stage.StageCode) < currentOrder)
            .OrderBy(stage => ProcurementWorkflow.OrderOf(project.WorkflowVersion, stage.StageCode))
            .ThenBy(stage => stage.StageCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var fields = new List<ApplicableField>(historicalStages.Count);
        var actionUrl = WorkspaceRouteHelper.ProjectTimelineActuals(project.Id);

        foreach (var stage in historicalStages)
        {
            fields.Add(new ApplicableField(
                stage.CompletedOn.HasValue,
                $"{stage.StageCode}_ACTUAL_COMPLETION",
                $"{stage.StageCode} — Actual Completion",
                stage.StageCode,
                $"Required because the {stage.StageCode} stage is completed.",
                actionUrl));
        }

        return ScoreApplicableFields(
            code: "HISTORICAL_TIMELINE",
            label: "Historical stage completion dates",
            weight: HistoricalTimelineWeight,
            fields: fields,
            componentReason: "Completed historical stages require an Actual Completion date. Actual Start is optional and may be inferred.",
            actionText: "Complete historical dates",
            icon: "bi-clock-history",
            priority: 30);
    }

    private static WorkspaceRecordComponentScoreVm ScoreCurrentStageTimeline(Project project)
    {
        var current = PresentStageHelper.Resolve(project);
        if (current is null || current.Status is StageStatus.Completed or StageStatus.Skipped)
        {
            return Component("CURRENT_STAGE_PDC", "Current-stage timeline (PDC)",
                CurrentStageTimelineWeight, CurrentStageTimelineWeight, Array.Empty<WorkspaceRecordGapVm>());
        }

        var gaps = new List<WorkspaceRecordGapVm>();
        decimal score = 0m;
        var actionUrl = WorkspaceRouteHelper.ProjectTimelineActuals(project.Id);

        if (current.Status == StageStatus.InProgress)
        {
            if (current.ActualStart.HasValue)
            {
                score += CurrentStageTimelineWeight * 0.40m;
            }
            else
            {
                gaps.Add(Gap(
                    "CURRENT_STAGE_ACTUAL_START",
                    "Current-stage timeline (PDC)",
                    $"{current.StageCode} — Actual Start",
                    $"Required because {current.StageCode} is the current in-progress stage.",
                    0m,
                    CurrentStageTimelineWeight * 0.40m,
                    "Update current-stage timeline",
                    actionUrl,
                    "bi-calendar-check",
                    10,
                    current.StageCode));
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
                gaps.Add(Gap(
                    "CURRENT_STAGE_PLANNED_START",
                    "Current-stage timeline (PDC)",
                    $"{current.StageCode} — Planned Start",
                    $"Required because {current.StageCode} is the current stage and has not started.",
                    0m,
                    CurrentStageTimelineWeight * 0.40m,
                    "Update current-stage timeline",
                    actionUrl,
                    "bi-calendar-event",
                    10,
                    current.StageCode));
            }
        }

        if (current.PlannedDue.HasValue)
        {
            score += CurrentStageTimelineWeight * 0.60m;
        }
        else
        {
            gaps.Add(Gap(
                "CURRENT_STAGE_PDC",
                "Current-stage timeline (PDC)",
                $"{current.StageCode} — PDC",
                $"Required because {current.StageCode} is the current stage.",
                0m,
                CurrentStageTimelineWeight * 0.60m,
                "Update current-stage timeline",
                actionUrl,
                "bi-calendar2-week",
                5,
                current.StageCode));
        }

        return Component("CURRENT_STAGE_PDC", "Current-stage timeline (PDC)", score,
            CurrentStageTimelineWeight, gaps);
    }

    private static WorkspaceRecordComponentScoreVm ScoreDocuments(
        int projectId,
        IReadOnlyDictionary<int, ProjectDataCounts> counts)
    {
        var count = counts.TryGetValue(projectId, out var value) ? value.DocumentCount : 0;
        var completed = Math.Min(count, 3);
        var score = DocumentsWeight * completed / 3m;
        var gaps = new List<WorkspaceRecordGapVm>();

        if (count < 3)
        {
            var remaining = 3 - count;
            gaps.Add(Gap(
                "PROJECT_DOCUMENTS",
                "Published project documents",
                $"{remaining} additional project document{(remaining == 1 ? "" : "s")}",
                "At least three published, non-archived project documents are required.",
                0m,
                DocumentsWeight - score,
                "Upload documents",
                WorkspaceRouteHelper.ProjectDocumentsTab(projectId),
                "bi-file-earmark-text",
                50));
        }

        return Component("DOCUMENTS", "Published project documents", score, DocumentsWeight, gaps);
    }

    private static WorkspaceRecordComponentScoreVm ScoreSupportingMedia(
        int projectId,
        IReadOnlyDictionary<int, ProjectDataCounts> counts)
    {
        var value = counts.TryGetValue(projectId, out var found)
            ? found
            : new ProjectDataCounts(0, 0, 0);

        decimal score = 0m;
        var gaps = new List<WorkspaceRecordGapVm>();

        var completedPhotoSlots = Math.Min(value.PhotoCount, 3);
        var photoScore = 6m * completedPhotoSlots / 3m;
        score += photoScore;
        if (value.PhotoCount < 3)
        {
            var remaining = 3 - value.PhotoCount;
            gaps.Add(Gap(
                "PROJECT_PHOTOS",
                "Supporting media",
                $"{remaining} additional project photo{(remaining == 1 ? "" : "s")}",
                "At least three project photos are required as supporting evidence.",
                0m,
                6m - photoScore,
                "Add photos",
                WorkspaceRouteHelper.ProjectPhotos(projectId),
                "bi-images",
                60));
        }

        if (value.VideoCount > 0)
        {
            score += 4m;
        }
        else
        {
            gaps.Add(Gap(
                "PROJECT_VIDEO",
                "Supporting media",
                "Project video",
                "At least one project video is required as supporting evidence.",
                0m,
                4m,
                "Add video",
                WorkspaceRouteHelper.ProjectVideos(projectId),
                "bi-camera-video",
                65));
        }

        return Component("MEDIA", "Supporting media", score, SupportingMediaWeight, gaps);
    }

    private static WorkspaceRecordComponentScoreVm ScoreApplicableFields(
        string code,
        string label,
        decimal weight,
        IReadOnlyCollection<ApplicableField> fields,
        string componentReason,
        string actionText,
        string icon,
        int priority)
    {
        if (fields.Count == 0)
        {
            return Component(code, label, weight, weight, Array.Empty<WorkspaceRecordGapVm>());
        }

        var fieldShare = weight / fields.Count;
        var completeCount = fields.Count(field => field.IsComplete);
        var earned = fieldShare * completeCount;
        var gaps = fields
            .Where(field => !field.IsComplete)
            .Select(field => Gap(
                field.Code,
                label,
                field.FieldLabel,
                field.Reason,
                0m,
                fieldShare,
                actionText,
                field.ActionUrl,
                icon,
                priority,
                field.StageCode))
            .ToList();

        return Component(code, label, earned, weight, gaps, componentReason);
    }

    private static WorkspaceRecordComponentScoreVm Component(
        string code,
        string label,
        decimal earned,
        decimal maximum,
        IReadOnlyList<WorkspaceRecordGapVm> gaps,
        string? explanation = null)
        => new()
        {
            Code = code,
            Label = label,
            EarnedPoints = decimal.Round(earned, 2),
            MaximumPoints = maximum,
            Status = gaps.Count == 0 ? "Complete" : earned > 0 ? "Partial" : "Pending",
            Explanation = explanation ?? string.Empty,
            Gaps = gaps
        };

    private static WorkspaceRecordGapVm Gap(
        string code,
        string component,
        string fieldLabel,
        string reason,
        decimal earned,
        decimal maximum,
        string actionText,
        string actionUrl,
        string icon,
        int priority,
        string? stageCode = null)
        => new()
        {
            Code = code,
            Component = component,
            FieldLabel = fieldLabel,
            StageCode = stageCode,
            Status = earned > 0 ? "Partial" : "Pending",
            Reason = reason,
            EarnedPoints = decimal.Round(earned, 2),
            MaximumPoints = decimal.Round(maximum, 2),
            ActionText = actionText,
            ActionUrl = actionUrl,
            Icon = icon,
            Priority = priority
        };

    private static bool IsStageCompleted(Project project, string stageCode)
        => project.ProjectStages.Any(stage =>
            string.Equals(stage.StageCode, stageCode, StringComparison.OrdinalIgnoreCase) &&
            stage.Status == StageStatus.Completed);

    private static string Label(int score)
        => score >= 80 ? "Good" : score >= 60 ? "Attention" : "Needs Work";

    private sealed record ProjectDataCounts(int PhotoCount, int DocumentCount, int VideoCount);

    private sealed record ApplicableField(
        bool IsComplete,
        string Code,
        string FieldLabel,
        string? StageCode,
        string Reason,
        string ActionUrl);
}
