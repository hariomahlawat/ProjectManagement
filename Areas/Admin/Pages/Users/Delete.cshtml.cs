using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.Admin.Pages.Users
{
    [Authorize(Roles = "Admin")]
    public class DeleteModel : PageModel
    {
        private readonly IUserManagementService _userService;
        public DeleteModel(IUserManagementService userService) => _userService = userService;

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
            await _userService.DeleteUserAsync(id);
            return RedirectToPage("Index");
        }
    }
}
