using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Pages.Projects
{
    [Authorize]
    public class OverviewModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public OverviewModel(ApplicationDbContext db)
        {
            _db = db;
        }

        public Project Project { get; private set; } = default!;
        public IList<ProjectStage> Stages { get; private set; } = new List<ProjectStage>();
        public IReadOnlyList<ProjectCategory> CategoryPath { get; private set; } = Array.Empty<ProjectCategory>();

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var project = await _db.Projects
                .Include(p => p.Category)
                .Include(p => p.HodUser)
                .Include(p => p.LeadPoUser)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (project is null)
            {
                return NotFound();
            }

            Project = project;

            Stages = await _db.ProjectStages
                .Where(s => s.ProjectId == id)
                .OrderBy(s =>
                {
                    var index = Array.IndexOf(StageCodes.All, s.StageCode);
                    return index >= 0 ? index : int.MaxValue;
                })
                .ThenBy(s => s.StageCode)
                .ToListAsync();

            if (project.CategoryId.HasValue)
            {
                CategoryPath = await BuildCategoryPathAsync(project.CategoryId.Value);
            }

            return Page();
        }

        private async Task<IReadOnlyList<ProjectCategory>> BuildCategoryPathAsync(int categoryId)
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

                var category = await _db.ProjectCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == currentId);
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
    }
}
