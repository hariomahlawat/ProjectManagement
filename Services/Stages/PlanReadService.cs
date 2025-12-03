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

    public async Task<PlanEditorVm> GetAsync(int projectId, string? currentUserId, CancellationToken cancellationToken = default)
    {
        var workflowVersion = await _db.Projects
            .AsNoTracking()
            .Where(project => project.Id == projectId)
            .Select(project => project.WorkflowVersion)
            .FirstOrDefaultAsync(cancellationToken) ?? PlanConstants.DefaultStageTemplateVersion;

        var stageTemplates = await _db.StageTemplates
            .AsNoTracking()
            .Where(template => template.Version == workflowVersion)
            .ToListAsync(cancellationToken);

        var stages = await _db.ProjectStages
            .Where(stage => stage.ProjectId == projectId)
            .ToListAsync(cancellationToken);

        // SECTION: Optional stage aggregation
        var optionalStages = new HashSet<string>(
            stageTemplates.Where(template => template.Optional).Select(template => template.Code),
            StringComparer.OrdinalIgnoreCase);

        var skippedStageCodes = stages
            .Where(stage => stage.Status == StageStatus.Skipped)
            .Select(stage => stage.StageCode);

        optionalStages.UnionWith(skippedStageCodes);

        var scheduleSettings = await _db.ProjectScheduleSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.ProjectId == projectId, cancellationToken);

        var durationRows = await _db.ProjectPlanDurations
            .Where(d => d.ProjectId == projectId)
            .OrderBy(d => d.SortOrder)
            .ToListAsync(cancellationToken);

        var planCandidates = await _db.PlanVersions
            .AsNoTracking()
            .Include(p => p.StagePlans)
            .Include(p => p.SubmittedByUser)
            .Include(p => p.RejectedByUser)
            .Include(p => p.ApprovalLogs).ThenInclude(log => log.PerformedByUser)
            .Where(p => p.ProjectId == projectId &&
                        (p.Status == PlanVersionStatus.PendingApproval || p.Status == PlanVersionStatus.Draft))
            .ToListAsync(cancellationToken);

        var pendingPlan = planCandidates
            .Where(p => p.Status == PlanVersionStatus.PendingApproval)
            .OrderByDescending(p => p.SubmittedOn)
            .ThenByDescending(p => p.VersionNo)
            .FirstOrDefault();

        PlanVersion? myDraft = null;
        if (!string.IsNullOrWhiteSpace(currentUserId))
        {
            myDraft = planCandidates
                .Where(p => p.Status == PlanVersionStatus.Draft &&
                            string.Equals(p.OwnerUserId, currentUserId, StringComparison.Ordinal))
                .OrderByDescending(p => p.VersionNo)
                .FirstOrDefault();
        }

        var draftPlan = myDraft;
        if (draftPlan is null && pendingPlan is not null && !string.IsNullOrWhiteSpace(currentUserId) &&
            string.Equals(pendingPlan.OwnerUserId ?? pendingPlan.SubmittedByUserId, currentUserId, StringComparison.Ordinal))
        {
            draftPlan = pendingPlan;
        }

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
                DurationDays = duration?.DurationDays,
                PreviewStart = draftStage?.PlannedStart,
                PreviewDue = draftStage?.PlannedDue,
                IsOptional = optionalStages.Contains(code)
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
                DurationDays = duration?.DurationDays,
                PreviewStart = draftStage?.PlannedStart,
                PreviewDue = draftStage?.PlannedDue,
                IsOptional = optionalStages.Contains(code)
            });
        }

        var activeMode = scheduleSettings?.AnchorStart is not null
            ? PlanEditorModes.Durations
            : PlanEditorModes.Exact;

        var pendingSubmittedBy = DisplayName(pendingPlan?.SubmittedByUser);

        var lastSavedOn = draftPlan?.SubmittedOn ?? draftPlan?.CreatedOn;
        if (lastSavedOn is null && pendingPlan is not null)
        {
            lastSavedOn = pendingPlan.SubmittedOn ?? pendingPlan.CreatedOn;
        }

        var pendingOwnerId = pendingPlan?.OwnerUserId ?? pendingPlan?.SubmittedByUserId;
        var isPendingMine = pendingPlan is not null && !string.IsNullOrWhiteSpace(currentUserId) &&
            string.Equals(pendingOwnerId, currentUserId, StringComparison.Ordinal);

        var state = new PlanEditorStateVm
        {
            HasDraft = myDraft is not null,
            IsLocked = isPendingMine,
            Status = draftPlan?.Status ?? pendingPlan?.Status,
            VersionNo = draftPlan?.VersionNo ?? pendingPlan?.VersionNo,
            CreatedOn = draftPlan?.CreatedOn ?? pendingPlan?.CreatedOn,
            SubmittedOn = draftPlan?.SubmittedOn ?? pendingPlan?.SubmittedOn,
            SubmittedBy = DisplayName(draftPlan?.SubmittedByUser) ?? pendingSubmittedBy,
            RejectedOn = draftPlan?.RejectedOn,
            RejectedBy = DisplayName(draftPlan?.RejectedByUser),
            RejectionNote = draftPlan?.RejectionNote,
            HasMyDraft = myDraft is not null,
            HasPendingSubmission = pendingPlan is not null,
            PendingOwnedByCurrentUser = isPendingMine,
            PendingSubmittedOn = pendingPlan?.SubmittedOn,
            PendingSubmittedBy = pendingSubmittedBy,
            CanSubmit = pendingPlan is null || isPendingMine,
            SubmissionBlockedReason = pendingPlan is not null && !isPendingMine
                ? "Another plan is already awaiting approval."
                : null,
            PncApplicable = draftPlan?.PncApplicable ?? pendingPlan?.PncApplicable ?? true,
            LastSavedOn = lastSavedOn,
            ApprovalHistory = BuildHistory(pendingPlan ?? draftPlan)
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

    private static IReadOnlyList<PlanApprovalHistoryVm> BuildHistory(PlanVersion? plan)
    {
        if (plan?.ApprovalLogs is not { Count: > 0 })
        {
            return Array.Empty<PlanApprovalHistoryVm>();
        }

        return plan.ApprovalLogs
            .OrderByDescending(log => log.PerformedOn)
            .Take(5)
            .Select(log => new PlanApprovalHistoryVm
            {
                Action = log.Action,
                Note = string.IsNullOrWhiteSpace(log.Note) ? null : log.Note.Trim(),
                PerformedOn = log.PerformedOn,
                PerformedBy = DisplayName(log.PerformedByUser)
            })
            .ToList();
    }
}
