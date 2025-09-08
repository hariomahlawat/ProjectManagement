using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Logging;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.Admin.Pages.Users
{
    [Authorize(Roles = "Admin")]
    public class EditModel : PageModel
    {
        private readonly IUserManagementService _users;
        private readonly ILogger<EditModel> _logger;

        public EditModel(IUserManagementService users, ILogger<EditModel> logger)
        {
            _users = users;
            _logger = logger;
        }

        [BindProperty] public InputModel Input { get; set; } = new();
        public IList<string> Roles { get; private set; } = new List<string>();
        public string? UserName { get; private set; }

        public class InputModel
        {
            [Required] public string Id { get; set; } = string.Empty;

            [Required, Display(Name = "Full name")]
            [StringLength(100)]
            public string FullName { get; set; } = string.Empty;

            [Required, Display(Name = "Rank")]
            [StringLength(32)]
            public string Rank { get; set; } = string.Empty;

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
                FullName = user.FullName,
                Rank = user.Rank,
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

            var detailsRes = await _users.UpdateUserDetailsAsync(Input.Id, Input.FullName, Input.Rank);
            if (!detailsRes.Succeeded)
            {
                foreach (var e in detailsRes.Errors) ModelState.AddModelError(string.Empty, e.Description);
                return Page();
            }

            var rolesRes = await _users.UpdateUserRolesAsync(Input.Id, Input.Roles);
            if (!rolesRes.Succeeded)
            {
                foreach (var e in rolesRes.Errors) ModelState.AddModelError(string.Empty, e.Description);
                return Page();
            }

            var activeRes = await _users.ToggleUserActivationAsync(Input.Id, Input.IsActive);
            if (!activeRes.Succeeded)
            {
                foreach (var e in activeRes.Errors) ModelState.AddModelError(string.Empty, e.Description);
                return Page();
            }

            _logger.LogInformation("Admin {Admin} updated user {UserId}: name {FullName}; rank {Rank}; roles {Roles}; active {Active}",
                User.Identity?.Name, Input.Id, Input.FullName, Input.Rank, string.Join(',', Input.Roles), Input.IsActive);

            TempData["ok"] = "User updated.";
            return RedirectToPage("Index");
        }
    }
}
