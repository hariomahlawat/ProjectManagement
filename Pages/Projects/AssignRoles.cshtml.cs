using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Pages.Projects
{
    [Authorize(Roles = "Admin,HoD")]
    public class AssignRolesModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _users;

        public AssignRolesModel(ApplicationDbContext db, UserManager<ApplicationUser> users)
        {
            _db = db;
            _users = users;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string ProjectName { get; private set; } = string.Empty;
        public IEnumerable<SelectListItem> HodList { get; private set; } = Array.Empty<SelectListItem>();
        public IEnumerable<SelectListItem> PoList { get; private set; } = Array.Empty<SelectListItem>();

        public class InputModel
        {
            public int ProjectId { get; set; }
            public string? HodUserId { get; set; }
            public string? PoUserId { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var project = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
            if (project is null)
            {
                return NotFound();
            }

            ProjectName = project.Name;
            Input.ProjectId = project.Id;
            Input.HodUserId = project.HodUserId;
            Input.PoUserId = project.LeadPoUserId;

            await LoadListsAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == Input.ProjectId);
            if (project is null)
            {
                return NotFound();
            }

            project.HodUserId = string.IsNullOrWhiteSpace(Input.HodUserId) ? null : Input.HodUserId;
            project.LeadPoUserId = string.IsNullOrWhiteSpace(Input.PoUserId) ? null : Input.PoUserId;

            await _db.SaveChangesAsync();

            return RedirectToPage("/Projects/Overview", new { id = project.Id });
        }

        private async Task LoadListsAsync()
        {
            var hodUsers = await _users.GetUsersInRoleAsync("HoD");
            HodList = BuildUserOptions(hodUsers);

            var poUsers = await _users.GetUsersInRoleAsync("Project Officer");
            PoList = BuildUserOptions(poUsers);
        }

        private static IEnumerable<SelectListItem> BuildUserOptions(IEnumerable<ApplicationUser> users)
        {
            var items = new List<SelectListItem>
            {
                new("— Unassigned —", string.Empty)
            };

            items.AddRange(users
                .OrderBy(u => string.IsNullOrWhiteSpace(u.FullName) ? u.UserName : u.FullName)
                .Select(u => new SelectListItem(DisplayName(u), u.Id)));

            return items;
        }

        private static string DisplayName(ApplicationUser user)
        {
            if (!string.IsNullOrWhiteSpace(user.FullName))
            {
                return user.FullName;
            }

            if (!string.IsNullOrWhiteSpace(user.UserName))
            {
                return user.UserName!;
            }

            return user.Email ?? user.Id;
        }
    }
}
