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
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Stages;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Pages.Projects.Timeline;

[Authorize(Roles = "Admin,HoD,PO")]
[ValidateAntiForgeryToken]
public class EditPlanModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IAuditService _audit;
    private readonly PlanGenerationService _planGeneration;

    public EditPlanModel(
        ApplicationDbContext db,
        UserManager<ApplicationUser> users,
        IAuditService audit,
        PlanGenerationService planGeneration)
    {
        _db = db;
        _users = users;
        _audit = audit;
        _planGeneration = planGeneration;
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

        if (string.Equals(Input.Mode, PlanEditorModes.Durations, StringComparison.OrdinalIgnoreCase))
        {
            return await HandleDurationsAsync(id, cancellationToken);
        }

        return await HandleExactAsync(id, cancellationToken);
    }

    private async Task<IActionResult> HandleExactAsync(int id, CancellationToken cancellationToken)
    {
        var validationErrors = ValidateExactInput();
        if (validationErrors.Count > 0)
        {
            TempData["Error"] = string.Join(" ", validationErrors);
            TempData["OpenOffcanvas"] = "plan-edit";
            return RedirectToPage("/Projects/Overview", new { id });
        }

        var project = await _db.Projects.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        var stageList = await _db.ProjectStages
            .Where(stage => stage.ProjectId == id)
            .ToListAsync(cancellationToken);

        var stageMap = stageList
            .Where(stage => !string.IsNullOrWhiteSpace(stage.StageCode))
            .ToDictionary(stage => stage.StageCode, StringComparer.OrdinalIgnoreCase);

        var changes = new List<StageChange>();

        foreach (var row in Input.Rows)
        {
            if (string.IsNullOrWhiteSpace(row.Code))
            {
                continue;
            }

            if (!stageMap.TryGetValue(row.Code, out var stage))
            {
                continue;
            }

            var previousStart = stage.PlannedStart;
            var previousDue = stage.PlannedDue;

            if (previousStart == row.PlannedStart && previousDue == row.PlannedDue)
            {
                continue;
            }

            stage.PlannedStart = row.PlannedStart;
            stage.PlannedDue = row.PlannedDue;

            changes.Add(new StageChange(row.Code, row.Name, previousStart, previousDue, row.PlannedStart, row.PlannedDue));
        }

        if (changes.Count == 0)
        {
            TempData["Flash"] = "No changes.";
            return RedirectToPage("/Projects/Overview", new { id });
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        project.PlanApprovedAt = null;
        project.PlanApprovedByUserId = null;

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var userId = _users.GetUserId(User);
        var userName = User.Identity?.Name;

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

        TempData["Flash"] = "Timeline plan updated.";
        return RedirectToPage("/Projects/Overview", new { id });
    }

    private async Task<IActionResult> HandleDurationsAsync(int id, CancellationToken ct)
    {
        var validationErrors = ValidateDurationsInput();
        if (validationErrors.Count > 0)
        {
            TempData["Error"] = string.Join(" ", validationErrors);
            TempData["OpenOffcanvas"] = "plan-edit";
            return RedirectToPage("/Projects/Overview", new { id });
        }

        var project = await _db.Projects.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (project is null)
        {
            return NotFound();
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

        await _planGeneration.GenerateAsync(id, ct);

        project.PlanApprovedAt = null;
        project.PlanApprovedByUserId = null;

        await _db.SaveChangesAsync(ct);

        var userId = _users.GetUserId(User);
        await _audit.LogAsync(
            "Projects.PlanGeneratedFromDurations",
            userId: userId,
            data: new Dictionary<string, string?>
            {
                ["ProjectId"] = id.ToString(CultureInfo.InvariantCulture),
                ["Action"] = Input.Action
            });

        TempData["Flash"] = string.Equals(Input.Action, "Save", StringComparison.OrdinalIgnoreCase)
            ? "Timeline plan recalculated and marked pending approval."
            : "Timeline plan recalculated.";

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
            errors.Add("Set an anchor start date before calculating the plan.");
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
            if (!string.IsNullOrWhiteSpace(row.Code) && row.DurationDays.HasValue && row.DurationDays < 0)
            {
                var name = string.IsNullOrWhiteSpace(row.Name) ? row.Code : row.Name;
                errors.Add($"Duration for {name} must be zero or greater.");
            }
        }

        return errors;
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
