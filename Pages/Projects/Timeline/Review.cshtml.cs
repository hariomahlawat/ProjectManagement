using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using ProjectManagement.Models;
using ProjectManagement.Helpers;
using ProjectManagement.Services.Plans;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Pages.Projects.Timeline;

[Authorize(Roles = "HoD")]
[ValidateAntiForgeryToken]
public class ReviewModel : PageModel
{
    private readonly PlanApprovalService _approval;
    private readonly ProjectTimelineReadService _timeline;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILogger<ReviewModel> _logger;

    public ReviewModel(PlanApprovalService approval, ProjectTimelineReadService timeline, UserManager<ApplicationUser> users, ILogger<ReviewModel> logger)
    {
        _approval = approval;
        _timeline = timeline;
        _users = users;
        _logger = logger;
    }

    public sealed class InputModel
    {
        public int ProjectId { get; set; }
        public string Decision { get; set; } = string.Empty;
        public string? Note { get; set; }
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

        var userId = _users.GetUserId(User);
        if (string.IsNullOrEmpty(userId))
        {
            return Forbid();
        }

        try
        {
            if (string.Equals(Input.Decision, "Reject", StringComparison.OrdinalIgnoreCase))
            {
                var rejected = await _approval.RejectLatestPendingAsync(id, userId, Input.Note, ct);
                if (rejected)
                {
                    TempData["Flash"] = "Draft rejected and returned to the Project Officer.";
                    _logger.LogInformation("Plan review rejected for project {ProjectId} by user {UserId}.", id, userId);
                    return RedirectToPage("/Projects/Overview", new { id });
                }

                TempData["Error"] = "No pending draft found to reject.";
                TempData["OpenOffcanvas"] = "plan-review";
                _logger.LogWarning("Plan rejection attempted for project {ProjectId} by user {UserId}, but no draft was available.", id, userId);
                return RedirectToPage("/Projects/Overview", new { id });
            }

            if (string.Equals(Input.Decision, "Approve", StringComparison.OrdinalIgnoreCase))
            {
                var hasBackfill = await _timeline.HasBackfillAsync(id, ct);
                if (hasBackfill)
                {
                    TempData["Error"] = "Resolve required procurement backfill before approval.";
                    TempData["OpenOffcanvas"] = "plan-review";
                    _logger.LogInformation("Plan approval blocked for project {ProjectId} due to pending backfill.", id);
                    return RedirectToPage("/Projects/Overview", new { id });
                }

                var approved = await _approval.ApproveLatestDraftAsync(id, userId, ct);
                if (approved)
                {
                    TempData["Flash"] = "Plan approved.";
                    _logger.LogInformation("Plan approved for project {ProjectId} by user {UserId}.", id, userId);
                }
                else
                {
                    TempData["Error"] = "No draft to approve.";
                    TempData["OpenOffcanvas"] = "plan-review";
                    _logger.LogWarning("Plan approval attempted for project {ProjectId} by user {UserId}, but no draft was available.", id, userId);
                }

                return RedirectToPage("/Projects/Overview", new { id });
            }

            TempData["Error"] = "Unknown action.";
            TempData["OpenOffcanvas"] = "plan-review";
            _logger.LogWarning("Unknown timeline review action {Decision} for project {ProjectId} by user {UserId}.", Input.Decision, id, userId);
        }
        catch (PlanApprovalValidationException ex)
        {
            TempData["Error"] = ex.Errors.Count > 0 ? string.Join(" ", ex.Errors) : ex.Message;
            TempData["OpenOffcanvas"] = "plan-review";
            _logger.LogWarning(ex, "Plan approval validation failed for project {ProjectId} by user {UserId}.", id, userId);
        }
        catch (ForbiddenException ex)
        {
            TempData["Error"] = ex.Message;
            TempData["OpenOffcanvas"] = "plan-review";
            _logger.LogWarning(ex, "Plan approval forbidden for project {ProjectId} by user {UserId}.", id, userId);
        }
        catch (ValidationException ex)
        {
            TempData["Error"] = ex.Message;
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
