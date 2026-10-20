using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Services.Stages;
using ProjectManagement.Services;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Pages.Projects.Timeline;

[Authorize(Roles = "Admin,Project Officer,HoD")]
[AutoValidateAntiforgeryToken]
public class EditActualsModel : PageModel
{
    internal const string AccessChangedMessage =
        "Your access to this project changed since you opened the editor. Nothing was saved or submitted. Reload the page to continue.";

    private readonly StageActualsUpdateService _actualsUpdateService;
    private readonly ActualCorrectionProposalService? _proposals;
    private readonly ApplicationDbContext _db;
    private readonly IUserContext _userContext;
    private readonly ILogger<EditActualsModel> _logger;

    public EditActualsModel(
        StageActualsUpdateService actualsUpdateService,
        ApplicationDbContext db,
        IUserContext userContext,
        ILogger<EditActualsModel> logger,
        ActualCorrectionProposalService? proposals = null)
    {
        _actualsUpdateService = actualsUpdateService;
        _proposals = proposals;
        _db = db;
        _userContext = userContext;
        _logger = logger;
    }

    [BindProperty]
    public ActualsEditInput Input { get; set; } = new();

    public IActionResult OnGet(int id) => NotFound();

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        if (id != Input.ProjectId)
        {
            TempData["Error"] = "Unable to process the request. Please reload and try again.";
            TempData["OpenOffcanvas"] = "actuals-edit";
            return RedirectToPage("/Projects/Overview", new { id });
        }

        var userId = _userContext.UserId;
        var userName = _userContext.User.Identity?.Name;
        var principal = _userContext.User;

        if (string.IsNullOrWhiteSpace(userId) || principal is null)
        {
            return Forbid();
        }

        var isAdmin = principal.IsInRole("Admin");
        var isHoD = principal.IsInRole("HoD");
        var isPo = principal.IsInRole("Project Officer");

        if (!isAdmin && !isHoD && !isPo)
        {
            return Forbid();
        }

        var project = await _db.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        var isProjectsHod = isHoD;
        var isProjectsPo = string.Equals(project.LeadPoUserId, userId, StringComparison.OrdinalIgnoreCase);

        if (!isAdmin && !isProjectsHod && !isProjectsPo)
        {
            _logger.LogWarning("User {UserId} attempted to edit actuals for project {ProjectId} without permission.", userId, id);
            return Forbid();
        }

        // Only Admin and HoD publish Actual dates directly; that authority supersedes the
        // PO role. The assigned PO-only user submits a proposal instead (D-3c). The posted
        // mode records what the form was rendered for: a mismatch with the current
        // authority is refused, so a proposal form never becomes a direct publication
        // (or the reverse) because roles changed after rendering.
        var isDirect = isAdmin || isProjectsHod;
        var postedProposal = string.Equals(Input.SubmissionMode, ActualsSubmissionMode.Proposal, StringComparison.Ordinal);
        if (isDirect == postedProposal)
        {
            return AccessChanged(id);
        }

        if (!isDirect)
        {
            // A malformed baseline (for example an unparseable epoch) is never read as "none".
            return ModelState.IsValid
                ? await SubmitProposalAsync(id, userId, principal, cancellationToken)
                : Fail(id, ActualProposalMessages.OutOfDate);
        }

        try
        {
            var actor = ActualWriteActor.FromPrincipal(principal, userId);
            var result = await _actualsUpdateService.UpdateAsync(Input, actor, userName, cancellationToken);

            if (result.UpdatedCount > 0)
            {
                // Pre-existing chronology issues in untouched records do not block a
                // save, but the user should see them.
                TempData["Flash"] = result.Warnings is { Count: > 0 } warnings
                    ? "Actual dates updated successfully. Note: " + string.Join(" ", warnings)
                    : "Actual dates updated successfully.";
            }
            else
            {
                TempData["Flash"] = "No changes detected.";
            }

            return RedirectToPage("/Projects/Overview", new { id });
        }
        catch (StageActualsValidationException vex)
        {
            var message = vex.Errors?.Count > 0
                ? string.Join(" ", vex.Errors)
                : vex.Message;

            KeepEntries(userId, vex.Errors?.Count > 0 ? vex.Errors : new[] { vex.Message });
            TempData["Error"] = message;
            TempData["OpenOffcanvas"] = "actuals-edit";
            return RedirectToPage("/Projects/Overview", new { id });
        }
        catch (ActualsApprovalRequiredException)
        {
            // Roles changed between the page check and the transaction; nothing was published.
            return AccessChanged(id);
        }
        catch (ActualsWriteForbiddenException)
        {
            _logger.LogWarning("User {UserId} lost write authority over project {ProjectId} before actual dates were saved.", userId, id);
            return Forbid();
        }
        catch (ActualsProjectUnavailableException)
        {
            return NotFound();
        }
        catch (StageActualsStaleException sex)
        {
            TempData["Error"] =
                $"These stages changed since you opened the editor: {string.Join(", ", sex.StageCodes)}. Nothing was saved. Reload and review the current dates.";
            TempData["OpenOffcanvas"] = "actuals-edit";
            return RedirectToPage("/Projects/Overview", new { id });
        }
        catch (StageActualsConflictException cex)
        {
            var blocked = cex.StageCodes is { Count: > 0 }
                ? $"The following stages have pending decisions: {string.Join(", ", cex.StageCodes)}."
                : "One or more stages cannot be edited while pending approval.";

            TempData["Error"] = blocked;
            TempData["OpenOffcanvas"] = "actuals-edit";
            return RedirectToPage("/Projects/Overview", new { id });
        }
        catch (StageActualsNotFoundException nfex)
        {
            var missing = nfex.MissingStageCodes is { Count: > 0 }
                ? $"Missing stage codes: {string.Join(", ", nfex.MissingStageCodes)}."
                : nfex.Message;

            TempData["Error"] = missing;
            TempData["OpenOffcanvas"] = "actuals-edit";
            return RedirectToPage("/Projects/Overview", new { id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while updating actuals for project {ProjectId}.", id);
            TempData["Error"] = "Something went wrong while saving actuals. Please try again.";
            TempData["OpenOffcanvas"] = "actuals-edit";
            return RedirectToPage("/Projects/Overview", new { id });
        }
    }

    // SECTION: Withdrawal of the pending proposal (assigned PO-only)
    public async Task<IActionResult> OnPostWithdrawAsync(
        int id,
        int revisionId,
        DateTimeOffset? submittedOn,
        CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId;
        var principal = _userContext.User;
        if (string.IsNullOrWhiteSpace(userId) || principal is null || _proposals is null)
        {
            return Forbid();
        }

        if (revisionId <= 0 || submittedOn is null || !ModelState.IsValid)
        {
            return Fail(id, "This page is out of date. Reload to review the pending update. Nothing was changed.");
        }

        // Authority and the exact pending identity are re-checked inside the service transaction.
        var result = await _proposals.WithdrawAsync(
            id, revisionId, submittedOn.Value, ActualWriteActor.FromPrincipal(principal, userId), cancellationToken: cancellationToken);

        switch (result.Outcome)
        {
            case ActualCorrectionWithdrawalOutcome.Withdrawn:
                TempData["Flash"] = $"Your pending correction (revision {result.Revision!.RevisionNo}) was withdrawn. Official dates are unchanged.";
                return RedirectToPage("/Projects/Overview", new { id });
            case ActualCorrectionWithdrawalOutcome.NotActionable:
                return Fail(id, "This pending update was already withdrawn, replaced or decided. Reload to see its current state. Nothing was changed.");
            case ActualCorrectionWithdrawalOutcome.Invalid:
                return Fail(id, result.Error ?? "The withdrawal could not be processed. Nothing was changed.");
            case ActualCorrectionWithdrawalOutcome.DirectAuthority:
                return AccessChanged(id);
            case ActualCorrectionWithdrawalOutcome.NotFound:
                return NotFound();
            default:
                _logger.LogWarning("User {UserId} may not withdraw the actual correction proposal for project {ProjectId}.", userId, id);
                return Forbid();
        }
    }

    // SECTION: Proposal submission (assigned PO-only, D-3c)
    private async Task<IActionResult> SubmitProposalAsync(
        int id,
        string userId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        if (_proposals is null)
        {
            return Forbid();
        }

        var submission = new ActualCorrectionSubmission(
            id,
            ActualChangeEntryPoint.Editor,
            (Input.Rows ?? new List<ActualsEditRowInput>())
                .Where(row => row is not null)
                .Select(row => new ActualCorrectionProposalRow(
                    row.StageCode, row.ActualStart, row.ClearActualStart, row.CompletedOn, row.StageToken))
                .ToArray(),
            new ActualCorrectionBaseline(Input.BaselineVersion, Input.BasedOnRevisionId, Input.BasedOnSubmittedOn),
            Input.Note);

        ActualCorrectionSubmissionResult result;
        try
        {
            result = await _proposals.SubmitAsync(submission, ActualWriteActor.FromPrincipal(principal, userId), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unexpected error while submitting an actual correction proposal for project {ProjectId}.", id);
            return Fail(id, "Something went wrong while submitting the correction. Nothing was submitted. Please try again.");
        }

        var message = ActualProposalMessages.Describe(result);
        switch (result.Outcome)
        {
            case ActualCorrectionSubmissionOutcome.Submitted:
            case ActualCorrectionSubmissionOutcome.Unchanged:
                TempData["Flash"] = message;
                return RedirectToPage("/Projects/Overview", new { id });
            case ActualCorrectionSubmissionOutcome.Forbidden:
                _logger.LogWarning("User {UserId} may not submit an actual correction proposal for project {ProjectId}.", userId, id);
                return Forbid();
            case ActualCorrectionSubmissionOutcome.NotFound:
                return NotFound();
            case ActualCorrectionSubmissionOutcome.DirectAuthority:
                return AccessChanged(id);
            case ActualCorrectionSubmissionOutcome.Invalid:
                KeepEntries(userId, result.Errors ?? Array.Empty<string>());
                return Fail(id, message);
            default:
                return Fail(id, message);
        }
    }

    // A validation refusal reopens the editor with what the user entered (C7.2).
    private void KeepEntries(string userId, IEnumerable<string> errors) =>
        TempData[ActualsEditorRecovery.TempDataKey] = ActualsEditorRecovery.Serialize(
            Input, Request.HasFormContentType ? Request.Form : null, userId, errors);

    private IActionResult AccessChanged(int id)
    {
        TempData["Error"] = AccessChangedMessage;
        return RedirectToPage("/Projects/Overview", new { id });
    }

    private IActionResult Fail(int id, string message)
    {
        TempData["Error"] = message;
        TempData["OpenOffcanvas"] = "actuals-edit";
        return RedirectToPage("/Projects/Overview", new { id });
    }
}
