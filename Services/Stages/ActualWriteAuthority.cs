using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Stages;

/// <summary>
/// The acting user for an authoritative Actual-date write, derived on the
/// server from the authenticated principal. Never built from request data.
/// </summary>
public sealed record ActualWriteActor(string UserId, bool IsAdmin, bool IsHoD, bool IsProjectOfficer)
{
    public static ActualWriteActor FromPrincipal(ClaimsPrincipal principal, string userId)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("A valid user identifier is required.", nameof(userId));
        }

        return new ActualWriteActor(
            userId,
            principal.IsInRole(RoleNames.Admin),
            principal.IsInRole(RoleNames.HoD),
            principal.IsInRole(RoleNames.ProjectOfficer));
    }
}

/// <summary>
/// Re-establishes project-level write authority inside the publication
/// transaction, after the project gate. Only Admin and HoD may publish Actual
/// dates directly; that authority supersedes the Project Officer role, so a user
/// who also holds PO keeps it. From Phase D-2 a PO-only user may not publish
/// directly: the project's current lead PO is told approval is required (D-3 routes
/// that user to a proposal), and any other PO-only user is forbidden.
/// The check reads the project row under a PostgreSQL share lock,
/// which conflicts with the row updates that reassign the lead PO
/// (<c>AssignRoles</c>) or soft-delete the project (<c>ProjectModerationService</c>).
/// Those writers do not take the project gate, so the gate alone would not
/// hold the checked state stable until commit.
/// </summary>
internal static class ActualWriteAuthority
{
    /// <summary>
    /// Call after <c>PlanRevisionConsistency.LockProjectAsync</c> and before
    /// locking stage rows (lock order: gate, project row, stage rows).
    /// </summary>
    internal static async Task EnsureAsync(
        ApplicationDbContext db,
        int projectId,
        ActualWriteActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(actor);

        var project = await ReadLockedProjectAsync(db, projectId, cancellationToken);

        if (project is null || project.IsDeleted)
        {
            throw new ActualsProjectUnavailableException(projectId);
        }

        if (actor.IsAdmin || actor.IsHoD)
        {
            return;
        }

        if (actor.IsProjectOfficer
            && string.Equals(project.LeadPoUserId, actor.UserId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ActualsApprovalRequiredException(projectId, actor.UserId);
        }

        throw new ActualsWriteForbiddenException(projectId, actor.UserId);
    }

    /// <summary>
    /// Phase D-3 proposal authority, the counterpart of <see cref="EnsureAsync"/>:
    /// only the project's current lead PO without Admin or HoD authority proposes
    /// Actual corrections. Same lock order and project-row share lock. Submission
    /// additionally requires an active, non-legacy project, matching the editor
    /// the UI offers; withdrawal does not, so a pending revision stays closable.
    /// </summary>
    internal static async Task<ActualProposalAuthority> ResolveProposalAsync(
        ApplicationDbContext db,
        int projectId,
        ActualWriteActor actor,
        bool requireEditableProject,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(actor);

        var project = await ReadLockedProjectAsync(db, projectId, cancellationToken);

        if (project is null || project.IsDeleted)
        {
            return ActualProposalAuthority.ProjectUnavailable;
        }

        // HoD/Admin authority supersedes the PO role: such users publish directly.
        if (actor.IsAdmin || actor.IsHoD)
        {
            return ActualProposalAuthority.DirectAuthority;
        }

        if (!actor.IsProjectOfficer
            || !string.Equals(project.LeadPoUserId, actor.UserId, StringComparison.OrdinalIgnoreCase))
        {
            return ActualProposalAuthority.Forbidden;
        }

        if (requireEditableProject
            && (project.LifecycleStatus != ProjectLifecycleStatus.Active || project.IsLegacy))
        {
            return ActualProposalAuthority.ProjectNotEditable;
        }

        return ActualProposalAuthority.Allowed;
    }

    /// <summary>
    /// HoD/Admin decision path (D-3d): the project must exist and not be deleted, read
    /// under the same project-row share lock and lock order as the write paths.
    /// </summary>
    internal static async Task<bool> IsProjectAvailableAsync(
        ApplicationDbContext db,
        int projectId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var project = await ReadLockedProjectAsync(db, projectId, cancellationToken);
        return project is { IsDeleted: false };
    }

    private static async Task<LockedProject?> ReadLockedProjectAsync(
        ApplicationDbContext db,
        int projectId,
        CancellationToken cancellationToken)
    {
        if (db.Database.IsNpgsql())
        {
            // Waits for an in-flight reassignment or deletion to commit, then
            // keeps the row unchanged until this transaction ends.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"Projects\" WHERE \"Id\" = {projectId} FOR SHARE",
                cancellationToken);
        }

        return await db.Projects
            .AsNoTracking()
            .Where(item => item.Id == projectId)
            .Select(item => new LockedProject(item.IsDeleted, item.LeadPoUserId, item.LifecycleStatus, item.IsLegacy))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private sealed record LockedProject(
        bool IsDeleted,
        string? LeadPoUserId,
        ProjectLifecycleStatus LifecycleStatus,
        bool IsLegacy);
}

internal enum ActualProposalAuthority
{
    Allowed,
    ProjectUnavailable,
    DirectAuthority,
    Forbidden,
    ProjectNotEditable
}

/// <summary>The actor has no current write authority over the project's Actual dates.</summary>
public sealed class ActualsWriteForbiddenException : Exception
{
    public ActualsWriteForbiddenException(int projectId, string userId)
        : base("The user is not authorised to change this project's actual dates.")
    {
        ProjectId = projectId;
        UserId = userId;
    }

    public int ProjectId { get; }

    public string UserId { get; }
}

/// <summary>
/// The actor is the project's assigned Project Officer without Admin or HoD
/// authority. From Phase D-2 such a user cannot publish Actual dates directly;
/// the correction needs a HoD or Admin (D-3 adds the proposal route).
/// </summary>
public sealed class ActualsApprovalRequiredException : Exception
{
    public const string UserMessage =
        "Actual-date corrections now require HoD or Admin approval. Ask a HoD or Admin to apply this change. Nothing was saved.";

    public ActualsApprovalRequiredException(int projectId, string userId)
        : base(UserMessage)
    {
        ProjectId = projectId;
        UserId = userId;
    }

    public int ProjectId { get; }

    public string UserId { get; }
}

/// <summary>The project does not exist or has been moved to Trash.</summary>
public sealed class ActualsProjectUnavailableException : Exception
{
    public ActualsProjectUnavailableException(int projectId)
        : base("The project was not found or has been deleted.")
    {
        ProjectId = projectId;
    }

    public int ProjectId { get; }
}
