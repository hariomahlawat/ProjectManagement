using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Plans;

namespace ProjectManagement.Pages.Projects.Timeline;

[Authorize(Roles = "HoD")]
[ValidateAntiForgeryToken]
public class ReviewModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILogger<ReviewModel> _logger;
    private readonly PlanApprovalService _approval;

    public ReviewModel(ApplicationDbContext db, PlanApprovalService approval, UserManager<ApplicationUser> users, ILogger<ReviewModel> logger)
    {
        _db = db;
        _approval = approval;
        _users = users;
        _logger = logger;
    }

    public sealed class InputModel
    {
        public int ProjectId { get; set; }
        public string Decision { get; set; } = string.Empty;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IActionResult OnGet(int id) => NotFound();

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken ct)
    {
        if (id != Input.ProjectId)
        {
            return BadRequest();
        }

        var decision = Input.Decision ?? string.Empty;
        var isApprove = string.Equals(decision, "Approve", StringComparison.OrdinalIgnoreCase);

        if (isApprove)
        {
            var hasBackfill = await _db.ProjectStages
                .AnyAsync(s => s.ProjectId == id && s.RequiresBackfill, ct);
            if (hasBackfill)
            {
                TempData["Error"] = "Backfill required data before approval.";
                TempData["OpenOffcanvas"] = "plan-review";
                _logger.LogInformation("Plan approval blocked for project {ProjectId} due to pending backfill.", id);
                return RedirectToPage("/Projects/Overview", new { id });
            }
        }

        var userId = _users.GetUserId(User);
        if (string.IsNullOrEmpty(userId))
        {
            return Forbid();
        }

        try
        {
            if (isApprove)
            {
                var approved = await _approval.ApproveLatestDraftAsync(id, userId, ct);
                if (approved)
                {
                    TempData["Flash"] = "Plan approved.";
                    _logger.LogInformation("Plan approved for project {ProjectId} by user {UserId}.", id, userId);
                }
                else
                {
                    TempData["Error"] = "No draft plan found to approve.";
                    TempData["OpenOffcanvas"] = "plan-review";
                    _logger.LogWarning("Plan approval attempted for project {ProjectId} by user {UserId}, but no draft was available.", id, userId);
                }
            }
            else
            {
                TempData["Flash"] = "Plan review rejected.";
                _logger.LogInformation("Plan review rejected for project {ProjectId} by user {UserId}.", id, userId);
            }
        }
        catch (PlanApprovalValidationException ex)
        {
            TempData["Error"] = ex.Errors.Count > 0 ? string.Join(" ", ex.Errors) : ex.Message;
            TempData["OpenOffcanvas"] = "plan-review";
            _logger.LogWarning(ex, "Plan approval validation failed for project {ProjectId} by user {UserId}.", id, userId);
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            TempData["OpenOffcanvas"] = "plan-review";
            _logger.LogWarning(ex, "Plan approval failed for project {ProjectId} by user {UserId}.", id, userId);
        }

        return RedirectToPage("/Projects/Overview", new { id });
    }
}
