using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Plans;

namespace ProjectManagement.Services.Plans;

/// <summary>
/// Single source of truth for actionable timeline submissions. A later reviewed
/// revision permanently supersedes earlier submissions, even after rejection.
/// Ordinary, never-submitted drafts do not supersede anything.
/// </summary>
internal static class PlanRevisionConsistency
{
    internal static IQueryable<PlanVersion> ActionablePending(ApplicationDbContext db) =>
        db.PlanVersions.Where(plan =>
            plan.Status == PlanVersionStatus.PendingApproval &&
            !plan.ApprovalLogs.Any(log => log.Action == "Superseded" || log.Action == "Withdrawn") &&
            !db.Projects.Any(project => project.Id == plan.ProjectId &&
                                        project.ActivePlanVersionNo >= plan.VersionNo) &&
            !db.PlanVersions.Any(later =>
                later.ProjectId == plan.ProjectId &&
                later.VersionNo > plan.VersionNo &&
                (later.Status == PlanVersionStatus.PendingApproval ||
                 later.Status == PlanVersionStatus.Approved ||
             later.Status == PlanVersionStatus.Superseded ||
             later.Status == PlanVersionStatus.Withdrawn ||
             later.Status == PlanVersionStatus.Rejected ||
                 later.Status == PlanVersionStatus.Superseded ||
                 later.Status == PlanVersionStatus.Withdrawn ||
                 later.Status == PlanVersionStatus.Rejected ||
                 later.RejectedOn != null ||
                 later.ApprovalLogs.Any())));

    internal static async Task<bool> IsSupersededAsync(
        ApplicationDbContext db, int projectId, int versionNo, CancellationToken ct)
    {
        var alreadyPublished = await db.Projects.AsNoTracking().AnyAsync(
            project => project.Id == projectId && project.ActivePlanVersionNo >= versionNo, ct);
        return alreadyPublished || await db.PlanVersions.AsNoTracking().AnyAsync(later =>
            later.ProjectId == projectId &&
            later.VersionNo > versionNo &&
            (later.Status == PlanVersionStatus.PendingApproval ||
             later.Status == PlanVersionStatus.Approved ||
             later.RejectedOn != null ||
             later.ApprovalLogs.Any()), ct);
    }

    /// <summary>
    /// Promote an actual material successor to the new revision identity. The
    /// caller MUST own the project transaction gate and persist the candidate
    /// changes in that same transaction. Opening or copying a draft is inert.
    /// </summary>
    internal static async Task<bool> SupersedeOwnedPendingIfChangedAsync(
        ApplicationDbContext db,
        PlanVersion successor,
        string actorUserId,
        DateTimeOffset decidedOn,
        CancellationToken ct)
    {
        if (successor.Status != PlanVersionStatus.Draft ||
            !string.Equals(successor.OwnerUserId, actorUserId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only the owner can replace their editable draft.");

        var previous = await ActionablePending(db)
            .Include(p => p.StagePlans)
            .Where(p => p.ProjectId == successor.ProjectId && p.VersionNo < successor.VersionNo &&
                        (p.OwnerUserId == actorUserId || p.SubmittedByUserId == actorUserId))
            .OrderByDescending(p => p.VersionNo)
            .FirstOrDefaultAsync(ct);

        if (previous is null || !HasMaterialChanges(previous, successor))
            return false;

        previous.Status = PlanVersionStatus.Superseded;
        db.PlanApprovalLogs.Add(new PlanApprovalLog
        {
            PlanVersionId = previous.Id,
            Action = "Superseded",
            Note = $"Replaced by plan revision {successor.VersionNo} (ID {successor.Id}).",
            PerformedByUserId = actorUserId,
            PerformedOn = decidedOn
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    internal static bool HasMaterialChanges(PlanVersion reviewed, PlanVersion draft)
    {
        if (reviewed.PlanningInputVersion != draft.PlanningInputVersion ||
            reviewed.AnchorStageCode != draft.AnchorStageCode ||
            reviewed.AnchorDate != draft.AnchorDate ||
            reviewed.SkipWeekends != draft.SkipWeekends ||
            reviewed.SkipHolidays != draft.SkipHolidays ||
            reviewed.TransitionRule != draft.TransitionRule ||
            reviewed.PncApplicable != draft.PncApplicable)
            return true;

        if (reviewed.StagePlans.Count != draft.StagePlans.Count)
            return true;

        var stages = reviewed.StagePlans.ToDictionary(p => p.StageCode, StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in draft.StagePlans)
        {
            if (!stages.TryGetValue(candidate.StageCode, out var prior) ||
                prior.PlannedStart != candidate.PlannedStart ||
                prior.PlannedDue != candidate.PlannedDue ||
                prior.DurationDays != candidate.DurationDays ||
                prior.SortOrder != candidate.SortOrder)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Every writer of submission/publication state takes this gate before its
    /// authoritative reads and writes, within the same database transaction.
    /// PostgreSQL's transaction-scoped advisory lock also prevents phantom
    /// insertion of a higher PlanVersion while a decision publishes the older one.
    /// </summary>
    internal static async Task LockProjectAsync(ApplicationDbContext db, int projectId, CancellationToken ct)
    {
        if (db.Database.IsNpgsql())
        {
            // Two-key advisory lock: fixed namespace and project identity.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({1347566670}, {projectId})", ct);
        }
        else if (db.Database.IsRelational())
        {
            // SQLite test path and other relational providers: acquire a database
            // write lock before evaluating pending/superseded rows.
            await db.Projects.Where(project => project.Id == projectId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.Name, p => p.Name), ct);
        }
    }
}
