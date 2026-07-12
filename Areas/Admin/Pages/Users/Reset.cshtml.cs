using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Infrastructure;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.Admin.Pages.Users
{
    [Authorize(Roles = "Admin")]
    public class ResetModel : PageModel
    {
        private readonly IUserManagementService _userService;
        private readonly ILogger<ResetModel> _logger;
        private readonly PasswordOptions _passwordOptions;

        public ResetModel(
            IUserManagementService userService,
            ILogger<ResetModel> logger,
            IOptions<IdentityOptions> identityOptions)
        {
            _userService = userService;
            _logger = logger;
            _passwordOptions = identityOptions.Value.Password;
        }

        [BindProperty, Required, StringLength(100)]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; } = string.Empty;

        public string PasswordPolicyDescription => IdentityPasswordPolicy.Describe(_passwordOptions);

        public int GeneratedPasswordLength => IdentityPasswordPolicy.SuggestedGeneratedLength(_passwordOptions);

        public int MinimumPasswordLength => _passwordOptions.RequiredLength;

        public int RequiredUniqueCharacters => _passwordOptions.RequiredUniqueChars;

        public async Task<IActionResult> OnPostAsync(string id)
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var result = await _userService.ResetPasswordAsync(id, NewPassword);
            if (result.Succeeded)
            {
                _logger.LogInformation("Admin {Admin} reset password for user {UserId}", User.Identity?.Name, id);
                TempData["ok"] = "Password reset.";
                return RedirectToPage("Index");
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }
    }
}
