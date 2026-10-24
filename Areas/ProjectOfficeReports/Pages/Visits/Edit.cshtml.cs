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
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Visits;

[Authorize(Policy = ProjectOfficeReportsPolicies.ManageVisits)]
public class EditModel : PageModel
{
    // SECTION: Dependencies
    private readonly VisitService _visitService;
    private readonly VisitTypeService _visitTypeService;
    private readonly IVisitPhotoService _photoService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly VisitPhotoOptions _photoOptions;

    public EditModel(
        VisitService visitService,
        VisitTypeService visitTypeService,
        IVisitPhotoService photoService,
        UserManager<ApplicationUser> userManager,
        IOptions<VisitPhotoOptions> photoOptions)
    {
        _visitService = visitService;
        _visitTypeService = visitTypeService;
        _photoService = photoService;
        _userManager = userManager;
        _photoOptions = photoOptions.Value;
    }

    // SECTION: Upload configuration helpers
    public long VisitPhotoMaxBytes => _photoOptions.MaxFileSizeBytes;

    public int VisitPhotoMaxFiles => _photoOptions.MaxFilesPerUpload;

    public long VisitPhotoMaxBatchBytes => _photoOptions.MaxBatchSizeBytes;

    public int VisitPhotoMaxBatchMb => Math.Max(1, (int)Math.Floor(VisitPhotoMaxBatchBytes / 1024d / 1024d));

    public int VisitPhotoMaxMb => Math.Max(1, (int)Math.Floor(VisitPhotoMaxBytes / 1024d / 1024d));

    public string VisitPhotoHelpText =>
        $"JPEG, PNG or WebP · up to {VisitPhotoMaxFiles} photos, {VisitPhotoMaxMb} MB each, {VisitPhotoMaxBatchMb} MB in all";

    public const string PhotoManagerAnchor = "visit-photo-manager";

    public VisitPhotoPickerModel PhotoPicker => new(
        "visit-edit-photo",
        VisitPhotoHelpText,
        VisitPhotoMaxFiles,
        VisitPhotoMaxBytes,
        VisitPhotoMaxBatchBytes,
        UploadCaption,
        ModelState.TryGetValue(nameof(Uploads), out var entry) && entry.Errors.Count > 0 ? entry.Errors[0].ErrorMessage : null);

    // The visitor's name as saved, for the page header (the field itself may hold unsaved edits).
    public string? SavedVisitorName { get; private set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty]
    public string RowVersion { get; set; } = string.Empty;

    [BindProperty]
    [StringLength(512)]
    public string? UploadCaption { get; set; }

    [BindProperty]
    public IFormFile? Upload { get; set; }

    [BindProperty]
    public List<IFormFile> Uploads { get; set; } = new();

    public IReadOnlyList<SelectListItem> VisitTypeOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<VisitPhoto> Photos { get; private set; } = Array.Empty<VisitPhoto>();

    public Guid? CoverPhotoId { get; private set; }

    public Guid VisitId { get; private set; }

    public VisitPhotosViewModel PhotoGallery => new(VisitId, Photos, CoverPhotoId, CanManage);

    // The visit as saved; the view builds the photo viewer's items from it (VisitMediaViewerItems).
    public Visit? SavedVisit { get; private set; }

    public bool CanManage => User.IsInRole("Admin") || User.IsInRole("HoD") || IsProjectOfficeMember();

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            // The record page has the "Visit not found" screen.
            return RedirectToPage("Details", new { id });
        }

        if (!CanManage)
        {
            return Forbid();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!CanManage)
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync(id, cancellationToken, keepInput: true);
            return Page();
        }

        var rowVersionBytes = DecodeRowVersion(RowVersion);
        if (rowVersionBytes == null)
        {
            ModelState.AddModelError(string.Empty, "We could not verify your request. Please reload and try again.");
            await LoadAsync(id, cancellationToken, keepInput: true);
            return Page();
        }

        var result = await _visitService.UpdateAsync(id, Input.VisitTypeId!.Value, Input.DateOfVisit!.Value, Input.VisitorName, Input.Strength, Input.Remarks, rowVersionBytes, _userManager.GetUserId(User) ?? string.Empty, cancellationToken);
        if (result.Outcome == VisitMutationOutcome.Success)
        {
            TempData["ToastSuccess"] = "Visit updated.";
            return RedirectToPage("Details", new { id });
        }

        if (result.Outcome == VisitMutationOutcome.VisitTypeInactive || result.Outcome == VisitMutationOutcome.VisitTypeNotFound)
        {
            ModelState.AddModelError(nameof(Input.VisitTypeId), "Please choose an active visit type.");
        }
        else if (result.Outcome == VisitMutationOutcome.ConcurrencyConflict)
        {
            ModelState.AddModelError(string.Empty, "Another user updated this visit. Please reload and try again.");
        }
        else if (result.Errors.Count > 0)
        {
            ModelState.AddModelError(string.Empty, result.Errors[0]);
        }
        else
        {
            ModelState.AddModelError(string.Empty, "Unable to update the visit.");
        }

        await LoadAsync(id, cancellationToken, keepInput: true);
        return Page();
    }

    public async Task<IActionResult> OnPostUploadAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!CanManage)
        {
            return Forbid();
        }

        // SECTION: Upload validation bootstrap
        ClearInputValidationErrors();

        var visitId = ResolveVisitId(id);
        if (visitId == Guid.Empty)
        {
            TempData["ToastError"] = "Visit not found.";
            return RedirectToPage("Index");
        }

        var uploads = CollectUploadFiles();

        // SECTION: Upload guardrails
        if (uploads.Count == 0)
        {
            ModelState.AddModelError(nameof(Uploads), "Please select at least one photo to upload.");
            await LoadAsync(visitId, cancellationToken);
            return Page();
        }

        var maxBytes = VisitPhotoMaxBytes;
        var maxMb = VisitPhotoMaxMb;

        if (uploads.Count > VisitPhotoMaxFiles)
        {
            ModelState.AddModelError(nameof(Uploads), $"You can upload up to {VisitPhotoMaxFiles} photos at a time.");
            await LoadAsync(visitId, cancellationToken);
            return Page();
        }

        if (uploads.Sum(file => file.Length) > VisitPhotoMaxBatchBytes)
        {
            ModelState.AddModelError(nameof(Uploads), $"Total upload size cannot exceed {VisitPhotoMaxBatchMb} MB.");
            await LoadAsync(visitId, cancellationToken);
            return Page();
        }

        if (uploads.Any(file => file.Length == 0))
        {
            ModelState.AddModelError(nameof(Uploads), "One or more selected photos were empty. Please choose valid images.");
            await LoadAsync(visitId, cancellationToken);
            return Page();
        }

        if (uploads.Any(file => file.Length > maxBytes))
        {
            ModelState.AddModelError(nameof(Uploads), $"One or more photos exceed {maxMb} MB. Please choose smaller files.");
            await LoadAsync(visitId, cancellationToken);
            return Page();
        }

        var caption = string.IsNullOrWhiteSpace(UploadCaption) ? null : UploadCaption!.Trim();
        var userId = _userManager.GetUserId(User) ?? string.Empty;
        var successfulUploads = 0;
        var errors = new List<string>();

        // SECTION: Upload processing
        foreach (var file in uploads)
        {
            if (file.Length > maxBytes)
            {
                errors.Add($"File exceeds the maximum size of {maxMb} MB.");
                continue;
            }

            await using var stream = file.OpenReadStream();
            var result = await _photoService.UploadAsync(visitId, stream, file.FileName, file.ContentType, caption, userId, cancellationToken);

            if (result.Outcome == VisitPhotoUploadOutcome.Success)
            {
                successfulUploads++;
                continue;
            }

            if (result.Outcome == VisitPhotoUploadOutcome.NotFound)
            {
                TempData["ToastError"] = "Visit not found.";
                return RedirectToPage("Index");
            }

            if (result.Errors.Count > 0)
            {
                errors.AddRange(result.Errors);
            }
            else
            {
                errors.Add("Unable to upload one of the selected photos. Please try again.");
            }
        }

        if (successfulUploads > 0)
        {
            var suffix = successfulUploads == 1 ? "photo" : "photos";
            TempData["ToastSuccess"] = $"{successfulUploads} {suffix} uploaded.";
        }

        if (errors.Count > 0)
        {
            if (successfulUploads > 0)
            {
                TempData["ToastError"] = string.Join(" ", errors);
                return RedirectToPhotos(visitId);
            }

            foreach (var error in errors)
            {
                ModelState.AddModelError(nameof(Uploads), error);
            }

            await LoadAsync(visitId, cancellationToken);
            return Page();
        }

        return RedirectToPhotos(visitId);
    }

    public async Task<IActionResult> OnPostDeletePhotoAsync(Guid id, Guid photoId, CancellationToken cancellationToken)
    {
        if (!CanManage)
        {
            return Forbid();
        }

        var visitId = ResolveVisitId(id);
        if (visitId == Guid.Empty)
        {
            TempData["ToastError"] = "Visit not found.";
            return RedirectToPage("Index");
        }

        var result = await _photoService.RemoveAsync(visitId, photoId, _userManager.GetUserId(User) ?? string.Empty, cancellationToken);
        if (result.Outcome == VisitPhotoDeletionOutcome.Success)
        {
            TempData["ToastSuccess"] = "Photo deleted.";
        }
        else if (result.Outcome == VisitPhotoDeletionOutcome.NotFound)
        {
            TempData["ToastError"] = "Photo not found.";
        }
        else
        {
            TempData["ToastError"] = "Unable to delete the photo.";
        }

        return RedirectToPhotos(visitId);
    }

    public async Task<IActionResult> OnPostSetCoverAsync(Guid id, Guid photoId, CancellationToken cancellationToken)
    {
        if (!CanManage)
        {
            return Forbid();
        }

        var visitId = ResolveVisitId(id);
        if (visitId == Guid.Empty)
        {
            TempData["ToastError"] = "Visit not found.";
            return RedirectToPage("Index");
        }

        var result = await _photoService.SetCoverAsync(visitId, photoId, _userManager.GetUserId(User) ?? string.Empty, cancellationToken);
        if (result.Outcome == VisitPhotoSetCoverOutcome.Success)
        {
            TempData["ToastSuccess"] = "Cover photo updated.";
        }
        else if (result.Outcome == VisitPhotoSetCoverOutcome.NotFound)
        {
            TempData["ToastError"] = "Photo not found.";
        }
        else
        {
            TempData["ToastError"] = "Unable to set cover photo.";
        }

        return RedirectToPhotos(visitId);
    }

    // Photo actions return to the photo manager, not the top of the page.
    private IActionResult RedirectToPhotos(Guid visitId) => RedirectToPage(null, null, new { id = visitId }, PhotoManagerAnchor);

    // SECTION: Visit resolution helpers
    private Guid ResolveVisitId(Guid id)
    {
        if (id != Guid.Empty)
        {
            return id;
        }

        if (RouteData.Values.TryGetValue("id", out var routeValue) &&
            Guid.TryParse(routeValue?.ToString(), out var routeId) &&
            routeId != Guid.Empty)
        {
            return routeId;
        }

        if (Request?.Form.TryGetValue("id", out var formValue) == true &&
            Guid.TryParse(formValue.ToString(), out var formId) &&
            formId != Guid.Empty)
        {
            return formId;
        }

        return Guid.Empty;
    }

    // SECTION: Page data loading
    // keepInput: re-showing the form after a failed save keeps what the officer typed.
    private async Task<bool> LoadAsync(Guid id, CancellationToken cancellationToken, bool keepInput = false)
    {
        var details = await _visitService.GetDetailsAsync(id, cancellationToken);
        if (details == null)
        {
            return false;
        }

        if (!keepInput)
        {
            Input = new InputModel
            {
                VisitTypeId = details.Visit.VisitTypeId,
                DateOfVisit = details.Visit.DateOfVisit,
                VisitorName = details.Visit.VisitorName,
                Strength = details.Visit.Strength,
                Remarks = details.Visit.Remarks
            };
            RowVersion = Convert.ToBase64String(details.Visit.RowVersion);
        }

        CoverPhotoId = details.Visit.CoverPhotoId;
        Photos = details.Photos;
        VisitId = details.Visit.Id;
        SavedVisitorName = details.Visit.VisitorName;
        SavedVisit = details.Visit;

        var visitTypes = await _visitTypeService.GetAllAsync(includeInactive: false, cancellationToken);
        var list = new List<SelectListItem>
        {
            new("Select a visit type", string.Empty)
        };

        var includeCurrent = visitTypes.Any(x => x.Id == details.Visit.VisitTypeId);
        foreach (var type in visitTypes)
        {
            list.Add(new SelectListItem(type.Name, type.Id.ToString())
            {
                Selected = type.Id == (Input.VisitTypeId ?? details.Visit.VisitTypeId)
            });
        }

        if (!includeCurrent)
        {
            list.Add(new SelectListItem(details.VisitType.Name + " (inactive)", details.Visit.VisitTypeId.ToString())
            {
                Selected = true
            });
        }

        VisitTypeOptions = list;
        return true;
    }

    // SECTION: Authorization helpers
    private bool IsProjectOfficeMember()
    {
        return User.IsInRole("Project Office") || User.IsInRole("ProjectOffice");
    }

    // SECTION: ModelState helpers
    private void ClearInputValidationErrors()
    {
        var prefix = nameof(Input);
        var keysToRemove = new List<string>();

        foreach (var entry in ModelState)
        {
            if (entry.Key.Equals(prefix, StringComparison.Ordinal) ||
                entry.Key.StartsWith(prefix + '.', StringComparison.Ordinal))
            {
                keysToRemove.Add(entry.Key);
            }
        }

        foreach (var key in keysToRemove)
        {
            ModelState.Remove(key);
        }

        ModelState.ClearValidationState(prefix);
        ModelState.ClearValidationState(string.Empty);
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

    // The fields live in VisitFormInput, shared with Record.
    public class InputModel : VisitFormInput
    {
    }

    // SECTION: Upload helpers
    private List<IFormFile> CollectUploadFiles()
    {
        var uploads = new List<IFormFile>();

        if (Upload is not null)
        {
            uploads.Add(Upload);
        }

        if (Uploads != null)
        {
            foreach (var file in Uploads)
            {
                if (file != null)
                {
                    uploads.Add(file);
                }
            }
        }

        if (uploads.Count == 0)
        {
            AppendRequestFiles(uploads);
        }

        return uploads;
    }

    private void AppendRequestFiles(List<IFormFile> uploads)
    {
        if (uploads.Count > 0)
        {
            return;
        }

        var formFiles = Request?.Form.Files;
        if (formFiles == null || formFiles.Count == 0)
        {
            return;
        }

        foreach (var file in formFiles)
        {
            if (file != null && IsImageFile(file))
            {
                uploads.Add(file);
            }
        }
    }

    private static bool IsImageFile(IFormFile file)
    {
        return file.ContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;
    }
}
