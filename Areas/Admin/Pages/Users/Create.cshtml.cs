using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.Admin.Pages.Users
{
    [Authorize(Roles = "Admin")]
    public class CreateModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        public CreateModel(UserManager<ApplicationUser> userManager) => _userManager = userManager;

        [BindProperty] public InputModel Input { get; set; } = new();

        public class InputModel
        {
            [Required, StringLength(32, MinimumLength = 3)]
            public string UserName { get; set; } = string.Empty;

            [Required, StringLength(100, MinimumLength = 6)]
            [DataType(DataType.Password)]
            public string Password { get; set; } = "ChangeMe!123";
        }

        public void OnGet() { }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid) return Page();

            var user = new ApplicationUser { UserName = Input.UserName, MustChangePassword = true };
            var result = await _userManager.CreateAsync(user, Input.Password);
            if (result.Succeeded) return RedirectToPage("Index");

            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
            return Page();
        }
    }
}
