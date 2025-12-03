using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Models.Plans;
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

        return await CreateOrGetDraftAsync(projectId, userId, cancellationToken);
    }

    public async Task<PlanVersion> CreateOrGetDraftAsync(
        int projectId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new InvalidOperationException("Missing user id");
        }

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

        var orphanDraft = await _db.PlanVersions
            .Include(p => p.StagePlans)
            .Where(p => p.ProjectId == projectId &&
                        p.Status == PlanVersionStatus.Draft &&
                        p.OwnerUserId == null)
            .OrderByDescending(p => p.VersionNo)
            .FirstOrDefaultAsync(cancellationToken);

        if (orphanDraft is not null)
        {
            orphanDraft.OwnerUserId = userId;
            await _db.SaveChangesAsync(cancellationToken);
            return orphanDraft;
        }

        var hasPending = await _db.PlanVersions
            .AsNoTracking()
            .AnyAsync(p => p.ProjectId == projectId &&
                           p.Status == PlanVersionStatus.PendingApproval &&
                           p.OwnerUserId == userId,
                cancellationToken);

        if (hasPending)
        {
            throw new PlanDraftLockedException("Your previous submission is awaiting approval and cannot be edited.");
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
            .FirstOrDefaultAsync(p => p.ProjectId == projectId && p.OwnerUserId == userId, cancellationToken);

        if (plan is null)
        {
            return PlanDraftDeleteResult.NotFound;
        }

        if (plan.Status != PlanVersionStatus.Draft)
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

    public async Task SavePlanAsync(
        int projectId,
        IEnumerable<StagePlanInput> stageInputs,
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new InvalidOperationException("A valid user id is required to save the plan.");
        }

        // SECTION: Resolve or create draft
        var draft = await CreateOrGetDraftAsync(projectId, userId, cancellationToken);

        var stageMap = draft.StagePlans
            .Where(stage => !string.IsNullOrWhiteSpace(stage.StageCode))
            .ToDictionary(stage => stage.StageCode!, stage => stage, StringComparer.OrdinalIgnoreCase);

        // SECTION: Apply provided stage inputs only
        foreach (var input in stageInputs)
        {
            if (string.IsNullOrWhiteSpace(input.StageCode))
            {
                continue;
            }

            if (!stageMap.TryGetValue(input.StageCode, out var stagePlan))
            {
                stagePlan = new StagePlan
                {
                    PlanVersionId = draft.Id,
                    StageCode = input.StageCode
                };

                draft.StagePlans.Add(stagePlan);
                stageMap[input.StageCode] = stagePlan;
            }

            stagePlan.PlannedStart = input.PlannedStart;
            stagePlan.PlannedDue = input.PlannedDue;
            stagePlan.DurationDays = CalculateDuration(input.PlannedStart, input.PlannedDue);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static int CalculateDuration(DateOnly? start, DateOnly? due)
    {
        if (start is DateOnly startDate && due is DateOnly dueDate && dueDate >= startDate)
        {
            return dueDate.DayNumber - startDate.DayNumber + 1;
        }

        return 0;
    }

    private async Task<PlanVersion> CreateDraftAsync(int projectId, string userId, CancellationToken cancellationToken)
    {
        var latestVersion = await _db.PlanVersions
            .Where(p => p.ProjectId == projectId)
            .Select(p => (int?)p.VersionNo)
            .MaxAsync(cancellationToken) ?? 0;

        // SECTION: Workflow Resolution
        var workflowVersion = await _db.Projects
            .Where(p => p.Id == projectId)
            .Select(p => p.WorkflowVersion)
            .SingleAsync(cancellationToken);
        workflowVersion ??= PlanConstants.StageTemplateVersionV1;

        var stageCodes = await _db.StageTemplates
            .AsNoTracking()
            .Where(t => t.Version == workflowVersion)
            .OrderBy(t => t.Sequence)
            .Select(t => t.Code)
            .ToListAsync(cancellationToken);

        if (stageCodes.Count == 0)
        {
            _logger.LogWarning("No stage templates found for version {Version}. Creating an empty draft for project {ProjectId}.",
                workflowVersion, projectId);
        }

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
            AnchorDate = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime),
            SkipWeekends = true,
            TransitionRule = PlanTransitionRule.NextWorkingDay,
            PncApplicable = true
        };

        foreach (var code in stageCodes)
        {
            plan.StagePlans.Add(new StagePlan
            {
                StageCode = code
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
