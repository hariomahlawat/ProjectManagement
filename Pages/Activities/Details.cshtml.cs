using System;
using System.Collections.Generic;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Contracts.Activities;
using ProjectManagement.Infrastructure.Ui;
using ProjectManagement.Models.Activities;
using ProjectManagement.Services.Activities;

namespace ProjectManagement.Pages.Activities;

[Authorize]
public sealed class DetailsModel : PageModel
{
    private static readonly IReadOnlyList<string> AttachmentSummaryLabels = new[]
    {
        "PDF", "DOC/DOCX", "XLS/XLSX", "PPT/PPTX", "PNG", "JPG/JPEG", "MP4", "MOV", "WEBM"
    };

    private readonly IActivityService _activityService;
    private readonly ILogger<DetailsModel> _logger;
    private readonly MediaLibraryDbContext? _mediaLibraryDb;

    public DetailsModel(
        IActivityService activityService,
        ILogger<DetailsModel> logger,
        MediaLibraryDbContext? mediaLibraryDb = null)
    {
        _activityService = activityService ?? throw new ArgumentNullException(nameof(activityService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mediaLibraryDb = mediaLibraryDb;
    }

    [BindProperty]
    public List<IFormFile>? Uploads { get; set; }

    public Activity? Activity { get; private set; }

    public IReadOnlyList<ActivityAttachmentMetadata> Attachments { get; private set; } = Array.Empty<ActivityAttachmentMetadata>();

    public IReadOnlyList<ActivityPhotoViewModel> PhotoAttachments { get; private set; } = Array.Empty<ActivityPhotoViewModel>();

    public IReadOnlyList<ActivityAttachmentMetadata> VideoAttachments { get; private set; } = Array.Empty<ActivityAttachmentMetadata>();

    public IReadOnlyList<ActivityAttachmentMetadata> DocumentAttachments { get; private set; } = Array.Empty<ActivityAttachmentMetadata>();

    public IReadOnlyList<ActivityAttachmentMetadata> OtherAttachments { get; private set; } = Array.Empty<ActivityAttachmentMetadata>();

    public bool CanManage { get; private set; }

    public bool CanRequestDelete { get; private set; }

    public bool HasPendingDelete { get; private set; }

    public int RemainingAttachmentSlots { get; private set; }

    public int MaxAttachments => ActivityAttachmentManager.MaxAttachmentsPerActivity;

    public string AllowedAttachmentSummary => string.Join(", ", AttachmentSummaryLabels);

    public long MaxStandardAttachmentSizeBytes => ActivityAttachmentValidator.MaxStandardAttachmentSizeBytes;

    public long MaxVideoAttachmentSizeBytes => ActivityAttachmentValidator.MaxVideoAttachmentSizeBytes;

    public long MaxUploadBatchSizeBytes => ActivityAttachmentValidator.MaxUploadBatchSizeBytes;

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
        var activity = await _activityService.GetAsync(id, cancellationToken);
        if (activity is null)
        {
            return NotFound();
        }

        if (!ActivityAuthorizationPolicy.CanManage(activity, User, User.FindFirstValue(ClaimTypes.NameIdentifier)))
        {
            return Forbid();
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

        if (files.Sum(file => file.Length) > ActivityAttachmentValidator.MaxUploadBatchSizeBytes)
        {
            TempData.ToastError("The selected files exceed the 200 MB upload batch limit. Upload large videos separately.");
            return RedirectToPage(new { id });
        }

        var uploadedCount = 0;
        foreach (var file in files)
        {
            try
            {
                await using var stream = file.OpenReadStream();
                var upload = new ActivityAttachmentUpload(stream, file.FileName, file.ContentType ?? string.Empty, file.Length);
                await _activityService.AddAttachmentAsync(activity.Id, upload, cancellationToken);
                uploadedCount++;
            }
            catch (ActivityValidationException ex)
            {
                var error = ex.Errors.SelectMany(pair => pair.Value).FirstOrDefault();
                TempData.ToastError(error ?? "The attachment could not be uploaded.");
                return RedirectToPage(new { id });
            }
            catch (ActivityAuthorizationException)
            {
                return Forbid();
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
        var activity = await _activityService.GetAsync(id, cancellationToken);
        if (activity is null)
        {
            return NotFound();
        }

        if (!ActivityAuthorizationPolicy.CanManage(activity, User, User.FindFirstValue(ClaimTypes.NameIdentifier)))
        {
            return Forbid();
        }

        if (activity.Attachments?.All(a => a.Id != attachmentId) != false)
        {
            TempData.ToastError("Attachment not found.");
            return RedirectToPage(new { id });
        }

        try
        {
            await _activityService.RemoveAttachmentAsync(attachmentId, cancellationToken);
            TempData["ToastMessage"] = "Attachment removed.";
        }
        catch (ActivityAuthorizationException)
        {
            return Forbid();
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
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        CanManage = ActivityAuthorizationPolicy.CanManage(activity, User, userId);
        CanRequestDelete = ActivityAuthorizationPolicy.CanRequestDelete(User);
        HasPendingDelete = activity.DeleteRequests.Any(request => request.ApprovedAtUtc == null && request.RejectedAtUtc == null);

        var attachments = await _activityService.GetAttachmentMetadataAsync(activity.Id, cancellationToken);
        Attachments = attachments;

        var photos = attachments
            .Where(a => ActivityAttachmentClassifier.Classify(a.FileName, a.ContentType) == ActivityAttachmentKind.Photo)
            .ToList();
        PhotoAttachments = await BuildPhotoViewModelsAsync(photos, cancellationToken);

        VideoAttachments = attachments
            .Where(a => ActivityAttachmentClassifier.Classify(a.FileName, a.ContentType) == ActivityAttachmentKind.Video)
            .ToList();
        DocumentAttachments = attachments
            .Where(a =>
            {
                var kind = ActivityAttachmentClassifier.Classify(a.FileName, a.ContentType);
                return kind is ActivityAttachmentKind.Pdf or ActivityAttachmentKind.Document;
            })
            .ToList();
        OtherAttachments = attachments
            .Where(a => ActivityAttachmentClassifier.Classify(a.FileName, a.ContentType) == ActivityAttachmentKind.Other)
            .ToList();

        RemainingAttachmentSlots = Math.Max(0, ActivityAttachmentManager.MaxAttachmentsPerActivity - attachments.Count);
    }

    private async Task<IReadOnlyList<ActivityPhotoViewModel>> BuildPhotoViewModelsAsync(
        IReadOnlyList<ActivityAttachmentMetadata> photos,
        CancellationToken cancellationToken)
    {
        if (photos.Count == 0)
        {
            return Array.Empty<ActivityPhotoViewModel>();
        }

        var assets = await LoadPhotoAssetsAsync(photos.Select(photo => photo.Id), cancellationToken);
        return photos.Select(photo =>
        {
            if (assets.TryGetValue(photo.Id, out var asset))
            {
                return new ActivityPhotoViewModel(
                    photo,
                    BuildMediaUrl(asset.Id, "thumb", asset.CacheVersion) ?? photo.InlineUrl,
                    BuildMediaUrl(asset.Id, "preview", asset.CacheVersion) ?? photo.InlineUrl);
            }

            return new ActivityPhotoViewModel(photo, photo.InlineUrl, photo.InlineUrl);
        }).ToList();
    }

    private async Task<IReadOnlyDictionary<int, ActivityMediaAssetReference>> LoadPhotoAssetsAsync(
        IEnumerable<int> attachmentIds,
        CancellationToken cancellationToken)
    {
        if (_mediaLibraryDb is null)
        {
            return new Dictionary<int, ActivityMediaAssetReference>();
        }

        var sourceEntityIds = attachmentIds
            .Distinct()
            .Select(id => $"activity-photo:{id.ToString(CultureInfo.InvariantCulture)}")
            .ToList();

        try
        {
            var rows = await _mediaLibraryDb.Assets
                .AsNoTracking()
                .Where(asset => asset.Origin == MediaAssetOrigin.ActivityPhoto
                                && !asset.IsDeleted
                                && asset.IsAvailable
                                && sourceEntityIds.Contains(asset.SourceEntityId))
                .Select(asset => new { asset.Id, asset.SourceEntityId, asset.CacheVersion })
                .ToListAsync(cancellationToken);

            var result = new Dictionary<int, ActivityMediaAssetReference>();
            foreach (var row in rows)
            {
                const string prefix = "activity-photo:";
                if (row.SourceEntityId.StartsWith(prefix, StringComparison.Ordinal)
                    && int.TryParse(row.SourceEntityId[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var attachmentId))
                {
                    result[attachmentId] = new ActivityMediaAssetReference(row.Id, row.CacheVersion);
                }
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Media derivatives are an optimisation only. Activity details must remain
            // available even if the optional media catalogue is temporarily unavailable.
            _logger.LogDebug(ex, "Media catalogue unavailable while resolving Activity photo thumbnails.");
            return new Dictionary<int, ActivityMediaAssetReference>();
        }
    }

    private string? BuildMediaUrl(long assetId, string variant, int cacheVersion)
        => Url.Page("/Photos/Media", new { id = assetId, variant, v = cacheVersion });

    public sealed record ActivityPhotoViewModel(
        ActivityAttachmentMetadata Attachment,
        string ThumbnailUrl,
        string PreviewUrl)
    {
        public int Id => Attachment.Id;
        public string FileName => Attachment.FileName;
        public string ContentType => Attachment.ContentType;
        public long FileSize => Attachment.FileSize;
        public string DownloadUrl => Attachment.DownloadUrl;
        public string OriginalUrl => Attachment.InlineUrl;
        public DateTimeOffset UploadedAtUtc => Attachment.UploadedAtUtc;
    }

    private sealed record ActivityMediaAssetReference(long Id, int CacheVersion);
}
