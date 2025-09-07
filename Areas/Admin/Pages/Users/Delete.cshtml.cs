using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Services;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace ProjectManagement.Areas.Admin.Pages.Users
{
    [Authorize(Roles = "Admin")]
    public class DeleteModel : PageModel
    {
        private readonly IUserManagementService _userService;
        private readonly ILogger<DeleteModel> _logger;
        public DeleteModel(IUserManagementService userService, ILogger<DeleteModel> logger)
        {
            _userService = userService;
            _logger = logger;
        }

        public string UserName { get; set; } = string.Empty;

        public async Task<IActionResult> OnGetAsync(string id)
        {
            var user = await _userService.GetUserByIdAsync(id);
            if (user == null) return NotFound();
            UserName = user.UserName ?? string.Empty;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string id)
        {
            var result = await _userService.DeleteUserAsync(id);
            if (result.Succeeded)
            {
                _logger.LogInformation("Admin {Admin} deleted user {UserId}", User.Identity?.Name, id);
                TempData["ok"] = "User deleted.";
            }
            else
            {
                TempData["err"] = string.Join(", ", result.Errors.Select(e => e.Description));
            }
            return RedirectToPage("Index");
        }
    }
}
