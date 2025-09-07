using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.Admin.Pages.Users
{
    [Authorize(Roles = "Admin")]
    public class EditModel : PageModel
    {
        private readonly IUserManagementService _users;

        public EditModel(IUserManagementService users) => _users = users;

        [BindProperty] public InputModel Input { get; set; } = new();
        public IList<string> Roles { get; private set; } = new List<string>();
        public string? UserName { get; private set; }

        public class InputModel
        {
            [Required] public string Id { get; set; } = string.Empty;

            [Required, Display(Name = "Roles")]
            public List<string> Roles { get; set; } = new();

            [Display(Name = "Active")]
            public bool IsActive { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(string id)
        {
            var user = await _users.GetUserByIdAsync(id);
            if (user == null) return NotFound();

            Roles = await _users.GetRolesAsync();
            var userRoles = await _users.GetUserRolesAsync(id);

            Input = new InputModel
            {
                Id = id,
                Roles = userRoles.ToList(),
                IsActive = !user.LockoutEnd.HasValue || user.LockoutEnd <= DateTimeOffset.UtcNow
            };
            UserName = user.UserName;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            Roles = await _users.GetRolesAsync();
            if (!ModelState.IsValid) return Page();

            var rolesRes = await _users.UpdateUserRolesAsync(Input.Id, Input.Roles);
            if (!rolesRes.Succeeded)
            {
                foreach (var e in rolesRes.Errors) ModelState.AddModelError(string.Empty, e.Description);
                return Page();
            }

            await _users.ToggleUserActivationAsync(Input.Id, Input.IsActive);

            TempData["ok"] = "User updated.";
            return RedirectToPage("Index");
        }
    }
}
