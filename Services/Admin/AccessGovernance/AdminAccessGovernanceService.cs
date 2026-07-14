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

        var allFindings = globalFindings
            .Concat(userRows.SelectMany(item => item.Findings))
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
            allFindings.Count(item => item.Severity >= AccessGovernanceFindingSeverity.Review));
    }

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
                "No active Administrator remains",
                "Administrative recovery and security operations may become inaccessible.",
                RoleName: RoleNames.Admin));
        }
        else if (activeAdmins == 1)
        {
            findings.Add(new AccessGovernanceFinding(
                "single-active-admin",
                AccessGovernanceFindingSeverity.Review,
                "Only one active Administrator remains",
                "Review succession and emergency-access arrangements before changing this account.",
                RoleName: RoleNames.Admin));
        }

        var rolelessCount = users.Count(user => !rolesByUser.TryGetValue(user.Id, out var roles) || roles.Count == 0);
        if (rolelessCount > 0)
        {
            findings.Add(new AccessGovernanceFinding(
                "roleless-users",
                AccessGovernanceFindingSeverity.Review,
                $"{rolelessCount} user account{(rolelessCount == 1 ? string.Empty : "s")} without a role",
                "Role-less accounts cannot access role-protected workflows and should be reviewed."));
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
                "user-no-role",
                AccessGovernanceFindingSeverity.Review,
                "No role assigned",
                "The account has no authorised operational capability.",
                user.Id));
        }

        if (privilegedRoles.Count > 0 && user.PendingDeletion)
        {
            findings.Add(new AccessGovernanceFinding(
                "privileged-pending-deletion",
                AccessGovernanceFindingSeverity.Critical,
                "Privileged account pending deletion",
                "Remove or transfer privileged access before the deletion workflow completes.",
                user.Id));
        }
        else if (privilegedRoles.Count > 0 && user.IsDisabled)
        {
            findings.Add(new AccessGovernanceFinding(
                "privileged-disabled",
                AccessGovernanceFindingSeverity.Review,
                "Disabled account still holds privileged roles",
                "The role assignment remains effective if the account is re-enabled.",
                user.Id));
        }
        else if (privilegedRoles.Count > 0 && IsLocked(user))
        {
            findings.Add(new AccessGovernanceFinding(
                "privileged-locked",
                AccessGovernanceFindingSeverity.Review,
                "Privileged account is temporarily locked",
                "Review failed authentication activity before relying on this account for administrative recovery.",
                user.Id));
        }

        if (privilegedRoles.Count > 0 && user.LastLoginUtc is null)
        {
            findings.Add(new AccessGovernanceFinding(
                "privileged-never-signed-in",
                AccessGovernanceFindingSeverity.Review,
                "Privileged account has never signed in",
                "Confirm that the access assignment remains necessary and that credentials were issued securely.",
                user.Id));
        }

        if (privilegedRoles.Count > 0 && user.MustChangePassword)
        {
            findings.Add(new AccessGovernanceFinding(
                "privileged-password-change",
                AccessGovernanceFindingSeverity.Review,
                "Privileged account requires a password change",
                "The user must establish a personal password before normal access continues.",
                user.Id));
        }

        if (privilegedRoles.Count > 1)
        {
            findings.Add(new AccessGovernanceFinding(
                "multiple-privileged-roles",
                AccessGovernanceFindingSeverity.Information,
                "Multiple privileged roles assigned",
                $"Assigned privileged roles: {string.Join(", ", privilegedRoles)}.",
                user.Id));
        }

        return findings;
    }

    private bool IsActive(UserProjection user) =>
        !user.IsDisabled && !user.PendingDeletion && !IsLocked(user);

    private bool IsLocked(UserProjection user) =>
        user.LockoutEnd.HasValue && user.LockoutEnd.Value > _time.UtcNow;

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
