using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;

namespace ProjectManagement.Pages.Projects
{
    [Authorize]
    public class ViewModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public ViewModel(ApplicationDbContext db)
        {
            _db = db;
        }

        public record ItemModel(int Id, string Name, string? Description, string? Hod, string? Po, DateTime CreatedAt);

        public ItemModel Item { get; private set; } = null!;

        [TempData]
        public string? StatusMessage { get; set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var item = await _db.Projects
                .Include(p => p.HodUser)
                .Include(p => p.LeadPoUser)
                .Where(p => p.Id == id)
                .Select(p => new ItemModel(
                    p.Id,
                    p.Name,
                    p.Description,
                    p.HodUser == null ? null : $"{p.HodUser.Rank} {p.HodUser.FullName}",
                    p.LeadPoUser == null ? null : $"{p.LeadPoUser.Rank} {p.LeadPoUser.FullName}",
                    p.CreatedAt))
                .FirstOrDefaultAsync();

            if (item == null)
            {
                return NotFound();
            }

            Item = item;
            return Page();
        }
    }
}
