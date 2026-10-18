using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Scheduling;
using ProjectManagement.Services.Stages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectManagement.Services.Plans;

public class PlanDraftService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<PlanDraftService> _logger;
    private readonly IAuditService _audit;
    private readonly IUserContext _userContext;
    private readonly IOfficeCalendarService? _officeCalendar;

    public PlanDraftService(
        ApplicationDbContext db,
        IClock clock,
        ILogger<PlanDraftService> logger,
        IAuditService audit,
        IUserContext userContext,
        IOfficeCalendarService? officeCalendar = null)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
        _audit = audit;
        _userContext = userContext;
        _officeCalendar = officeCalendar;
    }

    public async Task<PlanVersion> CreateOrGetDraftAsync(int projectId, CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();

        var myDraft = await _db.PlanVersions
            .Include(p => p.StagePlans)
            .Where(p => p.ProjectId == projectId &&
                        p.Status == PlanVersionStatus.Draft &&
                        p.OwnerUserId == userId)
            .OrderByDescending(p => p.VersionNo)
            .FirstOrDefaultAsync(cancellationToken);

        if (myDraft is not null)
        {
            return myDraft;
        }

        var hasPending = await PlanRevisionConsistency.ActionablePending(_db)
            .AsNoTracking()
            .AnyAsync(p => p.ProjectId == projectId &&
                           (p.OwnerUserId == userId || (p.OwnerUserId == null && p.CreatedByUserId == userId)),
                cancellationToken);

        if (hasPending)
        {
            throw new PlanDraftLockedException("Your previous submission is awaiting approval and cannot be edited.");
        }

        // Only a creator may recover their own pre-ownership draft. An ownerless
        // row is not a published baseline and cannot be claimed by another actor.
        var orphanDraft = await _db.PlanVersions
            .Include(p => p.StagePlans)
            .Where(p => p.ProjectId == projectId &&
                        p.Status == PlanVersionStatus.Draft &&
                        p.OwnerUserId == null && p.CreatedByUserId == userId)
            .OrderByDescending(p => p.VersionNo)
            .FirstOrDefaultAsync(cancellationToken);

        if (orphanDraft is not null)
        {
            orphanDraft.OwnerUserId = userId;
            await _db.SaveChangesAsync(cancellationToken);
            return orphanDraft;
        }

        return await CreateDraftAsync(projectId, userId, cancellationToken);
    }

    public async Task<PlanVersion?> GetMyDraftAsync(int projectId, CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserIdOrDefault();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        return await _db.PlanVersions
            .Include(p => p.StagePlans)
            .Where(p => p.ProjectId == projectId &&
                        p.Status == PlanVersionStatus.Draft &&
                        p.OwnerUserId == userId)
            .OrderByDescending(p => p.VersionNo)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<PlanDraftDeleteResult> DeleteDraftAsync(int projectId, CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();

        var plan = await _db.PlanVersions
            .Include(p => p.StagePlans)
            .Where(p => p.ProjectId == projectId && p.OwnerUserId == userId && p.Status == PlanVersionStatus.Draft)
            .OrderByDescending(p => p.VersionNo)
            .FirstOrDefaultAsync(cancellationToken);

        if (plan is null)
        {
            return await _db.PlanVersions.AsNoTracking().AnyAsync(
                p => p.ProjectId == projectId && p.OwnerUserId == userId, cancellationToken)
                ? PlanDraftDeleteResult.Conflict
                : PlanDraftDeleteResult.NotFound;
        }

        // A rejected draft is still an authoritative submitted revision. Removing
        // it would cascade its approval logs and could reactivate an older pending
        // version that this revision superseded. Only never-submitted drafts may
        // be discarded; retaining the version also retains its decision history.
        if (plan.SubmittedOn.HasValue || plan.RejectedOn.HasValue ||
            await _db.PlanApprovalLogs.AsNoTracking().AnyAsync(
                log => log.PlanVersionId == plan.Id, cancellationToken))
        {
            return PlanDraftDeleteResult.Conflict;
        }

        if (plan.StagePlans.Count > 0)
        {
            _db.StagePlans.RemoveRange(plan.StagePlans);
        }

        _db.PlanVersions.Remove(plan);

        await _db.SaveChangesAsync(cancellationToken);

        var deletedAt = _clock.UtcNow;
        await Audit.Events.DraftDeleted(projectId, plan.Id, userId, deletedAt).WriteAsync(_audit);

        return PlanDraftDeleteResult.Success;
    }

    private async Task<PlanVersion> CreateDraftAsync(int projectId, string userId, CancellationToken cancellationToken)
    {
        var latestVersion = await _db.PlanVersions
            .Where(p => p.ProjectId == projectId)
            .Select(p => (int?)p.VersionNo)
            .MaxAsync(cancellationToken) ?? 0;

        var plan = new PlanVersion
        {
            ProjectId = projectId,
            VersionNo = latestVersion + 1,
            Title = PlanVersion.ProjectTimelineTitle,
            Status = PlanVersionStatus.Draft,
            CreatedByUserId = userId,
            OwnerUserId = userId,
            CreatedOn = _clock.UtcNow,
            PlanningInputVersion = PlanningInputVersions.Current
        };
        await InitializeInputsAsync(plan, preserveExistingDates: false, cancellationToken);

        _db.PlanVersions.Add(plan);
        await _db.SaveChangesAsync(cancellationToken);

        return plan;
    }

    public async Task UpgradeLegacyDraftForExactAsync(
        PlanVersion draft,
        CancellationToken cancellationToken = default)
    {
        ValidateEditableDraftOwner(draft);
        if (draft.PlanningInputVersion != PlanningInputVersions.Legacy)
        {
            return;
        }

        await InitializeInputsAsync(draft, preserveExistingDates: true, cancellationToken);

        var datedRows = draft.StagePlans
            .Where(row => row.PlannedStart.HasValue || row.PlannedDue.HasValue)
            .ToArray();
        var dates = datedRows
            .SelectMany(row => new[] { row.PlannedStart, row.PlannedDue })
            .Where(date => date.HasValue)
            .Select(date => date!.Value)
            .ToArray();
        IReadOnlyCollection<DateOnly> holidays = Array.Empty<DateOnly>();
        if (dates.Length > 0)
        {
            var start = dates.Min().AddDays(-7);
            var endExclusive = dates.Max().AddDays(8);
            holidays = _officeCalendar is not null
                ? (await _officeCalendar.GetNonWorkingDatesAsync(start, endExclusive, cancellationToken)).ToArray()
                : await _db.Holidays.AsNoTracking()
                    .Where(holiday => holiday.Date >= start && holiday.Date < endExclusive &&
                        (holiday.Type == HolidayType.Gazetted || holiday.IsObservedAsOfficeHoliday))
                    .Select(holiday => holiday.Date)
                    .Distinct()
                    .ToListAsync(cancellationToken);
        }

        var calendar = new WorkingCalendar(holidays, includeWeekends: !draft.SkipWeekends, draft.SkipHolidays);
        foreach (var row in draft.StagePlans)
        {
            row.DurationDays = row.PlannedStart.HasValue && row.PlannedDue.HasValue
                ? calendar.CountWorkingDaysInclusive(row.PlannedStart.Value, row.PlannedDue.Value)
                : 0;
        }

        draft.PlanningInputVersion = PlanningInputVersions.Current;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpgradeLegacyDraftForDurationAsync(
        PlanVersion draft,
        CancellationToken cancellationToken = default)
    {
        ValidateEditableDraftOwner(draft);
        if (draft.PlanningInputVersion != PlanningInputVersions.Legacy)
        {
            return;
        }

        var baseline = await PublishedPlanningBaseline.LoadAsync(_db, draft.ProjectId, cancellationToken);
        var workflowVersion = baseline.WorkflowVersion ?? PlanConstants.StageTemplateVersionV1;
        var templateOrder = await _db.StageTemplates.AsNoTracking()
            .Where(template => template.Version == workflowVersion)
            .ToDictionaryAsync(template => template.Code, template => template.Sequence,
                StringComparer.OrdinalIgnoreCase, cancellationToken);
        var workflowOrder = ProcurementWorkflow.BuildOrderLookup(workflowVersion);

        draft.AnchorStageCode = PlanConstants.DefaultAnchorStageCode;
        // Legacy revisions predate proposal-owned holiday policy. The migration
        // supplied only a compatibility value, so establish it from trusted
        // published policy or the safe default without replacing owned inputs.
        draft.SkipHolidays = baseline.Settings?.SkipHolidays ?? true;
        foreach (var row in draft.StagePlans)
        {
            if (templateOrder.TryGetValue(row.StageCode, out var templateSequence))
            {
                row.SortOrder = templateSequence;
            }
            else if (workflowOrder.TryGetValue(row.StageCode, out var workflowSequence))
            {
                row.SortOrder = workflowSequence;
            }
        }

        if (!baseline.PncApplicable || !draft.PncApplicable)
        {
            draft.PncApplicable = false;
            foreach (var pnc in draft.StagePlans.Where(row =>
                         string.Equals(row.StageCode, StageCodes.PNC, StringComparison.OrdinalIgnoreCase)))
            {
                pnc.DurationDays = 0;
                pnc.PlannedStart = null;
                pnc.PlannedDue = null;
            }
        }

        draft.PlanningInputVersion = PlanningInputVersions.Current;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private void ValidateEditableDraftOwner(PlanVersion draft)
    {
        var userId = GetCurrentUserId();
        if (draft.Status != PlanVersionStatus.Draft ||
            !string.Equals(draft.OwnerUserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only the owner can upgrade an editable draft.");
        }
    }

    private async Task InitializeInputsAsync(PlanVersion plan, bool preserveExistingDates, CancellationToken cancellationToken)
    {
        var baseline = await PublishedPlanningBaseline.LoadAsync(_db, plan.ProjectId, cancellationToken);
        var workflowVersion = baseline.WorkflowVersion ?? PlanConstants.StageTemplateVersionV1;
        var templates = await _db.StageTemplates.AsNoTracking()
            .Where(template => template.Version == workflowVersion)
            .OrderBy(template => template.Sequence).ThenBy(template => template.Id)
            .ToListAsync(cancellationToken);
        var durationMap = baseline.Durations.ToDictionary(duration => duration.StageCode, StringComparer.OrdinalIgnoreCase);
        var stageMap = baseline.Stages.ToDictionary(stage => stage.StageCode, StringComparer.OrdinalIgnoreCase);
        var templateOrder = templates.ToDictionary(template => template.Code, template => template.Sequence, StringComparer.OrdinalIgnoreCase);
        var workflowOrder = ProcurementWorkflow.BuildOrderLookup(workflowVersion);
        var proposalMap = plan.StagePlans.ToDictionary(stage => stage.StageCode, StringComparer.OrdinalIgnoreCase);
        var stageCodes = (templates.Count > 0
                ? templates.Select(template => template.Code)
                : ProcurementWorkflow.StageCodesFor(workflowVersion))
            .Concat(baseline.Durations.Select(duration => duration.StageCode))
            .Concat(baseline.Stages.Select(stage => stage.StageCode))
            .Concat(proposalMap.Keys)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        plan.AnchorStageCode = PlanConstants.DefaultAnchorStageCode;
        plan.AnchorDate = baseline.Settings?.AnchorStart;
        plan.SkipWeekends = !(baseline.Settings?.IncludeWeekends ?? false);
        plan.SkipHolidays = baseline.Settings?.SkipHolidays ?? true;
        plan.TransitionRule = baseline.Settings?.NextStageStartPolicy == NextStageStartPolicies.SameDay
            ? PlanTransitionRule.SameDay : PlanTransitionRule.NextWorkingDay;
        plan.PncApplicable = baseline.PncApplicable;

        foreach (var code in stageCodes)
        {
            durationMap.TryGetValue(code, out var duration);
            stageMap.TryGetValue(code, out var official);
            if (!proposalMap.TryGetValue(code, out var row))
            {
                row = new StagePlan { StageCode = code, PlannedStart = official?.PlannedStart, PlannedDue = official?.PlannedDue };
                plan.StagePlans.Add(row);
            }
            else if (!preserveExistingDates)
            {
                row.PlannedStart = official?.PlannedStart;
                row.PlannedDue = official?.PlannedDue;
            }

            row.DurationDays = Math.Max(0, duration?.DurationDays ?? 0);
            row.SortOrder = templateOrder.TryGetValue(code, out var sequence) ? sequence
                : workflowOrder.TryGetValue(code, out var order) ? order
                : duration?.SortOrder ?? official?.SortOrder ?? (row.Id != 0 ? row.Id : duration?.Id ?? official?.Id ?? 0);

            // Live applicability overrides retained compatibility duration/date inputs.
            // Preserve the official row as evidence; only normalize the new proposal.
            if (!baseline.PncApplicable && string.Equals(code, StageCodes.PNC, StringComparison.OrdinalIgnoreCase))
            {
                row.DurationDays = 0;
                row.PlannedStart = null;
                row.PlannedDue = null;
            }
        }
    }

    private string GetCurrentUserId()
    {
        var userId = _userContext.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new InvalidOperationException("Missing user id");
        }

        return userId;
    }

    private string? GetCurrentUserIdOrDefault() => _userContext.UserId;
}

public enum PlanDraftDeleteResult
{
    Success,
    NotFound,
    Conflict
}
