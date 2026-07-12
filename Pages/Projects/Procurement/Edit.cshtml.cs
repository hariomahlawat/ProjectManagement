using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Services.Projects;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Pages.Projects.Procurement
{
    [Authorize(Roles = "Admin,HoD,Project Officer")]
    [ValidateAntiForgeryToken]
    public class EditModel : PageModel
    {
        private readonly ProjectFactsService _facts;
        private readonly UserManager<ApplicationUser> _users;
        private readonly ApplicationDbContext _db;
        private readonly ILogger<EditModel> _logger;

        public EditModel(
            ProjectFactsService facts,
            UserManager<ApplicationUser> users,
            ApplicationDbContext db,
            ILogger<EditModel> logger)
        {
            _facts = facts ?? throw new ArgumentNullException(nameof(facts));
            _users = users ?? throw new ArgumentNullException(nameof(users));
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [BindProperty]
        public ProcurementEditInput Input { get; set; } = new();

        public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
        {
            var projectExists = await _db.Projects.AnyAsync(p => p.Id == id, ct);
            if (!projectExists)
            {
                return NotFound();
            }

            return RedirectToPage("/Projects/Overview", new { id, oc = "procurement" });
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
                return RedirectToPage("/Projects/Overview", new { id, oc = "procurement" });
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
                return RedirectToPage("/Projects/Overview", new { id, oc = "procurement" });
            }

            if (Input.SupplyOrderDate is { } date && date > DateOnly.FromDateTime(DateTime.UtcNow))
            {
                TempData["Error"] = "Supply Order Date cannot be in the future.";
                return RedirectToPage("/Projects/Overview", new { id, oc = "procurement" });
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
            catch (OperationCanceledException)
            {
                await RollbackSafelyAsync(tx, id, CancellationToken.None);
                throw;
            }
            catch (DbUpdateException ex)
            {
                await RollbackSafelyAsync(tx, id, CancellationToken.None);

                var postgres = FindPostgresException(ex);
                _logger.LogError(
                    ex,
                    "Database rejected procurement details for project {ProjectId}. SqlState={SqlState}; Constraint={Constraint}; Table={Table}.",
                    id,
                    postgres?.SqlState,
                    postgres?.ConstraintName,
                    postgres?.TableName);

                TempData["Error"] = string.Equals(
                    postgres?.ConstraintName,
                    "CK_ProjectStages_CompletedHasDate",
                    StringComparison.Ordinal)
                    ? "Procurement details could not be saved because the project-stage database rule is not aligned. The application update must complete its pending migration before retrying."
                    : "Procurement details could not be saved because the database rejected the update. No changes were committed.";

                return RedirectToPage("/Projects/Overview", new { id, oc = "procurement" });
            }
            catch (Exception ex)
            {
                await RollbackSafelyAsync(tx, id, CancellationToken.None);
                _logger.LogError(ex, "Failed to save procurement details for project {ProjectId}.", id);

                TempData["Error"] = "Procurement details could not be saved. No changes were committed. Please try again or contact the administrator if the problem persists.";
                return RedirectToPage("/Projects/Overview", new { id, oc = "procurement" });
            }

            TempData["Flash"] = "Procurement details updated.";
            return RedirectToPage("/Projects/Overview", new { id, oc = "procurement" });
        }

        private async Task RollbackSafelyAsync(
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
            int projectId,
            CancellationToken ct)
        {
            try
            {
                await transaction.RollbackAsync(ct);
            }
            catch (Exception rollbackException)
            {
                _logger.LogError(
                    rollbackException,
                    "Rollback failed after procurement update error for project {ProjectId}.",
                    projectId);
            }
        }

        private static PostgresException? FindPostgresException(Exception exception)
        {
            for (var current = exception; current is not null; current = current.InnerException)
            {
                if (current is PostgresException postgres)
                {
                    return postgres;
                }
            }

            return null;
        }
    }
}
