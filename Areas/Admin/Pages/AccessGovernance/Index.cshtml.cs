using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.AccessGovernance;

namespace ProjectManagement.Areas.Admin.Pages.AccessGovernance;

[Authorize(Policy = AdminPolicies.AccessGovernanceView)]
[ResponseCache(NoStore = true)]
public sealed class IndexModel : PageModel
{
    private readonly IAdminAccessGovernanceService _governance;
    private readonly ISafeCsvWriter _csv;
    private readonly IAdminTimeService _time;
    private readonly IAdminRoleDescriptorCatalog _roles;

    public IndexModel(
        IAdminAccessGovernanceService governance,
        ISafeCsvWriter csv,
        IAdminTimeService time,
        IAdminRoleDescriptorCatalog roles)
    {
        _governance = governance ?? throw new ArgumentNullException(nameof(governance));
        _csv = csv ?? throw new ArgumentNullException(nameof(csv));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _roles = roles ?? throw new ArgumentNullException(nameof(roles));
    }

    public AccessGovernanceSnapshot Snapshot { get; private set; } = EmptySnapshot();
    public AdminPageHeaderModel Header { get; private set; } = new();
    public IReadOnlyList<string> MatrixRoles { get; private set; } = Array.Empty<string>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Snapshot = await _governance.GetSnapshotAsync(cancellationToken);
        MatrixRoles = Snapshot.Roles
            .OrderByDescending(role => role.IsPrivileged)
            .ThenBy(role => _roles.Describe(role.Name).SortOrder)
            .Select(role => role.Name)
            .ToArray();
        Header = BuildHeader();
    }

    public async Task<IActionResult> OnGetExportAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _governance.GetSnapshotAsync(cancellationToken);
        var rows = snapshot.Users.Select(user => (IReadOnlyList<object?>)new object?[]
        {
            user.UserName,
            user.FullName,
            user.Rank,
            AccountState(user),
            string.Join(';', user.Roles),
            string.Join(';', user.PrivilegedRoles),
            _time.FormatIst(user.LastLoginUtc),
            user.MustChangePassword ? "Yes" : "No",
            string.Join(" | ", user.Findings.Select(finding => finding.Title))
        });

        var bytes = _csv.Write(
            new[]
            {
                "Username", "FullName", "Rank", "AccountState", "Roles",
                "PrivilegedRoles", "LastLoginIST", "PasswordChangeRequired", "ReviewFindings"
            },
            rows);

        return File(
            bytes,
            "text/csv; charset=utf-8",
            $"prism-access-review-{_time.UtcNow:yyyyMMdd}.csv");
    }

    public bool RoleGrants(AdminCapabilityDescriptor capability, string roleName) =>
        capability.PermittedRoles.Contains(roleName, StringComparer.OrdinalIgnoreCase);

    public string RoleDisplayName(string roleName) => _roles.Describe(roleName).DisplayName;

    public string FormatIst(DateTimeOffset? value) => _time.FormatIst(value);

    public string AccountState(AccessGovernanceUserRow user) =>
        user.PendingDeletion ? "Pending deletion" : user.IsDisabled ? "Disabled" : user.IsLocked ? "Locked" : "Active";

    public string UserUrl(string userId) =>
        Url.Page("/Users/Details", new { area = "Admin", id = userId }) ?? "#";

    private AdminPageHeaderModel BuildHeader() => new()
    {
        Eyebrow = "Access & security",
        Title = "Access governance",
        Description = "Review privileged role holdings, policy coverage and account conditions requiring administrator attention.",
        Icon = "bi-shield-check",
        Actions = new[]
        {
            new AdminPageActionModel
            {
                Text = "Access guidance",
                Href = (Url.Page("/Help/Index", new { area = "Admin" }) ?? "/Admin/Help") + "#access-governance",
                Icon = "bi-question-circle"
            },
            new AdminPageActionModel
            {
                Text = "Export access review",
                Href = Url.Page("/AccessGovernance/Index", "Export", new { area = "Admin" }),
                Icon = "bi-download",
                IsPrimary = true
            }
        }
    };

    private static AccessGovernanceSnapshot EmptySnapshot() => new(
        Array.Empty<AccessGovernanceRoleRow>(),
        Array.Empty<AdminCapabilityDescriptor>(),
        Array.Empty<AccessGovernanceUserRow>(),
        Array.Empty<AccessGovernanceUserRow>(),
        Array.Empty<AccessGovernanceFinding>(),
        0, 0, 0, 0, 0, 0);
}
