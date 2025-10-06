using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Models;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ProjectManagement.Pages.Calendar
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public IndexModel(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        public bool CanEdit { get; private set; }
        public bool ShowCelebrations { get; private set; }

        public async Task OnGetAsync()
        {
            CanEdit = User.Claims.Where(c => c.Type == ClaimTypes.Role)
                .Any(r => string.Equals(r.Value, "Admin", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(r.Value, "TA", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(r.Value, "HoD", StringComparison.OrdinalIgnoreCase));

            var user = await _userManager.GetUserAsync(User);
            ShowCelebrations = user?.ShowCelebrationsInCalendar ?? true;
        }
    }
}
