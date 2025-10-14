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

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Admin.SocialMediaTypes;

[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private readonly SocialMediaEventTypeService _service;
    private readonly UserManager<ApplicationUser> _userManager;

    public IndexModel(SocialMediaEventTypeService service, UserManager<ApplicationUser> userManager)
    {
        _service = service;
        _userManager = userManager;
    }

    public IReadOnlyList<SocialMediaEventTypeSummary> Items { get; private set; } = Array.Empty<SocialMediaEventTypeSummary>();

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
            TempData["ToastError"] = "Social media event type not found.";
            return RedirectToPage();
        }

        var result = await _service.UpdateAsync(id, existing.Name, existing.Description, enable, bytes, userId, cancellationToken);
        if (result.Outcome == SocialMediaEventTypeMutationOutcome.Success)
        {
            TempData["ToastMessage"] = enable ? "Social media event type enabled." : "Social media event type disabled.";
        }
        else if (result.Outcome == SocialMediaEventTypeMutationOutcome.ConcurrencyConflict)
        {
            TempData["ToastError"] = "The social media event type was updated by someone else. Please reload the page.";
        }
        else if (result.Outcome == SocialMediaEventTypeMutationOutcome.DuplicateName)
        {
            TempData["ToastError"] = "Another social media event type already has this name.";
        }
        else
        {
            TempData["ToastError"] = "Unable to update the social media event type.";
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
            case SocialMediaEventTypeDeletionOutcome.Success:
                TempData["ToastMessage"] = "Social media event type deleted.";
                break;
            case SocialMediaEventTypeDeletionOutcome.InUse:
                TempData["ToastError"] = "This social media event type cannot be deleted because events are using it.";
                break;
            case SocialMediaEventTypeDeletionOutcome.ConcurrencyConflict:
                TempData["ToastError"] = "The social media event type was changed by someone else. Please reload the page.";
                break;
            default:
                TempData["ToastError"] = "Social media event type not found.";
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
