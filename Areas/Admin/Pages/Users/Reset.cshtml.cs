using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Models;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Areas.Admin.Pages.Users
{
    [Authorize(Roles = "Admin")]
    public class ResetModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        public ResetModel(UserManager<ApplicationUser> userManager) => _userManager = userManager;

        [BindProperty, Required, StringLength(100, MinimumLength = 6)]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; } = "ChangeMe!123";

        public async Task<IActionResult> OnPostAsync(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, NewPassword);
            if (result.Succeeded)
            {
                user.MustChangePassword = true;
                await _userManager.UpdateAsync(user);
                return RedirectToPage("Index");
            }

            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
            return Page();
        }
    }
}
