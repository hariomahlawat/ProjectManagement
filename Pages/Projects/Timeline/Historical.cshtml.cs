using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Services;
using ProjectManagement.Services.Stages;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Pages.Projects.Timeline;

[Authorize(Roles = "Admin,HoD")]
[AutoValidateAntiforgeryToken]
public sealed class HistoricalModel : PageModel
{
    private readonly HistoricalStageRecordService _historicalStageRecords;
    private readonly IUserContext _userContext;
    private readonly ILogger<HistoricalModel> _logger;

    public HistoricalModel(
        HistoricalStageRecordService historicalStageRecords,
        IUserContext userContext,
        ILogger<HistoricalModel> logger)
    {
        _historicalStageRecords = historicalStageRecords;
        _userContext = userContext;
        _logger = logger;
    }

    [BindProperty]
    public HistoricalStageRecordInput Input { get; set; } = new();

    public IActionResult OnGet(int id) => NotFound();

    public async Task<IActionResult> OnPostAsync(
        int id,
        CancellationToken cancellationToken)
    {
        if (id != Input.ProjectId)
        {
            SetError("Unable to process the request. Reload the project and try again.");
            return RedirectToOverview(id);
        }

        var userId = _userContext.UserId;
        var userName = _userContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(value => value.Errors)
                .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                    ? "One or more historical stage values are invalid."
                    : error.ErrorMessage)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            SetError(string.Join(" ", errors));
            return RedirectToOverview(id);
        }

        try
        {
            var result = await _historicalStageRecords.SaveAsync(
                Input,
                userId,
                userName,
                cancellationToken);

            TempData["Flash"] = result.UpdatedCount == 0
                ? "No historical stage changes were detected."
                : result.UpdatedCount == 1
                    ? "Historical stage data updated for 1 stage."
                    : $"Historical stage data updated for {result.UpdatedCount} stages.";

            return RedirectToOverview(id, openEditor: false);
        }
        catch (HistoricalStageRecordValidationException exception)
        {
            SetError(string.Join(" ", exception.Errors));
            return RedirectToOverview(id);
        }
        catch (HistoricalStageRecordConflictException exception)
        {
            SetError(
                exception.StageCodes.Count == 0
                    ? "A stage has a pending decision and cannot be changed."
                    : $"Resolve the pending decision for: {string.Join(", ", exception.StageCodes)}.");
            return RedirectToOverview(id);
        }
        catch (HistoricalStageRecordNotFoundException)
        {
            return NotFound();
        }
        catch (HistoricalStageRecordNotAllowedException)
        {
            return Forbid();
        }
        catch (DbUpdateException exception)
        {
            _logger.LogError(
                exception,
                "Unable to persist historical stage data for project {ProjectId}.",
                id);
            SetError("The historical stage data could not be saved. Reload the project and try again.");
            return RedirectToOverview(id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unexpected failure while updating historical stage data for project {ProjectId}.",
                id);
            SetError("Something went wrong while saving historical stage data. Try again.");
            return RedirectToOverview(id);
        }
    }

    private void SetError(string message)
    {
        TempData["Error"] = message;
        TempData["OpenOffcanvas"] = "historical-stages";
    }

    private IActionResult RedirectToOverview(int id, bool openEditor = true)
    {
        if (openEditor)
        {
            TempData["OpenOffcanvas"] = "historical-stages";
        }

        var overviewUrl = Url.Page("/Projects/Overview", values: new { id })
            ?? $"/Projects/Overview/{id}";
        return Redirect($"{overviewUrl}#timeline");
    }
}
