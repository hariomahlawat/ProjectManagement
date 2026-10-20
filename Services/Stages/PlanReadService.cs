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
using ProjectManagement.Services.Plans;

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
        var baseline = await PublishedPlanningBaseline.LoadAsync(_db, projectId, cancellationToken);
        var workflowVersion = baseline.WorkflowVersion ?? PlanConstants.DefaultStageTemplateVersion;

        var workflowStages = ProcurementWorkflow.StageDefinitionsFor(workflowVersion);

        var stageTemplates = await _db.StageTemplates
            .AsNoTracking()
            .Where(template => template.Version == workflowVersion)
            .ToListAsync(cancellationToken);

        var stages = baseline.Stages;

        // SECTION: Optional stage aggregation
        var optionalStages = new HashSet<string>(
            stageTemplates.Where(template => template.Optional).Select(template => template.Code),
            StringComparer.OrdinalIgnoreCase);

        var skippedStageCodes = stages
            .Where(stage => stage.Status == StageStatus.Skipped)
            .Select(stage => stage.StageCode);

        optionalStages.UnionWith(skippedStageCodes);

        var scheduleSettings = baseline.Settings;
        var durationRows = baseline.Durations;

        // Evaluate actionability in the SAME statement that loads candidates.
        // A separate pending-ID read would permit another submission to change
        // revision identity before the editor reads status and ownership.
        var actionablePending = PlanRevisionConsistency.ActionablePending(_db);
        var planCandidates = await _db.PlanVersions
            .AsNoTracking()
            .AsSingleQuery() // Keep status, actor metadata and child rows in one statement snapshot.
            .Include(p => p.StagePlans)
            .Include(p => p.SubmittedByUser)
            .Include(p => p.RejectedByUser)
            .Include(p => p.ApprovalLogs).ThenInclude(log => log.PerformedByUser)
            .Where(p => p.ProjectId == projectId &&
                        (p.Status == PlanVersionStatus.Draft ||
                         (p.Status == PlanVersionStatus.Rejected && currentUserId != null &&
                          (p.OwnerUserId == currentUserId || p.SubmittedByUserId == currentUserId)) ||
                         (p.Status == PlanVersionStatus.PendingApproval && actionablePending.Any(pending => pending.Id == p.Id))))
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
                            string.Equals(p.OwnerUserId, currentUserId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => p.VersionNo)
                .FirstOrDefault();
        }

        var myPending = string.IsNullOrWhiteSpace(currentUserId) ? null : planCandidates
            .Where(plan => plan.Status == PlanVersionStatus.PendingApproval &&
                string.Equals(plan.OwnerUserId ?? plan.SubmittedByUserId, currentUserId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(plan => plan.SubmittedOn)
            .ThenByDescending(plan => plan.VersionNo)
            .FirstOrDefault();
        var draftPlan = myDraft ?? myPending;
        var latestRejected = string.IsNullOrWhiteSpace(currentUserId) ? null : planCandidates
            .Where(plan => plan.Status == PlanVersionStatus.Rejected &&
                           string.Equals(plan.OwnerUserId ?? plan.SubmittedByUserId, currentUserId,
                                         StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(plan => plan.VersionNo).FirstOrDefault();
        // A rejection is the user's current outcome only until a later revision
        // is submitted or approved. After that it is history, and the editor
        // must not keep saying "Returned for updates" (discovery C1.6).
        if (latestRejected is not null)
        {
            var rejectedVersionNo = latestRejected.VersionNo;
            var overtaken = await _db.PlanVersions
                .AsNoTracking()
                .AnyAsync(plan => plan.ProjectId == projectId &&
                                  plan.VersionNo > rejectedVersionNo &&
                                  (plan.SubmittedOn != null || plan.Status == PlanVersionStatus.Approved),
                    cancellationToken);
            if (overtaken)
            {
                latestRejected = null;
            }
        }
        var ownsInputs = draftPlan?.PlanningInputVersion >= PlanningInputVersions.Current;
        var presentsLegacyDraftInputs = draftPlan is
            { Status: PlanVersionStatus.Draft, PlanningInputVersion: PlanningInputVersions.Legacy };
        var presentsDurationInputs = ownsInputs || presentsLegacyDraftInputs;
        // A legacy draft's pre-cutover Duration inputs remain private revision
        // candidates until the owner explicitly selects a mode and saves.
        // Pending revisions retain their historical applicability decision.
        var pncApplicable = draftPlan is { Status: PlanVersionStatus.PendingApproval }
            ? draftPlan.PncApplicable
            : draftPlan is not null && ownsInputs ? draftPlan.PncApplicable
            : presentsLegacyDraftInputs ? baseline.PncApplicable && draftPlan!.PncApplicable
            : baseline.PncApplicable;


        var exactVm = new PlanEditVm { ProjectId = projectId };
        var durationVm = new PlanDurationVm
        {
            ProjectId = projectId,
            AnchorStart = presentsDurationInputs ? draftPlan!.AnchorDate : scheduleSettings?.AnchorStart,
            IncludeWeekends = presentsDurationInputs ? !draftPlan!.SkipWeekends : scheduleSettings?.IncludeWeekends ?? false,
            SkipHolidays = ownsInputs ? draftPlan!.SkipHolidays : scheduleSettings?.SkipHolidays ?? true,
            NextStageStartPolicy = presentsDurationInputs
                ? draftPlan!.TransitionRule == PlanTransitionRule.SameDay ? NextStageStartPolicies.SameDay : NextStageStartPolicies.NextWorkingDay
                : scheduleSettings?.NextStageStartPolicy ?? NextStageStartPolicies.NextWorkingDay
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

        var knownCodes = new HashSet<string>(workflowStages.Select(item => item.Code), StringComparer.OrdinalIgnoreCase);
        var extraCodes = new List<(string Code, int Sort)>();
        var seenExtras = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddExtra(string? code)
        {
            if (string.IsNullOrWhiteSpace(code) || knownCodes.Contains(code) || !seenExtras.Add(code))
            {
                return;
            }

            var sort = draftMap.TryGetValue(code, out var proposalStage)
                ? proposalStage.SortOrder
                : draftPlan is null && durationMap.TryGetValue(code, out var duration)
                    ? duration.SortOrder
                    : stageMap.TryGetValue(code, out var stage) ? stage.SortOrder : workflowStages.Length + extraCodes.Count;
            extraCodes.Add((code, sort));
        }

        foreach (var definition in workflowStages)
        {
            var code = definition.Code;
            stageMap.TryGetValue(code, out var stage);
            durationMap.TryGetValue(code, out var duration);
            draftMap.TryGetValue(code, out var draftStage);
            var status = stage?.Status ?? StageStatus.NotStarted;

            exactVm.Rows.Add(new PlanEditVm.PlanEditRow
            {
                Code = code,
                Name = definition.Name,
                PlannedStart = draftStage is not null ? draftStage.PlannedStart : stage?.PlannedStart,
                PlannedDue = draftStage is not null ? draftStage.PlannedDue : stage?.PlannedDue,
                Status = status
            });

            durationVm.Rows.Add(new PlanDurationRowVm
            {
                Code = code,
                Name = definition.Name,
                DurationDays = presentsDurationInputs
                    ? draftStage is { DurationDays: > 0 } ? draftStage.DurationDays : null
                    : duration?.DurationDays,
                PreviewStart = draftStage is not null ? draftStage.PlannedStart : stage?.PlannedStart,
                PreviewDue = draftStage is not null ? draftStage.PlannedDue : stage?.PlannedDue,
                IsOptional = optionalStages.Contains(code),
                Status = status
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

        if (draftPlan is null)
        {
            foreach (var code in durationMap.Keys) AddExtra(code);
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
                PlannedStart = draftStage is not null ? draftStage.PlannedStart : stage?.PlannedStart,
                PlannedDue = draftStage is not null ? draftStage.PlannedDue : stage?.PlannedDue,
                Status = stage?.Status ?? StageStatus.NotStarted
            });

            durationVm.Rows.Add(new PlanDurationRowVm
            {
                Code = code,
                Name = StageCodes.DisplayNameOf(code),
                DurationDays = presentsDurationInputs
                    ? draftStage is { DurationDays: > 0 } ? draftStage.DurationDays : null
                    : duration?.DurationDays,
                PreviewStart = draftStage is not null ? draftStage.PlannedStart : stage?.PlannedStart,
                PreviewDue = draftStage is not null ? draftStage.PlannedDue : stage?.PlannedDue,
                IsOptional = optionalStages.Contains(code),
                Status = stage?.Status ?? StageStatus.NotStarted
            });
        }

        if (!pncApplicable)
        {
            foreach (var row in exactVm.Rows.Where(row => string.Equals(row.Code, StageCodes.PNC, StringComparison.OrdinalIgnoreCase)))
            {
                row.PlannedStart = null;
                row.PlannedDue = null;
            }
            foreach (var row in durationVm.Rows.Where(row => string.Equals(row.Code, StageCodes.PNC, StringComparison.OrdinalIgnoreCase)))
            {
                row.DurationDays = null;
                row.PreviewStart = null;
                row.PreviewDue = null;
            }
        }

        var activeModeAnchor = presentsLegacyDraftInputs ? scheduleSettings?.AnchorStart : durationVm.AnchorStart;
        var activeMode = activeModeAnchor is not null
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
            string.Equals(pendingOwnerId, currentUserId, StringComparison.OrdinalIgnoreCase);

        var state = new PlanEditorStateVm
        {
            HasDraft = myDraft is not null,
            EditToken = PlanEditFingerprint.Compute(draftPlan),
            // Reviewed content remains immutable, but PO successor edits are allowed.
            IsLocked = false,
            Status = draftPlan?.Status ?? pendingPlan?.Status,
            VersionNo = draftPlan?.VersionNo ?? pendingPlan?.VersionNo,
            CreatedOn = draftPlan?.CreatedOn ?? pendingPlan?.CreatedOn,
            SubmittedOn = draftPlan?.SubmittedOn ?? pendingPlan?.SubmittedOn,
            SubmittedBy = DisplayName(draftPlan?.SubmittedByUser) ?? pendingSubmittedBy,
            RejectedOn = draftPlan?.RejectedOn ?? latestRejected?.RejectedOn,
            RejectedBy = DisplayName(draftPlan?.RejectedByUser ?? latestRejected?.RejectedByUser),
            RejectionNote = draftPlan?.RejectionNote ?? latestRejected?.RejectionNote,
            HasMyDraft = myDraft is not null,
            CanDiscardDraft = myDraft is not null &&
                !myDraft.RejectedOn.HasValue && !myDraft.SubmittedOn.HasValue &&
                !myDraft.ApprovalLogs.Any(),
            HasPendingSubmission = pendingPlan is not null,
            PendingPlanVersionId = pendingPlan?.Id,
            PendingOwnedByCurrentUser = isPendingMine,
            PendingSubmittedOn = pendingPlan?.SubmittedOn,
            PendingSubmittedBy = pendingSubmittedBy,
            CanSubmit = pendingPlan is null || isPendingMine,
            SubmissionBlockedReason = pendingPlan is not null && !isPendingMine
                ? "Another plan is already awaiting approval."
                : null,
            PncApplicable = pncApplicable,
            LastSavedOn = lastSavedOn,
            ApprovalHistory = BuildHistory(pendingPlan ?? draftPlan ?? latestRejected)
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
