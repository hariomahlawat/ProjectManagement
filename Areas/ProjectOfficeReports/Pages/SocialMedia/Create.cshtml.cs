using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.SocialMedia.ViewModels;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.SocialMedia;

[Authorize(Policy = ProjectOfficeReportsPolicies.ManageSocialMediaEvents)]
public sealed class CreateModel : PageModel
{
    private readonly SocialMediaEventService _eventService;
    private readonly SocialMediaPlatformService _platformService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateModel(
        SocialMediaEventService eventService,
        SocialMediaPlatformService platformService,
        UserManager<ApplicationUser> userManager)
    {
        _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
        _platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
    }

    [BindProperty]
    public SocialMediaEventEditModel Input { get; set; } = new();

    public IReadOnlyList<SelectListItem> EventTypeOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> PlatformOptions { get; private set; } = Array.Empty<SelectListItem>();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await PopulateEventTypesAsync(cancellationToken);
        await PopulatePlatformsAsync(cancellationToken);
        if (Input.DateOfEvent is null)
        {
            Input.DateOfEvent = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await PopulateEventTypesAsync(cancellationToken);
        await PopulatePlatformsAsync(cancellationToken);
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (Input.EventTypeId is null || Input.DateOfEvent is null || Input.PlatformId is null)
        {
            if (Input.EventTypeId is null)
            {
                ModelState.AddModelError(nameof(Input.EventTypeId), "Please select an event type.");
            }

            if (Input.DateOfEvent is null)
            {
                ModelState.AddModelError(nameof(Input.DateOfEvent), "Please select a date.");
            }

            if (Input.PlatformId is null)
            {
                ModelState.AddModelError(nameof(Input.PlatformId), "Please select a platform.");
            }

            return Page();
        }

        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var result = await _eventService.CreateAsync(
            Input.EventTypeId.Value,
            Input.PlatformId.Value,
            Input.DateOfEvent.Value,
            Input.Title,
            Input.Description,
            userId,
            cancellationToken);

        if (result.Outcome == SocialMediaEventMutationOutcome.Success && result.Entity is not null)
        {
            TempData["ToastMessage"] = "Social media event created.";
            return RedirectToPage("Edit", new { id = result.Entity.Id });
        }

        if (result.Outcome == SocialMediaEventMutationOutcome.EventTypeInactive || result.Outcome == SocialMediaEventMutationOutcome.EventTypeNotFound)
        {
            ModelState.AddModelError(nameof(Input.EventTypeId), "Please choose an active event type.");
        }
        else if (result.Outcome == SocialMediaEventMutationOutcome.PlatformInactive || result.Outcome == SocialMediaEventMutationOutcome.PlatformNotFound)
        {
            ModelState.AddModelError(nameof(Input.PlatformId), "Please choose an active platform.");
        }
        else if (result.Errors.Count > 0)
        {
            ModelState.AddModelError(string.Empty, result.Errors[0]);
        }
        else
        {
            ModelState.AddModelError(string.Empty, "Unable to create the event.");
        }

        return Page();
    }

    private async Task PopulateEventTypesAsync(CancellationToken cancellationToken)
    {
        var eventTypes = await _eventService.GetEventTypesAsync(includeInactive: false, cancellationToken);
        var options = new List<SelectListItem>
        {
            new("Select event type", string.Empty)
        };

        foreach (var type in eventTypes)
        {
            options.Add(new SelectListItem(type.Name, type.Id.ToString())
            {
                Selected = Input.EventTypeId.HasValue && Input.EventTypeId.Value == type.Id
            });
        }

        EventTypeOptions = options;
    }

    private async Task PopulatePlatformsAsync(CancellationToken cancellationToken)
    {
        var platforms = await _platformService.GetAllAsync(includeInactive: false, cancellationToken);
        var options = new List<SelectListItem>
        {
            new("Select platform", string.Empty)
        };

        foreach (var platform in platforms)
        {
            options.Add(new SelectListItem(platform.Name, platform.Id.ToString())
            {
                Selected = Input.PlatformId.HasValue && Input.PlatformId.Value == platform.Id
            });
        }

        PlatformOptions = options;
    }
}
