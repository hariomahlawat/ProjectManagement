using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
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

    public PlanDraftService(
        ApplicationDbContext db,
        IClock clock,
        ILogger<PlanDraftService> logger,
        IAuditService audit,
        IUserContext userContext)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
        _audit = audit;
        _userContext = userContext;
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

        var hasPending = await _db.PlanVersions
            .AsNoTracking()
            .AnyAsync(p => p.ProjectId == projectId &&
                           p.Status == PlanVersionStatus.PendingApproval &&
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

        // One statement captures one published version even when an approval or
        // direct publication commits between this service's other queries.
        var baseline = await _db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .AsSingleQuery()
            .Select(p => new
            {
                p.WorkflowVersion,
                p.PncApplicable,
                Settings = _db.ProjectScheduleSettings
                    .Where(settings => settings.ProjectId == p.Id).FirstOrDefault(),
                Durations = _db.ProjectPlanDurations
                    .Where(duration => duration.ProjectId == p.Id)
                    .OrderBy(duration => duration.SortOrder).ThenBy(duration => duration.Id).ToList(),
                Stages = _db.ProjectStages
                    .Where(stage => stage.ProjectId == p.Id)
                    .OrderBy(stage => stage.SortOrder).ThenBy(stage => stage.Id).ToList()
            })
            .SingleAsync(cancellationToken);
        var workflowVersion = baseline.WorkflowVersion ?? PlanConstants.StageTemplateVersionV1;
        var templates = await _db.StageTemplates.AsNoTracking()
            .Where(t => t.Version == workflowVersion)
            .OrderBy(t => t.Sequence).ThenBy(t => t.Id)
            .ToListAsync(cancellationToken);
        var settings = baseline.Settings;
        var durations = baseline.Durations;
        var stages = baseline.Stages;
        var durationMap = durations.ToDictionary(d => d.StageCode, StringComparer.OrdinalIgnoreCase);
        var stageMap = stages.ToDictionary(s => s.StageCode, StringComparer.OrdinalIgnoreCase);
        var templateOrder = templates.ToDictionary(t => t.Code, t => t.Sequence, StringComparer.OrdinalIgnoreCase);
        var workflowOrder = ProcurementWorkflow.BuildOrderLookup(workflowVersion);
        var stageCodes = (templates.Count > 0
                ? templates.Select(t => t.Code)
                : ProcurementWorkflow.StageCodesFor(workflowVersion))
            .Concat(durations.Select(d => d.StageCode))
            .Concat(stages.Select(s => s.StageCode))
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var plan = new PlanVersion
        {
            ProjectId = projectId,
            VersionNo = latestVersion + 1,
            Title = PlanVersion.ProjectTimelineTitle,
            Status = PlanVersionStatus.Draft,
            CreatedByUserId = userId,
            OwnerUserId = userId,
            CreatedOn = _clock.UtcNow,
            AnchorStageCode = PlanConstants.DefaultAnchorStageCode,
            AnchorDate = settings?.AnchorStart,
            SkipWeekends = !(settings?.IncludeWeekends ?? false),
            SkipHolidays = settings?.SkipHolidays ?? true,
            TransitionRule = settings?.NextStageStartPolicy == NextStageStartPolicies.SameDay
                ? PlanTransitionRule.SameDay : PlanTransitionRule.NextWorkingDay,
            PncApplicable = baseline.PncApplicable
        };

        foreach (var code in stageCodes)
        {
            durationMap.TryGetValue(code, out var duration);
            stageMap.TryGetValue(code, out var stage);
            plan.StagePlans.Add(new StagePlan
            {
                StageCode = code,
                DurationDays = Math.Max(0, duration?.DurationDays ?? 0),
                PlannedStart = stage?.PlannedStart,
                PlannedDue = stage?.PlannedDue,
                SortOrder = duration?.SortOrder ?? stage?.SortOrder
                    ?? (templateOrder.TryGetValue(code, out var sequence) ? sequence
                        : workflowOrder.TryGetValue(code, out var order) ? order : 0)
            });
        }

        _db.PlanVersions.Add(plan);
        await _db.SaveChangesAsync(cancellationToken);

        return plan;
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
