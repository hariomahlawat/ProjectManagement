using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Models;

namespace ProjectManagement.Services.ActionTasks;

public sealed class ActionTaskUserLookupService
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly ActionTaskPermissionService _permission;
    private readonly IActionTrackerClock _clock;

    public ActionTaskUserLookupService(UserManager<ApplicationUser> users, ActionTaskPermissionService permission, IActionTrackerClock clock)
    {
        _users = users;
        _permission = permission;
        _clock = clock;
    }

    // SECTION: Assignable-user lookup centralizes active-account and role assignment filtering.
    public async Task<IReadOnlyList<UserOption>> LoadAssignableUsersAsync(string currentRole)
    {
        var utcNow = new DateTimeOffset(_clock.UtcNow, TimeSpan.Zero);
        var users = await _users.Users
            .Where(u => !u.IsDisabled)
            .Where(u => !u.PendingDeletion)
            .Where(u => !u.LockoutEnd.HasValue || u.LockoutEnd <= utcNow)
            .OrderBy(u => u.Rank)
            .ThenBy(u => u.FullName)
            .ThenBy(u => u.UserName)
            .Take(200)
            .ToListAsync();

        var list = new List<UserOption>();
        foreach (var user in users)
        {
            var roles = await _users.GetRolesAsync(user);
            var matchedRole = ActionTaskRoleResolver.ResolveAssignableRoleFromRoles(roles);
            if (matchedRole is null || !_permission.CanAssign(currentRole, matchedRole))
            {
                continue;
            }

            list.Add(new UserOption(user.Id, BuildPersonDisplayName(user), matchedRole));
        }

        return list;
    }

    // SECTION: Task assignee display lookup keeps register and card projection names consistent.
    public Task<IReadOnlyDictionary<string, string>> LoadTaskAssigneeNamesAsync(IReadOnlyList<ActionTaskItem> tasks)
        => LoadUserDisplayNamesAsync(tasks.Select(t => t.AssignedToUserId));

    // SECTION: Task audit actor display lookup keeps inspector history names consistent.
    public Task<IReadOnlyDictionary<string, string>> LoadTaskActorNamesAsync(IReadOnlyList<ActionTaskAuditLog> logs)
        => LoadUserDisplayNamesAsync(logs.Select(log => log.PerformedByUserId));

    // SECTION: Sprint audit actor display lookup keeps lifecycle history names consistent.
    public Task<IReadOnlyDictionary<string, string>> LoadSprintActorNamesAsync(IReadOnlyList<ActionSprintAuditLog> logs)
        => LoadUserDisplayNamesAsync(logs.Select(log => log.PerformedByUserId));

    // SECTION: Collaboration update actor merge fills names that are absent from audit actor lookups.
    public async Task<IReadOnlyDictionary<string, string>> MergeUpdateActorNamesAsync(IReadOnlyDictionary<string, string> current, IReadOnlyList<ActionTaskUpdate> updates)
    {
        var updateNames = await LoadUserDisplayNamesAsync(updates.Select(update => update.CreatedByUserId));
        if (updateNames.Count == 0)
        {
            return current;
        }

        var merged = new Dictionary<string, string>(current, StringComparer.Ordinal);
        foreach (var name in updateNames)
        {
            merged.TryAdd(name.Key, name.Value);
        }

        return merged;
    }

    // SECTION: Shared user display-name query avoids duplicate actor and assignee projection logic.
    private async Task<IReadOnlyDictionary<string, string>> LoadUserDisplayNamesAsync(IEnumerable<string?> userIds)
    {
        var ids = userIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (ids.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var users = await _users.Users
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.Rank, u.FullName, u.UserName, u.Email })
            .ToListAsync();

        return users.ToDictionary(
            u => u.Id,
            u => BuildPersonDisplayName(u.Rank, u.FullName, u.UserName, u.Email),
            StringComparer.Ordinal);
    }

    // SECTION: Person display helper preserves rank and full-name-first rendering across Action Tracker views.
    private static string BuildPersonDisplayName(ApplicationUser user)
        => BuildPersonDisplayName(user.Rank, user.FullName, user.UserName, user.Email);

    private static string BuildPersonDisplayName(string? rank, string? fullName, string? userName, string? email)
    {
        var trimmedRank = rank?.Trim();
        var trimmedFullName = fullName?.Trim();

        if (!string.IsNullOrWhiteSpace(trimmedRank) && !string.IsNullOrWhiteSpace(trimmedFullName))
        {
            return $"{trimmedRank} {trimmedFullName}";
        }

        if (!string.IsNullOrWhiteSpace(trimmedFullName))
        {
            return trimmedFullName;
        }

        if (!string.IsNullOrWhiteSpace(userName))
        {
            return userName;
        }

        return string.IsNullOrWhiteSpace(email) ? "User" : email;
    }
}

public sealed record UserOption(string UserId, string DisplayName, string Role);
