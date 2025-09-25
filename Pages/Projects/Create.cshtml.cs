using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Pages.Projects
{
    [Authorize(Roles = "Admin,HoD")]
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public CreateModel(ApplicationDbContext db, UserManager<ApplicationUser> um)
        {
            _db = db;
            _userManager = um;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public List<ApplicationUser> HodUsers { get; set; } = new();
        public List<ApplicationUser> PoUsers { get; set; } = new();

        public class InputModel
        {
            public string Name { get; set; } = string.Empty;
            public string? Description { get; set; }
            public string? HodUserId { get; set; }
            public string? LeadPoUserId { get; set; }
        }

        public async Task OnGetAsync()
        {
            HodUsers = (await _userManager.GetUsersInRoleAsync("HoD")).OrderBy(u => u.FullName).ToList();

            var poRoleNames = new[] { "Project Officer", "Project Offr" };
            var poUsers = new Dictionary<string, ApplicationUser>();

            foreach (var role in poRoleNames)
            {
                var usersInRole = await _userManager.GetUsersInRoleAsync(role);
                foreach (var user in usersInRole)
                    poUsers[user.Id] = user;
            }

            PoUsers = poUsers.Values.OrderBy(u => u.FullName).ToList();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                await OnGetAsync();
                return Page();
            }

            var proj = new Project
            {
                Name = Input.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description.Trim(),
                HodUserId = Input.HodUserId,
                LeadPoUserId = Input.LeadPoUserId
            };

            _db.Projects.Add(proj);
            await _db.SaveChangesAsync();

            return RedirectToPage("Index");
        }
    }
}
