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
using Npgsql;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
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

        public DateOnly? StartDate { get; set; }

        [StringLength(1024)]
        public string? Note { get; set; }

        public bool ForceBackfillPredecessors { get; set; }
    }

    public async Task<IActionResult> OnPostAsync([FromBody] ApplyChangeInput input, CancellationToken ct)
    {
        // Defence in depth: the page is HoD-only and every HoD may apply a direct
        // stage change irrespective of which HoD is assigned to the project.
        if (!User.IsInRole("HoD"))
        {
            return Forbid();
        }

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
                input.StartDate,
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
                    completedOn = result.CompletedOn?.ToString("yyyy-MM-dd"),
                    requiresBackfill = result.RequiresBackfill
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
        catch (StageDirectApplyValidationException ex)
        {
            return ValidationFailure(ex.Details, ex.MissingPredecessors);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            var reference = CreateDiagnosticReference();
            _logger.LogWarning(
                ex,
                "Concurrent direct stage update rejected. ProjectId={ProjectId}; StageCode={StageCode}; Reference={Reference}.",
                input.ProjectId,
                input.StageCode,
                reference);

            return StatusCode(StatusCodes.Status409Conflict, new
            {
                ok = false,
                error = "concurrency-conflict",
                message = "The stage changed while this form was open. Refresh the timeline and try again.",
                reference
            });
        }
        catch (DbUpdateException ex)
        {
            var reference = CreateDiagnosticReference();
            var postgres = FindPostgresException(ex);

            _logger.LogError(
                ex,
                "Database rejected direct stage update. ProjectId={ProjectId}; StageCode={StageCode}; SqlState={SqlState}; Constraint={Constraint}; Table={Table}; Column={Column}; Reference={Reference}.",
                input.ProjectId,
                input.StageCode,
                postgres?.SqlState,
                postgres?.ConstraintName,
                postgres?.TableName,
                postgres?.ColumnName,
                reference);

            if (string.Equals(
                    postgres?.ConstraintName,
                    ApplicationDatabaseSchemaValidator.ProjectStageCompletionConstraint,
                    StringComparison.Ordinal))
            {
                return StatusCode(StatusCodes.Status409Conflict, new
                {
                    ok = false,
                    error = "database-schema-conflict",
                    message = "The project-stage database rule is not aligned with this application version. Restart after applying the pending database migration, then retry.",
                    reference
                });
            }

            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                ok = false,
                error = "database-error",
                message = "The stage update could not be saved. No changes were committed.",
                reference
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var reference = CreateDiagnosticReference();
            _logger.LogError(
                ex,
                "Unexpected direct stage update failure. ProjectId={ProjectId}; StageCode={StageCode}; Reference={Reference}.",
                input.ProjectId,
                input.StageCode,
                reference);

            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                ok = false,
                error = "server-error",
                message = "The stage update could not be completed. No changes were committed.",
                reference
            });
        }
    }


    private static PostgresException? FindPostgresException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
            {
                return postgres;
            }
        }

        return null;
    }

    private static string CreateDiagnosticReference()
        => $"STG-{Guid.NewGuid():N}"[..14].ToUpperInvariant();

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
