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

    public ProjectTimelineReadService(ApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public Task<bool> HasBackfillAsync(int projectId, CancellationToken ct = default)
        => _db.ProjectStages.AnyAsync(s => s.ProjectId == projectId && s.RequiresBackfill, ct);

    public async Task<TimelineVm> GetAsync(int projectId, CancellationToken ct = default)
    {
        var rows = await _db.ProjectStages
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .ToListAsync(ct);

        var pendingRequests = await _db.StageChangeRequests
            .AsNoTracking()
            .Where(r => r.ProjectId == projectId && r.DecisionStatus == PendingDecisionStatus)
            .ToListAsync(ct);

        var pendingLookup = pendingRequests
            .GroupBy(r => r.StageCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(r => r.RequestedOn).First(),
                StringComparer.OrdinalIgnoreCase);

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.UtcNow, IndiaTimeZone).Date);

        var items = new List<TimelineItemVm>();
        var index = 0;
        foreach (var code in StageCodes.All)
        {
            var r = rows.FirstOrDefault(x => x.StageCode == code);
            pendingLookup.TryGetValue(code, out var pendingRequest);

            var plannedStart = r?.PlannedStart;
            var actualStart = r?.ActualStart;
            var plannedEnd = r?.PlannedDue;
            var actualEnd = r?.CompletedOn;

            int? startVarianceDays = null;
            if (plannedStart.HasValue && actualStart.HasValue)
            {
                var diff = actualStart.Value.DayNumber - plannedStart.Value.DayNumber;
                if (diff != 0)
                {
                    startVarianceDays = diff;
                }
            }

            int? finishVarianceDays = null;
            if (plannedEnd.HasValue && actualEnd.HasValue)
            {
                var diff = actualEnd.Value.DayNumber - plannedEnd.Value.DayNumber;
                if (diff != 0)
                {
                    finishVarianceDays = diff;
                }
            }

            items.Add(new TimelineItemVm
            {
                Code = code,
                Name = StageCodes.DisplayNameOf(code),
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
                FinishVarianceDays = finishVarianceDays
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
            PlanPendingApproval = openPlan?.Status == PlanVersionStatus.PendingApproval,
            HasDraft = openPlan is not null,
            LatestApprovalAt = approvalInfo?.PlanApprovedAt,
            LatestApprovalBy = approvalInfo?.ApprovedBy
        };
    }
}
