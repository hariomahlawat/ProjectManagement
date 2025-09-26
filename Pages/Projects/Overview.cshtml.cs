using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
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
        private readonly UserManager<ApplicationUser> _users;

        public OverviewModel(ApplicationDbContext db, ProjectProcurementReadService procureRead, UserManager<ApplicationUser> users)
        {
            _db = db;
            _procureRead = procureRead;
            _users = users;
        }

        public Project Project { get; private set; } = default!;
        public IList<ProjectStage> Stages { get; private set; } = new List<ProjectStage>();
        public IReadOnlyList<ProjectCategory> CategoryPath { get; private set; } = Array.Empty<ProjectCategory>();
        public ProcurementAtAGlanceVm Procurement { get; private set; } = default!;
        public ProcurementEditVm ProcurementEdit { get; private set; } = default!;
        public AssignRolesVm AssignRoles { get; private set; } = default!;

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

            var stageLookup = projectStages
                .Where(s => s.StageCode is not null)
                .ToDictionary(s => s.StageCode!, s => s.Status, StringComparer.OrdinalIgnoreCase);

            bool Completed(string code) => stageLookup.TryGetValue(code, out var status) && status == StageStatus.Completed;

            if (project.CategoryId.HasValue)
            {
                CategoryPath = await BuildCategoryPathAsync(project.CategoryId.Value, ct);
            }

            Procurement = await _procureRead.GetAsync(id, ct);

            ProcurementEdit = new ProcurementEditVm
            {
                Input = new ProcurementEditInput
                {
                    ProjectId = id,
                    IpaCost = Procurement.IpaCost,
                    AonCost = Procurement.AonCost,
                    BenchmarkCost = Procurement.BenchmarkCost,
                    L1Cost = Procurement.L1Cost,
                    PncCost = Procurement.PncCost,
                    SupplyOrderDate = Procurement.SupplyOrderDate
                },
                CanEditIpaCost = Completed(ProcurementStageRules.StageForIpaCost),
                CanEditAonCost = Completed(ProcurementStageRules.StageForAonCost),
                CanEditBenchmarkCost = Completed(ProcurementStageRules.StageForBenchmarkCost),
                CanEditL1Cost = Completed(ProcurementStageRules.StageForL1Cost),
                CanEditPncCost = Completed(ProcurementStageRules.StageForPncCost),
                CanEditSupplyOrderDate = Completed(ProcurementStageRules.StageForSupplyOrder)
            };

            AssignRoles = await BuildAssignRolesVmAsync(project);

            return Page();
        }

        private async Task<AssignRolesVm> BuildAssignRolesVmAsync(Project project)
        {
            var hodUsers = await _users.GetUsersInRoleAsync("HoD");
            var poUsers = await _users.GetUsersInRoleAsync("Project Officer");

            static string DisplayName(ApplicationUser user)
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

            var hodOptions = hodUsers
                .Select(user => (user.Id, Name: DisplayName(user)))
                .OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var poOptions = poUsers
                .Select(user => (user.Id, Name: DisplayName(user)))
                .OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new AssignRolesVm
            {
                ProjectId = project.Id,
                RowVersion = project.RowVersion,
                HodUserId = project.HodUserId,
                PoUserId = project.LeadPoUserId,
                HodOptions = hodOptions,
                PoOptions = poOptions
            };
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
