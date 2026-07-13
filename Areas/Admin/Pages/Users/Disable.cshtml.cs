using System.ComponentModel.DataAnnotations;
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
public sealed class DisableModel : PageModel
{
    private readonly IUserLifecycleService _lifecycle;
    private readonly IAdminUserQueryService _queries;
    private readonly ILogger<DisableModel> _logger;

    public DisableModel(
        IUserLifecycleService lifecycle,
        IAdminUserQueryService queries,
        ILogger<DisableModel> logger)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _queries = queries ?? throw new ArgumentNullException(nameof(queries));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public AdminUserDetails? Account { get; private set; }

    public AdminPageHeaderModel Header { get; private set; } = new();

    [BindProperty, Required, StringLength(500, MinimumLength = 5)]
    [Display(Name = "Reason for disabling access")]
    public string Reason { get; set; } = string.Empty;

    [BindProperty, Display(Name = "Username confirmation")]
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

        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.Equals(actorId, Account.Id, StringComparison.Ordinal))
        {
            TempData[FlashMessageKeys.AdminUsersError] = "You cannot disable your own signed-in account.";
            return RedirectToPage("./Details", new { id });
        }

        if (Account.AccountState.State == AdminUserAccountState.PendingDeletion)
        {
            TempData[FlashMessageKeys.AdminUsersError] = "This account is pending deletion. Review or undo that request before changing its state.";
            return RedirectToPage("./Details", new { id });
        }

        if (Account.AccountState.State == AdminUserAccountState.Disabled)
        {
            TempData[FlashMessageKeys.AdminUsersSuccess] = $"@{Account.UserName} is already disabled.";
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
        Reason = Reason?.Trim() ?? string.Empty;
        ConfirmUser = ConfirmUser?.Trim() ?? string.Empty;

        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        if (string.Equals(actorId, Account.Id, StringComparison.Ordinal))
        {
            ModelState.AddModelError(string.Empty, "You cannot disable your own signed-in account.");
        }

        if (Account.AccountState.State == AdminUserAccountState.PendingDeletion)
        {
            ModelState.AddModelError(string.Empty, "This account is pending deletion. Undo that request before changing its state.");
        }
        else if (Account.AccountState.State == AdminUserAccountState.Disabled)
        {
            ModelState.AddModelError(string.Empty, "This account is already disabled.");
        }

        if (Reason.Length < 5)
        {
            ModelState.AddModelError(nameof(Reason), "Provide a reason of at least 5 characters.");
        }

        if (!string.Equals(ConfirmUser, Account.UserName, StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(ConfirmUser), $"Type {Account.UserName} exactly to confirm.");
        }

        if (!Acknowledge)
        {
            ModelState.AddModelError(nameof(Acknowledge), "Acknowledge the operational impact before disabling the account.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            await _lifecycle.DisableAsync(id, actorId, Reason);
            TempData[FlashMessageKeys.AdminUsersSuccess] = $"@{Account.UserName} was disabled. Existing sessions were invalidated.";
            return RedirectToPage("./Details", new { id });
        }
        catch (InvalidOperationException exception)
        {
            var reference = HttpContext.TraceIdentifier;
            _logger.LogWarning(
                exception,
                "Disable request for user {UserId} was rejected by an account safeguard. Reference {Reference}.",
                id,
                reference);
            ModelState.AddModelError(
                string.Empty,
                PublicDisableFailure(exception.Message, reference));
            return Page();
        }
        catch (Exception exception)
        {
            var reference = HttpContext.TraceIdentifier;
            _logger.LogError(exception, "Failed to disable user {UserId}. Reference {Reference}.", id, reference);
            ModelState.AddModelError(string.Empty, $"The account could not be disabled. Reference: {reference}.");
            return Page();
        }
    }


    private static string PublicDisableFailure(string? internalMessage, string reference) =>
        internalMessage switch
        {
            "You cannot disable your own account." =>
                "You cannot disable your own signed-in account.",
            "The account is pending deletion. Undo the deletion request before changing its status." =>
                "Undo the deletion request before changing this account's state.",
            "Cannot disable the last active Admin." =>
                "This account is the last active Administrator and cannot be disabled.",
            "User not found." =>
                "The account no longer exists or was removed by another administrator.",
            _ => $"The account could not be disabled. Reference: {reference}."
        };

    private void BuildHeader()
    {
        Header = new AdminPageHeaderModel
        {
            Eyebrow = "Access control",
            Title = "Disable account",
            Description = Account is null
                ? "Block sign-in access while preserving records."
                : $"Block sign-in access for @{Account.UserName} while preserving records and ownership history.",
            Icon = "bi-person-slash",
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
