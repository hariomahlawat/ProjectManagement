using System.ComponentModel.DataAnnotations;
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
public sealed class EditModel : PageModel
{
    private readonly IUserManagementService _users;
    private readonly IAdminUserQueryService _queries;
    private readonly IAdminRoleDescriptorCatalog _roleCatalog;
    private readonly ILogger<EditModel> _logger;

    public EditModel(
        IUserManagementService users,
        IAdminUserQueryService queries,
        IAdminRoleDescriptorCatalog roleCatalog,
        ILogger<EditModel> logger)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _queries = queries ?? throw new ArgumentNullException(nameof(queries));
        _roleCatalog = roleCatalog ?? throw new ArgumentNullException(nameof(roleCatalog));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public AdminUserDetails? Account { get; private set; }

    public AdminPageHeaderModel Header { get; private set; } = new();

    public IReadOnlyList<AdminRoleDescriptor> RoleOptions { get; private set; } =
        Array.Empty<AdminRoleDescriptor>();

    public sealed class InputModel
    {
        [Required]
        public string Id { get; set; } = string.Empty;

        [Required, Display(Name = "Full name")]
        [StringLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required, Display(Name = "Rank")]
        [StringLength(32)]
        public string Rank { get; set; } = string.Empty;

        [MinLength(1, ErrorMessage = "Assign at least one role to the user.")]
        public List<string> Roles { get; set; } = new();
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
            TempData[FlashMessageKeys.AdminUsersError] = "Undo the deletion request before editing this account.";
            return RedirectToPage("./Details", new { id });
        }

        Input = new InputModel
        {
            Id = Account.Id,
            FullName = Account.FullName,
            Rank = Account.Rank,
            Roles = Account.Roles.ToList()
        };

        await LoadPresentationAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string id, CancellationToken cancellationToken)
    {
        Input.Id = string.IsNullOrWhiteSpace(Input.Id) ? id : Input.Id;
        NormaliseInput();
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

        await LoadPresentationAsync(cancellationToken);

        if (Account.AccountState.State == AdminUserAccountState.PendingDeletion)
        {
            ModelState.AddModelError(string.Empty, "Undo the deletion request before editing this account.");
        }

        var availableRoleNames = RoleOptions
            .Select(role => role.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (Input.Roles.Any(role => !availableRoleNames.Contains(role)))
        {
            ModelState.AddModelError("Input.Roles", "One or more selected roles are not available.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _users.UpdateUserAsync(
            Input.Id,
            Input.FullName,
            Input.Rank,
            Input.Roles);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }

        _logger.LogInformation(
            "Administrator {Administrator} updated user {UserId} with roles {Roles}.",
            User.Identity?.Name,
            Input.Id,
            string.Join(',', Input.Roles));

        TempData[FlashMessageKeys.AdminUsersSuccess] = $"Account @{Account.UserName} was updated.";
        return RedirectToPage("./Details", new { id = Input.Id });
    }

    private async Task LoadPresentationAsync(CancellationToken cancellationToken)
    {
        var roles = await _users.GetRolesAsync();
        RoleOptions = _roleCatalog.DescribeMany(roles);

        var title = Account is null
            ? "Edit user"
            : $"Edit {(string.IsNullOrWhiteSpace(Account.FullName) ? Account.UserName : Account.FullName)}";
        Header = new AdminPageHeaderModel
        {
            Eyebrow = "People and access",
            Title = title,
            Description = Account is null
                ? "Update identity details and assigned roles."
                : $"Update identity details and access for @{Account.UserName}.",
            Icon = "bi-person-gear",
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

    private void NormaliseInput()
    {
        Input.Id = Input.Id?.Trim() ?? string.Empty;
        Input.FullName = Input.FullName?.Trim() ?? string.Empty;
        Input.Rank = Input.Rank?.Trim() ?? string.Empty;
        Input.Roles = (Input.Roles ?? new List<string>())
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
