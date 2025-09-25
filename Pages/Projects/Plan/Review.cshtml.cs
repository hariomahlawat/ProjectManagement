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
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Plans;

namespace ProjectManagement.Pages.Projects.Plan;

[Authorize(Roles = "HoD,Admin")]
public class ReviewModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly PlanApprovalService _approvals;
    private readonly UserManager<ApplicationUser> _users;

    public ReviewModel(ApplicationDbContext db, PlanApprovalService approvals, UserManager<ApplicationUser> users)
    {
        _db = db;
        _approvals = approvals;
        _users = users;
    }

    public record StageRow(string Code, string Name, DateOnly? PlannedStart, DateOnly? PlannedDue);

    public PlanVersion? Plan { get; private set; }
    public List<StageRow> Stages { get; private set; } = new();
    public string ProjectName { get; private set; } = string.Empty;
    public int ProjectId { get; private set; }
    public bool CanAct { get; private set; }

    [BindProperty]
    public string? Note { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken = default)
    {
        ProjectId = id;
        var loadResult = await LoadAsync(id, cancellationToken);
        if (loadResult != null)
        {
            return loadResult;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostApproveAsync(int id, CancellationToken cancellationToken = default)
    {
        ProjectId = id;
        var userId = _users.GetUserId(User);
        if (userId == null)
        {
            return Challenge();
        }

        try
        {
            await _approvals.ApproveAsync(id, userId, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var loadResult = await LoadAsync(id, cancellationToken);
            return loadResult ?? Page();
        }

        StatusMessage = "Project Timeline approved.";
        return RedirectToPage("/Projects/View", new { id });
    }

    public async Task<IActionResult> OnPostRejectAsync(int id, CancellationToken cancellationToken = default)
    {
        ProjectId = id;
        var trimmedNote = Note?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedNote))
        {
            ModelState.AddModelError(nameof(Note), "A note is required to reject the plan.");
            var loadResult = await LoadAsync(id, cancellationToken);
            return loadResult ?? Page();
        }

        Note = trimmedNote;

        var userId = _users.GetUserId(User);
        if (userId == null)
        {
            return Challenge();
        }

        try
        {
            await _approvals.RejectAsync(id, userId, trimmedNote!, cancellationToken);
        }
        catch (PlanApprovalValidationException ex)
        {
            foreach (var error in ex.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }
            var loadResult = await LoadAsync(id, cancellationToken);
            return loadResult ?? Page();
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var loadResult = await LoadAsync(id, cancellationToken);
            return loadResult ?? Page();
        }

        StatusMessage = "Project Timeline rejected and returned to draft.";
        return RedirectToPage("/Projects/View", new { id });
    }

    private async Task<IActionResult?> LoadAsync(int projectId, CancellationToken cancellationToken)
    {
        var project = await _db.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new { p.Name })
            .FirstOrDefaultAsync(cancellationToken);

        if (project == null)
        {
            return NotFound();
        }

        ProjectName = project.Name;

        Plan = await _db.PlanVersions
            .Include(p => p.StagePlans)
            .Include(p => p.SubmittedByUser)
            .Where(p => p.ProjectId == projectId && p.Status == PlanVersionStatus.PendingApproval)
            .OrderByDescending(p => p.VersionNo)
            .FirstOrDefaultAsync(cancellationToken);

        if (Plan == null)
        {
            return NotFound();
        }

        var templates = await _db.StageTemplates
            .AsNoTracking()
            .Where(t => t.Version == PlanConstants.StageTemplateVersion)
            .OrderBy(t => t.Sequence)
            .ToListAsync(cancellationToken);

        var planStages = Plan.StagePlans.ToDictionary(s => s.StageCode, StringComparer.OrdinalIgnoreCase);
        Stages = templates
            .Select(t =>
            {
                planStages.TryGetValue(t.Code, out var stage);
                return new StageRow(t.Code, t.Name, stage?.PlannedStart, stage?.PlannedDue);
            })
            .ToList();

        CanAct = Plan.Status == PlanVersionStatus.PendingApproval;
        Note ??= string.Empty;
        return null;
    }
}
