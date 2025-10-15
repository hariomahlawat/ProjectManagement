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

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Admin.SocialMediaTypes.Platforms;

[Authorize(Roles = "Admin,HoD")]
public class IndexModel : PageModel
{
    private readonly SocialMediaPlatformService _service;
    private readonly UserManager<ApplicationUser> _userManager;

    public IndexModel(SocialMediaPlatformService service, UserManager<ApplicationUser> userManager)
    {
        _service = service;
        _userManager = userManager;
    }

    public IReadOnlyList<SocialMediaPlatformSummary> Platforms { get; private set; } = Array.Empty<SocialMediaPlatformSummary>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Platforms = await _service.GetSummariesAsync(cancellationToken);
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

        var result = await _service.ToggleAsync(id, enable, bytes, userId, cancellationToken);
        switch (result.Outcome)
        {
            case SocialMediaPlatformMutationOutcome.Success:
                TempData["ToastMessage"] = enable ? "Social media platform enabled." : "Social media platform disabled.";
                break;
            case SocialMediaPlatformMutationOutcome.ConcurrencyConflict:
                TempData["ToastError"] = "The social media platform was updated by someone else. Please reload the page.";
                break;
            case SocialMediaPlatformMutationOutcome.NotFound:
                TempData["ToastError"] = "Social media platform not found.";
                break;
            default:
                TempData["ToastError"] = "Unable to update the social media platform.";
                break;
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
            case SocialMediaPlatformDeletionOutcome.Success:
                TempData["ToastMessage"] = "Social media platform deleted.";
                break;
            case SocialMediaPlatformDeletionOutcome.ConcurrencyConflict:
                TempData["ToastError"] = "The social media platform was changed by someone else. Please reload the page.";
                break;
            default:
                TempData["ToastError"] = "Social media platform not found.";
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
