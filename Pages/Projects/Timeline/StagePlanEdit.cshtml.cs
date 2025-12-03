using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Plans;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Pages.Projects.Timeline;

[Authorize(Roles = "Admin,Project Officer,HoD")]
[AutoValidateAntiforgeryToken]
public class StagePlanEditModel : PageModel
{
    // SECTION: Dependencies
    private readonly ApplicationDbContext _db;
    private readonly PlanDraftService _planDraft;
    private readonly IUserContext _userContext;
    private readonly ILogger<StagePlanEditModel> _logger;

    // SECTION: Construction
    public StagePlanEditModel(
        ApplicationDbContext db,
        PlanDraftService planDraft,
        IUserContext userContext,
        ILogger<StagePlanEditModel> logger)
    {
        _db = db;
        _planDraft = planDraft;
        _userContext = userContext;
        _logger = logger;
    }

    // SECTION: Bound input
    [BindProperty]
    public StagePlanEditInput Input { get; set; } = new();

    // SECTION: View state
    public string StageName { get; private set; } = string.Empty;
    public string? ProjectName { get; private set; }
    public bool IsLocked { get; private set; }


    public async Task<IActionResult> OnGetAsync(int id, string stageCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stageCode))
        {
            return BadRequest();
        }

        Input.ProjectId = id;
        Input.StageCode = stageCode;

        var loadResult = await LoadAsync(id, stageCode, cancellationToken);
        if (loadResult is not null)
        {
            return loadResult;
        }

        if (IsLocked)
        {
            ModelState.AddModelError(string.Empty, "Plan is awaiting approval and cannot be edited.");
            return Page();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        if (id != Input.ProjectId)
        {
            TempData["Error"] = "Unable to process the request. Please try again.";
            return RedirectToPage("/Projects/Overview", new { id });
        }

        var stageCode = Input.StageCode;
        if (string.IsNullOrWhiteSpace(stageCode))
        {
            TempData["Error"] = "A valid stage is required.";
            return RedirectToPage("/Projects/Overview", new { id });
        }

        var loadResult = await LoadAsync(id, stageCode, cancellationToken, populatePlanFromExisting: false);
        if (loadResult is not null)
        {
            return loadResult;
        }

        if (IsLocked)
        {
            ModelState.AddModelError(string.Empty, "Plan is awaiting approval and cannot be edited.");
            return Page();
        }

        // SECTION: Validation
        if ((Input.PlannedStart.HasValue && !Input.PlannedDue.HasValue) ||
            (!Input.PlannedStart.HasValue && Input.PlannedDue.HasValue))
        {
            ModelState.AddModelError(string.Empty, "Provide both planned start and planned due, or leave both empty.");
        }

        if (Input.PlannedStart is DateOnly start && Input.PlannedDue is DateOnly due && start > due)
        {
            ModelState.AddModelError(string.Empty, "Planned start cannot be after planned due.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        // SECTION: Persist
        try
        {
            var userId = _userContext.UserId;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Forbid();
            }

            await _planDraft.SavePlanAsync(
                id,
                new[]
                {
                    new StagePlanInput
                    {
                        StageCode = stageCode,
                        PlannedStart = Input.PlannedStart,
                        PlannedDue = Input.PlannedDue
                    }
                },
                userId,
                cancellationToken);
        }
        catch (PlanDraftLockedException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToPage("/Projects/Overview", new { id });
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToPage("/Projects/Overview", new { id });
        }

        TempData["Flash"] = "Stage plan saved.";
        return RedirectToPage("/Projects/Overview", new { id });
    }

    private async Task<IActionResult?> LoadAsync(int projectId, string stageCode, CancellationToken cancellationToken, bool populatePlanFromExisting = true)
    {
        var userId = _userContext.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Forbid();
        }

        var principal = _userContext.User;
        var isAdmin = principal.IsInRole("Admin");
        var isHoD = principal.IsInRole("HoD");

        var project = await _db.Projects
            .SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        ProjectName = project.Name;

        var isProjectsHod = isHoD && string.Equals(project.HodUserId, userId, StringComparison.Ordinal);
        var isProjectsPo = string.Equals(project.LeadPoUserId, userId, StringComparison.Ordinal);

        if (!isAdmin && !isProjectsPo && !isProjectsHod)
        {
            _logger.LogWarning("Unauthorized stage plan edit attempt for Project {ProjectId} by {UserId}.", projectId, userId);
            return Forbid();
        }

        var stageCodes = ProcurementWorkflow.StageCodesFor(project.WorkflowVersion);
        if (!stageCodes.Contains(stageCode, StringComparer.OrdinalIgnoreCase))
        {
            return BadRequest();
        }

        StageName = StageCodes.DisplayNameOf(stageCode);

        var draft = await _planDraft.GetMyDraftAsync(projectId, cancellationToken);
        var draftStage = draft?.StagePlans.FirstOrDefault(p => string.Equals(p.StageCode, stageCode, StringComparison.OrdinalIgnoreCase));

        var stage = await _db.ProjectStages
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.ProjectId == projectId && s.StageCode == stageCode, cancellationToken);

        Input.ProjectId = projectId;
        Input.StageCode = stageCode;

        if (populatePlanFromExisting)
        {
            Input.PlannedStart = draftStage?.PlannedStart ?? stage?.PlannedStart;
            Input.PlannedDue = draftStage?.PlannedDue ?? stage?.PlannedDue;
        }

        IsLocked = await _db.PlanVersions
            .AsNoTracking()
            .AnyAsync(p => p.ProjectId == projectId && p.Status == PlanVersionStatus.PendingApproval, cancellationToken);

        return null;
    }
}
