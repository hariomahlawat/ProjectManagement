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
using ProjectManagement.Services;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Pages.Projects.Timeline;

[Authorize(Roles = "Admin,HoD,PO")]
[ValidateAntiForgeryToken]
public class EditPlanModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IAuditService _audit;

    public EditPlanModel(ApplicationDbContext db, UserManager<ApplicationUser> users, IAuditService audit)
    {
        _db = db;
        _users = users;
        _audit = audit;
    }

    [BindProperty]
    public PlanEditVm Input { get; set; } = new();

    public IActionResult OnGet(int id) => NotFound();

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        if (id != Input.ProjectId)
        {
            TempData["Error"] = "Unable to process the request. Please reload and try again.";
            TempData["OpenOffcanvas"] = "plan-edit";
            return RedirectToPage("/Projects/Overview", new { id });
        }

        var validationErrors = ValidateInput();
        if (validationErrors.Count > 0)
        {
            TempData["Error"] = string.Join(" ", validationErrors);
            TempData["OpenOffcanvas"] = "plan-edit";
            return RedirectToPage("/Projects/Overview", new { id });
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

    private List<string> ValidateInput()
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

    private static string? FormatDate(DateOnly? value)
        => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed record StageChange(
        string Code,
        string Name,
        DateOnly? PreviousStart,
        DateOnly? PreviousDue,
        DateOnly? NewStart,
        DateOnly? NewDue);
}
