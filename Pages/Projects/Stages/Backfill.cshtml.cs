using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
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
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Stages;

namespace ProjectManagement.Pages.Projects.Stages;

[Authorize(Roles = "Admin,HoD")]
[AutoValidateAntiforgeryToken]
public sealed class BackfillModel : PageModel
{
    private const string BackfillNote = "Schedule backfill";

    private readonly ApplicationDbContext _db;
    private readonly StageDirectApplyService _directApply;
    private readonly ILogger<BackfillModel> _logger;

    public BackfillModel(ApplicationDbContext db, StageDirectApplyService directApply, ILogger<BackfillModel> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _directApply = directApply ?? throw new ArgumentNullException(nameof(directApply));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty]
    public StageBackfillInput Input { get; set; } = new();

    public sealed class StageBackfillInput
    {
        [Required]
        public int ProjectId { get; set; }

        [Required]
        [StringLength(32)]
        public string StageCode { get; set; } = string.Empty;

        public DateOnly? ActualStart { get; set; }
        public DateOnly? CompletedOn { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(int projectId, string? stageCode, CancellationToken ct)
    {
        if (projectId <= 0 || string.IsNullOrWhiteSpace(stageCode))
        {
            return NotFound();
        }

        var projectExists = await _db.Projects.AsNoTracking().AnyAsync(p => p.Id == projectId, ct);
        if (!projectExists)
        {
            return NotFound();
        }

        var normalizedStage = stageCode.Trim().ToUpperInvariant();
        if (!StageCodes.All.Contains(normalizedStage, StringComparer.OrdinalIgnoreCase))
        {
            return RedirectToPage("/Projects/Overview", new { id = projectId });
        }

        var redirectValues = new
        {
            id = projectId,
            oc = "backfill",
            stage = normalizedStage
        };

        return RedirectToPage("/Projects/Overview", redirectValues);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "Invalid value." : e.ErrorMessage)
                .ToArray();

            return RedirectWithError(errors);
        }

        var normalizedStage = Input.StageCode.Trim().ToUpperInvariant();
        Input.StageCode = normalizedStage;
        if (!StageCodes.All.Contains(normalizedStage, StringComparer.OrdinalIgnoreCase))
        {
            return RedirectWithError(new[] { "Stage not recognised." });
        }

        var stage = await _db.ProjectStages
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.ProjectId == Input.ProjectId && s.StageCode == normalizedStage, ct);

        if (stage is null)
        {
            var projectExists = await _db.Projects.AsNoTracking().AnyAsync(p => p.Id == Input.ProjectId, ct);
            if (!projectExists)
            {
                return NotFound();
            }

            return RedirectWithError(new[] { "Stage not found for this project." });
        }

        var targetStart = Input.ActualStart ?? stage.ActualStart;
        var targetCompletion = Input.CompletedOn ?? stage.CompletedOn;

        var validationErrors = new List<string>();
        if (targetStart is null)
        {
            validationErrors.Add("Actual start date is required.");
        }

        if (targetCompletion is null)
        {
            validationErrors.Add("Completion date is required.");
        }

        if (targetStart.HasValue && targetCompletion.HasValue && targetCompletion.Value < targetStart.Value)
        {
            validationErrors.Add("Completion date cannot be before the actual start date.");
        }

        if (validationErrors.Count > 0)
        {
            return RedirectWithError(validationErrors, targetStart, targetCompletion);
        }

        var userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Forbid();
        }

        try
        {
            var needsStartUpdate = !stage.ActualStart.HasValue || (Input.ActualStart.HasValue && Input.ActualStart.Value != stage.ActualStart.Value);
            var needsCompletionUpdate = !stage.CompletedOn.HasValue || (Input.CompletedOn.HasValue && Input.CompletedOn.Value != stage.CompletedOn.Value);

            if (needsStartUpdate)
            {
                await _directApply.ApplyAsync(
                    Input.ProjectId,
                    normalizedStage,
                    "Reopen",
                    targetStart,
                    BackfillNote,
                    userId,
                    forceBackfillPredecessors: false,
                    ct);
            }

            if (needsCompletionUpdate || needsStartUpdate)
            {
                await _directApply.ApplyAsync(
                    Input.ProjectId,
                    normalizedStage,
                    "Completed",
                    targetCompletion,
                    BackfillNote,
                    userId,
                    forceBackfillPredecessors: false,
                    ct);
            }

            TempData["Flash"] = "Stage actuals updated.";
            return RedirectToPage("/Projects/Overview", new { id = Input.ProjectId });
        }
        catch (StageDirectApplyNotFoundException)
        {
            return RedirectWithError(new[] { "Stage could not be updated." }, targetStart, targetCompletion);
        }
        catch (StageDirectApplyNotHeadOfDepartmentException)
        {
            TempData["Error"] = "Only the assigned Head of Department can update this stage.";
            return RedirectToPage("/Projects/Overview", new { id = Input.ProjectId });
        }
        catch (StageDirectApplyValidationException ex)
        {
            var details = ex.Details?.Where(d => !string.IsNullOrWhiteSpace(d)).ToArray() ?? Array.Empty<string>();
            return RedirectWithError(details.Length > 0 ? details : new[] { "Unable to update stage." }, targetStart, targetCompletion);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Stage backfill failed for project {ProjectId} stage {StageCode}.", Input.ProjectId, normalizedStage);
            return RedirectWithError(new[] { "Could not save stage updates." }, targetStart, targetCompletion);
        }
    }

    private RedirectToPageResult RedirectWithError(IEnumerable<string> errors, DateOnly? start = null, DateOnly? completion = null)
    {
        var message = string.Join(" ", errors.Where(e => !string.IsNullOrWhiteSpace(e)));
        if (!string.IsNullOrWhiteSpace(message))
        {
            TempData["Error"] = message;
        }

        return RedirectToPage(
            "/Projects/Overview",
            new
            {
                id = Input.ProjectId,
                oc = "backfill",
                stage = string.IsNullOrWhiteSpace(Input.StageCode) ? null : Input.StageCode,
                start = start?.ToString("yyyy-MM-dd"),
                finish = completion?.ToString("yyyy-MM-dd")
            });
    }
}
