using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Npgsql;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Pages.Projects.Timeline;
using ProjectManagement.Services.Stages;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Pages.Projects.Stages;

[Authorize(Roles = "Admin,HoD,Project Officer")]
[AutoValidateAntiforgeryToken]
public class BackfillApplyModel : PageModel
{
    private readonly StageBackfillService _backfillService;
    private readonly ActualCorrectionProposalService? _proposals;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<BackfillApplyModel> _logger;

    public BackfillApplyModel(
        StageBackfillService backfillService,
        ApplicationDbContext db,
        ILogger<BackfillApplyModel> logger,
        ActualCorrectionProposalService? proposals = null)
    {
        _backfillService = backfillService ?? throw new ArgumentNullException(nameof(backfillService));
        _proposals = proposals;
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public class BackfillStageInput
    {
        [Required]
        [StringLength(32)]
        public string StageCode { get; set; } = string.Empty;

        public DateOnly? ActualStart { get; set; }

        public DateOnly? CompletedOn { get; set; }

        // Stored row token when the form was rendered (stale protection).
        [StringLength(64)]
        public string? StageToken { get; set; }
    }

    public class BackfillApplyInput
    {
        [Required]
        public int ProjectId { get; set; }

        [MinLength(1)]
        public List<BackfillStageInput> Stages { get; set; } = new();

        // Absent on forms rendered before stale protection.
        public int? BaselineVersion { get; set; }

        // Phase D-3c (ActualsSubmissionMode); absent on forms rendered before D-3c.
        public string? SubmissionMode { get; set; }

        // The pending proposal the form was rendered with; both empty for "none".
        public int? BasedOnRevisionId { get; set; }

        public DateTimeOffset? BasedOnSubmittedOn { get; set; }
    }

    public async Task<IActionResult> OnPostAsync([FromBody] BackfillApplyInput input, CancellationToken ct)
    {
        _logger.LogInformation(
            "BackfillApply POST ProjectId={ProjectId}",
            input?.ProjectId);

        if (input is null)
        {
            return ValidationFailure(new[] { "A request body is required." });
        }

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

        var principal = User;
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.Identity?.Name
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Forbid();
        }

        var isAdminOrHod = principal.IsInRole(RoleNames.Admin) || principal.IsInRole(RoleNames.HoD);
        var postedProposal = string.Equals(input.SubmissionMode, ActualsSubmissionMode.Proposal, StringComparison.Ordinal);
        if (isAdminOrHod && postedProposal)
        {
            // A form rendered for a proposal is never published directly.
            return AccessChanged();
        }

        if (!isAdminOrHod)
        {
            if (!principal.IsInRole(RoleNames.ProjectOfficer))
            {
                return Forbid();
            }

            var project = await _db.Projects
                .AsNoTracking()
                .Where(item => item.Id == input.ProjectId)
                .Select(item => new { item.IsDeleted, item.LeadPoUserId })
                .SingleOrDefaultAsync(ct);

            // A missing or deleted project is NotFound for every actor (D-1b contract).
            if (project is null || project.IsDeleted)
            {
                return NotFound(new
                {
                    ok = false,
                    error = "not-found"
                });
            }

            if (!string.Equals(project.LeadPoUserId, userId, StringComparison.OrdinalIgnoreCase))
            {
                return Forbid();
            }

            // Only Admin and HoD publish backfill dates directly. The assigned PO-only user
            // submits a proposal (D-3c); a form rendered for direct publication is refused.
            return postedProposal ? await SubmitProposalAsync(input, principal, userId, ct) : AccessChanged();
        }

        try
        {
            var updates = input.Stages
                .Where(stage => !string.IsNullOrWhiteSpace(stage.StageCode))
                .Select(stage => new StageBackfillUpdate(stage.StageCode, stage.ActualStart, stage.CompletedOn, stage.StageToken))
                .ToArray();

            if (updates.Length == 0)
            {
                return ValidationFailure(new[] { "At least one stage update must be provided." });
            }

            var actor = ActualWriteActor.FromPrincipal(principal, userId);
            var result = await _backfillService.ApplyAsync(input.ProjectId, updates, actor, ct, input.BaselineVersion);

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
        catch (StageBackfillStaleException ex)
        {
            return StatusCode(StatusCodes.Status409Conflict, new
            {
                ok = false,
                error = "stale",
                stages = ex.StaleStages,
                message = $"These stages changed since you opened the form: {string.Join(", ", ex.StaleStages)}. Nothing was saved. Refresh the page and review the current dates."
            });
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
        catch (ActualsApprovalRequiredException)
        {
            // Roles changed between the page check and the transaction; nothing was published.
            return AccessChanged();
        }
        catch (ActualsWriteForbiddenException)
        {
            _logger.LogWarning("User {UserId} lost write authority over project {ProjectId} before backfill was saved.", userId, input.ProjectId);
            return Forbid();
        }
        catch (ActualsProjectUnavailableException)
        {
            return NotFound(new
            {
                ok = false,
                error = "not-found"
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
        catch (DbUpdateException ex)
        {
            var postgres = FindPostgresException(ex);
            _logger.LogError(
                ex,
                "Database rejected stage backfill for project {ProjectId}. SqlState={SqlState}; Constraint={Constraint}; Table={Table}.",
                input.ProjectId,
                postgres?.SqlState,
                postgres?.ConstraintName,
                postgres?.TableName);

            if (string.Equals(
                    postgres?.ConstraintName,
                    "CK_ProjectStages_CompletedHasDate",
                    StringComparison.Ordinal))
            {
                return StatusCode(StatusCodes.Status409Conflict, new
                {
                    ok = false,
                    error = "database-rule-conflict",
                    message = "The stage dates could not be saved because the project-stage database upgrade did not complete. Restart the application and contact the administrator if the problem persists."
                });
            }

            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                ok = false,
                error = "database-error",
                message = "The stage dates could not be saved. No changes were committed."
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

    // SECTION: Proposal submission (assigned PO-only, D-3c)
    private async Task<IActionResult> SubmitProposalAsync(
        BackfillApplyInput input,
        ClaimsPrincipal principal,
        string userId,
        CancellationToken ct)
    {
        if (_proposals is null)
        {
            return Forbid();
        }

        var submission = new ActualCorrectionSubmission(
            input.ProjectId,
            ActualChangeEntryPoint.Backfill,
            input.Stages
                .Select(stage => new ActualCorrectionProposalRow(
                    stage.StageCode, stage.ActualStart, CompletedOn: stage.CompletedOn, StageToken: stage.StageToken))
                .ToArray(),
            new ActualCorrectionBaseline(input.BaselineVersion, input.BasedOnRevisionId, input.BasedOnSubmittedOn));

        ActualCorrectionSubmissionResult result;
        try
        {
            result = await _proposals.SubmitAsync(submission, ActualWriteActor.FromPrincipal(principal, userId), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to submit a backfill proposal for project {ProjectId}.", input.ProjectId);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                ok = false,
                error = "server-error",
                message = "The completion details could not be submitted. Nothing was submitted."
            });
        }

        var message = ActualProposalMessages.Describe(result);
        switch (result.Outcome)
        {
            case ActualCorrectionSubmissionOutcome.Submitted:
            case ActualCorrectionSubmissionOutcome.Unchanged:
                // ok stays true for compatibility; pendingApproval says nothing was published.
                return new JsonResult(new
                {
                    ok = true,
                    pendingApproval = true,
                    outcome = result.Outcome == ActualCorrectionSubmissionOutcome.Submitted ? "submitted" : "unchanged",
                    revisionNo = result.Revision!.RevisionNo,
                    message
                });
            case ActualCorrectionSubmissionOutcome.Invalid:
                return ValidationFailure(result.Errors ?? Array.Empty<string>());
            case ActualCorrectionSubmissionOutcome.Stale:
                return StatusCode(StatusCodes.Status409Conflict, new
                {
                    ok = false,
                    error = "stale",
                    stages = result.StageCodes ?? Array.Empty<string>(),
                    message
                });
            case ActualCorrectionSubmissionOutcome.Conflict:
                return StatusCode(StatusCodes.Status409Conflict, new
                {
                    ok = false,
                    error = "conflict",
                    stages = result.StageCodes ?? Array.Empty<string>(),
                    message
                });
            case ActualCorrectionSubmissionOutcome.ProjectNotEditable:
                return StatusCode(StatusCodes.Status409Conflict, new
                {
                    ok = false,
                    error = "project-not-editable",
                    message
                });
            case ActualCorrectionSubmissionOutcome.DirectAuthority:
                return AccessChanged();
            case ActualCorrectionSubmissionOutcome.NotFound:
                return NotFound(new
                {
                    ok = false,
                    error = "not-found"
                });
            default:
                return Forbid();
        }
    }

    // The posted mode no longer matches the user's authority: refuse and ask for a reload.
    private ObjectResult AccessChanged() => StatusCode(StatusCodes.Status409Conflict, new
    {
        ok = false,
        error = "reload-required",
        message = EditActualsModel.AccessChangedMessage
    });

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
