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
using ProjectManagement.Services.Stages;

namespace ProjectManagement.Pages.Projects.Stages;

[Authorize(Roles = "HoD")]
[AutoValidateAntiforgeryToken]
public class ApplyChangeModel : PageModel
{
    private static readonly string[] AllowedStatuses =
    {
        "NotStarted",
        "InProgress",
        "Completed",
        "Blocked",
        "Skipped"
    };

    private readonly StageDirectApplyService _service;

    public ApplyChangeModel(StageDirectApplyService service)
    {
        _service = service;
    }

    public class ApplyChangeInput
    {
        [Required]
        public int ProjectId { get; set; }

        [Required]
        [StringLength(32)]
        public string StageCode { get; set; } = string.Empty;

        [Required]
        [StringLength(16)]
        public string Status { get; set; } = string.Empty;

        public DateOnly? Date { get; set; }

        [StringLength(1024)]
        public string? Note { get; set; }
    }

    public async Task<IActionResult> OnPostAsync([FromBody] ApplyChangeInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            var errs = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "Invalid value." : e.ErrorMessage)
                .ToArray();

            return new UnprocessableEntityObjectResult(new { ok = false, error = "Validation failed", details = errs });
        }

        if (input.ProjectId <= 0)
        {
            return new UnprocessableEntityObjectResult(new
            {
                ok = false,
                error = "Validation failed",
                details = new[] { "ProjectId must be greater than zero." }
            });
        }

        if (string.IsNullOrWhiteSpace(input.StageCode))
        {
            return new UnprocessableEntityObjectResult(new
            {
                ok = false,
                error = "Validation failed",
                details = new[] { "StageCode is required." }
            });
        }

        var statusMatch = AllowedStatuses.FirstOrDefault(
            s => string.Equals(s, input.Status, StringComparison.OrdinalIgnoreCase));

        if (statusMatch is null)
        {
            return new UnprocessableEntityObjectResult(new
            {
                ok = false,
                error = "Validation failed",
                details = new[]
                {
                    "Status must be one of: NotStarted, InProgress, Completed, Blocked, Skipped."
                }
            });
        }

        var needsDate = string.Equals(statusMatch, "Completed", StringComparison.Ordinal)
                         || string.Equals(statusMatch, "InProgress", StringComparison.Ordinal);

        if (needsDate && input.Date is null)
        {
            return new UnprocessableEntityObjectResult(new
            {
                ok = false,
                error = "Validation failed",
                details = new[] { "Date is required for the selected status." }
            });
        }

        var hodUserId = User?.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? User?.Identity?.Name
                        ?? string.Empty;

        if (string.IsNullOrWhiteSpace(hodUserId))
        {
            return Forbid();
        }

        try
        {
            var result = await _service.ApplyAsync(
                input.ProjectId,
                input.StageCode,
                statusMatch,
                input.Date,
                input.Note,
                hodUserId,
                ct);

            switch (result.Outcome)
            {
                case DirectApplyOutcome.StageNotFound:
                    return NotFound(new { ok = false, error = "Project or stage not found." });
                case DirectApplyOutcome.NotHeadOfDepartment:
                    return Forbid();
                case DirectApplyOutcome.ValidationFailed:
                    return new UnprocessableEntityObjectResult(new
                    {
                        ok = false,
                        error = "Validation failed",
                        details = new[] { result.Error ?? "Validation failed." }
                    });
                case DirectApplyOutcome.Success:
                default:
                    break;
            }

            var warnings = result.Warnings?.ToList() ?? new List<string>();

            if (result.SupersededRequest)
            {
                warnings.Add("Pending request was superseded by this change.");
            }

            return new OkObjectResult(new
            {
                ok = true,
                updated = new
                {
                    status = result.UpdatedStatus?.ToString(),
                    actualStart = result.ActualStart?.ToString("yyyy-MM-dd"),
                    completedOn = result.CompletedOn?.ToString("yyyy-MM-dd")
                },
                warnings = warnings.Count == 0 ? Array.Empty<string>() : warnings.ToArray()
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }
}
