using System;
using System.Collections.Generic;
using System.Linq;
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

namespace ProjectManagement.Pages.Projects.Remarks
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly ProjectRemarksPanelService _remarksPanelService;

        public IndexModel(ApplicationDbContext db, ProjectRemarksPanelService remarksPanelService)
        {
            _db = db;
            _remarksPanelService = remarksPanelService;
        }

        public Project Project { get; private set; } = default!;

        public IReadOnlyList<ProjectStage> Stages { get; private set; } = Array.Empty<ProjectStage>();

        public ProjectRemarksPanelViewModel RemarksPanel { get; private set; } = ProjectRemarksPanelViewModel.Empty;

        public int InitialPage { get; private set; } = 1;

        [BindProperty(Name = "page", SupportsGet = true)]
        public int PageNumber { get; set; } = 1;

        public async Task<IActionResult> OnGetAsync(int projectId, CancellationToken ct)
        {
            var project = await _db.Projects
                .AsNoTracking()
                .Include(p => p.Category)
                .FirstOrDefaultAsync(p => p.Id == projectId, ct);

            if (project is null)
            {
                return NotFound();
            }

            Project = project;

            var stages = await _db.ProjectStages
                .AsNoTracking()
                .Where(s => s.ProjectId == project.Id)
                .ToListAsync(ct);

            Stages = stages
                .OrderBy(s => StageOrder(s.StageCode))
                .ThenBy(s => s.StageCode, StringComparer.OrdinalIgnoreCase)
                .ToList();

            RemarksPanel = await _remarksPanelService.BuildAsync(project, Stages, User, ct);

            InitialPage = PageNumber > 0 ? PageNumber : 1;

            return Page();
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
