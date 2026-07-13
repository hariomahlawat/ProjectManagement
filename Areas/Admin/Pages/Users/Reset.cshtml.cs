using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Configuration;
using ProjectManagement.Infrastructure;
using ProjectManagement.Services;
using ProjectManagement.Services.Admin;

namespace ProjectManagement.Areas.Admin.Pages.Users;

[Authorize(Policy = AdminPolicies.UsersManage)]
[ResponseCache(NoStore = true)]
public sealed class ResetModel : PageModel
{
    private readonly IUserManagementService _users;
    private readonly IAdminUserQueryService _queries;
    private readonly ILogger<ResetModel> _logger;
    private readonly PasswordOptions _passwordOptions;

    public ResetModel(
        IUserManagementService users,
        IAdminUserQueryService queries,
        ILogger<ResetModel> logger,
        IOptions<IdentityOptions> identityOptions)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _queries = queries ?? throw new ArgumentNullException(nameof(queries));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _passwordOptions = identityOptions?.Value.Password ?? throw new ArgumentNullException(nameof(identityOptions));
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public AdminUserDetails? Account { get; private set; }

    public AdminPageHeaderModel Header { get; private set; } = new();

    public string PasswordPolicyDescription => IdentityPasswordPolicy.Describe(_passwordOptions);

    public int GeneratedPasswordLength => IdentityPasswordPolicy.SuggestedGeneratedLength(_passwordOptions);

    public int MinimumPasswordLength => _passwordOptions.RequiredLength;

    public int RequiredUniqueCharacters => _passwordOptions.RequiredUniqueChars;

    public sealed class InputModel
    {
        [Required]
        public string Id { get; set; } = string.Empty;

        [Required, Display(Name = "New temporary password"), DataType(DataType.Password)]
        [StringLength(100)]
        public string Password { get; set; } = string.Empty;

        public bool Acknowledge { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(string id, CancellationToken cancellationToken)
    {
        Account = await _queries.GetDetailsAsync(id, cancellationToken);
        if (Account is null)
        {
            return NotFound();
        }

        if (Account.AccountState.State == AdminUserAccountState.PendingDeletion)
        {
            TempData[FlashMessageKeys.AdminUsersError] = "Undo the deletion request before resetting this account's password.";
            return RedirectToPage("./Details", new { id });
        }

        Input.Id = id;
        BuildHeader();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string id, CancellationToken cancellationToken)
    {
        Input.Id = string.IsNullOrWhiteSpace(Input.Id) ? id : Input.Id.Trim();
        Input.Password ??= string.Empty;
        ModelState.Clear();
        TryValidateModel(Input, nameof(Input));

        if (!string.Equals(Input.Id, id, StringComparison.Ordinal))
        {
            return BadRequest();
        }

        Account = await _queries.GetDetailsAsync(id, cancellationToken);
        if (Account is null)
        {
            return NotFound();
        }

        BuildHeader();
        if (Account.AccountState.State == AdminUserAccountState.PendingDeletion)
        {
            ModelState.AddModelError(string.Empty, "Undo the deletion request before resetting this account's password.");
        }

        if (!Input.Acknowledge)
        {
            ModelState.AddModelError("Input.Acknowledge", "Acknowledge the security impact before resetting the password.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _users.ResetPasswordAsync(id, Input.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }

        _logger.LogInformation(
            "Administrator {Administrator} reset the password for user {UserId}.",
            User.Identity?.Name,
            id);
        TempData[FlashMessageKeys.AdminUsersSuccess] = $"Password reset for @{Account.UserName}. Existing sessions were invalidated and a password change is required at next sign-in.";
        return RedirectToPage("./Details", new { id });
    }

    private void BuildHeader()
    {
        Header = new AdminPageHeaderModel
        {
            Eyebrow = "Security operation",
            Title = "Reset password",
            Description = Account is null
                ? "Issue a new temporary password."
                : $"Issue a new temporary password for @{Account.UserName}.",
            Icon = "bi-key",
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
