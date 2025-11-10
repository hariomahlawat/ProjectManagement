using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;

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

            // SECTION: Prepare new plan shell
            var newPlan = new PlanVersion
            {
                ProjectId = projectId,
                CreatedByUserId = triggeredByUserId,
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
                var copy = new StagePlan
                {
                    StageCode = stagePlan.StageCode,
                    DurationDays = stagePlan.DurationDays,
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
        }
    }
}
