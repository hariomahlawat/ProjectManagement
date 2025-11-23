using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
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
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Plans;
using ProjectManagement.Services.Stages;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Pages.Projects.Timeline;

[Authorize(Roles = "Admin,Project Officer,HoD")]
[AutoValidateAntiforgeryToken]
public class EditPlanModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;
    private readonly PlanGenerationService _planGeneration;
    private readonly PlanDraftService _planDraft;
    private readonly PlanApprovalService _planApproval;
    private readonly ILogger<EditPlanModel> _logger;
    private readonly IUserContext _userContext;

    public EditPlanModel(
        ApplicationDbContext db,
        IAuditService audit,
        PlanGenerationService planGeneration,
        PlanDraftService planDraft,
        PlanApprovalService planApproval,
        ILogger<EditPlanModel> logger,
        IUserContext userContext)
    {
        _db = db;
        _audit = audit;
        _planGeneration = planGeneration;
        _planDraft = planDraft;
        _planApproval = planApproval;
        _logger = logger;
        _userContext = userContext;
    }

    [BindProperty]
    public PlanEditInput Input { get; set; } = new();

    public IActionResult OnGet(int id) => NotFound();

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        if (id != Input.ProjectId)
        {
            TempData["Error"] = "Unable to process the request. Please reload and try again.";
            TempData["OpenOffcanvas"] = "plan-edit";
            return RedirectToPage("/Projects/Overview", new { id });
        }

        var userId = _userContext.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            return Forbid();
        }

        var principal = _userContext.User;
        var isAdmin = principal.IsInRole("Admin");
        var isHoD = principal.IsInRole("HoD");

        var project = await _db.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        var workflowVersion = project.WorkflowVersion;

        var isProjectsHod = isHoD && string.Equals(project.HodUserId, userId, StringComparison.Ordinal);
        var isProjectsPo = string.Equals(project.LeadPoUserId, userId, StringComparison.Ordinal);

        if (!isAdmin && !isProjectsPo && !isProjectsHod)
        {
            _logger.LogWarning("User {UserId} attempted to edit plan for project {ProjectId} without permission.", userId, id);
            return Forbid();
        }

        if (string.Equals(Input.Mode, PlanEditorModes.Durations, StringComparison.OrdinalIgnoreCase))
        {
            return await HandleDurationsAsync(id, userId, cancellationToken, workflowVersion);
        }

        return await HandleExactAsync(id, userId, principal, cancellationToken, workflowVersion);
    }

    public async Task<IActionResult> OnGetValidateAsync(int id, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            return Forbid();
        }

        var project = await _db.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        var principal = _userContext.User;
        var isAdmin = principal.IsInRole("Admin");
        var isHoD = principal.IsInRole("HoD");
        var isProjectsHod = isHoD && string.Equals(project.HodUserId, userId, StringComparison.Ordinal);
        var isProjectsPo = string.Equals(project.LeadPoUserId, userId, StringComparison.Ordinal);

        if (!isAdmin && !isProjectsPo && !isProjectsHod)
        {
            return Forbid();
        }

        var draft = await _planDraft.GetMyDraftAsync(id, cancellationToken);
        if (draft is null)
        {
            return new JsonResult(new { ok = true, errors = Array.Empty<string>() });
        }

        var errors = await _planApproval.GetValidationErrorsAsync(draft.Id, cancellationToken);
        return new JsonResult(new { ok = errors.Count == 0, errors });
    }

    public async Task<IActionResult> OnPostDeleteDraftAsync(int id, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            return Forbid();
        }

        var project = await _db.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        var principal = _userContext.User;
        var isAdmin = principal.IsInRole("Admin");
        var isHoD = principal.IsInRole("HoD");
        var isProjectsHod = isHoD && string.Equals(project.HodUserId, userId, StringComparison.Ordinal);
        var isProjectsPo = string.Equals(project.LeadPoUserId, userId, StringComparison.Ordinal);

        if (!isAdmin && !isProjectsPo && !isProjectsHod)
        {
            _logger.LogWarning("User {UserId} attempted to delete plan draft for project {ProjectId} without permission.", userId, id);
            return Forbid();
        }

        var result = await _planDraft.DeleteDraftAsync(id, cancellationToken);

        switch (result)
        {
            case PlanDraftDeleteResult.Success:
                TempData["Flash"] = "Draft discarded.";
                break;
            case PlanDraftDeleteResult.NotFound:
                TempData["Error"] = "No draft to discard.";
                break;
            case PlanDraftDeleteResult.Conflict:
                TempData["Error"] = "Draft already submitted. Nothing to discard.";
                break;
        }

        return RedirectToPage("/Projects/Overview", new { id });
    }

    private async Task<IActionResult> HandleExactAsync(int id, string userId, ClaimsPrincipal principal, CancellationToken cancellationToken, string? workflowVersion)
    {
        var rows = Input.Rows ??= new List<PlanEditInputRow>();

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Code))
            {
                continue;
            }

            if (row.PlannedStart.HasValue && row.PlannedDue.HasValue &&
                row.PlannedStart.Value > row.PlannedDue.Value)
            {
                var name = string.IsNullOrWhiteSpace(row.Name) ? row.Code : row.Name;
                ModelState.AddModelError(string.Empty, $"For {name}, Start date cannot be after Due date.");
            }
        }

        if (!ModelState.IsValid)
        {
            return RedirectWithValidationErrors(id);
        }

        var action = NormalizeAction(Input.Action);
        var submitForApproval = string.Equals(action, PlanEditActions.Submit, StringComparison.OrdinalIgnoreCase);

        PlanVersion draft;
        try
        {
            draft = await _planDraft.CreateOrGetDraftAsync(id, cancellationToken);
        }
        catch (PlanDraftLockedException ex)
        {
            TempData["Error"] = ex.Message;
            TempData["OpenOffcanvas"] = "plan-edit";
            return RedirectToPage("/Projects/Overview", new { id });
        }

        var userName = principal.Identity?.Name;

        var stageMap = draft.StagePlans
            .Where(stage => !string.IsNullOrWhiteSpace(stage.StageCode))
            .ToDictionary(stage => stage.StageCode!, stage => stage, StringComparer.OrdinalIgnoreCase);

        var changes = new List<StageChange>();

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Code))
            {
                continue;
            }

            if (!stageMap.TryGetValue(row.Code, out var stagePlan))
            {
                stagePlan = new StagePlan
                {
                    PlanVersionId = draft.Id,
                    StageCode = row.Code
                };
                draft.StagePlans.Add(stagePlan);
                stageMap[row.Code] = stagePlan;
            }

            var previousStart = stagePlan.PlannedStart;
            var previousDue = stagePlan.PlannedDue;

            if (previousStart == row.PlannedStart && previousDue == row.PlannedDue)
            {
                continue;
            }

            stagePlan.PlannedStart = row.PlannedStart;
            stagePlan.PlannedDue = row.PlannedDue;
            stagePlan.DurationDays = CalculateDuration(row.PlannedStart, row.PlannedDue);

            changes.Add(new StageChange(row.Code, row.Name, previousStart, previousDue, row.PlannedStart, row.PlannedDue));
        }

        var pncApplicable = stageMap.TryGetValue(StageCodes.PNC, out var pncStage) &&
            pncStage.PlannedStart is DateOnly && pncStage.PlannedDue is DateOnly;

        if (draft.PncApplicable != pncApplicable)
        {
            draft.PncApplicable = pncApplicable;
            if (changes.Count == 0)
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        if (changes.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Exact timeline saved for Project {ProjectId}. Conn: {Conn}",
                id,
                _db.Database.GetConnectionString());

            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = id.ToString(CultureInfo.InvariantCulture),
                ["ChangedStages"] = string.Join(";", changes.Select(change => change.Code))
            };

            for (var index = 0; index < changes.Count; index++)
            {
                var change = changes[index];
                var prefix = $"Stage[{index}]";
                data[$"{prefix}.Code"] = change.Code;
                data[$"{prefix}.Name"] = change.Name;
                data[$"{prefix}.Start.Before"] = FormatDate(change.PreviousStart);
                data[$"{prefix}.Start.After"] = FormatDate(change.NewStart);
                data[$"{prefix}.Due.Before"] = FormatDate(change.PreviousDue);
                data[$"{prefix}.Due.After"] = FormatDate(change.NewDue);
            }

            await _audit.LogAsync(
                "Projects.PlanUpdated",
                userId: userId,
                userName: userName,
                data: data);
        }

        if (submitForApproval)
        {
            try
            {
                await _planApproval.SubmitForApprovalAsync(id, userId, cancellationToken);
            }
            catch (PlanApprovalValidationException ex)
            {
                TempData["Error"] = ex.Errors.Count > 0 ? string.Join(" ", ex.Errors) : ex.Message;
                TempData["OpenOffcanvas"] = "plan-edit";
                _logger.LogWarning(ex, "Draft submission failed validation for project {ProjectId} by user {UserId}.", id, userId);
                return RedirectToPage("/Projects/Overview", new { id });
            }
            catch (DomainException ex)
            {
                TempData["Error"] = ex.Message;
                TempData["OpenOffcanvas"] = "plan-edit";
                _logger.LogWarning(ex, "Draft submission blocked for project {ProjectId} by user {UserId}.", id, userId);
                return RedirectToPage("/Projects/Overview", new { id });
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
                TempData["OpenOffcanvas"] = "plan-edit";
                _logger.LogWarning(ex, "Draft submission failed for project {ProjectId} by user {UserId}.", id, userId);
                return RedirectToPage("/Projects/Overview", new { id });
            }

            TempData["Flash"] = "Plan submitted for HoD review.";
        }
        else
        {
            TempData["OpenOffcanvas"] = "plan-edit";

            if (changes.Count > 0)
            {
                TempData["Flash"] = "Draft saved.";
            }
            else
            {
                TempData["Flash"] = "Draft saved. No changes detected.";
            }
        }

        return RedirectToPage("/Projects/Overview", new { id });
    }

    private async Task<IActionResult> HandleDurationsAsync(int id, string userId, CancellationToken ct, string? workflowVersion)
    {
        var action = NormalizeAction(Input.Action);
        var calculateOnly = string.Equals(action, PlanEditActions.Calculate, StringComparison.OrdinalIgnoreCase);
        var submitForApproval = string.Equals(action, PlanEditActions.Submit, StringComparison.OrdinalIgnoreCase);
        var saveDraft = string.Equals(action, PlanEditActions.SaveDraft, StringComparison.OrdinalIgnoreCase) ||
                        (!calculateOnly && !submitForApproval);

        var optionalStages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            StageCodes.PNC
        };

        if (Input.AnchorStart is null)
        {
            ModelState.AddModelError(nameof(Input.AnchorStart), "Please provide the anchor start date.");
        }

        if (!NextStageStartPolicies.IsValid(Input.NextStageStartPolicy))
        {
            ModelState.AddModelError(nameof(Input.NextStageStartPolicy), "Choose a valid next stage start policy.");
        }

        var rows = Input.Rows ??= new List<PlanEditInputRow>();

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Code))
            {
                continue;
            }

            if (optionalStages.Contains(row.Code))
            {
                if (!row.DurationDays.HasValue || row.DurationDays.Value <= 0)
                {
                    row.DurationDays = null;
                }

                continue;
            }

            if (!row.DurationDays.HasValue || row.DurationDays.Value <= 0)
            {
                var name = string.IsNullOrWhiteSpace(row.Name) ? row.Code : row.Name;
                ModelState.AddModelError(string.Empty, $"Duration for {name} must be a positive number of days.");
            }
        }

        var anyDurationProvided = rows.Any(r =>
            !string.IsNullOrWhiteSpace(r.Code) &&
            r.DurationDays.HasValue &&
            r.DurationDays.Value > 0);

        if (submitForApproval && !anyDurationProvided)
        {
            ModelState.AddModelError(string.Empty, "Please provide at least one stage duration before submitting.");
        }

        if (!ModelState.IsValid)
        {
            return RedirectWithValidationErrors(id);
        }

        var settings = await _db.ProjectScheduleSettings.SingleOrDefaultAsync(s => s.ProjectId == id, ct);
        if (settings is null)
        {
            settings = new ProjectScheduleSettings
            {
                ProjectId = id
            };
            _db.ProjectScheduleSettings.Add(settings);
        }

        settings.AnchorStart = Input.AnchorStart;
        settings.IncludeWeekends = Input.IncludeWeekends;
        settings.SkipHolidays = Input.SkipHolidays;
        settings.NextStageStartPolicy = NextStageStartPolicies.IsValid(Input.NextStageStartPolicy)
            ? Input.NextStageStartPolicy
            : NextStageStartPolicies.NextWorkingDay;

        var durationRows = await _db.ProjectPlanDurations
            .Where(d => d.ProjectId == id)
            .ToListAsync(ct);

        var durationMap = durationRows
            .Where(d => !string.IsNullOrWhiteSpace(d.StageCode))
            .ToDictionary(d => d.StageCode!, StringComparer.OrdinalIgnoreCase);

        var stageOrderLookup = ProcurementWorkflow.BuildOrderLookup(workflowVersion);
        var extraSortStart = stageOrderLookup.Count;

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Code))
            {
                continue;
            }

            var sortOrder = StageOrder(row.Code, stageOrderLookup, ref extraSortStart);

            if (!durationMap.TryGetValue(row.Code, out var duration))
            {
                duration = new ProjectPlanDuration
                {
                    ProjectId = id,
                    StageCode = row.Code,
                    SortOrder = sortOrder
                };
                _db.ProjectPlanDurations.Add(duration);
                durationMap[row.Code] = duration;
            }

            duration.DurationDays = row.DurationDays;
            duration.SortOrder = sortOrder;
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Durations saved for Project {ProjectId}. Conn: {Conn}",
            id,
            _db.Database.GetConnectionString());

        PlanVersion draft;
        try
        {
            draft = await _planDraft.CreateOrGetDraftAsync(id, ct);
        }
        catch (PlanDraftLockedException ex)
        {
            TempData["Error"] = ex.Message;
            TempData["OpenOffcanvas"] = "plan-edit";
            return RedirectToPage("/Projects/Overview", new { id });
        }

        var pncApplicable = durationMap.TryGetValue(StageCodes.PNC, out var pncRow) &&
            pncRow.DurationDays.GetValueOrDefault() > 0;

        draft.PncApplicable = pncApplicable;
        await _db.SaveChangesAsync(ct);
        await _planGeneration.GenerateDraftAsync(id, draft.Id, ct);

        if (!submitForApproval)
        {
            await _db.SaveChangesAsync(ct);
        }

        if (submitForApproval)
        {
            try
            {
                await _planApproval.SubmitForApprovalAsync(id, userId, ct);
            }
            catch (PlanApprovalValidationException ex)
            {
                TempData["Error"] = ex.Errors.Count > 0 ? string.Join(" ", ex.Errors) : ex.Message;
                TempData["OpenOffcanvas"] = "plan-edit";
                _logger.LogWarning(ex, "Draft submission failed validation for project {ProjectId} by user {UserId}.", id, userId);
                return RedirectToPage("/Projects/Overview", new { id });
            }
            catch (DomainException ex)
            {
                TempData["Error"] = ex.Message;
                TempData["OpenOffcanvas"] = "plan-edit";
                _logger.LogWarning(ex, "Draft submission blocked for project {ProjectId} by user {UserId}.", id, userId);
                return RedirectToPage("/Projects/Overview", new { id });
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
                TempData["OpenOffcanvas"] = "plan-edit";
                _logger.LogWarning(ex, "Draft submission failed for project {ProjectId} by user {UserId}.", id, userId);
                return RedirectToPage("/Projects/Overview", new { id });
            }
        }

        await _audit.LogAsync(
            "Projects.PlanGeneratedFromDurations",
            userId: userId,
            data: new Dictionary<string, string?>
            {
                ["ProjectId"] = id.ToString(CultureInfo.InvariantCulture),
                ["Action"] = action
            });

        if (submitForApproval)
        {
            TempData["Flash"] = "Plan submitted for HoD review.";
        }
        else
        {
            TempData["OpenOffcanvas"] = "plan-edit";

            if (saveDraft)
            {
                TempData["Flash"] = "Draft saved.";
            }
            else if (calculateOnly)
            {
                TempData["Flash"] = "Draft recalculated.";
            }
            else
            {
                TempData["Flash"] = "Draft updated.";
            }
        }

        return RedirectToPage("/Projects/Overview", new { id });
    }

    private IActionResult RedirectWithValidationErrors(int projectId)
    {
        var messages = ModelState.Values
            .SelectMany(value => value.Errors)
            .Select(error => error.ErrorMessage)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .ToArray();

        TempData["Error"] = messages.Length > 0
            ? string.Join(" ", messages)
            : "Please correct the highlighted errors and try again.";

        TempData["OpenOffcanvas"] = "plan-edit";
        return RedirectToPage("/Projects/Overview", new { id = projectId });
    }

    private static string NormalizeAction(string? action) => string.IsNullOrWhiteSpace(action)
        ? string.Empty
        : action.Trim();

    private static int CalculateDuration(DateOnly? start, DateOnly? due)
    {
        if (start.HasValue && due.HasValue && due.Value >= start.Value)
        {
            return due.Value.DayNumber - start.Value.DayNumber + 1;
        }

        return 0;
    }

    private static string? FormatDate(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // SECTION: Stage ordering helpers
    private static int StageOrder(string stageCode, IReadOnlyDictionary<string, int> stageOrderLookup, ref int extraSortStart)
    {
        if (stageOrderLookup.TryGetValue(stageCode, out var index))
        {
            return index;
        }

        return extraSortStart++;
    }

    private sealed record StageChange(
        string Code,
        string Name,
        DateOnly? PreviousStart,
        DateOnly? PreviousDue,
        DateOnly? NewStart,
        DateOnly? NewDue);
}
