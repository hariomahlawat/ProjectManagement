using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.Admin.Pages.Users
{
    [Authorize(Roles = "Admin")]
    public class ResetModel : PageModel
    {
        private readonly IUserManagementService _userService;
        public ResetModel(IUserManagementService userService) => _userService = userService;

        [BindProperty, Required, StringLength(100, MinimumLength = 6)]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; } = "ChangeMe!123";

        public async Task<IActionResult> OnPostAsync(string id)
        {
            var result = await _userService.ResetPasswordAsync(id, NewPassword);
            if (result.Succeeded) return RedirectToPage("Index");

            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
            return Page();
        }
    }
}
