using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Logging;
using ProjectManagement.Contracts.Activities;
using ProjectManagement.Models.Activities;
using ProjectManagement.Services.Activities;

namespace ProjectManagement.Pages.Activities;

[Authorize(Roles = "Admin,HoD,Project Office,TA")]
public sealed class EditModel : PageModel
{
    private static readonly IReadOnlyList<string> AttachmentExtensions = new[]
    {
        "pdf", "png", "jpg", "jpeg", "mp4", "mov", "webm"
    };

    private static readonly IReadOnlyList<string> AttachmentContentTypes = new[]
    {
        "application/pdf",
        "image/png",
        "image/jpeg",
        "video/mp4",
        "video/quicktime",
        "video/webm"
    };

    private static readonly IReadOnlyList<string> AttachmentSummaryLabels = new[]
    {
        "PDF", "PNG", "JPG/JPEG", "MP4", "MOV", "WEBM"
    };

    private readonly IActivityService _activityService;
    private readonly IActivityTypeService _activityTypeService;
    private readonly IActivityAttachmentManager _activityAttachmentManager;
    private readonly IActivityAttachmentValidator _attachmentValidator;
    private readonly ILogger<EditModel> _logger;

    public EditModel(IActivityService activityService,
                     IActivityTypeService activityTypeService,
                     IActivityAttachmentManager activityAttachmentManager,
                     IActivityAttachmentValidator attachmentValidator,
                     ILogger<EditModel> logger)
    {
        _activityService = activityService;
        _activityTypeService = activityTypeService;
        _activityAttachmentManager = activityAttachmentManager;
        _attachmentValidator = attachmentValidator;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IReadOnlyList<SelectListItem> ActivityTypeOptions { get; private set; } = Array.Empty<SelectListItem>();

    public bool IsEdit => Input?.Id is int id && id > 0;

    public int ExistingAttachmentCount { get; private set; }

    public int RemainingAttachmentSlots => Math.Max(0, ActivityAttachmentManager.MaxAttachmentsPerActivity - ExistingAttachmentCount);

    public long MaxAttachmentSizeBytes => ActivityAttachmentValidator.MaxAttachmentSizeBytes;

    public int MaxAttachmentSizeMegabytes => (int)Math.Ceiling(MaxAttachmentSizeBytes / (1024m * 1024m));

    public IReadOnlyList<string> AllowedAttachmentExtensions => AttachmentExtensions;

    public IReadOnlyList<string> AllowedAttachmentContentTypes => AttachmentContentTypes;

    public string AllowedAttachmentSummary => string.Join(", ", AttachmentSummaryLabels);

    public string AttachmentAcceptAttribute => string.Join(',', new[]
    {
        "application/pdf",
        "image/png",
        "image/jpeg",
        "video/mp4",
        "video/quicktime",
        "video/webm",
        ".pdf",
        ".png",
        ".jpg",
        ".jpeg",
        ".mp4",
        ".mov",
        ".webm"
    });

    public async Task<IActionResult> OnGetAsync(int? id, CancellationToken cancellationToken)
    {
        if (!IsManager(User))
        {
            return Forbid();
        }

        if (id.HasValue)
        {
            var activity = await _activityService.GetAsync(id.Value, cancellationToken);
            if (activity is null)
            {
                return NotFound();
            }

            ApplyActivityToInput(activity);
        }
        else
        {
            Input = new InputModel();
            ExistingAttachmentCount = 0;
        }

        await PopulateActivityTypesAsync(Input.ActivityTypeId, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!IsManager(User))
        {
            return Forbid();
        }

        Input ??= new InputModel();
        Activity? existing = null;

        if (Input.Id.HasValue)
        {
            existing = await _activityService.GetAsync(Input.Id.Value, cancellationToken);
            if (existing is null)
            {
                TempData["Error"] = "The selected activity could not be found.";
                return RedirectToPage("./Index");
            }

            ExistingAttachmentCount = existing.Attachments?.Count ?? 0;
        }
        else
        {
            ExistingAttachmentCount = 0;
        }

        ValidateUploadedFiles(existing);

        if (!ModelState.IsValid)
        {
            await PopulateActivityTypesAsync(Input.ActivityTypeId, cancellationToken);
            return Page();
        }

        var input = new ActivityInput(
            Input.Title ?? string.Empty,
            Input.Description,
            Input.Location,
            Input.ActivityTypeId!.Value,
            ConvertToUtc(Input.ScheduledStart),
            ConvertToUtc(Input.ScheduledEnd));

        Activity activity;
        var isNew = !Input.Id.HasValue;

        try
        {
            activity = isNew
                ? await _activityService.CreateAsync(input, cancellationToken)
                : await _activityService.UpdateAsync(Input.Id!.Value, input, cancellationToken);
        }
        catch (ActivityValidationException ex)
        {
            AddErrorsToModelState(ex);
            await PopulateActivityTypesAsync(Input.ActivityTypeId, cancellationToken);
            ExistingAttachmentCount = existing?.Attachments?.Count ?? 0;
            return Page();
        }
        catch (ActivityAuthorizationException)
        {
            TempData["Error"] = "You are not authorised to manage activities.";
            return RedirectToPage("./Index");
        }
        catch (KeyNotFoundException)
        {
            TempData["Error"] = "The selected activity could not be found.";
            return RedirectToPage("./Index");
        }

        try
        {
            await SaveAttachmentsAsync(activity, cancellationToken);
        }
        catch (ActivityValidationException ex)
        {
            AddErrorsToModelState(ex);
            ApplyActivityToInput(activity);
            await PopulateActivityTypesAsync(Input.ActivityTypeId, cancellationToken);
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload attachments for activity {ActivityId}", activity.Id);
            TempData["Error"] = "Activity saved, but attachments could not be uploaded.";
            return RedirectToPage("./Index");
        }

        TempData["ToastMessage"] = isNew ? "Activity created." : "Activity updated.";
        return RedirectToPage("./Index");
    }

    private async Task PopulateActivityTypesAsync(int? selectedTypeId, CancellationToken cancellationToken)
    {
        var types = await _activityTypeService.ListAsync(cancellationToken);
        var sorted = types
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var items = new List<SelectListItem>
        {
            new("Select an activity type", string.Empty, selectedTypeId is null or <= 0)
        };

        foreach (var type in sorted)
        {
            var item = new SelectListItem(type.Name, type.Id.ToString(CultureInfo.InvariantCulture), type.Id == selectedTypeId)
            {
                Disabled = !type.IsActive && type.Id != selectedTypeId
            };
            items.Add(item);
        }

        ActivityTypeOptions = items;
    }

    private void ValidateUploadedFiles(Activity? existingActivity)
    {
        if (Input.Files is null || Input.Files.Count == 0)
        {
            return;
        }

        var existingCount = existingActivity?.Attachments?.Count ?? 0;
        var remainingSlots = ActivityAttachmentManager.MaxAttachmentsPerActivity - existingCount;

        if (remainingSlots <= 0)
        {
            ModelState.AddModelError(nameof(Input.Files), "This activity already has the maximum number of attachments.");
            return;
        }

        if (Input.Files.Count > remainingSlots)
        {
            var message = remainingSlots == 1
                ? "Only one additional attachment can be uploaded."
                : $"Only {remainingSlots} additional attachments can be uploaded.";
            ModelState.AddModelError(nameof(Input.Files), message);
        }

        foreach (var file in Input.Files)
        {
            if (file is null)
            {
                continue;
            }

            if (file.Length <= 0)
            {
                ModelState.AddModelError(nameof(Input.Files), $"{file.FileName} is empty.");
                continue;
            }

            try
            {
                using var stream = file.OpenReadStream();
                var upload = new ActivityAttachmentUpload(stream, file.FileName, file.ContentType ?? string.Empty, file.Length);
                _attachmentValidator.Validate(upload);
            }
            catch (ActivityValidationException ex)
            {
                foreach (var error in ex.Errors.SelectMany(pair => pair.Value))
                {
                    ModelState.AddModelError(nameof(Input.Files), error);
                }
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Failed to read uploaded file {FileName}", file.FileName);
                ModelState.AddModelError(nameof(Input.Files), $"Could not read {file.FileName}.");
            }
        }
    }

    private async Task SaveAttachmentsAsync(Activity activity, CancellationToken cancellationToken)
    {
        if (Input.Files is null || Input.Files.Count == 0)
        {
            ExistingAttachmentCount = activity.Attachments?.Count ?? 0;
            return;
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ActivityAuthorizationException("A signed-in user is required.");
        }

        foreach (var file in Input.Files)
        {
            if (file is null || file.Length <= 0)
            {
                continue;
            }

            using var stream = file.OpenReadStream();
            var upload = new ActivityAttachmentUpload(stream, file.FileName, file.ContentType ?? string.Empty, file.Length);
            await _activityAttachmentManager.AddAsync(activity, upload, userId, cancellationToken);
        }

        ExistingAttachmentCount = activity.Attachments?.Count ?? 0;
    }

    private void ApplyActivityToInput(Activity activity)
    {
        Input = new InputModel
        {
            Id = activity.Id,
            Title = activity.Title,
            Description = activity.Description,
            Location = activity.Location,
            ActivityTypeId = activity.ActivityTypeId,
            ScheduledStart = ConvertToLocal(activity.ScheduledStartUtc),
            ScheduledEnd = ConvertToLocal(activity.ScheduledEndUtc)
        };

        ExistingAttachmentCount = activity.Attachments?.Count ?? 0;
    }

    private static bool IsManager(ClaimsPrincipal user)
    {
        return user.IsInRole("Admin") ||
               user.IsInRole("HoD") ||
               user.IsInRole("Project Office") ||
               user.IsInRole("TA");
    }

    private void AddErrorsToModelState(ActivityValidationException ex)
    {
        foreach (var (key, errors) in ex.Errors)
        {
            var modelKey = key switch
            {
                nameof(ActivityInput.Title) => $"{nameof(Input)}.{nameof(Input.Title)}",
                nameof(ActivityInput.Description) => $"{nameof(Input)}.{nameof(Input.Description)}",
                nameof(ActivityInput.Location) => $"{nameof(Input)}.{nameof(Input.Location)}",
                nameof(ActivityInput.ActivityTypeId) => $"{nameof(Input)}.{nameof(Input.ActivityTypeId)}",
                nameof(ActivityInput.ScheduledStartUtc) => $"{nameof(Input)}.{nameof(Input.ScheduledStart)}",
                nameof(ActivityInput.ScheduledEndUtc) => $"{nameof(Input)}.{nameof(Input.ScheduledEnd)}",
                nameof(Activity.Attachments) => $"{nameof(Input)}.{nameof(Input.Files)}",
                nameof(ActivityAttachmentUpload.FileName) => $"{nameof(Input)}.{nameof(Input.Files)}",
                nameof(ActivityAttachmentUpload.ContentType) => $"{nameof(Input)}.{nameof(Input.Files)}",
                nameof(ActivityAttachmentUpload.Length) => $"{nameof(Input)}.{nameof(Input.Files)}",
                _ => string.Empty
            };

            foreach (var error in errors)
            {
                ModelState.AddModelError(modelKey, error);
            }
        }
    }

    private static DateTime? ConvertToLocal(DateTimeOffset? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        var local = value.Value.ToLocalTime();
        var date = local.Date;
        return DateTime.SpecifyKind(date, DateTimeKind.Local);
    }

    private static DateTimeOffset? ConvertToUtc(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        var localDate = value.Value.Date;
        var local = DateTime.SpecifyKind(localDate, DateTimeKind.Local);
        return new DateTimeOffset(local).ToUniversalTime();
    }

    public sealed class InputModel
    {
        public int? Id { get; set; }

        [Required]
        [StringLength(200)]
        [Display(Name = "Nomenclature")]
        public string? Title { get; set; }

        [StringLength(2000)]
        [Display(Name = "Description")]
        public string? Description { get; set; }

        [StringLength(450)]
        [Display(Name = "Place")]
        public string? Location { get; set; }

        [Required]
        [Display(Name = "Activity type")]
        public int? ActivityTypeId { get; set; }

        [Display(Name = "Start date")]
        public DateTime? ScheduledStart { get; set; }

        [Display(Name = "End date")]
        public DateTime? ScheduledEnd { get; set; }

        [Display(Name = "Attachments")]
        public IList<IFormFile> Files { get; set; } = new List<IFormFile>();
    }
}
