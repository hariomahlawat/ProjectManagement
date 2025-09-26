using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Projects;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Pages.Projects
{
    [Authorize]
    public class OverviewModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly ProjectProcurementReadService _procureRead;

        public OverviewModel(ApplicationDbContext db, ProjectProcurementReadService procureRead)
        {
            _db = db;
            _procureRead = procureRead;
        }

        public Project Project { get; private set; } = default!;
        public IList<ProjectStage> Stages { get; private set; } = new List<ProjectStage>();
        public IReadOnlyList<ProjectCategory> CategoryPath { get; private set; } = Array.Empty<ProjectCategory>();
        public ProcurementAtAGlanceVm Procurement { get; private set; } = default!;
        public ProcurementEditInput ProcurementEdit { get; private set; } = default!;

        public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
        {
            var project = await _db.Projects
                .Include(p => p.Category)
                .Include(p => p.HodUser)
                .Include(p => p.LeadPoUser)
                .FirstOrDefaultAsync(p => p.Id == id, ct);

            if (project is null)
            {
                return NotFound();
            }

            Project = project;

            var projectStages = await _db.ProjectStages
                .Where(s => s.ProjectId == id)
                .ToListAsync(ct);

            Stages = projectStages
                .OrderBy(s => StageOrder(s.StageCode))
                .ThenBy(s => s.StageCode, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (project.CategoryId.HasValue)
            {
                CategoryPath = await BuildCategoryPathAsync(project.CategoryId.Value, ct);
            }

            Procurement = await _procureRead.GetAsync(id, ct);

            ProcurementEdit = new ProcurementEditInput
            {
                ProjectId = id,
                IpaCost = Procurement.IpaCost,
                AonCost = Procurement.AonCost,
                BenchmarkCost = Procurement.BenchmarkCost,
                L1Cost = Procurement.L1Cost,
                PncCost = Procurement.PncCost,
                SupplyOrderDate = Procurement.SupplyOrderDate
            };

            return Page();
        }

        private async Task<IReadOnlyList<ProjectCategory>> BuildCategoryPathAsync(int categoryId, CancellationToken ct)
        {
            var path = new List<ProjectCategory>();
            var visited = new HashSet<int>();
            var currentId = categoryId;

            while (true)
            {
                if (!visited.Add(currentId))
                {
                    break;
                }

                var category = await _db.ProjectCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == currentId, ct);
                if (category is null)
                {
                    break;
                }

                path.Insert(0, category);

                if (category.ParentId is null)
                {
                    break;
                }

                currentId = category.ParentId.Value;
            }

            return path;
        }

        private static int StageOrder(string? stageCode)
        {
            if (stageCode is null)
            {
                return int.MaxValue;
            }

            var index = Array.IndexOf(StageCodes.All, stageCode);
            return index >= 0 ? index : int.MaxValue;
        }
    }
}
