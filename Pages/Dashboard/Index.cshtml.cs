using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Models;
using ProjectManagement.Services;
using System;
using System.Threading.Tasks;

namespace ProjectManagement.Pages.Dashboard
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly ITodoService _todo;
        private readonly UserManager<ApplicationUser> _users;

        public IndexModel(ITodoService todo, UserManager<ApplicationUser> users)
        {
            _todo = todo;
            _users = users;
        }

        public TodoWidgetResult? TodoWidget { get; set; }

        [BindProperty]
        public string? NewTitle { get; set; }

        public async Task OnGetAsync()
        {
            var uid = _users.GetUserId(User);
            if (uid != null)
            {
                TodoWidget = await _todo.GetWidgetAsync(uid, take: 20);
            }
        }

        public async Task<IActionResult> OnPostAddAsync()
        {
            if (string.IsNullOrWhiteSpace(NewTitle))
                return RedirectToPage();

            var uid = _users.GetUserId(User);
            if (uid == null) return Unauthorized();

            await _todo.CreateAsync(uid, NewTitle.Trim());
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostToggleAsync(Guid id)
        {
            var uid = _users.GetUserId(User);
            if (uid == null) return Unauthorized();
            // Widget shows only Open items; toggling marks them done.
            await _todo.ToggleDoneAsync(uid, id, done: true);
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteAsync(Guid id)
        {
            var uid = _users.GetUserId(User);
            if (uid == null) return Unauthorized();
            await _todo.DeleteAsync(uid, id);
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostPinAsync(Guid id, bool pin)
        {
            var uid = _users.GetUserId(User);
            if (uid == null) return Unauthorized();
            await _todo.EditAsync(uid, id, pinned: pin);
            return RedirectToPage();
        }
    }
}
