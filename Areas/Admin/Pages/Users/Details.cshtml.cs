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
    private readonly IAdminClientDescriptorService _clients;
    private readonly IAdminTimeService _time;
    private readonly IUserManagementService _users;

    public DetailsModel(
        IAdminUserQueryService queries,
        IAdminRoleDescriptorCatalog roles,
        IAuditActionPresentationCatalog auditActions,
        IAdminClientDescriptorService clients,
        IAdminTimeService time,
        IUserManagementService users)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _queries = queries ?? throw new ArgumentNullException(nameof(queries));
        _roles = roles ?? throw new ArgumentNullException(nameof(roles));
        _auditActions = auditActions ?? throw new ArgumentNullException(nameof(auditActions));
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
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

    // An account locked after repeated failed sign-ins used to stay locked until the lockout
    // expired: nothing in Administration could clear it, and a password reset did not either.
    public async Task<IActionResult> OnPostUnlockAsync(string id)
    {
        var result = await _users.UnlockAsync(id);
        if (result.Succeeded)
        {
            TempData[FlashMessageKeys.AdminUsersSuccess] = "The lockout was cleared. The user can sign in again.";
        }
        else
        {
            TempData[FlashMessageKeys.AdminUsersError] = string.Join(" ", result.Errors.Select(error => error.Description));
        }

        return RedirectToPage(new { id });
    }

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
            limit: 5,
            cancellationToken);
        RecentAdministrativeActivity = await _queries.GetRecentAdministrativeActivityAsync(
            Account.Id,
            limit: 5,
            cancellationToken);

        Header = new AdminPageHeaderModel
        {
            Title = string.IsNullOrWhiteSpace(Account.FullName) ? Account.UserName : Account.FullName,
            Description = $"Who @{Account.UserName} is, what they can do in PRISM, and their recent sign-ins.",
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

    public AdminClientDescriptor ClientDescriptor(string? userAgent) => _clients.Describe(userAgent);
}
