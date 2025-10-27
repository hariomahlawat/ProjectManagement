using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using ProjectManagement.Infrastructure.Ui;
using ProjectManagement.Models.Activities;
using ProjectManagement.Services.Activities;

namespace ProjectManagement.Pages.Activities;

[Authorize]
public sealed class DetailsModel : PageModel
{
    private static readonly string[] ManagerRoles = { "Admin", "HoD", "ProjectOffice", "TA" };
    private static readonly IReadOnlyList<string> AttachmentSummaryLabels = new[]
    {
        "PDF", "PNG", "JPG", "DOCX", "XLSX", "MP4", "MOV", "WEBM"
    };

    private readonly IActivityService _activityService;
    private readonly IActivityAttachmentManager _activityAttachmentManager;
    private readonly ILogger<DetailsModel> _logger;

    public DetailsModel(IActivityService activityService,
                        IActivityAttachmentManager activityAttachmentManager,
                        ILogger<DetailsModel> logger)
    {
        _activityService = activityService ?? throw new ArgumentNullException(nameof(activityService));
        _activityAttachmentManager = activityAttachmentManager ?? throw new ArgumentNullException(nameof(activityAttachmentManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty]
    public List<IFormFile>? Uploads { get; set; }

    public Activity? Activity { get; private set; }

    public IReadOnlyList<ActivityAttachmentMetadata> Attachments { get; private set; } = Array.Empty<ActivityAttachmentMetadata>();

    public IReadOnlyList<ActivityAttachmentMetadata> PhotoAttachments { get; private set; } = Array.Empty<ActivityAttachmentMetadata>();

    public IReadOnlyList<ActivityAttachmentMetadata> VideoAttachments { get; private set; } = Array.Empty<ActivityAttachmentMetadata>();

    public IReadOnlyList<ActivityAttachmentMetadata> PdfAttachments { get; private set; } = Array.Empty<ActivityAttachmentMetadata>();

    public IReadOnlyList<ActivityAttachmentMetadata> OtherAttachments { get; private set; } = Array.Empty<ActivityAttachmentMetadata>();

    public bool CanManage { get; private set; }

    public int RemainingAttachmentSlots { get; private set; }

    public int MaxAttachments => ActivityAttachmentManager.MaxAttachmentsPerActivity;

    public string AllowedAttachmentSummary => string.Join(", ", AttachmentSummaryLabels);

    public long MaxAttachmentSizeBytes => ActivityAttachmentValidator.MaxAttachmentSizeBytes;

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var activity = await _activityService.GetAsync(id, cancellationToken);
        if (activity is null)
        {
            return NotFound();
        }

        await PopulateAsync(activity, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostUploadAsync(int id, CancellationToken cancellationToken)
    {
        if (!IsManager(User))
        {
            return Forbid();
        }

        var activity = await _activityService.GetAsync(id, cancellationToken);
        if (activity is null)
        {
            return NotFound();
        }

        var files = Uploads?.Where(file => file is not null && file.Length > 0).ToList() ?? new List<IFormFile>();
        if (files.Count == 0)
        {
            TempData.ToastError("Select at least one file to upload.");
            return RedirectToPage(new { id });
        }

        var existingCount = activity.Attachments?.Count ?? 0;
        var remainingSlots = ActivityAttachmentManager.MaxAttachmentsPerActivity - existingCount;
        if (remainingSlots <= 0)
        {
            TempData.ToastError("This activity already has the maximum number of attachments.");
            return RedirectToPage(new { id });
        }

        if (files.Count > remainingSlots)
        {
            var message = remainingSlots == 1
                ? "Only one additional attachment can be uploaded."
                : $"Only {remainingSlots} additional attachments can be uploaded.";
            TempData.ToastError(message);
            return RedirectToPage(new { id });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var uploadedCount = 0;
        foreach (var file in files)
        {
            try
            {
                await using var stream = file.OpenReadStream();
                var upload = new ActivityAttachmentUpload(stream, file.FileName, file.ContentType ?? string.Empty, file.Length);
                await _activityAttachmentManager.AddAsync(activity, upload, userId, cancellationToken);
                uploadedCount++;
            }
            catch (ActivityValidationException ex)
            {
                var error = ex.Errors.SelectMany(pair => pair.Value).FirstOrDefault();
                TempData.ToastError(error ?? "The attachment could not be uploaded.");
                return RedirectToPage(new { id });
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Failed to read uploaded file {FileName} for activity {ActivityId}.", file.FileName, activity.Id);
                TempData.ToastError($"Could not read {file.FileName}.");
                return RedirectToPage(new { id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload file {FileName} for activity {ActivityId}.", file.FileName, activity.Id);
                TempData.ToastError($"Failed to upload {file.FileName}. Please try again.");
                return RedirectToPage(new { id });
            }
        }

        if (uploadedCount > 0)
        {
            TempData["ToastMessage"] = uploadedCount == 1 ? "Attachment uploaded." : $"{uploadedCount} attachments uploaded.";
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRemoveAttachmentAsync(int id, int attachmentId, CancellationToken cancellationToken)
    {
        if (!IsManager(User))
        {
            return Forbid();
        }

        var activity = await _activityService.GetAsync(id, cancellationToken);
        if (activity is null)
        {
            return NotFound();
        }

        var attachment = activity.Attachments?.FirstOrDefault(a => a.Id == attachmentId);
        if (attachment is null)
        {
            TempData.ToastError("Attachment not found.");
            return RedirectToPage(new { id });
        }

        try
        {
            await _activityAttachmentManager.RemoveAsync(attachment, cancellationToken);
            TempData["ToastMessage"] = "Attachment removed.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove attachment {AttachmentId} for activity {ActivityId}.", attachmentId, id);
            TempData.ToastError("Unable to remove the attachment. Please try again.");
        }

        return RedirectToPage(new { id });
    }

    private async Task PopulateAsync(Activity activity, CancellationToken cancellationToken)
    {
        Activity = activity;
        CanManage = IsManager(User);

        var attachments = await _activityService.GetAttachmentMetadataAsync(activity.Id, cancellationToken);
        Attachments = attachments;
        PhotoAttachments = attachments.Where(IsPhoto).ToList();
        VideoAttachments = attachments.Where(IsVideo).ToList();
        PdfAttachments = attachments.Where(IsPdf).ToList();
        OtherAttachments = attachments.Except(PhotoAttachments.Concat(VideoAttachments).Concat(PdfAttachments)).ToList();

        RemainingAttachmentSlots = Math.Max(0, ActivityAttachmentManager.MaxAttachmentsPerActivity - attachments.Count);
    }

    private static bool IsPhoto(ActivityAttachmentMetadata attachment)
    {
        return attachment.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVideo(ActivityAttachmentMetadata attachment)
    {
        return attachment.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPdf(ActivityAttachmentMetadata attachment)
    {
        return string.Equals(attachment.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase) ||
               attachment.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsManager(ClaimsPrincipal user)
    {
        foreach (var role in ManagerRoles)
        {
            if (user.IsInRole(role))
            {
                return true;
            }
        }

        return false;
    }
}
