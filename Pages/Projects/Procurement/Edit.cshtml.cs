using System;
using System.Collections.Generic;
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
using ProjectManagement.Services.Projects;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Pages.Projects.Procurement
{
    [Authorize(Roles = "Admin,HoD,PO")]
    [ValidateAntiForgeryToken]
    public class EditModel : PageModel
    {
        private readonly ProjectFactsService _facts;
        private readonly UserManager<ApplicationUser> _users;
        private readonly ApplicationDbContext _db;

        public EditModel(ProjectFactsService facts, UserManager<ApplicationUser> users, ApplicationDbContext db)
        {
            _facts = facts;
            _users = users;
            _db = db;
        }

        [BindProperty]
        public ProcurementEditInput Input { get; set; } = new();

        public IActionResult OnGet(int id)
        {
            return NotFound();
        }

        public async Task<IActionResult> OnPostAsync(int id, CancellationToken ct)
        {
            if (id != Input.ProjectId)
            {
                return BadRequest();
            }

            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Unable to save procurement details. Please review your input.";
                TempData["OpenOffcanvas"] = "procurement";
                return RedirectToPage("/Projects/Overview", new { id });
            }

            var userId = _users.GetUserId(User);
            if (userId is null)
            {
                return Forbid();
            }

            var stages = await _db.ProjectStages
                .Where(s => s.ProjectId == id && s.StageCode != null)
                .ToDictionaryAsync(s => s.StageCode!, s => s.Status, StringComparer.OrdinalIgnoreCase, ct);

            bool Completed(string code) => stages.TryGetValue(code, out var status) && status == StageStatus.Completed;

            var errors = new List<string>();

            if (Input.IpaCost.HasValue && !Completed(ProcurementStageRules.StageForIpaCost))
            {
                errors.Add("IPA Cost cannot be set before IPA stage is completed.");
            }

            if (Input.AonCost.HasValue && !Completed(ProcurementStageRules.StageForAonCost))
            {
                errors.Add("AON Cost cannot be set before AON stage is completed.");
            }

            if (Input.BenchmarkCost.HasValue && !Completed(ProcurementStageRules.StageForBenchmarkCost))
            {
                errors.Add("Benchmark Cost cannot be set before BM stage is completed.");
            }

            if (Input.L1Cost.HasValue && !Completed(ProcurementStageRules.StageForL1Cost))
            {
                errors.Add("L1 Cost cannot be set before COB stage is completed.");
            }

            if (Input.PncCost.HasValue && !Completed(ProcurementStageRules.StageForPncCost))
            {
                errors.Add("PNC Cost cannot be set before PNC stage is completed.");
            }

            if (Input.SupplyOrderDate.HasValue && !Completed(ProcurementStageRules.StageForSupplyOrder))
            {
                errors.Add("Supply Order Date cannot be set before SO stage is completed.");
            }

            if (errors.Count > 0)
            {
                TempData["Error"] = string.Join(" ", errors);
                TempData["OpenOffcanvas"] = "procurement";
                return RedirectToPage("/Projects/Overview", new { id });
            }

            if (Input.SupplyOrderDate is { } date && date > DateOnly.FromDateTime(DateTime.UtcNow))
            {
                TempData["Error"] = "Supply Order Date cannot be in the future.";
                TempData["OpenOffcanvas"] = "procurement";
                return RedirectToPage("/Projects/Overview", new { id });
            }

            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                if (Input.IpaCost.HasValue)
                {
                    await _facts.UpsertIpaCostAsync(id, Input.IpaCost.Value, userId, ct);
                }

                if (Input.AonCost.HasValue)
                {
                    await _facts.UpsertAonCostAsync(id, Input.AonCost.Value, userId, ct);
                }

                if (Input.BenchmarkCost.HasValue)
                {
                    await _facts.UpsertBenchmarkCostAsync(id, Input.BenchmarkCost.Value, userId, ct);
                }

                if (Input.L1Cost.HasValue)
                {
                    await _facts.UpsertL1CostAsync(id, Input.L1Cost.Value, userId, ct);
                }

                if (Input.PncCost.HasValue)
                {
                    await _facts.UpsertPncCostAsync(id, Input.PncCost.Value, userId, ct);
                }

                if (Input.SupplyOrderDate.HasValue)
                {
                    await _facts.UpsertSupplyOrderDateAsync(id, Input.SupplyOrderDate.Value, userId, ct);
                }

                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }

            TempData["Flash"] = "Procurement details updated.";
            return RedirectToPage("/Projects/Overview", new { id });
        }
    }
}
