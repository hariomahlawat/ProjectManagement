using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.Admin.Pages.Users
{
    [Authorize(Roles = "Admin")]
    public class ResetModel : PageModel
    {
        private readonly IUserManagementService _userService;
        private readonly ILogger<ResetModel> _logger;
        public ResetModel(IUserManagementService userService, ILogger<ResetModel> logger)
        {
            _userService = userService;
            _logger = logger;
        }

        [BindProperty, Required, StringLength(100, MinimumLength = 8)]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; } = "ChangeMe!123";

        public async Task<IActionResult> OnPostAsync(string id)
        {
            var result = await _userService.ResetPasswordAsync(id, NewPassword);
            if (result.Succeeded)
            {
                _logger.LogInformation("Admin {Admin} reset password for user {UserId}", User.Identity?.Name, id);
                TempData["ok"] = "Password reset.";
                return RedirectToPage("Index");
            }

            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
            return Page();
        }
    }
}
