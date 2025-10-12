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

[Authorize]
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
    public string? UploadCaption { get; set; }

    [BindProperty]
    public IFormFile? Upload { get; set; }

    public IReadOnlyList<SelectListItem> VisitTypeOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<VisitPhoto> Photos { get; private set; } = Array.Empty<VisitPhoto>();

    public Guid? CoverPhotoId { get; private set; }

    public Guid VisitId { get; private set; }

    public VisitPhotosViewModel PhotoGallery => new(VisitId, Photos, CoverPhotoId, CanManage);

    public bool CanManage => User.IsInRole("Admin") || User.IsInRole("HoD") || User.IsInRole("ProjectOffice");

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

        var result = await _visitService.UpdateAsync(id, Input.VisitTypeId!.Value, Input.DateOfVisit!.Value, Input.Strength, Input.Remarks, rowVersionBytes, _userManager.GetUserId(User) ?? string.Empty, cancellationToken);
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

        if (Upload == null || Upload.Length == 0)
        {
            ModelState.AddModelError(string.Empty, "Please select a photo to upload.");
            await LoadAsync(id, cancellationToken);
            return Page();
        }

        await using var stream = Upload.OpenReadStream();
        var result = await _photoService.UploadAsync(id, stream, Upload.FileName, Upload.ContentType, UploadCaption, _userManager.GetUserId(User) ?? string.Empty, cancellationToken);
        if (result.Outcome == VisitPhotoUploadOutcome.Success)
        {
            TempData["ToastMessage"] = "Photo uploaded.";
            return RedirectToPage(new { id });
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error);
        }

        await LoadAsync(id, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostDeletePhotoAsync(Guid id, Guid photoId, CancellationToken cancellationToken)
    {
        if (!CanManage)
        {
            return Forbid();
        }

        var result = await _photoService.RemoveAsync(id, photoId, _userManager.GetUserId(User) ?? string.Empty, cancellationToken);
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

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostSetCoverAsync(Guid id, Guid photoId, CancellationToken cancellationToken)
    {
        if (!CanManage)
        {
            return Forbid();
        }

        var result = await _photoService.SetCoverAsync(id, photoId, _userManager.GetUserId(User) ?? string.Empty, cancellationToken);
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

        return RedirectToPage(new { id });
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

        [Range(1, int.MaxValue, ErrorMessage = "Strength must be greater than zero.")]
        public int Strength { get; set; }

        [StringLength(2000)]
        public string? Remarks { get; set; }
    }
}
