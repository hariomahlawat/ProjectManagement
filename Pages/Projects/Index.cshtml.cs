using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;

namespace ProjectManagement.Pages.Projects
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public IndexModel(ApplicationDbContext db)
        {
            _db = db;
        }

        public record Row(int Id, string Name, string? Hod, string? Po, DateTime CreatedAt);
        public List<Row> Items { get; set; } = new();

        public async Task OnGetAsync()
        {
            Items = await _db.Projects
                .Include(p => p.HodUser)
                .Include(p => p.LeadPoUser)
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => new Row(
                    p.Id,
                    p.Name,
                    p.HodUser == null ? null : $"{p.HodUser.Rank} {p.HodUser.FullName}",
                    p.LeadPoUser == null ? null : $"{p.LeadPoUser.Rank} {p.LeadPoUser.FullName}",
                    p.CreatedAt))
                .ToListAsync();
        }
    }
}
