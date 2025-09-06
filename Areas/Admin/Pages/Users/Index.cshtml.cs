using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.Admin.Pages.Users
{
    [Authorize(Roles = "Admin")]
    public class IndexModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        public IList<ApplicationUser> Users { get; private set; } = new List<ApplicationUser>();
        public IndexModel(UserManager<ApplicationUser> userManager) => _userManager = userManager;
        public void OnGet() => Users = _userManager.Users.OrderBy(u => u.UserName).ToList();
    }
}
