using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ProjectManagement.Models;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.Admin.Pages.Users
{
    [Authorize(Roles = "Admin")]
    [ResponseCache(NoStore = true)]
    public class DeleteModel : PageModel
    {
        private readonly IUserLifecycleService _lifecycle;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly UserLifecycleOptions _opts;

        public DeleteModel(IUserLifecycleService lifecycle, UserManager<ApplicationUser> userManager, IOptions<UserLifecycleOptions> opts)
        {
            _lifecycle = lifecycle;
            _userManager = userManager;
            _opts = opts.Value;
        }

        public ApplicationUser? UserEntity { get; private set; }
        public UserLifecycleOptions Options => _opts;

        [BindProperty]
        public string ConfirmUser { get; set; } = string.Empty;
        [BindProperty]
        public bool Ack { get; set; }
        public async Task<IActionResult> OnGetAsync(string id)
        {
            UserEntity = await _userManager.FindByIdAsync(id);
            if (UserEntity == null)
                return NotFound();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string id)
        {
            UserEntity = await _userManager.FindByIdAsync(id);
            if (UserEntity == null)
                return NotFound();

            if (ConfirmUser != (UserEntity.UserName ?? string.Empty) || !Ack)
            {
                ModelState.AddModelError(string.Empty, "Confirmation required.");
                return Page();
            }

            var actorId = _userManager.GetUserId(User) ?? string.Empty;
            var res = await _lifecycle.RequestHardDeleteAsync(id, actorId);
            if (!res.Allowed)
            {
                TempData["err"] = res.ReasonBlocked;
            }
            else
            {
                TempData["ok"] = "Deletion requested.";
            }
            return RedirectToPage("Index");
        }

        public async Task<IActionResult> OnPostUndoAsync(string id)
        {
            var actorId = _userManager.GetUserId(User) ?? string.Empty;
            var ok = await _lifecycle.UndoHardDeleteAsync(id, actorId);
            TempData[ok ? "ok" : "err"] = ok ? "Deletion undone." : "Undo window expired.";
            return RedirectToPage("Index");
        }
    }
}
