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
public sealed class CreateModel : PageModel
{
    private readonly IUserManagementService _users;
    private readonly IAdminRoleDescriptorCatalog _roleCatalog;
    private readonly ILogger<CreateModel> _logger;
    private readonly PasswordOptions _passwordOptions;

    public CreateModel(
        IUserManagementService users,
        IAdminRoleDescriptorCatalog roleCatalog,
        ILogger<CreateModel> logger,
        IOptions<IdentityOptions> identityOptions)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _roleCatalog = roleCatalog ?? throw new ArgumentNullException(nameof(roleCatalog));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _passwordOptions = identityOptions?.Value.Password ?? throw new ArgumentNullException(nameof(identityOptions));
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public AdminPageHeaderModel Header { get; private set; } = new();

    public IReadOnlyList<AdminRoleDescriptor> RoleOptions { get; private set; } =
        Array.Empty<AdminRoleDescriptor>();

    public string PasswordPolicyDescription => IdentityPasswordPolicy.Describe(_passwordOptions);

    public int GeneratedPasswordLength => IdentityPasswordPolicy.SuggestedGeneratedLength(_passwordOptions);

    public int MinimumPasswordLength => _passwordOptions.RequiredLength;

    public int RequiredUniqueCharacters => _passwordOptions.RequiredUniqueChars;

    public sealed class InputModel
    {
        [Required, Display(Name = "Username")]
        [StringLength(32, MinimumLength = 3)]
        [RegularExpression(@"^[a-zA-Z0-9_.-]+$", ErrorMessage = "Use letters, numbers, dot, underscore or hyphen only.")]
        public string UserName { get; set; } = string.Empty;

        [Required, Display(Name = "Full name")]
        [StringLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required, Display(Name = "Rank")]
        [StringLength(32)]
        public string Rank { get; set; } = string.Empty;

        [Required, DataType(DataType.Password)]
        [StringLength(100)]
        public string Password { get; set; } = string.Empty;

        [MinLength(1, ErrorMessage = "Assign at least one role to the user.")]
        public List<string> Roles { get; set; } = new();
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadPageAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        NormaliseInput();
        ModelState.Clear();
        TryValidateModel(Input, nameof(Input));
        await LoadPageAsync(cancellationToken);

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

        var result = await _users.CreateUserAsync(
            Input.UserName,
            Input.Password,
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
            "Administrator {Administrator} created user {UserName} with roles {Roles}.",
            User.Identity?.Name,
            Input.UserName,
            string.Join(',', Input.Roles));

        TempData[FlashMessageKeys.AdminUsersSuccess] = $"Account @{Input.UserName} was created. The user must change the temporary password at first sign-in.";
        return RedirectToPage("./Index");
    }

    private async Task LoadPageAsync(CancellationToken cancellationToken)
    {
        var roles = await _users.GetRolesAsync();
        RoleOptions = _roleCatalog.DescribeMany(roles);
        Header = new AdminPageHeaderModel
        {
            Eyebrow = "People and access",
            Title = "Create user",
            Description = "Create an account, issue a temporary password and assign the minimum required access.",
            Icon = "bi-person-plus",
            Actions = new[]
            {
                new AdminPageActionModel
                {
                    Text = "Back to users",
                    Href = Url.Page("./Index"),
                    Icon = "bi-arrow-left"
                }
            }
        };
    }

    private void NormaliseInput()
    {
        Input.UserName = Input.UserName?.Trim() ?? string.Empty;
        Input.FullName = Input.FullName?.Trim() ?? string.Empty;
        Input.Rank = Input.Rank?.Trim() ?? string.Empty;
        Input.Password ??= string.Empty;
        Input.Roles = (Input.Roles ?? new List<string>())
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
