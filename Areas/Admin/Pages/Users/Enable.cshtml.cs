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
public sealed class EnableModel : PageModel
{
    private readonly IUserLifecycleService _lifecycle;
    private readonly IAdminUserQueryService _queries;
    private readonly ILogger<EnableModel> _logger;

    public EnableModel(
        IUserLifecycleService lifecycle,
        IAdminUserQueryService queries,
        ILogger<EnableModel> logger)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _queries = queries ?? throw new ArgumentNullException(nameof(queries));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public AdminUserDetails? Account { get; private set; }

    public AdminPageHeaderModel Header { get; private set; } = new();

    [BindProperty]
    public string ConfirmUser { get; set; } = string.Empty;

    [BindProperty]
    public bool Acknowledge { get; set; }

    public async Task<IActionResult> OnGetAsync(string id, CancellationToken cancellationToken)
    {
        Account = await _queries.GetDetailsAsync(id, cancellationToken);
        if (Account is null)
        {
            return NotFound();
        }

        if (Account.AccountState.State == AdminUserAccountState.PendingDeletion)
        {
            TempData[FlashMessageKeys.AdminUsersError] = "Undo the deletion request before enabling this account.";
            return RedirectToPage("./Details", new { id });
        }

        if (Account.AccountState.State != AdminUserAccountState.Disabled)
        {
            TempData[FlashMessageKeys.AdminUsersSuccess] = $"@{Account.UserName} is already enabled.";
            return RedirectToPage("./Details", new { id });
        }

        BuildHeader();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string id, CancellationToken cancellationToken)
    {
        Account = await _queries.GetDetailsAsync(id, cancellationToken);
        if (Account is null)
        {
            return NotFound();
        }

        BuildHeader();
        ConfirmUser = ConfirmUser?.Trim() ?? string.Empty;

        if (Account.AccountState.State == AdminUserAccountState.PendingDeletion)
        {
            ModelState.AddModelError(string.Empty, "Undo the deletion request before enabling this account.");
        }
        else if (Account.AccountState.State != AdminUserAccountState.Disabled)
        {
            ModelState.AddModelError(string.Empty, "This account is not disabled.");
        }

        if (!string.Equals(ConfirmUser, Account.UserName, StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(ConfirmUser), $"Type {Account.UserName} exactly to confirm.");
        }

        if (!Acknowledge)
        {
            ModelState.AddModelError(nameof(Acknowledge), "Acknowledge that access will be restored.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        try
        {
            await _lifecycle.EnableAsync(id, actorId);
            TempData[FlashMessageKeys.AdminUsersSuccess] = $"@{Account.UserName} was enabled. Account lockout was cleared.";
            return RedirectToPage("./Details", new { id });
        }
        catch (InvalidOperationException exception)
        {
            var reference = HttpContext.TraceIdentifier;
            _logger.LogWarning(
                exception,
                "Enable request for user {UserId} was rejected by an account safeguard. Reference {Reference}.",
                id,
                reference);
            ModelState.AddModelError(
                string.Empty,
                PublicEnableFailure(exception.Message, reference));
            return Page();
        }
        catch (Exception exception)
        {
            var reference = HttpContext.TraceIdentifier;
            _logger.LogError(exception, "Failed to enable user {UserId}. Reference {Reference}.", id, reference);
            ModelState.AddModelError(string.Empty, $"The account could not be enabled. Reference: {reference}.");
            return Page();
        }
    }


    private static string PublicEnableFailure(string? internalMessage, string reference) =>
        internalMessage switch
        {
            "The account is pending deletion. Undo the deletion request before enabling it." =>
                "Undo the deletion request before enabling this account.",
            "User not found." =>
                "The account no longer exists or was removed by another administrator.",
            _ => $"The account could not be enabled. Reference: {reference}."
        };

    private void BuildHeader()
    {
        Header = new AdminPageHeaderModel
        {
            Eyebrow = "Access control",
            Title = "Enable account",
            Description = Account is null
                ? "Restore sign-in access."
                : $"Restore sign-in access for @{Account.UserName} and clear account lockout.",
            Icon = "bi-person-check",
            Actions = Account is null
                ? Array.Empty<AdminPageActionModel>()
                : new[]
                {
                    new AdminPageActionModel
                    {
                        Text = "Back to account",
                        Href = Url.Page("./Details", new { id = Account.Id }),
                        Icon = "bi-arrow-left"
                    }
                }
        };
    }
}
