using System;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.Admin.Pages.Users
{
    [Authorize(Roles = "Admin")]
    public class EditModel : PageModel
    {
        private readonly IUserManagementService _userService;
        public EditModel(IUserManagementService userService) => _userService = userService;

        public IList<string> Roles { get; private set; } = new List<string>();

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public class InputModel
        {
            public string Id { get; set; } = string.Empty;
            [Required]
            public string Role { get; set; } = string.Empty;
            public bool IsActive { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(string id)
        {
            var user = await _userService.GetUserByIdAsync(id);
            if (user == null) return NotFound();

            Roles = await _userService.GetRolesAsync();
            var userRoles = await _userService.GetUserRolesAsync(id);
            Input = new InputModel
            {
                Id = id,
                Role = userRoles.FirstOrDefault() ?? string.Empty,
                IsActive = !user.LockoutEnd.HasValue || user.LockoutEnd <= DateTimeOffset.UtcNow
            };
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            Roles = await _userService.GetRolesAsync();
            if (!ModelState.IsValid) return Page();

            var result = await _userService.UpdateUserRoleAsync(Input.Id, Input.Role);
            if (result.Succeeded)
            {
                await _userService.ToggleUserActivationAsync(Input.Id, Input.IsActive);
                TempData["ok"] = "User updated.";
                return RedirectToPage("Index");
            }

            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
            return Page();
        }
    }
}
