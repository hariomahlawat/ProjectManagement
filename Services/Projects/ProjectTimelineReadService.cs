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
using ProjectManagement.Utilities;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Services.Projects;

public sealed class ProjectTimelineReadService
{
    private const string PendingDecisionStatus = "Pending";
    private static readonly TimeZoneInfo IndiaTimeZone = TimeZoneHelper.GetIst();

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IWorkflowStageMetadataProvider _workflowStageMetadataProvider;

    public ProjectTimelineReadService(
        ApplicationDbContext db,
        IClock clock,
        IWorkflowStageMetadataProvider workflowStageMetadataProvider)
    {
        _db = db;
        _clock = clock;
        _workflowStageMetadataProvider = workflowStageMetadataProvider;
    }

    public Task<bool> HasBackfillAsync(int projectId, CancellationToken ct = default)
        => _db.ProjectStages.AnyAsync(s => s.ProjectId == projectId && s.RequiresBackfill, ct);

    public async Task<TimelineVm> GetAsync(int projectId, CancellationToken ct = default)
    {
        var workflowVersion = await _db.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => p.WorkflowVersion)
            .SingleOrDefaultAsync(ct);

        var rows = await _db.ProjectStages
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .ToListAsync(ct);

        var pendingRequests = await _db.StageChangeRequests
            .AsNoTracking()
            .Where(r => r.ProjectId == projectId && r.DecisionStatus == PendingDecisionStatus)
            .ToListAsync(ct);

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

        var pendingRequestVms = pendingRequests
            .OrderByDescending(r => r.RequestedOn)
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
                    StageName = _workflowStageMetadataProvider.GetDisplayName(workflowVersion, r.StageCode),
                    CurrentStatus = stageRow?.Status ?? StageStatus.NotStarted,
                    RequestedStatus = r.RequestedStatus,
                    RequestedDate = r.RequestedDate,
                    Note = r.Note,
                    RequestedBy = requestedBy,
                    RequestedOn = r.RequestedOn
                };
            })
            .ToList();

        var pendingLookup = pendingRequests
            .GroupBy(r => r.StageCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(r => r.RequestedOn).First(),
                StringComparer.OrdinalIgnoreCase);

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.UtcNow, IndiaTimeZone).Date);

        var items = new List<TimelineItemVm>();
        var index = 0;
        foreach (var code in ProcurementWorkflow.StageCodesFor(workflowVersion))
        {
            rowLookup.TryGetValue(code, out var r);
            pendingLookup.TryGetValue(code, out var pendingRequest);

            var plannedStart = r?.PlannedStart;
            var actualStart = r?.ActualStart;
            var plannedEnd = r?.PlannedDue;
            var actualEnd = r?.CompletedOn;

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
                Name = _workflowStageMetadataProvider.GetDisplayName(workflowVersion, code),
                Status = r?.Status ?? StageStatus.NotStarted,
                PlannedStart = plannedStart,
                PlannedEnd = plannedEnd,
                ActualStart = actualStart,
                CompletedOn = actualEnd,
                IsAutoCompleted = r?.IsAutoCompleted ?? false,
                AutoCompletedFromCode = r?.AutoCompletedFromCode,
                RequiresBackfill = r?.RequiresBackfill ?? false,
                SortOrder = index++,
                Today = today,
                HasPendingRequest = pendingRequest is not null,
                PendingStatus = pendingRequest?.RequestedStatus,
                PendingDate = pendingRequest?.RequestedDate,
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
