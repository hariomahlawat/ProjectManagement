using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Infrastructure;

namespace ProjectManagement.Services.Plans
{
    // SECTION: Realignment service implementation
    public sealed class PlanRealignmentService : IPlanRealignment
    {
        private readonly ApplicationDbContext _db;
        private readonly IClock _clock;

        public PlanRealignmentService(ApplicationDbContext db, IClock clock)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public async Task CreateRealignmentDraftAsync(
            int projectId,
            string sourceStageCode,
            int delayDays,
            string triggeredByUserId,
            CancellationToken cancellationToken = default)
        {
            // SECTION: Guard clauses
            if (delayDays <= 0)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(sourceStageCode))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(triggeredByUserId))
            {
                return;
            }

            // One gate for draft creation, submission, approval and rejection.
            // A nested call participates in its caller's stage transaction.
            await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);
            await PlanRevisionConsistency.LockProjectAsync(_db, projectId, cancellationToken);

            // SECTION: Retrieve active plan
            var activePlan = await _db.PlanVersions
                .Include(p => p.StagePlans)
                .Where(p => p.ProjectId == projectId && p.Status == PlanVersionStatus.Approved)
                .OrderByDescending(p => p.VersionNo)
                .FirstOrDefaultAsync(cancellationToken);

            if (activePlan is null)
            {
                return;
            }

            // Legacy shared globals may contain another actor's private work.
            // Resolve the accepted policy/duration/date/PNC boundary in one SQL snapshot.
            var baseline = await PublishedPlanningBaseline.LoadAsync(_db, projectId, cancellationToken);
            var settings = baseline.Settings;
            var durations = baseline.Durations.ToDictionary(d => d.StageCode, StringComparer.OrdinalIgnoreCase);
            var stageOrder = baseline.Stages.ToDictionary(s => s.StageCode, s => s.SortOrder, StringComparer.OrdinalIgnoreCase);
            // Templates are shared workflow metadata, outside the mutable published
            // baseline; materializing the version avoids a provider-specific APPLY.
            var workflowVersion = baseline.WorkflowVersion ?? PlanConstants.StageTemplateVersionV1;
            var templateOrder = await _db.StageTemplates.AsNoTracking()
                .Where(t => t.Version == workflowVersion)
                .ToDictionaryAsync(t => t.Code, t => t.Sequence, StringComparer.OrdinalIgnoreCase, cancellationToken);
            var workflowOrder = ProcurementWorkflow.BuildOrderLookup(baseline.WorkflowVersion);

            // SECTION: Prepare new plan shell
            var newPlan = new PlanVersion
            {
                ProjectId = projectId,
                PlanningInputVersion = PlanningInputVersions.Current,
                CreatedByUserId = triggeredByUserId,
                AnchorStageCode = activePlan.AnchorStageCode,
                AnchorDate = settings?.AnchorStart,
                SkipWeekends = !(settings?.IncludeWeekends ?? false),
                SkipHolidays = settings?.SkipHolidays ?? true,
                TransitionRule = settings?.NextStageStartPolicy == NextStageStartPolicies.SameDay
                    ? PlanTransitionRule.SameDay : PlanTransitionRule.NextWorkingDay,
                PncApplicable = baseline.PncApplicable,
                Status = PlanVersionStatus.Draft
            };

            var maxVersionNo = await _db.PlanVersions
                .Where(p => p.ProjectId == projectId)
                .MaxAsync(p => (int?)p.VersionNo, cancellationToken) ?? 0;

            newPlan.VersionNo = maxVersionNo + 1;

            // SECTION: Clone and shift stages
            var orderedStages = activePlan.StagePlans
                .OrderBy(sp => Array.IndexOf(StageCodes.All, sp.StageCode ?? string.Empty))
                .ToList();

            var foundSource = false;

            foreach (var stagePlan in orderedStages)
            {
                durations.TryGetValue(stagePlan.StageCode, out var duration);
                var copy = new StagePlan
                {
                    StageCode = stagePlan.StageCode,
                    DurationDays = Math.Max(0, duration?.DurationDays ?? 0),
                    SortOrder = templateOrder.TryGetValue(stagePlan.StageCode, out var sequence) ? sequence
                        : workflowOrder.TryGetValue(stagePlan.StageCode, out var order) ? order
                        : duration?.SortOrder
                            ?? (stageOrder.TryGetValue(stagePlan.StageCode, out var officialOrder) ? officialOrder : stagePlan.Id),
                    PlannedStart = stagePlan.PlannedStart,
                    PlannedDue = stagePlan.PlannedDue
                };

                if (string.Equals(stagePlan.StageCode, sourceStageCode, StringComparison.OrdinalIgnoreCase))
                {
                    foundSource = true;
                }
                else if (foundSource)
                {
                    if (copy.PlannedStart.HasValue)
                    {
                        copy.PlannedStart = copy.PlannedStart.Value.AddDays(delayDays);
                    }

                    if (copy.PlannedDue.HasValue)
                    {
                        copy.PlannedDue = copy.PlannedDue.Value.AddDays(delayDays);
                    }
                }

                // Retained official/history PNC values are not an applicability decision.
                if (!baseline.PncApplicable && string.Equals(copy.StageCode, StageCodes.PNC, StringComparison.OrdinalIgnoreCase))
                {
                    copy.DurationDays = 0;
                    copy.PlannedStart = null;
                    copy.PlannedDue = null;
                }

                newPlan.StagePlans.Add(copy);
            }

            if (!foundSource)
            {
                return;
            }

            // SECTION: Finalise submission metadata
            var now = _clock.UtcNow;
            newPlan.Status = PlanVersionStatus.PendingApproval;
            newPlan.SubmittedByUserId = triggeredByUserId;
            newPlan.SubmittedOn = now;

            _db.PlanVersions.Add(newPlan);

            // SECTION: Audit trail
            var audit = new PlanRealignmentAudit
            {
                ProjectId = projectId,
                PlanVersionNo = newPlan.VersionNo,
                SourceStageCode = sourceStageCode,
                DelayDays = delayDays,
                CreatedAtUtc = now.UtcDateTime,
                CreatedByUserId = triggeredByUserId
            };

            _db.PlanRealignmentAudits.Add(audit);

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }
}
