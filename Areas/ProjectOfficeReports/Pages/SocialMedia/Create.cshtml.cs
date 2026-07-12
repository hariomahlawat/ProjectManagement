using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.SocialMedia.ViewModels;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.SocialMedia;

[Authorize(Policy = ProjectOfficeReportsPolicies.ManageSocialMediaEvents)]
public sealed class CreateModel : PageModel
{
    private readonly SocialMediaEventService _eventService;
    private readonly SocialMediaPlatformService _platformService;
    private readonly ISocialMediaEventPhotoService _photoService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SocialMediaPhotoOptions _photoOptions;
    private readonly ILogger<CreateModel> _logger;

    public CreateModel(
        SocialMediaEventService eventService,
        SocialMediaPlatformService platformService,
        ISocialMediaEventPhotoService photoService,
        UserManager<ApplicationUser> userManager,
        IOptions<SocialMediaPhotoOptions> photoOptions,
        ILogger<CreateModel> logger)
    {
        _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
        _platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
        _photoService = photoService ?? throw new ArgumentNullException(nameof(photoService));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _photoOptions = photoOptions?.Value ?? throw new ArgumentNullException(nameof(photoOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

    public long SocialPhotoMaxBytes => _photoOptions.MaxFileSizeBytes;

    public int SocialPhotoMaxFiles => _photoOptions.MaxFilesPerUpload;

    public int SocialPhotoMaxMb => Math.Max(1, (int)Math.Floor(SocialPhotoMaxBytes / 1024d / 1024d));

    public string SocialPhotoHelpText =>
        $"JPEG, PNG, or WebP images up to {SocialPhotoMaxMb} MB each. Select up to {SocialPhotoMaxFiles} photographs.";

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await PopulateOptionsAsync(cancellationToken);
        Input.DateOfEvent ??= DateOnly.FromDateTime(DateTime.UtcNow.Date);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await PopulateOptionsAsync(cancellationToken);
        ValidateRequiredSelections();
        ValidateUploads();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var createResult = await _eventService.CreateAsync(
            Input.EventTypeId!.Value,
            Input.PlatformId!.Value,
            Input.DateOfEvent!.Value,
            Input.Title,
            Input.Description,
            userId,
            cancellationToken);

        if (createResult.Outcome != SocialMediaEventMutationOutcome.Success || createResult.Entity is null)
        {
            AddCreateError(createResult);
            return Page();
        }

        var eventId = createResult.Entity.Id;
        var uploadErrors = new List<string>();
        var successfulUploads = 0;
        var caption = string.IsNullOrWhiteSpace(UploadCaption) ? null : UploadCaption.Trim();

        foreach (var file in Uploads.Where(file => file is not null && file.Length > 0))
        {
            try
            {
                await using var stream = file.OpenReadStream();
                var uploadResult = await _photoService.UploadAsync(
                    eventId,
                    stream,
                    file.FileName,
                    file.ContentType,
                    caption,
                    userId,
                    cancellationToken);

                if (uploadResult.Outcome == SocialMediaEventPhotoUploadOutcome.Success)
                {
                    successfulUploads++;
                    continue;
                }

                uploadErrors.AddRange(uploadResult.Errors.Count > 0
                    ? uploadResult.Errors
                    : new[] { $"{file.FileName}: the photograph could not be processed." });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload social media photograph {FileName} for activity {EventId}.", file.FileName, eventId);
                uploadErrors.Add($"{file.FileName}: the photograph could not be processed.");
            }
        }

        if (uploadErrors.Count > 0)
        {
            TempData["ToastError"] = successfulUploads > 0
                ? $"Activity created and {successfulUploads} photograph(s) added. Some photographs could not be uploaded."
                : "Activity created, but the photographs could not be uploaded. Please try again below.";
            return RedirectToPage("Edit", new { id = eventId });
        }

        TempData["ToastMessage"] = successfulUploads switch
        {
            0 => "Social media activity created.",
            1 => "Social media activity created with 1 photograph.",
            _ => $"Social media activity created with {successfulUploads} photographs."
        };

        return RedirectToPage("Details", new { id = eventId });
    }

    private void ValidateRequiredSelections()
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
    }

    private void ValidateUploads()
    {
        var uploads = Uploads?.Where(file => file is not null).ToList() ?? new List<IFormFile>();
        if (uploads.Count > SocialPhotoMaxFiles)
        {
            ModelState.AddModelError(nameof(Uploads), $"You can add up to {SocialPhotoMaxFiles} photographs at a time.");
        }

        foreach (var file in uploads)
        {
            if (file.Length == 0)
            {
                ModelState.AddModelError(nameof(Uploads), $"{file.FileName}: the selected file is empty.");
            }
            else if (file.Length > SocialPhotoMaxBytes)
            {
                ModelState.AddModelError(nameof(Uploads), $"{file.FileName}: the file exceeds {SocialPhotoMaxMb} MB.");
            }
        }
    }

    private void AddCreateError(SocialMediaEventMutationResult result)
    {
        if (result.Outcome is SocialMediaEventMutationOutcome.EventTypeInactive or SocialMediaEventMutationOutcome.EventTypeNotFound)
        {
            ModelState.AddModelError(nameof(Input.EventTypeId), "Please choose an active event type.");
        }
        else if (result.Outcome is SocialMediaEventMutationOutcome.PlatformInactive or SocialMediaEventMutationOutcome.PlatformNotFound)
        {
            ModelState.AddModelError(nameof(Input.PlatformId), "Please choose an active platform.");
        }
        else if (result.Errors.Count > 0)
        {
            ModelState.AddModelError(string.Empty, result.Errors[0]);
        }
        else
        {
            ModelState.AddModelError(string.Empty, "Unable to create the activity.");
        }
    }

    private async Task PopulateOptionsAsync(CancellationToken cancellationToken)
    {
        await PopulateEventTypesAsync(cancellationToken);
        await PopulatePlatformsAsync(cancellationToken);
    }

    private async Task PopulateEventTypesAsync(CancellationToken cancellationToken)
    {
        var eventTypes = await _eventService.GetEventTypesAsync(includeInactive: false, cancellationToken);
        var options = new List<SelectListItem> { new("Select event type", string.Empty) };

        foreach (var type in eventTypes)
        {
            options.Add(new SelectListItem(type.Name, type.Id.ToString())
            {
                Selected = Input.EventTypeId == type.Id
            });
        }

        EventTypeOptions = options;
    }

    private async Task PopulatePlatformsAsync(CancellationToken cancellationToken)
    {
        var platforms = await _platformService.GetAllAsync(includeInactive: false, cancellationToken);
        var options = new List<SelectListItem> { new("Select platform", string.Empty) };

        foreach (var platform in platforms)
        {
            options.Add(new SelectListItem(platform.Name, platform.Id.ToString())
            {
                Selected = Input.PlatformId == platform.Id
            });
        }

        PlatformOptions = options;
    }
}
