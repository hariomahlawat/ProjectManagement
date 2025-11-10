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
    public sealed class PlanRealignmentService : IPlanRealignment
    {
        private readonly ApplicationDbContext _db;
        private readonly IClock _clock;

        public PlanRealignmentService(ApplicationDbContext db, IClock clock)
        {
            _db = db;
            _clock = clock;
        }

        public async Task CreateRealignmentDraftAsync(
            int projectId,
            string sourceStageCode,
            int delayDays,
            string triggeredByUserId,
            CancellationToken cancellationToken = default)
        {
            if (delayDays <= 0)
                return;

            // latest approved plan
            var activePlan = await _db.PlanVersions
                .Include(p => p.StagePlans)
                .Where(p => p.ProjectId == projectId && p.Status == PlanVersionStatus.Approved)
                .OrderByDescending(p => p.VersionNo)
                .FirstOrDefaultAsync(cancellationToken);

            if (activePlan is null)
                return;

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

            // order by master stage list
            var ordered = activePlan.StagePlans
                .OrderBy(sp => Array.IndexOf(StageCodes.All, sp.StageCode ?? string.Empty))
                .ToList();

            var foundSource = false;

            foreach (var sp in ordered)
            {
                var copy = new StagePlan
                {
                    StageCode = sp.StageCode,
                    DurationDays = sp.DurationDays,
                    PlannedStart = sp.PlannedStart,
                    PlannedDue = sp.PlannedDue
                };

                if (string.Equals(sp.StageCode, sourceStageCode, StringComparison.OrdinalIgnoreCase))
                {
                    foundSource = true;
                }
                else if (foundSource)
                {
                    if (copy.PlannedStart.HasValue)
                        copy.PlannedStart = copy.PlannedStart.Value.AddDays(delayDays);
                    if (copy.PlannedDue.HasValue)
                        copy.PlannedDue = copy.PlannedDue.Value.AddDays(delayDays);
                }

                newPlan.StagePlans.Add(copy);
            }

            if (!foundSource)
                return;

            // send to HoD
            newPlan.Status = PlanVersionStatus.PendingApproval;
            newPlan.SubmittedByUserId = triggeredByUserId;

            _db.PlanVersions.Add(newPlan);

            var audit = new PlanRealignmentAudit
            {
                ProjectId = projectId,
                PlanVersionNo = newPlan.VersionNo,
                SourceStageCode = sourceStageCode,
                DelayDays = delayDays,
                CreatedAtUtc = _clock.UtcNow.UtcDateTime,
                CreatedByUserId = triggeredByUserId
            };
            _db.PlanRealignmentAudits.Add(audit);

            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}
