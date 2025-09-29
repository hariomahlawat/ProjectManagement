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
using Microsoft.Extensions.Logging;
using ProjectManagement.Services.Stages;

namespace ProjectManagement.Pages.Projects.Stages;

[Authorize(Roles = "Admin,HoD,Project Officer")]
[AutoValidateAntiforgeryToken]
public class BackfillApplyModel : PageModel
{
    private readonly StageBackfillService _backfillService;
    private readonly ILogger<BackfillApplyModel> _logger;

    public BackfillApplyModel(StageBackfillService backfillService, ILogger<BackfillApplyModel> logger)
    {
        _backfillService = backfillService ?? throw new ArgumentNullException(nameof(backfillService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public class BackfillStageInput
    {
        [Required]
        [StringLength(32)]
        public string StageCode { get; set; } = string.Empty;

        public DateOnly? ActualStart { get; set; }

        public DateOnly? CompletedOn { get; set; }
    }

    public class BackfillApplyInput
    {
        [Required]
        public int ProjectId { get; set; }

        [MinLength(1)]
        public List<BackfillStageInput> Stages { get; set; } = new();
    }

    public async Task<IActionResult> OnPostAsync([FromBody] BackfillApplyInput input, CancellationToken ct)
    {
        _logger.LogInformation(
            "BackfillApply POST ProjectId={ProjectId}",
            input?.ProjectId);

        if (!ModelState.IsValid)
        {
            var details = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "Invalid value." : e.ErrorMessage)
                .ToArray();

            return ValidationFailure(details);
        }

        if (input.ProjectId <= 0)
        {
            return ValidationFailure(new[] { "ProjectId must be greater than zero." });
        }

        if (input.Stages is null || input.Stages.Count == 0)
        {
            return ValidationFailure(new[] { "At least one stage update must be provided." });
        }

        var userId = User?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User?.Identity?.Name
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Forbid();
        }

        try
        {
            var updates = input.Stages
                .Where(stage => !string.IsNullOrWhiteSpace(stage.StageCode))
                .Select(stage => new StageBackfillUpdate(stage.StageCode, stage.ActualStart, stage.CompletedOn))
                .ToArray();

            if (updates.Length == 0)
            {
                return ValidationFailure(new[] { "At least one stage update must be provided." });
            }

            var result = await _backfillService.ApplyAsync(input.ProjectId, updates, userId, ct);

            return new JsonResult(new
            {
                ok = true,
                updated = new
                {
                    count = result.UpdatedCount,
                    stages = result.StageCodes
                }
            });
        }
        catch (StageBackfillValidationException ex)
        {
            return ValidationFailure(ex.Details);
        }
        catch (StageBackfillConflictException ex)
        {
            return StatusCode(StatusCodes.Status409Conflict, new
            {
                ok = false,
                error = "conflict",
                stages = ex.ConflictingStages,
                message = "One or more stages no longer require backfill."
            });
        }
        catch (StageBackfillNotFoundException)
        {
            return NotFound(new
            {
                ok = false,
                error = "not-found"
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply backfill for project {ProjectId}.", input.ProjectId);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                ok = false,
                error = "server-error"
            });
        }
    }

    private static UnprocessableEntityObjectResult ValidationFailure(IEnumerable<string> details)
    {
        var clean = details?
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .ToArray() ?? Array.Empty<string>();

        return new UnprocessableEntityObjectResult(new
        {
            ok = false,
            error = "validation",
            details = clean
        });
    }
}
