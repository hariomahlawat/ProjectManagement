using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Linq;
using System.Security.Claims;

namespace ProjectManagement.Pages.Calendar
{
    [Authorize]
    public class IndexModel : PageModel
    {
        public bool CanEdit { get; private set; }

        public void OnGet()
        {
            CanEdit = User.Claims.Where(c => c.Type == ClaimTypes.Role)
                .Any(r => string.Equals(r.Value, "Admin", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(r.Value, "TA", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(r.Value, "HoD", StringComparison.OrdinalIgnoreCase));
        }
    }
}
