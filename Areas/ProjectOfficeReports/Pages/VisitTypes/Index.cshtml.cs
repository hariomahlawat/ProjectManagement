using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.VisitTypes;

[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private readonly VisitTypeService _service;
    private readonly UserManager<ApplicationUser> _userManager;
    public IndexModel(VisitTypeService service, UserManager<ApplicationUser> userManager)
    {
        _service = service;
        _userManager = userManager;
    }

    public IReadOnlyList<VisitTypeSummary> Items { get; private set; } = Array.Empty<VisitTypeSummary>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Items = await _service.GetSummariesAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id, bool enable, string rowVersion, CancellationToken cancellationToken)
    {
        var userId = _userManager.GetUserId(User) ?? string.Empty;
        var bytes = DecodeRowVersion(rowVersion);
        if (bytes == null)
        {
            TempData["ToastError"] = "Unable to process the request. Please try again.";
            return RedirectToPage();
        }

        var existing = await _service.FindAsync(id, cancellationToken);
        if (existing == null)
        {
            TempData["ToastError"] = "Visit type not found.";
            return RedirectToPage();
        }

        var result = await _service.UpdateAsync(id, existing.Name, existing.Description, enable, bytes, userId, cancellationToken);
        if (result.Outcome == VisitTypeMutationOutcome.Success)
        {
            TempData["ToastMessage"] = enable ? "Visit type enabled." : "Visit type disabled.";
        }
        else if (result.Outcome == VisitTypeMutationOutcome.ConcurrencyConflict)
        {
            TempData["ToastError"] = "The visit type was updated by someone else. Please reload the page.";
        }
        else if (result.Outcome == VisitTypeMutationOutcome.DuplicateName)
        {
            TempData["ToastError"] = "Another visit type already has this name.";
        }
        else
        {
            TempData["ToastError"] = "Unable to update the visit type.";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, string rowVersion, CancellationToken cancellationToken)
    {
        var userId = _userManager.GetUserId(User) ?? string.Empty;
        var bytes = DecodeRowVersion(rowVersion);
        if (bytes == null)
        {
            TempData["ToastError"] = "Unable to process the request. Please try again.";
            return RedirectToPage();
        }

        var result = await _service.DeleteAsync(id, bytes, userId, cancellationToken);
        switch (result.Outcome)
        {
            case VisitTypeDeletionOutcome.Success:
                TempData["ToastMessage"] = "Visit type deleted.";
                break;
            case VisitTypeDeletionOutcome.InUse:
                TempData["ToastError"] = "This visit type cannot be deleted because visits are using it.";
                break;
            case VisitTypeDeletionOutcome.ConcurrencyConflict:
                TempData["ToastError"] = "The visit type was changed by someone else. Please reload the page.";
                break;
            default:
                TempData["ToastError"] = "Visit type not found.";
                break;
        }

        return RedirectToPage();
    }

    private static byte[]? DecodeRowVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
