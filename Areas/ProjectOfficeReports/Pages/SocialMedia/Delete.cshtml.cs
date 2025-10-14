using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.SocialMedia;

[Authorize(Policy = ProjectOfficeReportsPolicies.ManageSocialMediaEvents)]
public sealed class DeleteModel : PageModel
{
    private readonly SocialMediaEventService _eventService;
    private readonly UserManager<ApplicationUser> _userManager;

    public DeleteModel(SocialMediaEventService eventService, UserManager<ApplicationUser> userManager)
    {
        _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
    }

    public SocialMediaEvent? Event { get; private set; }

    public SocialMediaEventType? EventType { get; private set; }

    public int PhotoCount { get; private set; }

    [BindProperty]
    public string RowVersion { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var details = await _eventService.GetDetailsAsync(id, cancellationToken);
        if (details is null)
        {
            return NotFound();
        }

        Event = details.Event;
        EventType = details.EventType;
        PhotoCount = details.Photos.Count;
        RowVersion = Convert.ToBase64String(details.Event.RowVersion);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(RowVersion))
        {
            TempData["ToastError"] = "We could not verify your request. Please reload and try again.";
            return RedirectToPage(new { id });
        }

        byte[] rowVersionBytes;
        try
        {
            rowVersionBytes = Convert.FromBase64String(RowVersion);
        }
        catch (FormatException)
        {
            TempData["ToastError"] = "We could not verify your request. Please reload and try again.";
            return RedirectToPage(new { id });
        }

        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var result = await _eventService.DeleteAsync(id, rowVersionBytes, userId, cancellationToken);
        switch (result.Outcome)
        {
            case SocialMediaEventDeletionOutcome.Success:
                TempData["ToastMessage"] = "Social media event deleted.";
                return RedirectToPage("Index");
            case SocialMediaEventDeletionOutcome.ConcurrencyConflict:
                TempData["ToastError"] = "The event was modified by someone else. Please reload and try again.";
                return RedirectToPage(new { id });
            default:
                return NotFound();
        }
    }
}
