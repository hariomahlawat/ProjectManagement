using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.SocialMedia.ViewModels;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.SocialMedia;

[Authorize(Policy = ProjectOfficeReportsPolicies.ManageSocialMediaEvents)]
public sealed class EditModel : PageModel
{
    private readonly SocialMediaEventService _eventService;
    private readonly SocialMediaPlatformService _platformService;
    private readonly ISocialMediaEventPhotoService _photoService;
    private readonly UserManager<ApplicationUser> _userManager;

    public EditModel(
        SocialMediaEventService eventService,
        SocialMediaPlatformService platformService,
        ISocialMediaEventPhotoService photoService,
        UserManager<ApplicationUser> userManager)
    {
        _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
        _platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
        _photoService = photoService ?? throw new ArgumentNullException(nameof(photoService));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
    }

    [BindProperty]
    public SocialMediaEventEditModel Input { get; set; } = new();

    [BindProperty]
    [StringLength(512)]
    public string? UploadCaption { get; set; }

    [BindProperty]
    public List<IFormFile> Uploads { get; set; } = new();

    public IReadOnlyList<SelectListItem> EventTypeOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> PlatformOptions { get; private set; } = Array.Empty<SelectListItem>();

    public SocialMediaEventPhotoGalleryModel PhotoGallery { get; private set; } = new(Guid.Empty, Array.Empty<SocialMediaEventPhotoItem>(), true);

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await EnsureEventExistsAsync(id, cancellationToken))
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync(id, cancellationToken);
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

            await LoadAsync(id, cancellationToken);
            return Page();
        }

        if (Input.RowVersion is null || Input.RowVersion.Length == 0)
        {
            ModelState.AddModelError(string.Empty, "We could not verify your request. Please reload and try again.");
            await LoadAsync(id, cancellationToken);
            return Page();
        }

        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var result = await _eventService.UpdateAsync(
            id,
            Input.EventTypeId.Value,
            Input.PlatformId.Value,
            Input.DateOfEvent.Value,
            Input.Title,
            Input.Description,
            Input.RowVersion,
            userId,
            cancellationToken);

        if (result.Outcome == SocialMediaEventMutationOutcome.Success)
        {
            TempData["ToastMessage"] = "Social media event updated.";
            return RedirectToPage(new { id });
        }

        if (result.Outcome == SocialMediaEventMutationOutcome.EventTypeInactive || result.Outcome == SocialMediaEventMutationOutcome.EventTypeNotFound)
        {
            ModelState.AddModelError(nameof(Input.EventTypeId), "Please choose an active event type.");
        }
        else if (result.Outcome == SocialMediaEventMutationOutcome.PlatformInactive || result.Outcome == SocialMediaEventMutationOutcome.PlatformNotFound)
        {
            ModelState.AddModelError(nameof(Input.PlatformId), "Please choose an active platform.");
        }
        else if (result.Outcome == SocialMediaEventMutationOutcome.ConcurrencyConflict)
        {
            ModelState.AddModelError(string.Empty, "Another user updated this event. Please reload and try again.");
        }
        else if (result.Errors.Count > 0)
        {
            ModelState.AddModelError(string.Empty, result.Errors[0]);
        }
        else
        {
            ModelState.AddModelError(string.Empty, "Unable to update the event.");
        }

        await LoadAsync(id, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostUploadAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await EnsureEventExistsAsync(id, cancellationToken))
        {
            TempData["ToastError"] = "Event not found.";
            return RedirectToPage("Index");
        }

        var uploads = Uploads?.Where(file => file != null).ToList() ?? new List<IFormFile>();
        if (uploads.Count == 0)
        {
            ModelState.AddModelError(nameof(Uploads), "Please select at least one photo to upload.");
            await LoadAsync(id, cancellationToken);
            return Page();
        }

        if (uploads.Any(file => file.Length == 0))
        {
            ModelState.AddModelError(nameof(Uploads), "One or more selected photos were empty. Please choose valid images.");
            await LoadAsync(id, cancellationToken);
            return Page();
        }

        var caption = string.IsNullOrWhiteSpace(UploadCaption) ? null : UploadCaption!.Trim();
        var userId = _userManager.GetUserId(User) ?? string.Empty;
        var successfulUploads = 0;
        var errors = new List<string>();

        foreach (var file in uploads)
        {
            await using var stream = file.OpenReadStream();
            var result = await _photoService.UploadAsync(id, stream, file.FileName, file.ContentType, caption, userId, cancellationToken);
            switch (result.Outcome)
            {
                case SocialMediaEventPhotoUploadOutcome.Success:
                    successfulUploads++;
                    break;
                case SocialMediaEventPhotoUploadOutcome.NotFound:
                    TempData["ToastError"] = "Event not found.";
                    return RedirectToPage("Index");
                default:
                    if (result.Errors.Count > 0)
                    {
                        errors.AddRange(result.Errors);
                    }
                    else
                    {
                        errors.Add("Unable to upload one of the selected photos. Please try again.");
                    }
                    break;
            }
        }

        if (successfulUploads > 0)
        {
            var suffix = successfulUploads == 1 ? "photo" : "photos";
            TempData["ToastMessage"] = $"{successfulUploads} {suffix} uploaded.";
        }

        foreach (var error in errors.Distinct())
        {
            ModelState.AddModelError(string.Empty, error);
        }

        await LoadAsync(id, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostDeletePhotoAsync(Guid id, Guid photoId, string rowVersion, CancellationToken cancellationToken)
    {
        if (!await EnsureEventExistsAsync(id, cancellationToken))
        {
            TempData["ToastError"] = "Event not found.";
            return RedirectToPage("Index");
        }

        var rowVersionBytes = DecodeRowVersion(rowVersion);
        if (rowVersionBytes is null)
        {
            TempData["ToastError"] = "We could not verify your request. Please reload and try again.";
            return RedirectToPage(new { id });
        }

        var userId = _userManager.GetUserId(User) ?? string.Empty;
        var result = await _photoService.RemoveAsync(id, photoId, rowVersionBytes, userId, cancellationToken);
        switch (result.Outcome)
        {
            case SocialMediaEventPhotoDeletionOutcome.Success:
                TempData["ToastMessage"] = "Photo deleted.";
                break;
            case SocialMediaEventPhotoDeletionOutcome.ConcurrencyConflict:
                TempData["ToastError"] = result.Errors.FirstOrDefault() ?? "The photo was modified. Please refresh.";
                break;
            default:
                TempData["ToastError"] = result.Errors.FirstOrDefault() ?? "Photo not found.";
                break;
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostSetCoverAsync(Guid id, Guid photoId, string rowVersion, CancellationToken cancellationToken)
    {
        if (!await EnsureEventExistsAsync(id, cancellationToken))
        {
            TempData["ToastError"] = "Event not found.";
            return RedirectToPage("Index");
        }

        var rowVersionBytes = DecodeRowVersion(rowVersion);
        if (rowVersionBytes is null)
        {
            TempData["ToastError"] = "We could not verify your request. Please reload and try again.";
            return RedirectToPage(new { id });
        }

        var userId = _userManager.GetUserId(User) ?? string.Empty;
        var result = await _photoService.SetCoverAsync(id, photoId, rowVersionBytes, userId, cancellationToken);
        switch (result.Outcome)
        {
            case SocialMediaEventPhotoSetCoverOutcome.Success:
                TempData["ToastMessage"] = "Cover photo updated.";
                break;
            case SocialMediaEventPhotoSetCoverOutcome.ConcurrencyConflict:
                TempData["ToastError"] = result.Errors.FirstOrDefault() ?? "The photo was modified. Please refresh.";
                break;
            case SocialMediaEventPhotoSetCoverOutcome.NotFound:
                TempData["ToastError"] = "Photo not found.";
                break;
            default:
                TempData["ToastError"] = result.Errors.FirstOrDefault() ?? "Unable to update the cover photo.";
                break;
        }

        return RedirectToPage(new { id });
    }

    private async Task<bool> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var details = await _eventService.GetDetailsAsync(id, cancellationToken);
        if (details is null)
        {
            return false;
        }

        Input = new SocialMediaEventEditModel
        {
            Id = details.Event.Id,
            EventTypeId = details.Event.SocialMediaEventTypeId,
            DateOfEvent = details.Event.DateOfEvent,
            Title = details.Event.Title,
            PlatformId = details.Event.SocialMediaPlatformId,
            Description = details.Event.Description,
            RowVersion = details.Event.RowVersion
        };

        var eventTypes = await _eventService.GetEventTypesAsync(includeInactive: true, cancellationToken);
        var options = new List<SelectListItem>();
        foreach (var type in eventTypes)
        {
            options.Add(new SelectListItem(type.Name, type.Id.ToString())
            {
                Selected = type.Id == details.Event.SocialMediaEventTypeId,
                Disabled = !type.IsActive && type.Id != details.Event.SocialMediaEventTypeId
            });
        }

        EventTypeOptions = options;

        var platforms = await _platformService.GetAllAsync(includeInactive: true, cancellationToken);
        var platformOptions = new List<SelectListItem>();
        foreach (var platform in platforms)
        {
            platformOptions.Add(new SelectListItem(platform.Name, platform.Id.ToString())
            {
                Selected = platform.Id == details.Event.SocialMediaPlatformId,
                Disabled = !platform.IsActive && platform.Id != details.Event.SocialMediaPlatformId
            });
        }

        PlatformOptions = platformOptions;

        var photos = details.Photos
            .OrderBy(x => x.CreatedAtUtc)
            .Select(photo => new SocialMediaEventPhotoItem(
                photo.Id,
                photo.Caption,
                photo.VersionStamp,
                photo.IsCover,
                Convert.ToBase64String(photo.RowVersion)))
            .ToList();

        PhotoGallery = new SocialMediaEventPhotoGalleryModel(details.Event.Id, photos, true);
        UploadCaption = null;
        Uploads = new List<IFormFile>();
        return true;
    }

    private async Task<bool> EnsureEventExistsAsync(Guid id, CancellationToken cancellationToken)
    {
        var details = await _eventService.GetDetailsAsync(id, cancellationToken);
        return details != null;
    }

    private static byte[]? DecodeRowVersion(string? value)
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
