using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;

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
/// transaction, after the project gate. Admin and any HoD keep their direct
/// authority (current product policy); a Project Officer must be the project's
/// current lead. The check reads the project row under a PostgreSQL share lock,
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

        if (db.Database.IsNpgsql())
        {
            // Waits for an in-flight reassignment or deletion to commit, then
            // keeps the row unchanged until this transaction ends.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"Projects\" WHERE \"Id\" = {projectId} FOR SHARE",
                cancellationToken);
        }

        var project = await db.Projects
            .AsNoTracking()
            .Where(item => item.Id == projectId)
            .Select(item => new { item.IsDeleted, item.LeadPoUserId })
            .SingleOrDefaultAsync(cancellationToken);

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
            return;
        }

        throw new ActualsWriteForbiddenException(projectId, actor.UserId);
    }
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
