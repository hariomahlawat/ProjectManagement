using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Data;
using ProjectManagement.Models;
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
                return RedirectToPage("/Projects/Overview", new { id });
            }

            var userId = _users.GetUserId(User);
            if (userId is null)
            {
                return Forbid();
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

            return RedirectToPage("/Projects/Overview", new { id });
        }
    }
}
