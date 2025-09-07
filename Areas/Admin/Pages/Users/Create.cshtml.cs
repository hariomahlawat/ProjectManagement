using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.Admin.Pages.Users
{
    [Authorize(Roles = "Admin")]
    public class CreateModel : PageModel
    {
        private readonly IUserManagementService _userService;
        public CreateModel(IUserManagementService userService) => _userService = userService;

        public IList<string> Roles { get; private set; } = new List<string>();

        [BindProperty] public InputModel Input { get; set; } = new();

        public class InputModel
        {
            [Required, StringLength(32, MinimumLength = 3)]
            public string UserName { get; set; } = string.Empty;

            [Required, StringLength(100, MinimumLength = 6)]
            [DataType(DataType.Password)]
            public string Password { get; set; } = "ChangeMe!123";

            [Required]
            public string Role { get; set; } = string.Empty;
        }

        public async Task OnGetAsync()
        {
            Roles = await _userService.GetRolesAsync();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            Roles = await _userService.GetRolesAsync();
            if (!ModelState.IsValid) return Page();

            var result = await _userService.CreateUserAsync(Input.UserName, Input.Password, Input.Role);
            if (result.Succeeded) return RedirectToPage("Index");

            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
            return Page();
        }
    }
}
