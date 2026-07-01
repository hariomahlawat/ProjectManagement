using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Stages;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Services.Approvals;

/// <summary>
/// Calculates whether pending stage requests can be decided against the workflow
/// version and approved lifecycle state of each individual project. The service is
/// intentionally read-only; the decision service repeats the assessment immediately
/// before applying a change.
/// </summary>
public sealed class StageApprovalSequenceService
{
    private const string PendingStatus = "Pending";

    private readonly ApplicationDbContext _db;
    private readonly IProjectStageWorkflowPolicy _workflowPolicy;
    private readonly ProjectFactsReadService _factsRead;

    public StageApprovalSequenceService(
        ApplicationDbContext db,
        IProjectStageWorkflowPolicy workflowPolicy,
        ProjectFactsReadService factsRead)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _workflowPolicy = workflowPolicy ?? throw new ArgumentNullException(nameof(workflowPolicy));
        _factsRead = factsRead ?? throw new ArgumentNullException(nameof(factsRead));
    }

    public async Task<StageApprovalAssessment?> AssessRequestAsync(
        int requestId,
        CancellationToken cancellationToken = default)
    {
        var request = await _db.StageChangeRequests
            .AsNoTracking()
            .Where(item => item.Id == requestId)
            .Select(item => new { item.ProjectId })
            .SingleOrDefaultAsync(cancellationToken);

        if (request is null)
        {
            return null;
        }

        var assessments = await AssessProjectsAsync(
            new[] { request.ProjectId },
            includeRequestId: requestId,
            cancellationToken);

        return assessments.TryGetValue(requestId, out var assessment)
            ? assessment
            : null;
    }

    public Task<IReadOnlyDictionary<int, StageApprovalAssessment>> AssessPendingAsync(
        IEnumerable<int> projectIds,
        CancellationToken cancellationToken = default)
        => AssessProjectsAsync(projectIds, includeRequestId: null, cancellationToken);

    private async Task<IReadOnlyDictionary<int, StageApprovalAssessment>> AssessProjectsAsync(
        IEnumerable<int> projectIds,
        int? includeRequestId,
        CancellationToken cancellationToken)
    {
        var ids = projectIds
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return new Dictionary<int, StageApprovalAssessment>();
        }

        var projects = await _db.Projects
            .AsNoTracking()
            .Where(project => ids.Contains(project.Id))
            .Select(project => new ProjectSnapshot(project.Id, project.Name, project.WorkflowVersion))
            .ToDictionaryAsync(project => project.Id, cancellationToken);

        var stages = await _db.ProjectStages
            .AsNoTracking()
            .Where(stage => ids.Contains(stage.ProjectId))
            .Select(stage => new StageSnapshot(
                stage.ProjectId,
                stage.StageCode,
                stage.Status,
                stage.ActualStart,
                stage.CompletedOn))
            .ToListAsync(cancellationToken);

        var allRequests = await _db.StageChangeRequests
            .AsNoTracking()
            .Where(request => ids.Contains(request.ProjectId))
            .OrderBy(request => request.RequestedOn)
            .ThenBy(request => request.Id)
            .Select(request => new RequestSnapshot(
                request.Id,
                request.ProjectId,
                request.StageCode,
                request.RequestedStatus,
                request.RequestedDate,
                request.RequestedStartDate,
                request.RequestedOn,
                request.DecisionStatus))
            .ToListAsync(cancellationToken);

        var candidates = allRequests
            .Where(request => string.Equals(request.DecisionStatus, PendingStatus, StringComparison.OrdinalIgnoreCase)
                || (includeRequestId.HasValue && request.Id == includeRequestId.Value))
            .ToArray();

        var result = new Dictionary<int, StageApprovalAssessment>();

        foreach (var projectGroup in candidates.GroupBy(request => request.ProjectId))
        {
            if (!projects.TryGetValue(projectGroup.Key, out var project))
            {
                foreach (var request in projectGroup)
                {
                    result[request.Id] = StageApprovalAssessment.Stale(
                        request.Id,
                        request.StageCode,
                        request.StageCode,
                        "Unknown",
                        int.MaxValue,
                        "The project no longer exists.");
                }

                continue;
            }

            var workflow = await _workflowPolicy.GetAsync(project.Id, cancellationToken);
            var projectStages = stages
                .Where(stage => stage.ProjectId == project.Id)
                .Where(stage => !string.IsNullOrWhiteSpace(stage.StageCode))
                .ToDictionary(stage => stage.StageCode, StringComparer.OrdinalIgnoreCase);

            var projectRequests = allRequests
                .Where(request => request.ProjectId == project.Id)
                .ToArray();

            var pendingRequests = projectRequests
                .Where(request => string.Equals(request.DecisionStatus, PendingStatus, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var latestPendingByStage = pendingRequests
                .GroupBy(request => request.StageCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderByDescending(request => request.RequestedOn)
                        .ThenByDescending(request => request.Id)
                        .First(),
                    StringComparer.OrdinalIgnoreCase);

            var revisionByRequestId = projectRequests
                .GroupBy(request => request.StageCode, StringComparer.OrdinalIgnoreCase)
                .SelectMany(group => group
                    .OrderBy(request => request.RequestedOn)
                    .ThenBy(request => request.Id)
                    .Select((request, index) => new { request.Id, Revision = index + 1 }))
                .ToDictionary(item => item.Id, item => item.Revision);

            foreach (var request in projectGroup)
            {
                var assessment = await AssessOneAsync(
                    project,
                    request,
                    workflow,
                    projectStages,
                    latestPendingByStage,
                    revisionByRequestId.TryGetValue(request.Id, out var revision) ? revision : 1,
                    cancellationToken);

                result[request.Id] = assessment;
            }
        }

        return result;
    }

    private async Task<StageApprovalAssessment> AssessOneAsync(
        ProjectSnapshot project,
        RequestSnapshot request,
        ProjectStageWorkflowSnapshot workflow,
        IReadOnlyDictionary<string, StageSnapshot> projectStages,
        IReadOnlyDictionary<string, RequestSnapshot> latestPendingByStage,
        int revisionNumber,
        CancellationToken cancellationToken)
    {
        var stageName = StageCodes.DisplayNameOf(workflow.WorkflowVersion, request.StageCode);
        var workflowOrder = workflow.OrderOf(request.StageCode);
        var checks = new List<ApprovalCheckVm>();
        var waitingOn = new List<int>();
        string? correctionUrl = null;

        if (!string.Equals(request.DecisionStatus, PendingStatus, StringComparison.OrdinalIgnoreCase))
        {
            return StageApprovalAssessment.Stale(
                request.Id,
                request.StageCode,
                stageName,
                workflow.WorkflowVersion,
                workflowOrder,
                "This request is no longer pending.",
                revisionNumber);
        }

        if (latestPendingByStage.TryGetValue(request.StageCode, out var latest)
            && latest.Id != request.Id)
        {
            checks.Add(new ApprovalCheckVm(
                ApprovalCheckState.Blocked,
                "Latest revision",
                $"Revision {revisionNumber} has been replaced by a newer request for {stageName}."));

            return new StageApprovalAssessment(
                request.Id,
                request.StageCode,
                stageName,
                workflow.WorkflowVersion,
                workflowOrder,
                revisionNumber,
                false,
                ApprovalReadiness.Superseded,
                "A newer request exists for this stage.",
                checks,
                waitingOn,
                null);
        }

        checks.Add(new ApprovalCheckVm(
            ApprovalCheckState.Passed,
            "Latest revision",
            $"Revision {revisionNumber} is the current pending request for this stage."));

        if (!workflow.ContainsStage(request.StageCode))
        {
            checks.Add(new ApprovalCheckVm(
                ApprovalCheckState.Blocked,
                "Workflow compatibility",
                $"{stageName} is not part of workflow {workflow.WorkflowVersion}."));

            return new StageApprovalAssessment(
                request.Id,
                request.StageCode,
                stageName,
                workflow.WorkflowVersion,
                workflowOrder,
                revisionNumber,
                true,
                ApprovalReadiness.Stale,
                "The project workflow has changed. This request must be resubmitted.",
                checks,
                waitingOn,
                $"/Projects/Overview/{project.Id}#timeline");
        }

        checks.Add(new ApprovalCheckVm(
            ApprovalCheckState.Passed,
            "Workflow compatibility",
            $"Evaluated against project workflow {workflow.WorkflowVersion}."));

        if (!projectStages.TryGetValue(request.StageCode, out var stage))
        {
            checks.Add(new ApprovalCheckVm(
                ApprovalCheckState.Blocked,
                "Stage record",
                "The project stage record is missing."));

            return new StageApprovalAssessment(
                request.Id,
                request.StageCode,
                stageName,
                workflow.WorkflowVersion,
                workflowOrder,
                revisionNumber,
                true,
                ApprovalReadiness.Stale,
                "The project stage record is no longer available.",
                checks,
                waitingOn,
                $"/Projects/Overview/{project.Id}#timeline");
        }

        if (!Enum.TryParse<StageStatus>(request.RequestedStatus, true, out var requestedStatus))
        {
            checks.Add(new ApprovalCheckVm(
                ApprovalCheckState.Blocked,
                "Requested status",
                "The requested status is not recognised."));

            return Build(
                ApprovalReadiness.Blocked,
                "The requested status is invalid.",
                correctionUrl: null);
        }

        if (stage.Status == requestedStatus)
        {
            checks.Add(new ApprovalCheckVm(
                ApprovalCheckState.Blocked,
                "Current project state",
                $"{stageName} is already {FormatStatus(requestedStatus)}."));

            return Build(
                ApprovalReadiness.Stale,
                "The project changed after this request was submitted.",
                $"/Projects/Overview/{project.Id}#timeline");
        }

        checks.Add(new ApprovalCheckVm(
            ApprovalCheckState.Passed,
            "Current project state",
            $"Current status: {FormatStatus(stage.Status)}. Requested status: {FormatStatus(requestedStatus)}."));

        if (requestedStatus == StageStatus.Completed)
        {
            if (!request.RequestedDate.HasValue)
            {
                checks.Add(new ApprovalCheckVm(
                    ApprovalCheckState.Blocked,
                    "Completion date",
                    "A completion date is required."));
            }
            else if ((request.RequestedStartDate ?? stage.ActualStart) is DateOnly proposedStart
                && request.RequestedDate.Value < proposedStart)
            {
                checks.Add(new ApprovalCheckVm(
                    ApprovalCheckState.Blocked,
                    "Completion date",
                    $"The proposed completion date precedes the proposed start date of {proposedStart:dd MMM yyyy}."));
            }
            else
            {
                checks.Add(new ApprovalCheckVm(
                    ApprovalCheckState.Passed,
                    "Completion date",
                    request.RequestedDate.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)));
            }

            var hasFacts = await _factsRead.HasRequiredFactsAsync(project.Id, request.StageCode, cancellationToken);
            if (!hasFacts)
            {
                correctionUrl = BuildFactsUrl(project.Id, request.StageCode);
                checks.Add(new ApprovalCheckVm(
                    ApprovalCheckState.Blocked,
                    "Required stage information",
                    $"Required information for {stageName} has not been recorded.",
                    "Open project details",
                    correctionUrl));
            }
            else
            {
                checks.Add(new ApprovalCheckVm(
                    ApprovalCheckState.Passed,
                    "Required stage information",
                    "All mandatory information needed for completion is available."));
            }
        }

        if (requestedStatus is StageStatus.InProgress or StageStatus.Completed)
        {
            var requiredPredecessors = workflow.RequiredPredecessorClosure(request.StageCode);
            var unresolvedWithoutRequest = new List<string>();

            foreach (var predecessorCode in requiredPredecessors)
            {
                if (projectStages.TryGetValue(predecessorCode, out var predecessor)
                    && predecessor.Status is StageStatus.Completed or StageStatus.Skipped)
                {
                    continue;
                }

                if (latestPendingByStage.TryGetValue(predecessorCode, out var predecessorRequest)
                    && Enum.TryParse<StageStatus>(predecessorRequest.RequestedStatus, true, out var predecessorRequestedStatus)
                    && predecessorRequestedStatus is StageStatus.Completed or StageStatus.Skipped)
                {
                    waitingOn.Add(predecessorRequest.Id);
                }
                else
                {
                    unresolvedWithoutRequest.Add(StageCodes.DisplayNameOf(workflow.WorkflowVersion, predecessorCode));
                }
            }

            if (unresolvedWithoutRequest.Count > 0)
            {
                checks.Add(new ApprovalCheckVm(
                    ApprovalCheckState.Blocked,
                    "Workflow predecessors",
                    $"No pending resolution exists for: {string.Join(", ", unresolvedWithoutRequest)}.",
                    "Open project timeline",
                    $"/Projects/Overview/{project.Id}#timeline"));
            }
            else if (waitingOn.Count > 0)
            {
                checks.Add(new ApprovalCheckVm(
                    ApprovalCheckState.Waiting,
                    "Workflow predecessors",
                    $"Approve {waitingOn.Count} earlier stage request{(waitingOn.Count == 1 ? string.Empty : "s")} first."));
            }
            else
            {
                checks.Add(new ApprovalCheckVm(
                    ApprovalCheckState.Passed,
                    "Workflow predecessors",
                    "All required predecessor stages are complete or skipped."));
            }

            var proposedStartDate = requestedStatus == StageStatus.InProgress
                ? request.RequestedDate
                : request.RequestedStartDate ?? stage.ActualStart;
            var startBoundary = ResolveImmediatePredecessorBoundary(
                workflow,
                request.StageCode,
                projectStages,
                latestPendingByStage);

            var chronologyDate = proposedStartDate
                ?? (requestedStatus == StageStatus.Completed ? request.RequestedDate : null);
            if (chronologyDate.HasValue && startBoundary.EarliestStartDate.HasValue)
            {
                var chronologyLabel = proposedStartDate.HasValue
                    ? "proposed start date"
                    : "proposed completion date";

                if (chronologyDate.Value < startBoundary.EarliestStartDate.Value)
                {
                    checks.Add(new ApprovalCheckVm(
                        ApprovalCheckState.Blocked,
                        "Lifecycle chronology",
                        $"The {chronologyLabel} must be on or after {startBoundary.EarliestStartDate:dd MMM yyyy}, " +
                        $"the day after {StageCodes.DisplayNameOf(workflow.WorkflowVersion, startBoundary.SourceStageCode!)} completion."));
                }
                else
                {
                    checks.Add(new ApprovalCheckVm(
                        ApprovalCheckState.Passed,
                        "Lifecycle chronology",
                        $"The {chronologyLabel} follows the effective predecessor stage."));
                }
            }
        }

        var blocked = checks.FirstOrDefault(check => check.State == ApprovalCheckState.Blocked);
        if (blocked is not null)
        {
            return Build(
                ApprovalReadiness.Blocked,
                blocked.Detail ?? blocked.Label,
                correctionUrl);
        }

        var waiting = checks.FirstOrDefault(check => check.State == ApprovalCheckState.Waiting);
        if (waiting is not null)
        {
            return Build(
                ApprovalReadiness.Waiting,
                waiting.Detail ?? waiting.Label,
                correctionUrl);
        }

        return Build(
            ApprovalReadiness.Ready,
            "Ready for decision.",
            correctionUrl);

        StageApprovalAssessment Build(
            ApprovalReadiness readiness,
            string message,
            string? correctionUrl)
            => new(
                request.Id,
                request.StageCode,
                stageName,
                workflow.WorkflowVersion,
                workflowOrder,
                revisionNumber,
                true,
                readiness,
                message,
                checks,
                waitingOn.Distinct().ToArray(),
                correctionUrl);
    }

    private static StartBoundary ResolveImmediatePredecessorBoundary(
        ProjectStageWorkflowSnapshot workflow,
        string stageCode,
        IReadOnlyDictionary<string, StageSnapshot> projectStages,
        IReadOnlyDictionary<string, RequestSnapshot> latestPendingByStage)
    {
        var targetIndex = workflow.OrderOf(stageCode);
        if (targetIndex <= 0 || targetIndex == int.MaxValue)
        {
            return StartBoundary.None;
        }

        for (var index = targetIndex - 1; index >= 0; index--)
        {
            var predecessorCode = workflow.Stages[index].Code;
            projectStages.TryGetValue(predecessorCode, out var official);
            latestPendingByStage.TryGetValue(predecessorCode, out var pending);

            StageStatus effectiveStatus;
            DateOnly? effectiveCompletion;
            if (pending is not null
                && Enum.TryParse<StageStatus>(pending.RequestedStatus, true, out var pendingStatus))
            {
                effectiveStatus = pendingStatus;
                effectiveCompletion = pendingStatus == StageStatus.Completed
                    ? pending.RequestedDate
                    : official?.CompletedOn;
            }
            else
            {
                effectiveStatus = official?.Status ?? StageStatus.NotStarted;
                effectiveCompletion = official?.CompletedOn;
            }

            if (effectiveStatus == StageStatus.Skipped)
            {
                continue;
            }

            return effectiveStatus == StageStatus.Completed && effectiveCompletion.HasValue
                ? new StartBoundary(effectiveCompletion.Value.AddDays(1), predecessorCode)
                : StartBoundary.None;
        }

        return StartBoundary.None;
    }

    private sealed record StartBoundary(DateOnly? EarliestStartDate, string? SourceStageCode)
    {
        public static StartBoundary None { get; } = new(null, null);
    }

    private static string BuildFactsUrl(int projectId, string stageCode)
        => stageCode switch
        {
            StageCodes.IPA or StageCodes.SOW or StageCodes.AON or StageCodes.BM or StageCodes.COB or StageCodes.PNC or StageCodes.SO
                => $"/Projects/Overview/{projectId}#procurement",
            _ => $"/Projects/Overview/{projectId}#timeline"
        };

    private static string FormatStatus(StageStatus status)
        => status switch
        {
            StageStatus.NotStarted => "not started",
            StageStatus.InProgress => "in progress",
            _ => status.ToString().ToLowerInvariant()
        };

    private sealed record ProjectSnapshot(int Id, string Name, string? WorkflowVersion);

    private sealed record StageSnapshot(
        int ProjectId,
        string StageCode,
        StageStatus Status,
        DateOnly? ActualStart,
        DateOnly? CompletedOn);

    private sealed record RequestSnapshot(
        int Id,
        int ProjectId,
        string StageCode,
        string RequestedStatus,
        DateOnly? RequestedDate,
        DateOnly? RequestedStartDate,
        DateTimeOffset RequestedOn,
        string DecisionStatus);
}

public sealed record StageApprovalAssessment(
    int RequestId,
    string StageCode,
    string StageName,
    string WorkflowVersion,
    int WorkflowOrder,
    int RevisionNumber,
    bool IsLatestRevision,
    ApprovalReadiness Readiness,
    string Message,
    IReadOnlyList<ApprovalCheckVm> Checks,
    IReadOnlyList<int> WaitingOnRequestIds,
    string? CorrectionUrl)
{
    public bool CanApprove => Readiness == ApprovalReadiness.Ready;

    public static StageApprovalAssessment Stale(
        int requestId,
        string stageCode,
        string stageName,
        string workflowVersion,
        int workflowOrder,
        string message,
        int revisionNumber = 1)
        => new(
            requestId,
            stageCode,
            stageName,
            workflowVersion,
            workflowOrder,
            revisionNumber,
            true,
            ApprovalReadiness.Stale,
            message,
            new[] { new ApprovalCheckVm(ApprovalCheckState.Blocked, "Request state", message) },
            Array.Empty<int>(),
            null);
}
