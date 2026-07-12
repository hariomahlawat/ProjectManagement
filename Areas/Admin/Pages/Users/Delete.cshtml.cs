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
using ProjectManagement.Services.Admin;

namespace ProjectManagement.Areas.Admin.Pages.Users
{
    [Authorize(Policy = ProjectManagement.Configuration.AdminPolicies.UsersManage)]
    [ResponseCache(NoStore = true)]
    public class DeleteModel : PageModel
    {
        private readonly IUserLifecycleService _lifecycle;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly UserLifecycleOptions _opts;
        private readonly IAdminTimeService _time;

        public DeleteModel(
            IUserLifecycleService lifecycle,
            UserManager<ApplicationUser> userManager,
            IOptions<UserLifecycleOptions> opts,
            IAdminTimeService time)
        {
            _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _opts = opts?.Value ?? throw new ArgumentNullException(nameof(opts));
            _time = time ?? throw new ArgumentNullException(nameof(time));
        }

        public ApplicationUser? UserEntity { get; private set; }
        public UserLifecycleOptions Options => _opts;
        public DateTime UtcNow => _time.UtcNow.UtcDateTime;
        public string FormatIst(DateTime utc) => _time.FormatIst(utc);

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
                TempData[FlashMessageKeys.AdminUsersError] = res.ReasonBlocked;
            }
            else
            {
                TempData[FlashMessageKeys.AdminUsersSuccess] = "Deletion requested.";
            }
            return RedirectToPage("Index");
        }

        public async Task<IActionResult> OnPostUndoAsync(string id)
        {
            var actorId = _userManager.GetUserId(User) ?? string.Empty;
            var ok = await _lifecycle.UndoHardDeleteAsync(id, actorId);
            TempData[ok ? FlashMessageKeys.AdminUsersSuccess : FlashMessageKeys.AdminUsersError] = ok ? "Deletion undone." : "Undo window expired.";
            return RedirectToPage("Index");
        }
    }
}
