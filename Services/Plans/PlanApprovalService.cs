using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Helpers;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Stages;
using ProjectManagement.Infrastructure;

namespace ProjectManagement.Services.Plans;

public class PlanApprovalService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<PlanApprovalService> _logger;
    private readonly PlanSnapshotService _snapshots;
    private readonly IPlanNotificationService _notifications;

    public PlanApprovalService(
        ApplicationDbContext db,
        IClock clock,
        ILogger<PlanApprovalService> logger,
        PlanSnapshotService snapshots,
        IPlanNotificationService notifications)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
        _snapshots = snapshots;
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
    }

    public async Task SubmitAsync(int projectId, string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("A valid user identifier is required to submit a plan for approval.", nameof(userId));
        }

        var plan = await _db.PlanVersions
            .Include(p => p.StagePlans)
            .Include(p => p.Project)
            .FirstOrDefaultAsync(p => p.ProjectId == projectId &&
                                      p.Status == PlanVersionStatus.Draft &&
                                      p.OwnerUserId == userId,
                cancellationToken);

        if (plan == null)
        {
            throw new InvalidOperationException("No draft plan was found to submit for approval.");
        }

        var alreadyPending = await _db.PlanVersions
            .AnyAsync(p => p.ProjectId == projectId && p.Status == PlanVersionStatus.PendingApproval, cancellationToken);

        if (alreadyPending)
        {
            throw new DomainException("Another submission is already pending approval.");
        }

        var errors = await ValidateStagePlansAsync(plan, cancellationToken);
        if (errors.Count > 0)
        {
            throw new PlanApprovalValidationException(errors);
        }

        plan.Status = PlanVersionStatus.PendingApproval;
        plan.OwnerUserId ??= userId;
        plan.SubmittedByUserId = userId;
        plan.SubmittedOn = _clock.UtcNow;
        plan.ApprovedByUserId = null;
        plan.ApprovedOn = null;
        plan.RejectedByUserId = null;
        plan.RejectedOn = null;
        plan.RejectionNote = null;

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Plan version {PlanVersionId} for project {ProjectId} submitted for approval by {UserId}.", plan.Id, projectId, userId);

        if (plan.Project is not null)
        {
            await _notifications.NotifyPlanSubmittedAsync(plan, plan.Project, userId, cancellationToken);
        }
    }

    public Task SubmitForApprovalAsync(int projectId, string userId, CancellationToken cancellationToken = default)
        => SubmitAsync(projectId, userId, cancellationToken);

    public Task<bool> ApproveLatestDraftAsync(int projectId, string hodUserId, CancellationToken cancellationToken = default)
        => ApproveLatestDraftInternalAsync(projectId, hodUserId, allowSelfApproval: false, cancellationToken);

    public Task<bool> ApproveLatestDraftAsHodAsync(int projectId, string hodUserId, CancellationToken cancellationToken = default)
        => ApproveLatestDraftInternalAsync(projectId, hodUserId, allowSelfApproval: true, cancellationToken);

    private async Task<bool> ApproveLatestDraftInternalAsync(
        int projectId,
        string hodUserId,
        bool allowSelfApproval,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(hodUserId))
        {
            throw new ArgumentException("A valid approver identifier is required.", nameof(hodUserId));
        }

        var plan = await _db.PlanVersions
            .Include(p => p.StagePlans)
            .Include(p => p.Project)
            .Where(p => p.ProjectId == projectId && p.Status == PlanVersionStatus.PendingApproval)
            .OrderByDescending(p => p.SubmittedOn)
            .ThenByDescending(p => p.VersionNo)
            .FirstOrDefaultAsync(cancellationToken);

        if (plan == null)
        {
            return false;
        }

        var project = await _db.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
            ?? throw new InvalidOperationException("Project not found.");

        var submitterId = plan.OwnerUserId ?? plan.SubmittedByUserId;
        var isSelfApproval = !string.IsNullOrEmpty(submitterId) && string.Equals(submitterId, hodUserId, StringComparison.Ordinal);

        if (isSelfApproval && !allowSelfApproval)
        {
            throw new ForbiddenException("The submitter cannot approve their own plan.");
        }

        if (isSelfApproval && allowSelfApproval && !string.Equals(project.HodUserId, hodUserId, StringComparison.Ordinal))
        {
            throw new ForbiddenException("Only the project's HoD can self-approve a plan update.");
        }

        var now = _clock.UtcNow;

        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);

        var currentStages = await _db.ProjectStages
            .Where(ps => ps.ProjectId == projectId)
            .ToListAsync(cancellationToken);

        var stageLookup = currentStages
            .Where(s => !string.IsNullOrWhiteSpace(s.StageCode))
            .ToDictionary(s => s.StageCode!, s => s, StringComparer.OrdinalIgnoreCase);

        foreach (var stagePlan in plan.StagePlans.Where(sp => !string.IsNullOrWhiteSpace(sp.StageCode)))
        {
            var code = stagePlan.StageCode!;
            if (!stageLookup.TryGetValue(code, out var stage))
            {
                stage = new ProjectStage
                {
                    ProjectId = projectId,
                    StageCode = code,
                    SortOrder = ResolveSortOrder(code),
                    Status = StageStatus.NotStarted
                };
                _db.ProjectStages.Add(stage);
                stageLookup[code] = stage;
            }

            stage.PlannedStart = stagePlan.PlannedStart;
            stage.PlannedDue = stagePlan.PlannedDue;
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _snapshots.CreateSnapshotAsync(projectId, hodUserId, cancellationToken);

        project.ActivePlanVersionNo = plan.VersionNo;
        project.PlanApprovedAt = now;
        project.PlanApprovedByUserId = hodUserId;

        plan.Status = PlanVersionStatus.Approved;
        plan.ApprovedByUserId = hodUserId;
        plan.ApprovedOn = now;
        plan.RejectedByUserId = null;
        plan.RejectedOn = null;
        plan.RejectionNote = null;

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation("Plan version {PlanVersionId} for project {ProjectId} approved by {UserId}.", plan.Id, projectId, hodUserId);

        if (plan.Project is null)
        {
            plan.Project = project;
        }

        if (plan.Project is not null)
        {
            await _notifications.NotifyPlanApprovedAsync(plan, plan.Project, hodUserId, cancellationToken);
        }
        return true;
    }

    public async Task ApproveAsync(int projectId, string approverUserId, CancellationToken cancellationToken = default)
    {
        var approved = await ApproveLatestDraftAsync(projectId, approverUserId, cancellationToken);
        if (!approved)
        {
            throw new InvalidOperationException("No plan is currently pending approval for this project.");
        }
    }

    public async Task<List<string>> GetValidationErrorsAsync(int planVersionId, CancellationToken cancellationToken = default)
    {
        var plan = await _db.PlanVersions
            .Include(p => p.StagePlans)
            .SingleOrDefaultAsync(p => p.Id == planVersionId, cancellationToken);

        if (plan is null)
        {
            throw new InvalidOperationException("Plan not found.");
        }

        return await ValidateStagePlansAsync(plan, cancellationToken);
    }

    private static int ResolveSortOrder(string code)
    {
        var index = Array.IndexOf(StageCodes.All, code);
        return index >= 0 ? index : int.MaxValue;
    }

    public async Task<bool> RejectLatestPendingAsync(int projectId, string hodUserId, string? reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hodUserId))
        {
            throw new ArgumentException("A valid approver identifier is required.", nameof(hodUserId));
        }

        var plan = await _db.PlanVersions
            .Include(p => p.ApprovalLogs)
            .Include(p => p.Project)
            .Where(p => p.ProjectId == projectId && p.Status == PlanVersionStatus.PendingApproval)
            .OrderByDescending(p => p.SubmittedOn)
            .ThenByDescending(p => p.VersionNo)
            .FirstOrDefaultAsync(cancellationToken);

        if (plan == null)
        {
            return false;
        }

        var trimmedNote = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        var rejectedOn = _clock.UtcNow;

        plan.Status = PlanVersionStatus.Draft;
        plan.SubmittedByUserId = null;
        plan.SubmittedOn = null;
        plan.ApprovedByUserId = null;
        plan.ApprovedOn = null;
        plan.RejectedByUserId = hodUserId;
        plan.RejectedOn = rejectedOn;
        plan.RejectionNote = trimmedNote;

        plan.ApprovalLogs.Add(new PlanApprovalLog
        {
            PlanVersionId = plan.Id,
            Action = "Rejected",
            Note = trimmedNote,
            PerformedByUserId = hodUserId,
            PerformedOn = rejectedOn
        });

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Plan version {PlanVersionId} for project {ProjectId} was rejected by {UserId}.", plan.Id, projectId, hodUserId);

        if (plan.Project is not null)
        {
            await _notifications.NotifyPlanRejectedAsync(plan, plan.Project, hodUserId, cancellationToken);
        }

        return true;
    }

    public async Task RejectAsync(int projectId, string approverUserId, string note, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(approverUserId))
        {
            throw new ArgumentException("A valid approver identifier is required.", nameof(approverUserId));
        }

        var trimmedNote = note?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedNote))
        {
            throw new PlanApprovalValidationException(new[] { "A rejection note is required." });
        }

        var rejected = await RejectLatestPendingAsync(projectId, approverUserId, trimmedNote, cancellationToken);

        if (!rejected)
        {
            throw new InvalidOperationException("No plan is currently pending approval for this project.");
        }
    }

    private async Task<List<string>> ValidateStagePlansAsync(PlanVersion plan, CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        // SECTION: Workflow Resolution
        var workflowVersion = plan.Project?.WorkflowVersion
            ?? await _db.Projects
                .Where(p => p.Id == plan.ProjectId)
                .Select(p => p.WorkflowVersion)
                .SingleAsync(cancellationToken);
        workflowVersion ??= PlanConstants.StageTemplateVersionV1;

        var allowedStageCodes = new HashSet<string>(
            ProcurementWorkflow.StageCodesFor(workflowVersion),
            StringComparer.OrdinalIgnoreCase);

        var stagePlans = plan.StagePlans
            .Where(stage => !string.IsNullOrWhiteSpace(stage.StageCode))
            .ToDictionary(stage => stage.StageCode!, StringComparer.OrdinalIgnoreCase);

        foreach (var (code, stage) in stagePlans)
        {
            var stageName = StageCodes.DisplayNameOf(code);

            if (!allowedStageCodes.Contains(code))
            {
                errors.Add($"Stage {stageName} ({code}) is not part of this workflow.");
                continue;
            }

            var start = stage.PlannedStart;
            var due = stage.PlannedDue;

            if (start is null && due is null)
            {
                continue;
            }

            if (start is null || due is null)
            {
                errors.Add($"Stage {stageName} must have both a planned start and due date or be left blank.");
                continue;
            }

            if (due < start)
            {
                errors.Add($"Stage {stageName} must end on or after its planned start date.");
            }
        }

        return errors;
    }
}

public class PlanApprovalValidationException : Exception
{
    public PlanApprovalValidationException(IEnumerable<string> errors)
        : base("The plan is not ready for approval.")
    {
        Errors = errors?.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()).Distinct().ToArray() ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> Errors { get; }
}
