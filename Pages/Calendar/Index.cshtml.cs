using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ProjectManagement.Pages.Calendar
{
    [Authorize]
    public class IndexModel : PageModel
    {
        public bool CanEdit { get; private set; }

        public void OnGet()
        {
            CanEdit = User.IsInRole("Admin") || User.IsInRole("TA") || User.IsInRole("HOD");
        }
    }
}
