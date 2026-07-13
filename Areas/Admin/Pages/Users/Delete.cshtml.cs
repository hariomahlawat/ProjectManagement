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
public sealed class DeleteModel : PageModel
{
    private readonly IUserLifecycleService _lifecycle;
    private readonly IAdminUserQueryService _queries;
    private readonly IAdminTimeService _time;
    private readonly ILogger<DeleteModel> _logger;

    public DeleteModel(
        IUserLifecycleService lifecycle,
        IAdminUserQueryService queries,
        IAdminTimeService time,
        ILogger<DeleteModel> logger)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _queries = queries ?? throw new ArgumentNullException(nameof(queries));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public AdminUserDetails? Account { get; private set; }

    public AdminPageHeaderModel Header { get; private set; } = new();

    [BindProperty]
    public string ConfirmUser { get; set; } = string.Empty;

    [BindProperty]
    public bool Acknowledge { get; set; }

    public bool UndoWindowOpen => Account?.ScheduledPurgeUtc is DateTime purgeUtc
        && _time.UtcNow.UtcDateTime < purgeUtc;

    public string FormatIst(DateTime? utc) => _time.FormatIst(utc);

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
            TempData[FlashMessageKeys.AdminUsersError] = "You cannot request deletion of your own signed-in account.";
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
        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        if (string.Equals(actorId, Account.Id, StringComparison.Ordinal))
        {
            ModelState.AddModelError(string.Empty, "You cannot request deletion of your own signed-in account.");
        }

        if (Account.AccountState.State == AdminUserAccountState.PendingDeletion)
        {
            ModelState.AddModelError(string.Empty, "A deletion request already exists for this account.");
            return Page();
        }

        if (!Account.CanRequestHardDelete)
        {
            ModelState.AddModelError(string.Empty, "This account is outside the controlled hard-deletion window. Disable it instead to preserve audit and ownership history.");
            return Page();
        }

        ConfirmUser = ConfirmUser?.Trim() ?? string.Empty;
        if (!string.Equals(ConfirmUser, Account.UserName, StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(ConfirmUser), $"Type {Account.UserName} exactly to confirm.");
        }

        if (!Acknowledge)
        {
            ModelState.AddModelError(nameof(Acknowledge), "Acknowledge the permanent-removal risk before continuing.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            var result = await _lifecycle.RequestHardDeleteAsync(id, actorId);
            if (!result.Allowed)
            {
                ModelState.AddModelError(string.Empty, result.ReasonBlocked ?? "The deletion request was not permitted.");
                return Page();
            }

            var dueText = result.ScheduledPurgeUtc.HasValue
                ? _time.FormatIst(result.ScheduledPurgeUtc)
                : "the configured purge time";
            TempData[FlashMessageKeys.AdminUsersSuccess] = $"Deletion requested for @{Account.UserName}. The request can be withdrawn until {dueText}.";
            return RedirectToPage("./Details", new { id });
        }
        catch (Exception exception)
        {
            var reference = HttpContext.TraceIdentifier;
            _logger.LogError(exception, "Failed to request deletion for user {UserId}. Reference {Reference}.", id, reference);
            ModelState.AddModelError(string.Empty, $"The deletion request could not be completed. Reference: {reference}.");
            return Page();
        }
    }

    public async Task<IActionResult> OnPostUndoAsync(string id, CancellationToken cancellationToken)
    {
        Account = await _queries.GetDetailsAsync(id, cancellationToken);
        if (Account is null)
        {
            return NotFound();
        }

        if (Account.AccountState.State != AdminUserAccountState.PendingDeletion)
        {
            TempData[FlashMessageKeys.AdminUsersError] = "No pending deletion request exists for this account.";
            return RedirectToPage("./Details", new { id });
        }

        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        try
        {
            var undone = await _lifecycle.UndoHardDeleteAsync(id, actorId);
            TempData[undone ? FlashMessageKeys.AdminUsersSuccess : FlashMessageKeys.AdminUsersError] = undone
                ? $"Deletion request for @{Account.UserName} was withdrawn and the previous account state was restored."
                : "The deletion request could not be withdrawn. The undo window may have expired.";
            return RedirectToPage("./Details", new { id });
        }
        catch (Exception exception)
        {
            var reference = HttpContext.TraceIdentifier;
            _logger.LogError(exception, "Failed to undo deletion for user {UserId}. Reference {Reference}.", id, reference);
            TempData[FlashMessageKeys.AdminUsersError] = $"The deletion request could not be withdrawn. Reference: {reference}.";
            return RedirectToPage("./Details", new { id });
        }
    }

    private void BuildHeader()
    {
        Header = new AdminPageHeaderModel
        {
            Eyebrow = "Account lifecycle",
            Title = Account?.AccountState.State == AdminUserAccountState.PendingDeletion
                ? "Review deletion request"
                : "Delete mistaken account",
            Description = Account is null
                ? "Review a controlled account deletion."
                : $"Review the controlled deletion workflow for @{Account.UserName}.",
            Icon = "bi-person-x",
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
