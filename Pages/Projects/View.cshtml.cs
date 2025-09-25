using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Services;

namespace ProjectManagement.Pages.Projects
{
    [Authorize]
    public class ViewModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IClock _clock;

        public ViewModel(ApplicationDbContext db, IClock clock)
        {
            _db = db;
            _clock = clock;
        }

        public record ItemModel(int Id, string Name, string? Description, string? Hod, string? Po, DateTime CreatedAt);

        public ItemModel Item { get; private set; } = null!;
        public List<StageSlipSummary> StageSlips { get; private set; } = new();
        public ProjectRagStatus ProjectRag { get; private set; } = ProjectRagStatus.Green;

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

            await LoadStageHealthAsync(id);
            return Page();
        }

        private async Task LoadStageHealthAsync(int projectId)
        {
            var cancellationToken = HttpContext.RequestAborted;

            var templates = await _db.StageTemplates
                .AsNoTracking()
                .Where(t => t.Version == PlanConstants.StageTemplateVersion)
                .OrderBy(t => t.Sequence)
                .Select(t => t.Code)
                .ToListAsync(cancellationToken);

            var stages = await _db.ProjectStages
                .AsNoTracking()
                .Where(ps => ps.ProjectId == projectId)
                .ToListAsync(cancellationToken);

            var health = StageHealthCalculator.Compute(stages, DateOnly.FromDateTime(_clock.UtcNow.DateTime));

            StageSlips = templates
                .Select(code => new StageSlipSummary(code, health.SlipByStage.TryGetValue(code, out var slip) ? slip : 0))
                .ToList();

            ProjectRag = health.Rag;
        }
    }
}
