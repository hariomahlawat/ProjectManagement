using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Models;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.Admin.Pages.Users
{
    [Authorize(Roles = "Admin")]
    [ResponseCache(NoStore = true)]
    public class EnableModel : PageModel
    {
        private readonly IUserLifecycleService _lifecycle;
        private readonly UserManager<ApplicationUser> _userManager;

        public EnableModel(IUserLifecycleService lifecycle, UserManager<ApplicationUser> userManager)
        {
            _lifecycle = lifecycle;
            _userManager = userManager;
        }

        public ApplicationUser? UserEntity { get; private set; }

        [BindProperty]
        public string ConfirmUser { get; set; } = string.Empty;

        [BindProperty]
        public bool Ack { get; set; }

        public async Task<IActionResult> OnGetAsync(string id)
        {
            UserEntity = await _userManager.FindByIdAsync(id);
            if (UserEntity == null) return NotFound();
            if (!UserEntity.IsDisabled)
            {
                TempData["ok"] = "User already active.";
                return RedirectToPage("Index");
            }
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string id)
        {
            UserEntity = await _userManager.FindByIdAsync(id);
            if (UserEntity == null) return NotFound();
            if (ConfirmUser != UserEntity.UserName)
            {
                ModelState.AddModelError("ConfirmUser", "Username mismatch.");
            }
            if (!Ack)
            {
                ModelState.AddModelError("Ack", "You must acknowledge.");
            }
            if (!ModelState.IsValid)
            {
                return Page();
            }
            var actorId = _userManager.GetUserId(User) ?? string.Empty;
            try
            {
                await _lifecycle.EnableAsync(id, actorId);
                TempData["ok"] = "User enabled.";
                return RedirectToPage("Index");
            }
            catch (System.Exception ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                return Page();
            }
        }
    }
}
