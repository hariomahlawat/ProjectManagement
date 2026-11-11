using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;

namespace ProjectManagement.Services.ItProjects;

/// <summary>
/// Who can be a project's assigned officer (docs/ux/molti-sre-projects.md): an active user who
/// holds the Project Officer role. The list the page offers and the check the command makes are
/// the same query, so the two cannot drift apart.
/// </summary>
public static class ItProjectOfficers
{
    public static async Task<IReadOnlyList<ItProjectOfficerOption>> ListAssignableAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var users = await Assignable(db)
            .Select(user => new { user.Id, user.FullName, user.UserName })
            .ToListAsync(cancellationToken);

        return users
            .Select(user => new ItProjectOfficerOption(user.Id, string.IsNullOrWhiteSpace(user.FullName) ? user.UserName ?? user.Id : user.FullName))
            .OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.UserId, StringComparer.Ordinal)
            .ToList();
    }

    public static Task<bool> IsAssignableAsync(ApplicationDbContext db, string userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        return Assignable(db).AnyAsync(user => user.Id == userId, cancellationToken);
    }

    private static IQueryable<Models.ApplicationUser> Assignable(ApplicationDbContext db)
    {
        var officerIds =
            from userRole in db.UserRoles
            join role in db.Roles on userRole.RoleId equals role.Id
            where role.Name == RoleNames.ProjectOfficer
            select userRole.UserId;

        return db.Users.AsNoTracking()
            .Where(user => !user.IsDisabled && !user.PendingDeletion && officerIds.Contains(user.Id));
    }
}
