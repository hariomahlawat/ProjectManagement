using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;

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

        public IList<Project> Projects { get; private set; } = new List<Project>();

        [BindProperty(SupportsGet = true)]
        public string? CaseFileQuery { get; set; }

        public async Task OnGetAsync()
        {
            var query = _db.Projects
                .AsNoTracking()
                .Include(p => p.Category)
                .Include(p => p.HodUser)
                .Include(p => p.LeadPoUser)
                .OrderByDescending(p => p.CreatedAt)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(CaseFileQuery))
            {
                var term = CaseFileQuery.Trim();
                query = query.Where(p => p.CaseFileNumber != null && EF.Functions.ILike(p.CaseFileNumber!, $"%{term}%"));
            }

            Projects = await query.Take(100).ToListAsync();
        }
    }
}
