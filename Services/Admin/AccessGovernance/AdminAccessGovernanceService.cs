using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;

namespace ProjectManagement.Services.Admin.AccessGovernance;

public enum AccessGovernanceFindingSeverity
{
    Information = 0,
    Review = 1,
    Critical = 2
}

public sealed record AccessGovernanceFinding(
    string Code,
    AccessGovernanceFindingSeverity Severity,
    string Title,
    string Description,
    string? UserId = null,
    string? RoleName = null);

public sealed record AccessGovernanceRoleRow(
    string Name,
    string DisplayName,
    string Description,
    string Category,
    string Icon,
    bool IsPrivileged,
    bool IsConfigured,
    int AssignedUsers,
    int ActiveUsers,
    IReadOnlyList<AdminCapabilityDescriptor> Capabilities,
    IReadOnlyList<string> UserNames);

public sealed record AccessGovernanceUserRow(
    string Id,
    string UserName,
    string FullName,
    string Rank,
    bool IsDisabled,
    bool IsLocked,
    bool PendingDeletion,
    bool MustChangePassword,
    DateTimeOffset? LastLoginUtc,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> PrivilegedRoles,
    IReadOnlyList<AccessGovernanceFinding> Findings);

public sealed record AccessGovernanceSnapshot(
    IReadOnlyList<AccessGovernanceRoleRow> Roles,
    IReadOnlyList<AdminCapabilityDescriptor> Capabilities,
    IReadOnlyList<AccessGovernanceUserRow> Users,
    IReadOnlyList<AccessGovernanceUserRow> PrivilegedUsers,
    IReadOnlyList<AccessGovernanceFinding> Findings,
    int PrivilegedUserCount,
    int AdministratorCount,
    int UsersWithoutRoles,
    int RestrictedPrivilegedUsers,
    int SingleHolderCriticalRoles,
    int AccessIssueCount);

public interface IAdminAccessGovernanceService
{
    Task<AccessGovernanceSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}

public sealed class AdminAccessGovernanceService : IAdminAccessGovernanceService
{
    private readonly ApplicationDbContext _db;
    private readonly IAdminRoleDescriptorCatalog _roleCatalog;
    private readonly IAdminCapabilityCatalog _capabilityCatalog;
    private readonly IAdminTimeService _time;

    public AdminAccessGovernanceService(
        ApplicationDbContext db,
        IAdminRoleDescriptorCatalog roleCatalog,
        IAdminCapabilityCatalog capabilityCatalog,
        IAdminTimeService time)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _roleCatalog = roleCatalog ?? throw new ArgumentNullException(nameof(roleCatalog));
        _capabilityCatalog = capabilityCatalog ?? throw new ArgumentNullException(nameof(capabilityCatalog));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public async Task<AccessGovernanceSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var users = await _db.Users.AsNoTracking()
            .Select(user => new UserProjection(
                user.Id,
                user.UserName ?? string.Empty,
                user.FullName,
                user.Rank,
                user.IsDisabled,
                user.LockoutEnd,
                user.PendingDeletion,
                user.MustChangePassword,
                user.LastLoginUtc))
            .ToListAsync(cancellationToken);

        var roleAssignments = await (
            from userRole in _db.UserRoles.AsNoTracking()
            join role in _db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            select new RoleAssignment(userRole.UserId, role.Name ?? string.Empty))
            .ToListAsync(cancellationToken);

        var rolesByUser = roleAssignments
            .GroupBy(item => item.UserId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(item => item.RoleName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => _roleCatalog.Describe(name).SortOrder)
                    .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray());

        var activeUsers = users.Where(IsActive).ToDictionary(item => item.Id, StringComparer.Ordinal);
        var configuredRoleNames = (await _db.Roles.AsNoTracking()
                .Select(role => role.Name ?? string.Empty)
                .Where(name => name != string.Empty)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allRoleNames = configuredRoleNames
            .Concat(_capabilityCatalog.Capabilities.SelectMany(item => item.PermittedRoles))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var roleRows = new List<AccessGovernanceRoleRow>();
        foreach (var roleName in allRoleNames)
        {
            var descriptor = _roleCatalog.Describe(roleName);
            var assignedIds = roleAssignments
                .Where(item => string.Equals(item.RoleName, roleName, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.UserId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            // An old role name nobody holds is not a role anyone needs to review.
            if (assignedIds.Length == 0 && RoleNames.IsLegacyAlias(roleName))
            {
                continue;
            }

            var assignedNames = users
                .Where(user => assignedIds.Contains(user.Id, StringComparer.Ordinal))
                .Select(DisplayName)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToArray();

            roleRows.Add(new AccessGovernanceRoleRow(
                roleName,
                descriptor.DisplayName,
                descriptor.Description,
                descriptor.Category,
                descriptor.Icon,
                descriptor.IsPrivileged,
                configuredRoleNames.Contains(roleName),
                assignedIds.Length,
                assignedIds.Count(activeUsers.ContainsKey),
                _capabilityCatalog.ForRole(roleName),
                assignedNames));
        }

        roleRows = roleRows
            .OrderByDescending(item => item.IsPrivileged)
            .ThenBy(item => _roleCatalog.Describe(item.Name).SortOrder)
            .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var globalFindings = BuildGlobalFindings(users, rolesByUser, roleRows);
        var userRows = new List<AccessGovernanceUserRow>();

        foreach (var user in users)
        {
            var roles = rolesByUser.GetValueOrDefault(user.Id) ?? Array.Empty<string>();
            var privilegedRoles = roles
                .Where(role => _roleCatalog.Describe(role).IsPrivileged)
                .ToArray();
            var findings = BuildUserFindings(user, roles, privilegedRoles);

            userRows.Add(new AccessGovernanceUserRow(
                user.Id,
                user.UserName,
                user.FullName,
                user.Rank,
                user.IsDisabled,
                IsLocked(user),
                user.PendingDeletion,
                user.MustChangePassword,
                ToOffset(user.LastLoginUtc),
                roles,
                privilegedRoles,
                findings));
        }

        // One list, counted once (docs/ux/admin-review-fixes.md, PR 8): an account without a role
        // is already covered by the "accounts without a role" line, so it is not listed again.
        var allFindings = globalFindings
            .Concat(userRows.SelectMany(item => item.Findings).Where(item => item.Code != NoRoleCode))
            .DistinctBy(item => (item.Code, item.UserId, item.RoleName))
            .OrderByDescending(item => item.Severity)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var privilegedUsers = userRows
            .Where(item => item.PrivilegedRoles.Count > 0)
            .OrderByDescending(item => item.Findings.Any(finding => finding.Severity == AccessGovernanceFindingSeverity.Critical))
            .ThenByDescending(item => item.Findings.Any())
            .ThenBy(item => item.FullName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var adminRole = roleRows.FirstOrDefault(item => string.Equals(item.Name, RoleNames.Admin, StringComparison.OrdinalIgnoreCase));
        var singleHolderRoles = roleRows.Count(item => item.IsPrivileged && item.ActiveUsers == 1);
        var restrictedPrivileged = privilegedUsers.Count(item => item.IsDisabled || item.PendingDeletion);
        var usersWithoutRoles = users.Count(user => !rolesByUser.TryGetValue(user.Id, out var roles) || roles.Count == 0);

        return new AccessGovernanceSnapshot(
            roleRows,
            _capabilityCatalog.Capabilities,
            userRows.OrderBy(item => item.FullName, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.UserName, StringComparer.OrdinalIgnoreCase).ToArray(),
            privilegedUsers,
            allFindings,
            privilegedUsers.Length,
            adminRole?.ActiveUsers ?? 0,
            usersWithoutRoles,
            restrictedPrivileged,
            singleHolderRoles,
            allFindings.Length);
    }

    internal const string NoRoleCode = "user-no-role";

    private static IReadOnlyList<AccessGovernanceFinding> BuildGlobalFindings(
        IReadOnlyList<UserProjection> users,
        IReadOnlyDictionary<string, IReadOnlyList<string>> rolesByUser,
        IReadOnlyList<AccessGovernanceRoleRow> roleRows)
    {
        var findings = new List<AccessGovernanceFinding>();
        var activeAdmins = roleRows.FirstOrDefault(item => string.Equals(item.Name, RoleNames.Admin, StringComparison.OrdinalIgnoreCase))?.ActiveUsers ?? 0;

        if (activeAdmins == 0)
        {
            findings.Add(new AccessGovernanceFinding(
                "no-active-admin",
                AccessGovernanceFindingSeverity.Critical,
                "No active Administrator",
                "Nobody can manage accounts, restore deleted items or change settings. Give the Administrator role to a trusted account.",
                RoleName: RoleNames.Admin));
        }
        else if (activeAdmins == 1)
        {
            findings.Add(new AccessGovernanceFinding(
                "single-active-admin",
                AccessGovernanceFindingSeverity.Review,
                "Only one active Administrator",
                "If this account is locked out or away, nobody can manage PRISM. Give the Administrator role to a second trusted person.",
                RoleName: RoleNames.Admin));
        }

        var rolelessCount = users.Count(user => !rolesByUser.TryGetValue(user.Id, out var roles) || roles.Count == 0);
        if (rolelessCount > 0)
        {
            findings.Add(new AccessGovernanceFinding(
                "roleless-users",
                AccessGovernanceFindingSeverity.Review,
                $"{rolelessCount} user account{(rolelessCount == 1 ? string.Empty : "s")} without a role",
                "These people can sign in but cannot open pages that need a role. Give each a role, or disable the account."));
        }

        return findings;
    }

    private IReadOnlyList<AccessGovernanceFinding> BuildUserFindings(
        UserProjection user,
        IReadOnlyList<string> roles,
        IReadOnlyList<string> privilegedRoles)
    {
        var findings = new List<AccessGovernanceFinding>();

        if (roles.Count == 0)
        {
            findings.Add(new AccessGovernanceFinding(
                NoRoleCode,
                AccessGovernanceFindingSeverity.Review,
                "No role given",
                "This account can sign in but cannot open pages that need a role.",
                user.Id));
        }

        if (privilegedRoles.Count > 0 && user.PendingDeletion)
        {
            findings.Add(new AccessGovernanceFinding(
                "privileged-pending-deletion",
                AccessGovernanceFindingSeverity.Critical,
                "Senior account waiting to be deleted",
                "The account is deleted with its roles when the deletion window ends. Make sure another active account holds these responsibilities; to change this account's roles first, undo the deletion request.",
                user.Id));
        }
        else if (privilegedRoles.Count > 0 && user.IsDisabled)
        {
            findings.Add(new AccessGovernanceFinding(
                "privileged-disabled",
                AccessGovernanceFindingSeverity.Review,
                "Disabled account still has a senior role",
                "If the account is enabled again, the role works again straight away.",
                user.Id));
        }
        else if (privilegedRoles.Count > 0 && IsLocked(user))
        {
            findings.Add(new AccessGovernanceFinding(
                "privileged-locked",
                AccessGovernanceFindingSeverity.Review,
                "Senior account is locked for now",
                "It was locked after failed sign-ins. Check Sign-in activity before relying on this account.",
                user.Id));
        }

        if (privilegedRoles.Count > 0 && user.LastLoginUtc is null)
        {
            findings.Add(new AccessGovernanceFinding(
                "privileged-never-signed-in",
                AccessGovernanceFindingSeverity.Review,
                "Senior account has never signed in",
                "Check the person still needs this role and received their password safely.",
                user.Id));
        }

        if (privilegedRoles.Count > 0 && user.MustChangePassword)
        {
            findings.Add(new AccessGovernanceFinding(
                "privileged-password-change",
                AccessGovernanceFindingSeverity.Review,
                "Senior account must change its password",
                "The person must set their own password at their next sign-in.",
                user.Id));
        }

        // Holding more than one privileged role (an Administrator who is also HoD) is normal, so it
        // is not a finding.

        return findings;
    }

    // The same rule the last-Administrator guards enforce (ActiveAdministratorRule): a temporarily
    // locked account still counts as active, so "No active Administrator" and "Only one active
    // Administrator" agree with what the guards allow. A lock is reported as its own finding.
    private static bool IsActive(UserProjection user) =>
        ActiveAdministratorRule.IsActive(user.IsDisabled, user.PendingDeletion);

    private bool IsLocked(UserProjection user) =>
        ActiveAdministratorRule.IsTemporarilyLocked(user.LockoutEnd, _time.UtcNow);

    private static string DisplayName(UserProjection user) =>
        string.IsNullOrWhiteSpace(user.FullName) ? user.UserName : user.FullName;

    private static DateTimeOffset? ToOffset(DateTime? value)
    {
        if (!value.HasValue) return null;
        var utc = value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
        };
        return new DateTimeOffset(utc);
    }

    private sealed record UserProjection(
        string Id,
        string UserName,
        string FullName,
        string Rank,
        bool IsDisabled,
        DateTimeOffset? LockoutEnd,
        bool PendingDeletion,
        bool MustChangePassword,
        DateTime? LastLoginUtc);

    private sealed record RoleAssignment(string UserId, string RoleName);
}
