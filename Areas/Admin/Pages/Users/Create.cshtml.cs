using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Logging;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.Admin.Pages.Users
{
    [Authorize(Roles = "Admin")]
    public class CreateModel : PageModel
    {
        private readonly IUserManagementService _users;
        private readonly ILogger<CreateModel> _logger;

        public CreateModel(IUserManagementService users, ILogger<CreateModel> logger)
        {
            _users = users;
            _logger = logger;
        }

        [BindProperty] public InputModel Input { get; set; } = new();
        public IList<string> Roles { get; private set; } = new List<string>();

        public class InputModel
        {
            [Required, Display(Name = "Username")]
            [StringLength(32, MinimumLength = 3)]
            [RegularExpression(@"^[a-zA-Z0-9_.-]+$", ErrorMessage = "Only letters, numbers, dot, underscore and hyphen.")]
            public string UserName { get; set; } = string.Empty;

            [Required, Display(Name = "Full name")]
            [StringLength(100)]
            public string FullName { get; set; } = string.Empty;

            [Required, Display(Name = "Rank")]
            [StringLength(32)]
            public string Rank { get; set; } = string.Empty;

            [Required, DataType(DataType.Password)]
            [StringLength(100, MinimumLength = 8)]
            public string Password { get; set; } = "ChangeMe!123";

            [Required, Display(Name = "Roles")]
            public List<string> Roles { get; set; } = new();
        }

        public async Task OnGetAsync() => Roles = await _users.GetRolesAsync();

        public async Task<IActionResult> OnPostAsync()
        {
            Roles = await _users.GetRolesAsync();
            if (!ModelState.IsValid) return Page();

            var res = await _users.CreateUserAsync(Input.UserName, Input.Password, Input.FullName, Input.Rank, Input.Roles);
            if (res.Succeeded)
            {
                _logger.LogInformation("Admin {Admin} created user {User} ({Rank}) with roles {Roles}", User.Identity?.Name, Input.UserName, Input.Rank, string.Join(',', Input.Roles));
                TempData["ok"] = "User created.";
                return RedirectToPage("Index");
            }

            foreach (var e in res.Errors) ModelState.AddModelError(string.Empty, e.Description);
            return Page();
        }
    }
}
