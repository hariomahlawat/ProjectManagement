using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Plans;
using ProjectManagement.Services.Stages;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Pages.Projects.Timeline;

[Authorize(Roles = "Admin,Project Officer")]
[ValidateAntiForgeryToken]
public class EditPlanModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IAuditService _audit;
    private readonly PlanGenerationService _planGeneration;
    private readonly PlanDraftService _planDraft;
    private readonly PlanApprovalService _planApproval;
    private readonly ILogger<EditPlanModel> _logger;

    public EditPlanModel(
        ApplicationDbContext db,
        UserManager<ApplicationUser> users,
        IAuditService audit,
        PlanGenerationService planGeneration,
        PlanDraftService planDraft,
        PlanApprovalService planApproval,
        ILogger<EditPlanModel> logger)
    {
        _db = db;
        _users = users;
        _audit = audit;
        _planGeneration = planGeneration;
        _planDraft = planDraft;
        _planApproval = planApproval;
        _logger = logger;
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

        var project = await _db.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        var userId = _users.GetUserId(User);
        if (string.IsNullOrEmpty(userId))
        {
            return Forbid();
        }

        var isAdmin = User.IsInRole("Admin");
        if (!isAdmin && !string.Equals(project.LeadPoUserId, userId, StringComparison.Ordinal))
        {
            _logger.LogWarning("User {UserId} attempted to edit plan for project {ProjectId} without permission.", userId, id);
            return Forbid();
        }

        if (string.Equals(Input.Mode, PlanEditorModes.Durations, StringComparison.OrdinalIgnoreCase))
        {
            return await HandleDurationsAsync(id, userId, cancellationToken);
        }

        return await HandleExactAsync(id, userId, cancellationToken);
    }

    private async Task<IActionResult> HandleExactAsync(int id, string userId, CancellationToken cancellationToken)
    {
        var validationErrors = ValidateExactInput();
        if (validationErrors.Count > 0)
        {
            TempData["Error"] = string.Join(" ", validationErrors);
            TempData["OpenOffcanvas"] = "plan-edit";
            return RedirectToPage("/Projects/Overview", new { id });
        }

        var action = NormalizeAction(Input.Action);
        var submitForApproval = string.Equals(action, PlanEditActions.Submit, StringComparison.OrdinalIgnoreCase);

        PlanVersion draft;
        try
        {
            draft = await _planDraft.CreateOrGetDraftAsync(id, userId, cancellationToken);
        }
        catch (PlanDraftLockedException ex)
        {
            TempData["Error"] = ex.Message;
            TempData["OpenOffcanvas"] = "plan-edit";
            return RedirectToPage("/Projects/Overview", new { id });
        }

        var userName = User.Identity?.Name;

        var stageMap = draft.StagePlans
            .Where(stage => !string.IsNullOrWhiteSpace(stage.StageCode))
            .ToDictionary(stage => stage.StageCode!, stage => stage, StringComparer.OrdinalIgnoreCase);

        var changes = new List<StageChange>();

        foreach (var row in Input.Rows)
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

        if (changes.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Exact timeline saved for Project {ProjectId}. Conn: {Conn}",
                id,
                _db.Database.GetDbConnection().ConnectionString);

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
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
                TempData["OpenOffcanvas"] = "plan-edit";
                _logger.LogWarning(ex, "Draft submission failed for project {ProjectId} by user {UserId}.", id, userId);
                return RedirectToPage("/Projects/Overview", new { id });
            }

            TempData["Flash"] = "Draft submitted for approval.";
        }
        else if (changes.Count > 0)
        {
            TempData["Flash"] = "Timeline saved as draft.";
        }
        else
        {
            TempData["Flash"] = "No changes were made.";
        }

        return RedirectToPage("/Projects/Overview", new { id });
    }

    private async Task<IActionResult> HandleDurationsAsync(int id, string userId, CancellationToken ct)
    {
        var action = NormalizeAction(Input.Action);
        var calculateOnly = string.Equals(action, PlanEditActions.Calculate, StringComparison.OrdinalIgnoreCase);
        var submitForApproval = string.Equals(action, PlanEditActions.Submit, StringComparison.OrdinalIgnoreCase);
        var saveDraft = string.Equals(action, PlanEditActions.SaveDraft, StringComparison.OrdinalIgnoreCase) ||
                        (!calculateOnly && !submitForApproval);

        var validationErrors = ValidateDurationsInput();
        if (validationErrors.Count > 0)
        {
            TempData["Error"] = string.Join(" ", validationErrors);
            TempData["OpenOffcanvas"] = "plan-edit";
            return RedirectToPage("/Projects/Overview", new { id });
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

        var extraSortStart = StageCodes.All.Length;

        foreach (var row in Input.Rows)
        {
            if (string.IsNullOrWhiteSpace(row.Code))
            {
                continue;
            }

            var sortOrder = StageOrder(row.Code, ref extraSortStart);

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
        _logger.LogInformation("Durations saved for Project {ProjectId}. Conn: {Conn}",
            id,
            _db.Database.GetDbConnection().ConnectionString);

        PlanVersion draft;
        try
        {
            draft = await _planDraft.CreateOrGetDraftAsync(id, userId, ct);
        }
        catch (PlanDraftLockedException ex)
        {
            TempData["Error"] = ex.Message;
            TempData["OpenOffcanvas"] = "plan-edit";
            return RedirectToPage("/Projects/Overview", new { id });
        }

        await _planGeneration.GenerateDraftAsync(id, draft.Id, ct);

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
            TempData["Flash"] = "Draft submitted for approval.";
        }
        else if (saveDraft)
        {
            TempData["Flash"] = "Timeline saved as draft.";
        }
        else
        {
            TempData["Flash"] = "Timeline recalculated as draft.";
        }

        return RedirectToPage("/Projects/Overview", new { id });
    }

    private List<string> ValidateExactInput()
    {
        var errors = new List<string>();

        if (Input.Rows is null)
        {
            return errors;
        }

        foreach (var row in Input.Rows)
        {
            if (row.PlannedStart.HasValue && row.PlannedDue.HasValue && row.PlannedStart > row.PlannedDue)
            {
                var name = string.IsNullOrWhiteSpace(row.Name) ? row.Code : row.Name;
                errors.Add($"Planned start must be on or before planned due for {name}.");
            }
        }

        return errors;
    }

    private List<string> ValidateDurationsInput()
    {
        var errors = new List<string>();

        if (Input.AnchorStart is null)
        {
            errors.Add("Set an anchor start date before calculating or saving the plan.");
        }

        if (!NextStageStartPolicies.IsValid(Input.NextStageStartPolicy))
        {
            errors.Add("Choose a valid next stage start policy.");
        }

        if (Input.Rows is null)
        {
            return errors;
        }

        foreach (var row in Input.Rows)
        {
            if (!string.IsNullOrWhiteSpace(row.Code) && row.DurationDays.HasValue && row.DurationDays <= 0)
            {
                var name = string.IsNullOrWhiteSpace(row.Name) ? row.Code : row.Name;
                errors.Add($"Duration for {name} must be a positive number of days.");
            }
        }

        return errors;
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

    private static int StageOrder(string stageCode, ref int extraSortStart)
    {
        var index = Array.IndexOf(StageCodes.All, stageCode);
        if (index >= 0)
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
