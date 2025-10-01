using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ProjectManagement.Areas.Admin.Pages.Documents;

[Authorize(Roles = "Admin")]
public class RecycleModel : PageModel
{
    public void OnGet()
    {
    }
}
