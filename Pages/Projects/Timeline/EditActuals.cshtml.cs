using System;
using System.Threading;
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
    private readonly StageActualsUpdateService _actualsUpdateService;
    private readonly ApplicationDbContext _db;
    private readonly IUserContext _userContext;
    private readonly ILogger<EditActualsModel> _logger;

    public EditActualsModel(
        StageActualsUpdateService actualsUpdateService,
        ApplicationDbContext db,
        IUserContext userContext,
        ILogger<EditActualsModel> logger)
    {
        _actualsUpdateService = actualsUpdateService;
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
        var userName = _userContext.UserName;
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
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        var isProjectsHod = isHoD && string.Equals(project.HodUserId, userId, StringComparison.Ordinal);
        var isProjectsPo = string.Equals(project.LeadPoUserId, userId, StringComparison.Ordinal);

        if (!isAdmin && !isProjectsHod && !isProjectsPo)
        {
            _logger.LogWarning("User {UserId} attempted to edit actuals for project {ProjectId} without permission.", userId, id);
            return Forbid();
        }

        try
        {
            var result = await _actualsUpdateService.UpdateAsync(Input, userId, userName, cancellationToken);

            if (result.UpdatedCount > 0)
            {
                TempData["Flash"] = "Actual dates updated successfully.";
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

            TempData["Error"] = message;
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
}
