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
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Visits;

[Authorize(Policy = ProjectOfficeReportsPolicies.ManageVisits)]
public class EditModel : PageModel
{
    private readonly VisitService _visitService;
    private readonly VisitTypeService _visitTypeService;
    private readonly IVisitPhotoService _photoService;
    private readonly UserManager<ApplicationUser> _userManager;

    public EditModel(VisitService visitService, VisitTypeService visitTypeService, IVisitPhotoService photoService, UserManager<ApplicationUser> userManager)
    {
        _visitService = visitService;
        _visitTypeService = visitTypeService;
        _photoService = photoService;
        _userManager = userManager;
    }

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

    public bool CanManage => User.IsInRole("Admin") || User.IsInRole("HoD") || IsProjectOfficeMember();

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
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
            await LoadAsync(id, cancellationToken);
            return Page();
        }

        var rowVersionBytes = DecodeRowVersion(RowVersion);
        if (rowVersionBytes == null)
        {
            ModelState.AddModelError(string.Empty, "We could not verify your request. Please reload and try again.");
            await LoadAsync(id, cancellationToken);
            return Page();
        }

        var result = await _visitService.UpdateAsync(id, Input.VisitTypeId!.Value, Input.DateOfVisit!.Value, Input.VisitorName, Input.Strength, Input.Remarks, rowVersionBytes, _userManager.GetUserId(User) ?? string.Empty, cancellationToken);
        if (result.Outcome == VisitMutationOutcome.Success)
        {
            TempData["ToastMessage"] = "Visit updated.";
            return RedirectToPage(new { id });
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

        await LoadAsync(id, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostUploadAsync(Guid id, CancellationToken cancellationToken)
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

        if (uploads.Count == 0)
        {
            ModelState.AddModelError(nameof(Uploads), "Please select at least one photo to upload.");
            await LoadAsync(visitId, cancellationToken);
            return Page();
        }

        if (uploads.Any(file => file.Length == 0))
        {
            ModelState.AddModelError(nameof(Uploads), "One or more selected photos were empty. Please choose valid images.");
            await LoadAsync(visitId, cancellationToken);
            return Page();
        }

        ClearInputValidationErrors();

        if (!ModelState.IsValid)
        {
            await LoadAsync(visitId, cancellationToken);
            return Page();
        }

        var caption = string.IsNullOrWhiteSpace(UploadCaption) ? null : UploadCaption!.Trim();
        var userId = _userManager.GetUserId(User) ?? string.Empty;
        var successfulUploads = 0;
        var errors = new List<string>();

        foreach (var file in uploads)
        {
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
            TempData["ToastMessage"] = $"{successfulUploads} {suffix} uploaded.";
        }

        if (errors.Count > 0)
        {
            if (successfulUploads > 0)
            {
                TempData["ToastError"] = string.Join(" ", errors);
                return RedirectToPage(new { id = visitId });
            }

            foreach (var error in errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            await LoadAsync(visitId, cancellationToken);
            return Page();
        }

        return RedirectToPage(new { id = visitId });
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
            TempData["ToastMessage"] = "Photo deleted.";
        }
        else if (result.Outcome == VisitPhotoDeletionOutcome.NotFound)
        {
            TempData["ToastError"] = "Photo not found.";
        }
        else
        {
            TempData["ToastError"] = "Unable to delete the photo.";
        }

        return RedirectToPage(new { id = visitId });
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
            TempData["ToastMessage"] = "Cover photo updated.";
        }
        else if (result.Outcome == VisitPhotoSetCoverOutcome.NotFound)
        {
            TempData["ToastError"] = "Photo not found.";
        }
        else
        {
            TempData["ToastError"] = "Unable to set cover photo.";
        }

        return RedirectToPage(new { id = visitId });
    }

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

    private async Task<bool> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var details = await _visitService.GetDetailsAsync(id, cancellationToken);
        if (details == null)
        {
            return false;
        }

        Input = new InputModel
        {
            VisitTypeId = details.Visit.VisitTypeId,
            DateOfVisit = details.Visit.DateOfVisit,
            VisitorName = details.Visit.VisitorName,
            Strength = details.Visit.Strength,
            Remarks = details.Visit.Remarks
        };
        RowVersion = Convert.ToBase64String(details.Visit.RowVersion);
        CoverPhotoId = details.Visit.CoverPhotoId;
        Photos = details.Photos;
        VisitId = details.Visit.Id;

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
                Selected = type.Id == details.Visit.VisitTypeId
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

    private bool IsProjectOfficeMember()
    {
        return User.IsInRole("Project Office") || User.IsInRole("ProjectOffice");
    }

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

    public class InputModel
    {
        [Required]
        public Guid? VisitTypeId { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateOnly? DateOfVisit { get; set; }

        [Required]
        [StringLength(200)]
        [Display(Name = "Visitor name")]
        public string VisitorName { get; set; } = string.Empty;

        [Range(1, int.MaxValue, ErrorMessage = "Strength must be greater than zero.")]
        public int Strength { get; set; }

        [StringLength(2000)]
        public string? Remarks { get; set; }
    }

    // SECTION: Upload helpers
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
            if (file != null)
            {
                uploads.Add(file);
            }
        }
    }
}
