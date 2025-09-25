using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Plans;

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

        public record Row(int Id, string Name, string? Hod, string? Po, DateTime CreatedAt, bool HasApprovedPlan, bool IsPlanPendingApproval);
        public List<Row> Items { get; set; } = new();

        public async Task OnGetAsync()
        {
            var cancellationToken = HttpContext.RequestAborted;

            var projects = await _db.Projects
                .Include(p => p.HodUser)
                .Include(p => p.LeadPoUser)
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    Hod = p.HodUser == null ? null : $"{p.HodUser.Rank} {p.HodUser.FullName}",
                    Po = p.LeadPoUser == null ? null : $"{p.LeadPoUser.Rank} {p.LeadPoUser.FullName}",
                    p.CreatedAt,
                    HasApprovedPlan = p.ActivePlanVersionNo != null
                })
                .ToListAsync(cancellationToken);

            var projectIds = projects.Select(p => p.Id).ToList();

            var pendingApprovals = await _db.PlanVersions
                .AsNoTracking()
                .Where(v => projectIds.Contains(v.ProjectId) && v.Status == PlanVersionStatus.PendingApproval)
                .GroupBy(v => v.ProjectId)
                .Select(g => g.Key)
                .ToListAsync(cancellationToken);

            var pendingLookup = new HashSet<int>(pendingApprovals);

            Items = projects
                .Select(p => new Row(
                    p.Id,
                    p.Name,
                    p.Hod,
                    p.Po,
                    p.CreatedAt,
                    p.HasApprovedPlan,
                    pendingLookup.Contains(p.Id)))
                .ToList();
        }
    }
}
