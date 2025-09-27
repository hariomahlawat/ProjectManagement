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
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Models.Stages;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Services.Stages;

public sealed class PlanReadService
{
    private readonly ApplicationDbContext _db;

    public PlanReadService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PlanEditorVm> GetAsync(int projectId, CancellationToken cancellationToken = default)
    {
        var stages = await _db.ProjectStages
            .Where(stage => stage.ProjectId == projectId)
            .ToListAsync(cancellationToken);

        var scheduleSettings = await _db.ProjectScheduleSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.ProjectId == projectId, cancellationToken);

        var durationRows = await _db.ProjectPlanDurations
            .Where(d => d.ProjectId == projectId)
            .OrderBy(d => d.SortOrder)
            .ToListAsync(cancellationToken);

        var draftPlan = await _db.PlanVersions
            .AsNoTracking()
            .Include(p => p.StagePlans)
            .Include(p => p.SubmittedByUser)
            .Include(p => p.RejectedByUser)
            .Where(p => p.ProjectId == projectId &&
                        (p.Status == PlanVersionStatus.PendingApproval || p.Status == PlanVersionStatus.Draft))
            .OrderByDescending(p => p.Status)
            .ThenByDescending(p => p.VersionNo)
            .FirstOrDefaultAsync(cancellationToken);

        var exactVm = new PlanEditVm { ProjectId = projectId };
        var durationVm = new PlanDurationVm
        {
            ProjectId = projectId,
            AnchorStart = scheduleSettings?.AnchorStart,
            IncludeWeekends = scheduleSettings?.IncludeWeekends ?? false,
            SkipHolidays = scheduleSettings?.SkipHolidays ?? true,
            NextStageStartPolicy = scheduleSettings?.NextStageStartPolicy ?? NextStageStartPolicies.NextWorkingDay
        };

        var stageMap = stages
            .Where(stage => !string.IsNullOrWhiteSpace(stage.StageCode))
            .ToDictionary(stage => stage.StageCode, StringComparer.OrdinalIgnoreCase);

        var durationMap = durationRows
            .Where(d => !string.IsNullOrWhiteSpace(d.StageCode))
            .ToDictionary(d => d.StageCode!, StringComparer.OrdinalIgnoreCase);

        var draftMap = draftPlan?.StagePlans
            .Where(sp => !string.IsNullOrWhiteSpace(sp.StageCode))
            .ToDictionary(sp => sp.StageCode!, sp => sp, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, StagePlan>(StringComparer.OrdinalIgnoreCase);

        var knownCodes = new HashSet<string>(StageCodes.All, StringComparer.OrdinalIgnoreCase);
        var extraCodes = new List<(string Code, int Sort)>();
        var seenExtras = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddExtra(string? code)
        {
            if (string.IsNullOrWhiteSpace(code) || knownCodes.Contains(code) || !seenExtras.Add(code))
            {
                return;
            }

            var sort = durationMap.TryGetValue(code, out var duration)
                ? duration.SortOrder
                : StageCodes.All.Length + extraCodes.Count;
            extraCodes.Add((code, sort));
        }

        foreach (var code in StageCodes.All)
        {
            stageMap.TryGetValue(code, out var stage);
            durationMap.TryGetValue(code, out var duration);
            draftMap.TryGetValue(code, out var draftStage);

            exactVm.Rows.Add(new PlanEditVm.PlanEditRow
            {
                Code = code,
                Name = StageCodes.DisplayNameOf(code),
                PlannedStart = draftStage?.PlannedStart ?? stage?.PlannedStart,
                PlannedDue = draftStage?.PlannedDue ?? stage?.PlannedDue,
            });

            durationVm.Rows.Add(new PlanDurationRowVm
            {
                Code = code,
                Name = StageCodes.DisplayNameOf(code),
                DurationDays = duration?.DurationDays
            });
        }

        foreach (var stage in stages)
        {
            AddExtra(stage.StageCode);
        }

        foreach (var code in draftMap.Keys)
        {
            AddExtra(code);
        }

        foreach (var (code, _) in extraCodes.OrderBy(s => s.Sort).ThenBy(s => s.Code, StringComparer.OrdinalIgnoreCase))
        {
            stageMap.TryGetValue(code, out var stage);
            draftMap.TryGetValue(code, out var draftStage);
            var duration = durationMap.TryGetValue(code, out var row) ? row : null;

            exactVm.Rows.Add(new PlanEditVm.PlanEditRow
            {
                Code = code,
                Name = StageCodes.DisplayNameOf(code),
                PlannedStart = draftStage?.PlannedStart ?? stage?.PlannedStart,
                PlannedDue = draftStage?.PlannedDue ?? stage?.PlannedDue,
            });

            durationVm.Rows.Add(new PlanDurationRowVm
            {
                Code = code,
                Name = StageCodes.DisplayNameOf(code),
                DurationDays = duration?.DurationDays
            });
        }

        var activeMode = scheduleSettings?.AnchorStart is not null
            ? PlanEditorModes.Durations
            : PlanEditorModes.Exact;

        var state = new PlanEditorStateVm
        {
            HasDraft = draftPlan is not null,
            IsLocked = draftPlan?.Status == PlanVersionStatus.PendingApproval,
            Status = draftPlan?.Status,
            VersionNo = draftPlan?.VersionNo,
            CreatedOn = draftPlan?.CreatedOn,
            SubmittedOn = draftPlan?.SubmittedOn,
            SubmittedBy = DisplayName(draftPlan?.SubmittedByUser),
            RejectedOn = draftPlan?.RejectedOn,
            RejectedBy = DisplayName(draftPlan?.RejectedByUser),
            RejectionNote = draftPlan?.RejectionNote
        };

        return new PlanEditorVm
        {
            Exact = exactVm,
            Durations = durationVm,
            ActiveMode = activeMode,
            State = state
        };
    }

    private static string? DisplayName(ApplicationUser? user)
    {
        if (user is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(user.FullName))
        {
            return user.FullName;
        }

        return user.UserName ?? user.Email;
    }
}
