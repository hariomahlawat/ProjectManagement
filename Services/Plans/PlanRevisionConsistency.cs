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
            !db.Projects.Any(project => project.Id == plan.ProjectId &&
                                        project.ActivePlanVersionNo >= plan.VersionNo) &&
            !db.PlanVersions.Any(later =>
                later.ProjectId == plan.ProjectId &&
                later.VersionNo > plan.VersionNo &&
                (later.Status == PlanVersionStatus.PendingApproval ||
                 later.Status == PlanVersionStatus.Approved ||
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
