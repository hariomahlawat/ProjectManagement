using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Configuration;
using ProjectManagement.Services;
using ProjectManagement.Services.Admin;

namespace ProjectManagement.Areas.Admin.Pages.Users;

[Authorize(Policy = AdminPolicies.UsersManage)]
[ResponseCache(NoStore = true)]
public sealed class DetailsModel : PageModel
{
    private readonly IAdminUserQueryService _queries;
    private readonly IAdminRoleDescriptorCatalog _roles;
    private readonly IAuditActionPresentationCatalog _auditActions;
    private readonly IAdminTimeService _time;

    public DetailsModel(
        IAdminUserQueryService queries,
        IAdminRoleDescriptorCatalog roles,
        IAuditActionPresentationCatalog auditActions,
        IAdminTimeService time)
    {
        _queries = queries ?? throw new ArgumentNullException(nameof(queries));
        _roles = roles ?? throw new ArgumentNullException(nameof(roles));
        _auditActions = auditActions ?? throw new ArgumentNullException(nameof(auditActions));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public AdminPageHeaderModel Header { get; private set; } = new();

    public AdminUserDetails? Account { get; private set; }

    public IReadOnlyList<AdminRoleDescriptor> RoleDescriptors { get; private set; } =
        Array.Empty<AdminRoleDescriptor>();

    public IReadOnlyList<AdminUserAuthenticationActivity> RecentAuthentication { get; private set; } =
        Array.Empty<AdminUserAuthenticationActivity>();

    public IReadOnlyList<AdminUserAdministrativeActivity> RecentAdministrativeActivity { get; private set; } =
        Array.Empty<AdminUserAdministrativeActivity>();

    public bool IsCurrentUser => string.Equals(
        User.FindFirstValue(ClaimTypes.NameIdentifier),
        Account?.Id,
        StringComparison.Ordinal);

    public bool HasPrivilegedRole => RoleDescriptors.Any(role => role.IsPrivileged);

    public async Task<IActionResult> OnGetAsync(string id, CancellationToken cancellationToken)
    {
        Account = await _queries.GetDetailsAsync(id, cancellationToken);
        if (Account is null)
        {
            return NotFound();
        }

        RoleDescriptors = _roles.DescribeMany(Account.Roles);
        RecentAuthentication = await _queries.GetRecentLoginActivityAsync(
            Account.Id,
            limit: 10,
            cancellationToken);
        RecentAdministrativeActivity = await _queries.GetRecentAdministrativeActivityAsync(
            Account.Id,
            limit: 8,
            cancellationToken);

        Header = new AdminPageHeaderModel
        {
            Eyebrow = "User account",
            Title = string.IsNullOrWhiteSpace(Account.FullName) ? Account.UserName : Account.FullName,
            Description = $"Review identity, access state and recent security activity for @{Account.UserName}.",
            Icon = "bi-person-vcard",
            Actions = Account.AccountState.State == AdminUserAccountState.PendingDeletion
                ? new[]
                {
                    new AdminPageActionModel
                    {
                        Text = "Back to users",
                        Href = Url.Page("./Index"),
                        Icon = "bi-arrow-left"
                    },
                    new AdminPageActionModel
                    {
                        Text = "Review deletion",
                        Href = Url.Page("./Delete", new { id = Account.Id }),
                        Icon = "bi-person-x",
                        IsPrimary = true
                    }
                }
                : new[]
                {
                    new AdminPageActionModel
                    {
                        Text = "Back to users",
                        Href = Url.Page("./Index"),
                        Icon = "bi-arrow-left"
                    },
                    new AdminPageActionModel
                    {
                        Text = "Edit account",
                        Href = Url.Page("./Edit", new { id = Account.Id }),
                        Icon = "bi-pencil-square",
                        IsPrimary = true
                    }
                }
        };

        return Page();
    }

    public string FormatIst(DateTime? utc) => _time.FormatIst(utc);

    public string FormatIst(DateTimeOffset? utc) => _time.FormatIst(utc);

    public AuditActionPresentation AuditPresentation(AdminUserAdministrativeActivity activity) =>
        _auditActions.Describe(activity.Action, activity.Level);

    public string AuthenticationLabel(AdminUserAuthenticationActivity activity) =>
        activity.Event switch
        {
            AuthenticationEventNames.LoginSucceeded => "Successful sign-in",
            AuthenticationEventNames.AuditLoginSuccess => "Successful sign-in",
            AuthenticationEventNames.AuditLoginFailed => "Failed sign-in",
            AuthenticationEventNames.AuditLoginLockedOut => "Sign-in blocked — account locked",
            _ => activity.Succeeded ? "Successful authentication" : "Authentication failed"
        };

    public string AuthenticationTone(AdminUserAuthenticationActivity activity) =>
        activity.Succeeded ? "success" : "danger";

    public string AuthenticationIcon(AdminUserAuthenticationActivity activity) =>
        activity.Succeeded ? "bi-box-arrow-in-right" : "bi-shield-exclamation";

    public string UserAgentSummary(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return "Client information unavailable";
        }

        var trimmed = userAgent.Trim();
        return trimmed.Length <= 92 ? trimmed : $"{trimmed[..89]}…";
    }
}
