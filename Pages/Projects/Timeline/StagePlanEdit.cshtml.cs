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
using ProjectManagement.Helpers;
using ProjectManagement.Models;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Plans;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Pages.Projects.Timeline;

[Authorize(Roles = "Admin,Project Officer,HoD")]
[AutoValidateAntiforgeryToken]
public class StagePlanEditModel : PageModel
{
    private const string ActionSubmit = "submit";

    // SECTION: Dependencies
    private readonly ApplicationDbContext _db;
    private readonly PlanDraftService _planDraft;
    private readonly PlanApprovalService _planApproval;
    private readonly IUserContext _userContext;
    private readonly ILogger<StagePlanEditModel> _logger;

    // SECTION: Construction
    public StagePlanEditModel(
        ApplicationDbContext db,
        PlanDraftService planDraft,
        PlanApprovalService planApproval,
        IUserContext userContext,
        ILogger<StagePlanEditModel> logger)
    {
        _db = db;
        _planDraft = planDraft;
        _planApproval = planApproval;
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
    public bool IsProjectHod { get; private set; }


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

        var submitForApproval = string.Equals(Input.Action, ActionSubmit, StringComparison.OrdinalIgnoreCase);

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

            if (submitForApproval)
            {
                await SubmitAndMaybeApproveAsync(id, userId, cancellationToken);
            }
        }
        catch (PlanDraftLockedException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToPage("/Projects/Overview", new { id });
        }
        catch (PlanApprovalValidationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Errors.Count > 0 ? string.Join(" ", ex.Errors) : ex.Message);
            return Page();
        }
        catch (DomainException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToPage("/Projects/Overview", new { id });
        }

        if (submitForApproval)
        {
            TempData["Flash"] = IsProjectHod
                ? "Stage plan submitted and approved."
                : "Stage plan submitted for approval.";
        }
        else
        {
            TempData["Flash"] = "Stage plan saved as draft.";
        }
        return RedirectToPage("/Projects/Overview", new { id });
    }

    private async Task SubmitAndMaybeApproveAsync(int projectId, string userId, CancellationToken cancellationToken)
    {
        await _planApproval.SubmitForApprovalAsync(projectId, userId, cancellationToken);

        if (IsProjectHod)
        {
            var approved = await _planApproval.ApproveLatestDraftAsHodAsync(projectId, userId, cancellationToken);

            if (!approved)
            {
                throw new InvalidOperationException("No submission was available to approve.");
            }
        }
    }

    private async Task<IActionResult?> LoadAsync(int projectId, string stageCode, CancellationToken cancellationToken)
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
        Input.PlannedStart = draftStage?.PlannedStart ?? stage?.PlannedStart;
        Input.PlannedDue = draftStage?.PlannedDue ?? stage?.PlannedDue;

        var hasPendingApproval = await _db.PlanVersions
            .AsNoTracking()
            .AnyAsync(p => p.ProjectId == projectId && p.Status == PlanVersionStatus.PendingApproval, cancellationToken);

        IsProjectHod = isProjectsHod;
        IsLocked = hasPendingApproval && !(isAdmin || isProjectsHod);

        return null;
    }
}
