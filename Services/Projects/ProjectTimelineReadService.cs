using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Stages;
using ProjectManagement.Services.Arpp;
using ProjectManagement.Utilities;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Services.Projects;

public sealed class ProjectTimelineReadService
{
    private const string PendingDecisionStatus = "Pending";
    private const string SupersededDecisionStatus = "Superseded";
    private static readonly TimeZoneInfo IndiaTimeZone = TimeZoneHelper.GetIst();

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IWorkflowStageMetadataProvider _workflowStageMetadataProvider;
    private readonly IProjectStageWorkflowPolicy _workflowPolicy;
    private readonly IArppIpaStageAuthorityService _arppIpaStageAuthority;

    public ProjectTimelineReadService(
        ApplicationDbContext db,
        IClock clock,
        IWorkflowStageMetadataProvider workflowStageMetadataProvider,
        IProjectStageWorkflowPolicy workflowPolicy,
        IArppIpaStageAuthorityService? arppIpaStageAuthority = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _workflowStageMetadataProvider = workflowStageMetadataProvider
            ?? throw new ArgumentNullException(nameof(workflowStageMetadataProvider));
        _workflowPolicy = workflowPolicy ?? throw new ArgumentNullException(nameof(workflowPolicy));
        _arppIpaStageAuthority = arppIpaStageAuthority ?? new ArppIpaStageAuthorityService(db);
    }

    public Task<bool> HasBackfillAsync(int projectId, CancellationToken ct = default)
        => _db.ProjectStages.AnyAsync(s =>
            s.ProjectId == projectId &&
            s.Status == StageStatus.Completed &&
            !s.CompletedOn.HasValue, ct);

    public async Task<ActualsEditorVm> GetActualsEditorAsync(int projectId, CancellationToken ct = default)
    {
        var workflow = await _workflowPolicy.GetAsync(projectId, ct);

        var stages = await _db.ProjectStages
            .AsNoTracking()
            .Where(s => s.ProjectId == projectId)
            .ToListAsync(ct);

        var pending = await _db.StageChangeRequests
            .AsNoTracking()
            .Where(r => r.ProjectId == projectId && r.DecisionStatus == PendingDecisionStatus)
            .ToListAsync(ct);

        var pendingLookup = pending
            .GroupBy(r => r.StageCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, _ => true, StringComparer.OrdinalIgnoreCase);

        var workflowStages = workflow.Stages;
        var stageLookup = stages
            .Where(s => !string.IsNullOrWhiteSpace(s.StageCode))
            .ToDictionary(s => s.StageCode!, StringComparer.OrdinalIgnoreCase);

        var ipaAuthority = await _arppIpaStageAuthority.ResolveAsync(projectId, ct);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.UtcNow, IndiaTimeZone).Date);
        var rows = workflowStages
            .Select(stage =>
            {
                stageLookup.TryGetValue(stage.Code, out var projectStage);
                var hasPending = pendingLookup.ContainsKey(stage.Code);
                var status = projectStage?.Status ?? StageStatus.NotStarted;
                var isEditable = status is StageStatus.InProgress or StageStatus.Completed;
                var startBoundary = StageDateSuggestionResolver.Resolve(workflow, stages, stage.Code);

                return new ActualsEditorRowVm
                {
                    StageCode = stage.Code,
                    StageName = stage.Name,
                    Status = status,
                    IsEditable = isEditable,
                    ActualStart = projectStage?.ActualStart,
                    CompletedOn = projectStage?.CompletedOn,
                    EarliestAllowedStartDate = startBoundary.EarliestAllowedStartDate,
                    StartBoundarySourceName = startBoundary.SourceStageName,
                    IsAutoCompleted = projectStage?.IsAutoCompleted ?? false,
                    RequiresBackfill = projectStage is not null &&
                        status == StageStatus.Completed &&
                        !projectStage.CompletedOn.HasValue,
                    HasPendingDecision = hasPending,
                    IsArppManaged = ipaAuthority is not null &&
                        string.Equals(stage.Code, StageCodes.IPA, StringComparison.OrdinalIgnoreCase),
                    ArppSourceLabel = ipaAuthority is not null &&
                        string.Equals(stage.Code, StageCodes.IPA, StringComparison.OrdinalIgnoreCase)
                            ? $"{ipaAuthority.DocumentLabel} · {ipaAuthority.IssueDate:dd MMM yyyy}"
                            : null,
                    ArppCompletionDate = ipaAuthority is not null &&
                        string.Equals(stage.Code, StageCodes.IPA, StringComparison.OrdinalIgnoreCase)
                            ? ipaAuthority.IssueDate
                            : null
                };
            })
            .ToArray();

        return new ActualsEditorVm
        {
            ProjectId = projectId,
            Today = today,
            Rows = rows
        };
    }

    public async Task<TimelineVm> GetAsync(int projectId, CancellationToken ct = default)
    {
        var workflow = await _workflowPolicy.GetAsync(projectId, ct);
        var workflowVersion = workflow.WorkflowVersion;

        var rows = await _db.ProjectStages
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .ToListAsync(ct);

        var requestHistory = await _db.StageChangeRequests
            .AsNoTracking()
            .Where(r =>
                r.ProjectId == projectId
                && (r.DecisionStatus == PendingDecisionStatus
                    || r.DecisionStatus == SupersededDecisionStatus))
            .ToListAsync(ct);

        var pendingRequests = requestHistory
            .Where(r => r.DecisionStatus == PendingDecisionStatus)
            .ToList();

        // SECTION: Workflow metadata
        var workflowStages = workflow.Stages;
        var stageNameLookup = workflowStages
            .ToDictionary(stage => stage.Code, stage => stage.Name, StringComparer.OrdinalIgnoreCase);

        var rowLookup = rows
            .Where(x => !string.IsNullOrWhiteSpace(x.StageCode))
            .ToDictionary(x => x.StageCode!, StringComparer.OrdinalIgnoreCase);

        var requestedByIds = pendingRequests
            .Select(r => r.RequestedByUserId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Dictionary<string, string> userLookup;
        if (requestedByIds.Length == 0)
        {
            userLookup = new Dictionary<string, string>(StringComparer.Ordinal);
        }
        else
        {
            var requestedUsers = await _db.Users
                .AsNoTracking()
                .Where(u => requestedByIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FullName, u.UserName, u.Email })
                .ToListAsync(ct);

            userLookup = requestedUsers.ToDictionary(
                u => u.Id,
                u => !string.IsNullOrWhiteSpace(u.FullName)
                    ? u.FullName!
                    : !string.IsNullOrWhiteSpace(u.UserName)
                        ? u.UserName!
                        : u.Email ?? u.Id,
                StringComparer.Ordinal);
        }

        // SECTION: Pending request normalization
        var latestPendingByStage = pendingRequests
            .GroupBy(r => r.StageCode, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(r => r.RequestedOn).First())
            .OrderByDescending(r => r.RequestedOn)
            .ToList();

        var proposedStartLookup = latestPendingByStage
            .Where(r => string.Equals(r.RequestedStatus, StageStatus.Completed.ToString(), StringComparison.OrdinalIgnoreCase))
            .Select(r => new
            {
                r.StageCode,
                StartDate = requestHistory
                    .Where(history =>
                        string.Equals(history.StageCode, r.StageCode, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(history.RequestedStatus, StageStatus.InProgress.ToString(), StringComparison.OrdinalIgnoreCase)
                        && history.RequestedDate.HasValue
                        && history.RequestedOn < r.RequestedOn
                        && history.DecisionStatus == SupersededDecisionStatus)
                    .OrderByDescending(history => history.RequestedOn)
                    .ThenByDescending(history => history.Id)
                    .Select(history => history.RequestedDate)
                    .FirstOrDefault()
            })
            .Where(item => item.StartDate.HasValue)
            .ToDictionary(item => item.StageCode, item => item.StartDate, StringComparer.OrdinalIgnoreCase);

        var pendingRequestVms = latestPendingByStage
            .Select(r =>
            {
                rowLookup.TryGetValue(r.StageCode, out var stageRow);
                var requestedBy = userLookup.TryGetValue(r.RequestedByUserId, out var name)
                    ? name
                    : r.RequestedByUserId;

                return new TimelineStageRequestVm
                {
                    RequestId = r.Id,
                    StageCode = r.StageCode,
                    StageName = !string.IsNullOrWhiteSpace(r.StageCode) && stageNameLookup.TryGetValue(r.StageCode, out var stageName)
                        ? stageName
                        : _workflowStageMetadataProvider.GetDisplayName(workflowVersion, r.StageCode),
                    CurrentStatus = stageRow?.Status ?? StageStatus.NotStarted,
                    RequestedStatus = r.RequestedStatus,
                    RequestedDate = r.RequestedDate,
                    ProposedStartDate = r.RequestedStartDate
                        ?? stageRow?.ActualStart
                        ?? (proposedStartLookup.TryGetValue(r.StageCode, out var proposedStart)
                            ? proposedStart
                            : string.Equals(r.RequestedStatus, StageStatus.InProgress.ToString(), StringComparison.OrdinalIgnoreCase)
                                ? r.RequestedDate
                                : null),
                    Note = r.Note,
                    RequestedBy = requestedBy,
                    RequestedOn = r.RequestedOn
                };
            })
            .ToList();

        var pendingLookup = latestPendingByStage
            .ToDictionary(r => r.StageCode, StringComparer.OrdinalIgnoreCase);

        var ipaAuthority = await _arppIpaStageAuthority.ResolveAsync(projectId, ct);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.UtcNow, IndiaTimeZone).Date);

        var items = new List<TimelineItemVm>();
        var index = 0;
        foreach (var stage in workflowStages)
        {
            var code = stage.Code;

            rowLookup.TryGetValue(code, out var r);
            pendingLookup.TryGetValue(code, out var pendingRequest);

            var plannedStart = r?.PlannedStart;
            var actualStart = r?.ActualStart;
            var plannedEnd = r?.PlannedDue;
            var actualEnd = r?.CompletedOn;
            var status = r?.Status ?? StageStatus.NotStarted;
            var isArppManaged = ipaAuthority is not null &&
                                string.Equals(code, StageCodes.IPA, StringComparison.OrdinalIgnoreCase);

            var startSuggestion = StageDateSuggestionResolver.Resolve(workflow, rows, code);

            DateOnly? effectiveActualStart = actualStart;
            var isActualStartInferred = false;
            if (!isArppManaged
                && status == StageStatus.Completed
                && !effectiveActualStart.HasValue
                && actualEnd.HasValue
                && startSuggestion.SuggestedStartDate.HasValue)
            {
                var inferred = startSuggestion.SuggestedStartDate.Value;
                effectiveActualStart = inferred > actualEnd.Value ? actualEnd.Value : inferred;
                isActualStartInferred = true;
            }

            int? startVarianceDays = null;
            if (plannedStart.HasValue && actualStart.HasValue)
            {
                startVarianceDays = actualStart.Value.DayNumber - plannedStart.Value.DayNumber;
            }

            int? finishVarianceDays = null;
            if (plannedEnd.HasValue && actualEnd.HasValue)
            {
                finishVarianceDays = actualEnd.Value.DayNumber - plannedEnd.Value.DayNumber;
            }

            items.Add(new TimelineItemVm
            {
                Code = code,
                Name = stage.Name,
                Status = status,
                PlannedStart = plannedStart,
                PlannedEnd = plannedEnd,
                ActualStart = actualStart,
                EffectiveActualStart = effectiveActualStart,
                IsActualStartInferred = isActualStartInferred,
                CompletedOn = actualEnd,
                SuggestedStartDate = startSuggestion.SuggestedStartDate,
                EarliestAllowedStartDate = startSuggestion.EarliestAllowedStartDate,
                SuggestedStartSourceCode = startSuggestion.SourceStageCode,
                SuggestedStartSourceName = startSuggestion.SourceStageName,
                IsAutoCompleted = r?.IsAutoCompleted ?? false,
                AutoCompletedFromCode = r?.AutoCompletedFromCode,
                RequiresBackfill = status == StageStatus.Completed && !actualEnd.HasValue,
                IsArppManaged = isArppManaged,
                ArppSourceIssueId = isArppManaged ? ipaAuthority!.IssueId : null,
                ArppSourceDocumentLabel = isArppManaged ? ipaAuthority!.DocumentLabel : null,
                ArppSourceIssueName = isArppManaged ? ipaAuthority!.IssueName : null,
                ArppSourceIssueDate = isArppManaged ? ipaAuthority!.IssueDate : null,
                ArppSourceSerialNumber = isArppManaged ? ipaAuthority!.SerialNumber : null,
                ArppSourcePppNumber = isArppManaged ? ipaAuthority!.PppNumber : null,
                SortOrder = index++,
                Today = today,
                HasPendingRequest = pendingRequest is not null,
                PendingStatus = pendingRequest?.RequestedStatus,
                PendingDate = pendingRequest?.RequestedDate,
                PendingStartDate = pendingRequest is null
                    ? null
                    : pendingRequest.RequestedStartDate
                        ?? actualStart
                        ?? (string.Equals(pendingRequest.RequestedStatus, StageStatus.InProgress.ToString(), StringComparison.OrdinalIgnoreCase)
                            ? pendingRequest.RequestedDate
                            : proposedStartLookup.TryGetValue(code, out var pendingStart)
                                ? pendingStart
                                : null),
                PendingNote = pendingRequest?.Note,
                PendingRequestedBy = pendingRequest is not null && userLookup.TryGetValue(pendingRequest.RequestedByUserId, out var pendingRequester)
                    ? pendingRequester
                    : pendingRequest?.RequestedByUserId,
                PendingRequestedOn = pendingRequest?.RequestedOn,
                StartVarianceDays = startVarianceDays,
                FinishVarianceDays = finishVarianceDays,
                PendingRequestId = pendingRequest?.Id
            });

        }

        var completed = items.Count(i => i.Status == StageStatus.Completed);

        var openPlan = await _db.PlanVersions
            .AsNoTracking()
            .Where(p => p.ProjectId == projectId &&
                        (p.Status == PlanVersionStatus.PendingApproval || p.Status == PlanVersionStatus.Draft))
            .OrderByDescending(p => p.Status)
            .ThenByDescending(p => p.VersionNo)
            .Select(p => new { p.Status })
            .FirstOrDefaultAsync(ct);

        var approvalInfo = await _db.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new
            {
                p.PlanApprovedAt,
                ApprovedBy = p.PlanApprovedByUser != null
                    ? (p.PlanApprovedByUser.FullName ?? p.PlanApprovedByUser.UserName ?? p.PlanApprovedByUser.Email)
                    : null
            })
            .FirstOrDefaultAsync(ct);

        return new TimelineVm
        {
            ProjectId = projectId,
            TotalStages = items.Count,
            CompletedCount = completed,
            Items = items,
            PendingRequests = pendingRequestVms,
            PlanPendingApproval = openPlan?.Status == PlanVersionStatus.PendingApproval,
            HasDraft = openPlan is not null,
            LatestApprovalAt = approvalInfo?.PlanApprovedAt,
            LatestApprovalBy = approvalInfo?.ApprovedBy
        };
    }
}
