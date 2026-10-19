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
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Stages;
using ProjectManagement.Services.Scheduling;
using ProjectManagement.Services.Authorization;
using ProjectManagement.Infrastructure;

namespace ProjectManagement.Services.Plans;

public class PlanApprovalService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<PlanApprovalService> _logger;
    private readonly PlanSnapshotService _snapshots;
    private readonly IPlanNotificationService _notifications;
    private readonly IPlannedTimelineValidationService _timelineValidation;

    public PlanApprovalService(
        ApplicationDbContext db,
        IClock clock,
        ILogger<PlanApprovalService> logger,
        PlanSnapshotService snapshots,
        IPlanNotificationService notifications)
        : this(
            db,
            clock,
            logger,
            snapshots,
            notifications,
            new PlannedTimelineValidationService(
                db,
                new ProjectStageWorkflowPolicy(db, new WorkflowStageMetadataProvider()),
                new OfficeCalendarService(db)))
    {
    }

    public PlanApprovalService(
        ApplicationDbContext db,
        IClock clock,
        ILogger<PlanApprovalService> logger,
        PlanSnapshotService snapshots,
        IPlanNotificationService notifications,
        IPlannedTimelineValidationService timelineValidation)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
        _snapshots = snapshots;
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _timelineValidation = timelineValidation ?? throw new ArgumentNullException(nameof(timelineValidation));
    }

    public async Task SubmitAsync(int projectId, string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("A valid user identifier is required to submit a plan for approval.", nameof(userId));
        }

        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);
        await PlanRevisionConsistency.LockProjectAsync(_db, projectId, cancellationToken);

        var plan = await _db.PlanVersions
            .Include(p => p.StagePlans)
            .Include(p => p.ApprovalLogs)
            .Include(p => p.Project)
            .FirstOrDefaultAsync(p => p.ProjectId == projectId &&
                                      p.Status == PlanVersionStatus.Draft &&
                                      p.OwnerUserId == userId,
                cancellationToken);

        if (plan == null)
        {
            throw new InvalidOperationException("No draft plan was found to submit for approval.");
        }

        if (await PlanRevisionConsistency.IsSupersededAsync(
                _db, projectId, plan.VersionNo, cancellationToken))
        {
            throw new DomainException("This revision was superseded by a later timeline submission.");
        }

        var pending = await PlanRevisionConsistency.ActionablePending(_db)
            .Include(p => p.StagePlans)
            .Include(p => p.Project)
            .Where(p => p.ProjectId == projectId)
            .OrderByDescending(p => p.VersionNo)
            .FirstOrDefaultAsync(cancellationToken);
        if (pending is not null &&
            (!string.Equals(pending.OwnerUserId ?? pending.SubmittedByUserId,
                            userId, StringComparison.OrdinalIgnoreCase) ||
             pending.VersionNo >= plan.VersionNo))
            throw new DomainException("Another submission is already pending approval.");

        if (pending is not null && !PlanRevisionConsistency.HasMaterialChanges(pending, plan))
            throw new DomainException("Make a change to the reviewed plan before submitting a replacement.");

        var errors = await ValidatePlanAsync(plan, cancellationToken);
        if (errors.Count > 0)
        {
            throw new PlanApprovalValidationException(errors);
        }

        if (pending is not null)
        {
            // Successful replacement is one atomic transition: the prior review
            // becomes permanently stale before the new submission is visible.
            await PlanRevisionConsistency.SupersedeOwnedPendingIfChangedAsync(
                _db, plan, userId, _clock.UtcNow, cancellationToken);
        }

        plan.Status = PlanVersionStatus.PendingApproval;
        plan.OwnerUserId ??= userId;
        plan.SubmittedByUserId = userId;
        // Rejected plans return to Draft with the same PlanVersionId. Ensure every
        // submission has a distinct review timestamp, even on a fixed test clock.
        var submittedOn = _clock.UtcNow;
        if (plan.RejectedOn.HasValue && submittedOn <= plan.RejectedOn.Value)
            submittedOn = plan.RejectedOn.Value.AddMilliseconds(1);
        plan.SubmittedOn = submittedOn;
        plan.ApprovedByUserId = null;
        plan.ApprovedOn = null;
        plan.RejectedByUserId = null;
        plan.RejectedOn = null;
        plan.RejectionNote = null;
        // New submissions carry their own immutable review-epoch evidence.
        // Historical pre-C2 Pending rows without this event retain the legacy
        // reject-to-Draft fallback; new rejection never edits submitted inputs.
        plan.ApprovalLogs.Add(new PlanApprovalLog
        {
            PlanVersionId = plan.Id,
            Action = "Submitted",
            PerformedByUserId = userId,
            PerformedOn = submittedOn
        });

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _logger.LogInformation("Plan version {PlanVersionId} for project {ProjectId} submitted for approval by {UserId}.", plan.Id, projectId, userId);
        if (pending?.Project is not null)
            await _notifications.NotifyPlanSupersededAsync(pending, pending.Project, userId, cancellationToken);

        if (plan.Project is not null)
        {
            await _notifications.NotifyPlanSubmittedAsync(plan, plan.Project, userId, cancellationToken);
        }
    }

    /// <summary>
    /// Withdraws the exact currently actionable submission without rewriting the
    /// reviewed content. A stale tab cannot withdraw a newer submission.
    /// </summary>
    public async Task<bool> WithdrawVersionAsync(
        int projectId, int planVersionId, DateTimeOffset? submittedOn,
        string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || !submittedOn.HasValue)
            return false;

        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);
        await PlanRevisionConsistency.LockProjectAsync(_db, projectId, cancellationToken);
        var plan = await PlanRevisionConsistency.ActionablePending(_db)
            .Include(p => p.Project)
            .SingleOrDefaultAsync(p => p.ProjectId == projectId && p.Id == planVersionId &&
                                       p.SubmittedOn == submittedOn, cancellationToken);
        if (plan is null ||
            !string.Equals(plan.OwnerUserId ?? plan.SubmittedByUserId,
                           userId, StringComparison.OrdinalIgnoreCase))
            return false;

        plan.Status = PlanVersionStatus.Withdrawn;
        _db.PlanApprovalLogs.Add(new PlanApprovalLog
        {
            PlanVersionId = plan.Id,
            Action = "Withdrawn",
            Note = "Submission withdrawn by its owner.",
            PerformedByUserId = userId,
            PerformedOn = _clock.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (plan.Project is not null)
            await _notifications.NotifyPlanWithdrawnAsync(plan, plan.Project, userId, cancellationToken);
        return true;
    }

    public Task SubmitForApprovalAsync(int projectId, string userId, CancellationToken cancellationToken = default)
        => SubmitAsync(projectId, userId, cancellationToken);

    public async Task<bool> ApproveLatestDraftAsync(
        int projectId,
        string hodUserId,
        bool isAdmin,
        bool isHoD,
        CancellationToken cancellationToken = default)
    {
        EnsureCanDecide(hodUserId, isAdmin, isHoD, "approve");

        var plan = await PlanRevisionConsistency.ActionablePending(_db)
            .Include(p => p.StagePlans)
            .Include(p => p.Project)
            .Where(p => p.ProjectId == projectId)
            .OrderByDescending(p => p.SubmittedOn)
            .ThenByDescending(p => p.VersionNo)
            .FirstOrDefaultAsync(cancellationToken);

        return await ApproveLoadedPlanAsync(plan, hodUserId, cancellationToken);
    }

    /// <summary>
    /// Approves the exact plan version reviewed by the decision maker. This prevents
    /// a newer submission from being approved through an older browser tab.
    /// </summary>
    public async Task<bool> ApproveVersionAsync(
        int planVersionId,
        string hodUserId,
        bool isAdmin,
        bool isHoD,
        CancellationToken cancellationToken = default,
        int? expectedProjectId = null,
        bool verifySubmissionIdentity = false,
        DateTimeOffset? expectedSubmittedOn = null)
    {
        EnsureCanDecide(hodUserId, isAdmin, isHoD, "approve");

        var plan = await _db.PlanVersions
            .Include(p => p.StagePlans)
            .Include(p => p.Project)
            .SingleOrDefaultAsync(
                p => p.Id == planVersionId &&
                     p.Status == PlanVersionStatus.PendingApproval &&
                     (!expectedProjectId.HasValue || p.ProjectId == expectedProjectId.Value),
                cancellationToken);

        if (verifySubmissionIdentity && (plan is null || plan.SubmittedOn != expectedSubmittedOn))
            return false;

        return await ApproveLoadedPlanAsync(plan, hodUserId, cancellationToken);
    }

    private async Task<bool> ApproveLoadedPlanAsync(
        PlanVersion? plan,
        string hodUserId,
        CancellationToken cancellationToken)
    {
        if (plan is null)
        {
            return false;
        }

        if (string.Equals(plan.SubmittedByUserId, hodUserId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Plan approval forbidden for self-approval. ProjectId={ProjectId}, UserId={UserId}",
                plan.ProjectId,
                hodUserId);
            throw new ForbiddenException("You cannot approve your own plan submission.");
        }

        var project = plan.Project
            ?? await _db.Projects
                .FirstOrDefaultAsync(p => p.Id == plan.ProjectId, cancellationToken)
            ?? throw new InvalidOperationException("Project not found.");

        var now = _clock.UtcNow;
        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);
        await PlanRevisionConsistency.LockProjectAsync(_db, plan.ProjectId, cancellationToken);
        if (!await IsCurrentReviewTargetAsync(plan, cancellationToken))
            return false;

        var validationErrors = await ValidatePlanAsync(plan, cancellationToken);
        if (validationErrors.Count > 0)
            throw new PlanApprovalValidationException(validationErrors);

        if (!await TryClaimPendingDecisionAsync(plan, PlanVersionStatus.Approved, cancellationToken))
            return false;

        var workflowVersion = project.WorkflowVersion ?? PlanConstants.StageTemplateVersionV1;
        var workflowOrder = await _db.StageTemplates
            .AsNoTracking()
            .Where(template => template.Version == workflowVersion)
            .ToDictionaryAsync(
                template => template.Code,
                template => template.Sequence,
                StringComparer.OrdinalIgnoreCase,
                cancellationToken);

        // Legacy pending revisions never owned the full calendar/duration inputs.
        // Preserve their pre-B1 dates/PNC publication authority without promoting globals.
        if (plan.PlanningInputVersion == PlanningInputVersions.Current)
        {
            var settings = await _db.ProjectScheduleSettings
                .SingleOrDefaultAsync(s => s.ProjectId == plan.ProjectId, cancellationToken);
            if (settings is null)
            {
                settings = new ProjectScheduleSettings { ProjectId = plan.ProjectId };
                _db.ProjectScheduleSettings.Add(settings);
            }

            settings.AnchorStart = plan.AnchorDate;
            settings.IncludeWeekends = !plan.SkipWeekends;
            settings.SkipHolidays = plan.SkipHolidays;
            settings.NextStageStartPolicy = plan.TransitionRule == PlanTransitionRule.SameDay
                ? NextStageStartPolicies.SameDay
                : NextStageStartPolicies.NextWorkingDay;

            var publishedDurationRows = await PlanningCompatibilityCatalog.PrepareDurationRowsForPublicationAsync(
                _db,
                plan.ProjectId,
                workflowVersion,
                project.PublishedPlanningInputVersion == PlanningInputVersions.Current,
                plan.StagePlans.Select(stage => stage.StageCode),
                cancellationToken);
            var publishedDurations = publishedDurationRows
                .Where(duration => !string.IsNullOrWhiteSpace(duration.StageCode))
                .ToDictionary(duration => duration.StageCode, StringComparer.OrdinalIgnoreCase);

            foreach (var stagePlan in plan.StagePlans.Where(sp => !string.IsNullOrWhiteSpace(sp.StageCode)))
            {
                if (!publishedDurations.TryGetValue(stagePlan.StageCode, out var duration))
                {
                    duration = new ProjectPlanDuration
                    {
                        ProjectId = plan.ProjectId,
                        StageCode = stagePlan.StageCode
                    };
                    _db.ProjectPlanDurations.Add(duration);
                    publishedDurations[stagePlan.StageCode] = duration;
                }

                var isExcludedPnc = !plan.PncApplicable &&
                    string.Equals(stagePlan.StageCode, StageCodes.PNC, StringComparison.OrdinalIgnoreCase);
                duration.DurationDays = !isExcludedPnc && stagePlan.DurationDays > 0
                    ? stagePlan.DurationDays : null;
                duration.SortOrder = workflowOrder.TryGetValue(stagePlan.StageCode, out var order)
                    ? order
                    : stagePlan.SortOrder;
            }

            // False is authoritative even when the reviewed proposal omits its PNC row.
            if (!plan.PncApplicable && publishedDurations.TryGetValue(StageCodes.PNC, out var pncDuration))
                pncDuration.DurationDays = null;

            project.PublishedPlanningInputVersion = PlanningInputVersions.Current;
        }

        var currentStages = await _db.ProjectStages
            .Where(ps => ps.ProjectId == plan.ProjectId)
            .ToListAsync(cancellationToken);

        var stageLookup = currentStages
            .Where(s => !string.IsNullOrWhiteSpace(s.StageCode))
            .ToDictionary(s => s.StageCode!, s => s, StringComparer.OrdinalIgnoreCase);

        // The reviewed applicability flag wins over stale or legacy draft rows.
        // Also clear an old official plan when no PNC row was submitted at all.
        if (!plan.PncApplicable && stageLookup.TryGetValue(StageCodes.PNC, out var excludedPnc))
        {
            excludedPnc.PlannedStart = null;
            excludedPnc.PlannedDue = null;
        }

        foreach (var stagePlan in plan.StagePlans.Where(sp => !string.IsNullOrWhiteSpace(sp.StageCode)))
        {
            var code = stagePlan.StageCode!;
            if (!plan.PncApplicable && string.Equals(code, StageCodes.PNC, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (!stageLookup.TryGetValue(code, out var stage))
            {
                stage = new ProjectStage
                {
                    ProjectId = plan.ProjectId,
                    StageCode = code,
                    SortOrder = workflowOrder.TryGetValue(code, out var order) ? order : stagePlan.SortOrder,
                    Status = StageStatus.NotStarted
                };
                _db.ProjectStages.Add(stage);
                stageLookup[code] = stage;
            }

            stage.PlannedStart = stagePlan.PlannedStart;
            stage.PlannedDue = stagePlan.PlannedDue;
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _snapshots.CreateSnapshotAsync(plan.ProjectId, hodUserId, cancellationToken);

        project.ActivePlanVersionNo = plan.VersionNo;
        project.PncApplicable = plan.PncApplicable;
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

        _logger.LogInformation(
            "Plan version {PlanVersionId} for project {ProjectId} approved by {UserId}.",
            plan.Id,
            plan.ProjectId,
            hodUserId);

        plan.Project ??= project;
        await _notifications.NotifyPlanApprovedAsync(plan, project, hodUserId, cancellationToken);
        return true;
    }

    public async Task ApproveAsync(
        int projectId,
        string approverUserId,
        bool isAdmin,
        bool isHoD,
        CancellationToken cancellationToken = default)
    {
        var approved = await ApproveLatestDraftAsync(projectId, approverUserId, isAdmin, isHoD, cancellationToken);
        if (!approved)
        {
            throw new InvalidOperationException("No plan is currently pending approval for this project.");
        }
    }

    private void EnsureCanDecide(string hodUserId, bool isAdmin, bool isHoD, string action)
    {
        if (!ApprovalAuthorization.CanApproveProjectChanges(isAdmin, isHoD))
        {
            _logger.LogWarning(
                "Plan {Action} forbidden. UserId={UserId}",
                action,
                hodUserId);
            throw new ForbiddenException($"Only Admin or HoD users can {action} plan drafts.");
        }

        if (string.IsNullOrWhiteSpace(hodUserId))
        {
            throw new ArgumentException("A valid approver identifier is required.", nameof(hodUserId));
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

        return await ValidatePlanAsync(plan, cancellationToken);
    }

    public async Task<bool> RejectLatestPendingAsync(
        int projectId,
        string hodUserId,
        bool isAdmin,
        bool isHoD,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        EnsureCanDecide(hodUserId, isAdmin, isHoD, "reject");

        var plan = await PlanRevisionConsistency.ActionablePending(_db)
            .Include(p => p.ApprovalLogs)
            .Include(p => p.StagePlans)
            .Include(p => p.Project)
            .Where(p => p.ProjectId == projectId)
            .OrderByDescending(p => p.SubmittedOn)
            .ThenByDescending(p => p.VersionNo)
            .FirstOrDefaultAsync(cancellationToken);

        return await RejectLoadedPlanAsync(plan, hodUserId, reason, cancellationToken);
    }

    /// <summary>
    /// Rejects the exact pending plan version reviewed by the decision maker.
    /// </summary>
    public async Task<bool> RejectVersionAsync(
        int planVersionId,
        string hodUserId,
        bool isAdmin,
        bool isHoD,
        string? reason,
        CancellationToken cancellationToken = default,
        int? expectedProjectId = null,
        bool verifySubmissionIdentity = false,
        DateTimeOffset? expectedSubmittedOn = null)
    {
        EnsureCanDecide(hodUserId, isAdmin, isHoD, "reject");

        var plan = await _db.PlanVersions
            .Include(p => p.ApprovalLogs)
            .Include(p => p.StagePlans)
            .Include(p => p.Project)
            .SingleOrDefaultAsync(
                p => p.Id == planVersionId &&
                     p.Status == PlanVersionStatus.PendingApproval &&
                     (!expectedProjectId.HasValue || p.ProjectId == expectedProjectId.Value),
                cancellationToken);

        if (verifySubmissionIdentity && (plan is null || plan.SubmittedOn != expectedSubmittedOn))
            return false;

        return await RejectLoadedPlanAsync(plan, hodUserId, reason, cancellationToken);
    }

    private async Task<bool> RejectLoadedPlanAsync(
        PlanVersion? plan,
        string hodUserId,
        string? reason,
        CancellationToken cancellationToken)
    {
        if (plan is null)
        {
            return false;
        }

        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);
        await PlanRevisionConsistency.LockProjectAsync(_db, plan.ProjectId, cancellationToken);
        var preserveSubmittedRevision = plan.ApprovalLogs.Any(log =>
            log.Action == "CreatedAsReplacement" || log.Action == "Submitted");
        var rejectionStatus = preserveSubmittedRevision ? PlanVersionStatus.Rejected : PlanVersionStatus.Draft;
        if (!await IsCurrentReviewTargetAsync(plan, cancellationToken) ||
            !await TryClaimPendingDecisionAsync(plan, rejectionStatus, cancellationToken))
            return false;

        var trimmedNote = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        var rejectedOn = _clock.UtcNow;

        plan.Status = rejectionStatus;
        // Legacy C1 rejected drafts preserve the existing resubmission path.
        // C2 successors remain immutable and keep the reviewed submission epoch.
        if (!preserveSubmittedRevision)
        {
            plan.SubmittedByUserId = null;
            plan.SubmittedOn = null;
        }
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

        if (preserveSubmittedRevision && !string.IsNullOrWhiteSpace(plan.OwnerUserId) &&
            !await _db.PlanVersions.AnyAsync(candidate =>
                candidate.ProjectId == plan.ProjectId &&
                candidate.Status == PlanVersionStatus.Draft &&
                candidate.OwnerUserId == plan.OwnerUserId, cancellationToken))
        {
            var latest = await _db.PlanVersions.Where(p => p.ProjectId == plan.ProjectId)
                .MaxAsync(p => (int?)p.VersionNo, cancellationToken) ?? 0;
            var correction = new PlanVersion
            {
                ProjectId = plan.ProjectId,
                VersionNo = latest + 1,
                Status = PlanVersionStatus.Draft,
                Title = PlanVersion.ProjectTimelineTitle,
                CreatedByUserId = plan.OwnerUserId!,
                OwnerUserId = plan.OwnerUserId,
                CreatedOn = rejectedOn,
                PlanningInputVersion = plan.PlanningInputVersion,
                AnchorStageCode = plan.AnchorStageCode,
                AnchorDate = plan.AnchorDate,
                SkipWeekends = plan.SkipWeekends,
                SkipHolidays = plan.SkipHolidays,
                TransitionRule = plan.TransitionRule,
                PncApplicable = plan.PncApplicable,
                StagePlans = plan.StagePlans.Select(row => new StagePlan
                {
                    StageCode = row.StageCode, DurationDays = row.DurationDays,
                    PlannedStart = row.PlannedStart, PlannedDue = row.PlannedDue,
                    SortOrder = row.SortOrder
                }).ToList()
            };
            _db.PlanVersions.Add(correction);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _logger.LogInformation(
            "Plan version {PlanVersionId} for project {ProjectId} was rejected by {UserId}.",
            plan.Id,
            plan.ProjectId,
            hodUserId);

        if (plan.Project is not null)
        {
            await _notifications.NotifyPlanRejectedAsync(plan, plan.Project, hodUserId, cancellationToken);
        }

        return true;
    }

    public async Task RejectAsync(
        int projectId,
        string approverUserId,
        bool isAdmin,
        bool isHoD,
        string note,
        CancellationToken cancellationToken = default)
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

        var rejected = await RejectLatestPendingAsync(projectId, approverUserId, isAdmin, isHoD, trimmedNote, cancellationToken);

        if (!rejected)
        {
            throw new InvalidOperationException("No plan is currently pending approval for this project.");
        }
    }

    // Supersession is durable: rejecting a newer submission returns it to Draft,
    // but must never make an older pending revision actionable again. A later
    // version that has been pending, approved or rejected supersedes earlier
    // submissions; an ordinary unsubmitted Draft does not.
    private Task<bool> IsCurrentReviewTargetAsync(PlanVersion plan, CancellationToken cancellationToken)
        => PlanRevisionConsistency.ActionablePending(_db).AsNoTracking()
            .AnyAsync(version => version.Id == plan.Id, cancellationToken);

    // Atomic compare-and-set inside the decision transaction. On a relational store
    // the row-level UPDATE serializes competing approvals/rejections; only the first
    // decision can claim PendingApproval. The caller rolls its claim back if
    // publication, snapshotting or the final decision write fails.
    private async Task<bool> TryClaimPendingDecisionAsync(
        PlanVersion reviewedPlan, PlanVersionStatus decidedStatus, CancellationToken cancellationToken)
    {
        // EF's in-memory test provider does not implement ExecuteUpdate. Relational
        // tests exercise the authoritative compare-and-set path.
        if (!_db.Database.IsRelational())
            return true;

        return await _db.PlanVersions
            .Where(plan => plan.Id == reviewedPlan.Id &&
                           plan.Status == PlanVersionStatus.PendingApproval &&
                           plan.SubmittedOn == reviewedPlan.SubmittedOn)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(plan => plan.Status, decidedStatus),
                cancellationToken) == 1;
    }

    private async Task<List<string>> ValidatePlanAsync(PlanVersion plan, CancellationToken cancellationToken)
    {
        var skippedStageCodes = await _db.ProjectStages
            .AsNoTracking()
            .Where(ps => ps.ProjectId == plan.ProjectId && ps.Status == StageStatus.Skipped)
            .Select(ps => ps.StageCode)
            .ToListAsync(cancellationToken);
        var skippedStageSet = new HashSet<string>(skippedStageCodes, StringComparer.OrdinalIgnoreCase);
        string? anchorStageCode;
        PlanTransitionRule transitionRule;
        bool includeWeekends;
        bool skipHolidays;
        if (plan.PlanningInputVersion == PlanningInputVersions.Current)
        {
            anchorStageCode = plan.AnchorStageCode;
            transitionRule = plan.TransitionRule;
            includeWeekends = !plan.SkipWeekends;
            skipHolidays = plan.SkipHolidays;
        }
        else
        {
            // Legacy Pending revisions own their reviewed dates and historical PNC
            // decision, but not the calendar policy introduced by B1.
            var baseline = await PublishedPlanningBaseline.LoadAsync(_db, plan.ProjectId, cancellationToken);
            anchorStageCode = PlanConstants.DefaultAnchorStageCode;
            transitionRule = baseline.Settings?.NextStageStartPolicy == NextStageStartPolicies.SameDay
                ? PlanTransitionRule.SameDay
                : PlanTransitionRule.NextWorkingDay;
            includeWeekends = baseline.Settings?.IncludeWeekends ?? false;
            skipHolidays = baseline.Settings?.SkipHolidays ?? true;
        }

        var candidate = new PlannedTimelineCandidate(
            plan.ProjectId,
            anchorStageCode,
            transitionRule,
            includeWeekends,
            skipHolidays,
            plan.PncApplicable,
            plan.StagePlans.Select(stage => new PlannedTimelineStage(
                stage.StageCode,
                stage.PlannedStart,
                stage.PlannedDue,
                skippedStageSet.Contains(stage.StageCode),
                stage.SortOrder)).ToArray(),
            RequireConfiguredStages: plan.PlanningInputVersion == PlanningInputVersions.Current);
        var result = await _timelineValidation.ValidateAsync(candidate, cancellationToken);
        return result.Errors.ToList();
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
