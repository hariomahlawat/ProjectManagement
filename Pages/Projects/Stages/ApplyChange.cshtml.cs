using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Services.Stages;
using ProjectManagement.Utilities;

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
        "Skipped",
        "Reopen"
    };

    private readonly ApplicationDbContext _db;
    private readonly StageDirectApplyService _service;
    private readonly ILogger<ApplyChangeModel> _logger;

    public ApplyChangeModel(
        ApplicationDbContext db,
        StageDirectApplyService service,
        ILogger<ApplyChangeModel> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        public bool ForceBackfillPredecessors { get; set; }
    }

    public async Task<IActionResult> OnPostAsync([FromBody] ApplyChangeInput input, CancellationToken ct)
    {
        _logger.LogInformation(
            "ApplyChange POST ConnHash={ConnHash}",
            ConnectionStringHasher.Hash(_db.Database.GetConnectionString()));

        if (!ModelState.IsValid)
        {
            var errs = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "Invalid value." : e.ErrorMessage)
                .ToArray();

            return ValidationFailure(errs);
        }

        if (input.ProjectId <= 0)
        {
            return ValidationFailure(new[] { "ProjectId must be greater than zero." });
        }

        if (string.IsNullOrWhiteSpace(input.StageCode))
        {
            return ValidationFailure(new[] { "StageCode is required." });
        }

        var statusMatch = AllowedStatuses.FirstOrDefault(
            s => string.Equals(s, input.Status, StringComparison.OrdinalIgnoreCase));

        if (statusMatch is null)
        {
            return ValidationFailure(new[]
            {
                "Status must be one of: NotStarted, InProgress, Completed, Blocked, Skipped, Reopen."
            });
        }

        var requiresDate = string.Equals(statusMatch, "InProgress", StringComparison.Ordinal);

        if (requiresDate && input.Date is null)
        {
            return ValidationFailure(new[] { "Start date is required for InProgress." });
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
                input.ForceBackfillPredecessors,
                ct);

            return new OkObjectResult(new
            {
                ok = true,
                updated = new
                {
                    status = result.UpdatedStatus,
                    actualStart = result.ActualStart?.ToString("yyyy-MM-dd"),
                    completedOn = result.CompletedOn?.ToString("yyyy-MM-dd")
                },
                backfilled = new
                {
                    count = result.BackfilledCount,
                    stages = result.BackfilledStages ?? Array.Empty<string>()
                },
                warnings = result.Warnings ?? Array.Empty<string>()
            });
        }
        catch (StageDirectApplyNotFoundException)
        {
            var projectExists = await _db.Projects
                .AsNoTracking()
                .AnyAsync(p => p.Id == input.ProjectId, ct);

            if (projectExists)
            {
                return StatusCode(StatusCodes.Status409Conflict, new
                {
                    ok = false,
                    error = "stale"
                });
            }

            return NotFound(new { ok = false, error = "Project or stage not found." });
        }
        catch (StageDirectApplyNotHeadOfDepartmentException)
        {
            return Forbid();
        }
        catch (StageDirectApplyValidationException ex)
        {
            return ValidationFailure(ex.Details, ex.MissingPredecessors);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to apply direct stage change for project {ProjectId} stage {StageCode}.", input.ProjectId, input.StageCode);
            return ValidationFailure(new[]
            {
                "Could not save stage update. The database may not allow blank dates for admin completion."
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    private static UnprocessableEntityObjectResult ValidationFailure(
        IEnumerable<string> details,
        IReadOnlyList<string>? missingPredecessors = null)
    {
        var detailArray = details?.Where(d => !string.IsNullOrWhiteSpace(d)).ToArray()
                         ?? Array.Empty<string>();

        var missingArray = missingPredecessors is { Count: > 0 }
            ? missingPredecessors.ToArray()
            : Array.Empty<string>();

        return new UnprocessableEntityObjectResult(new
        {
            ok = false,
            error = "validation",
            details = detailArray,
            missingPredecessors = missingArray
        });
    }
}
