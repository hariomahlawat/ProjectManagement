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
public class NewModel : PageModel
{
    private readonly VisitService _visitService;
    private readonly VisitTypeService _visitTypeService;
    private readonly IVisitPhotoService _photoService;
    private readonly UserManager<ApplicationUser> _userManager;

    public NewModel(VisitService visitService, VisitTypeService visitTypeService, IVisitPhotoService photoService, UserManager<ApplicationUser> userManager)
    {
        _visitService = visitService;
        _visitTypeService = visitTypeService;
        _photoService = photoService;
        _userManager = userManager;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty]
    [StringLength(512)]
    public string? UploadCaption { get; set; }

    [BindProperty]
    public List<IFormFile> Uploads { get; set; } = new();

    public IReadOnlyList<SelectListItem> VisitTypeOptions { get; private set; } = Array.Empty<SelectListItem>();

    public VisitPhotosViewModel EmptyPhotoGallery { get; } = new(Guid.Empty, Array.Empty<VisitPhoto>(), null, false);

    public bool CanManage => User.IsInRole("Admin") || User.IsInRole("HoD") || User.IsInRole("ProjectOffice");

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!CanManage)
        {
            return Forbid();
        }

        await LoadVisitTypesAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!CanManage)
        {
            return Forbid();
        }

        var uploads = Uploads?.Where(file => file != null).ToList() ?? new List<IFormFile>();
        if (uploads.Any(file => file.Length == 0))
        {
            ModelState.AddModelError(nameof(Uploads), "One or more selected photos were empty. Please choose valid images.");
        }

        if (!ModelState.IsValid)
        {
            await LoadVisitTypesAsync(cancellationToken);
            return Page();
        }

        var userId = _userManager.GetUserId(User) ?? string.Empty;
        var result = await _visitService.CreateAsync(Input.VisitTypeId!.Value, Input.DateOfVisit!.Value, Input.VisitorName, Input.Strength, Input.Remarks, userId, cancellationToken);
        if (result.Outcome == VisitMutationOutcome.Success && result.Entity != null)
        {
            var toastMessage = "Visit created.";

            if (uploads.Count > 0)
            {
                var caption = string.IsNullOrWhiteSpace(UploadCaption) ? null : UploadCaption!.Trim();
                var successfulUploads = 0;
                var errors = new List<string>();

                foreach (var file in uploads)
                {
                    if (file.Length == 0)
                    {
                        errors.Add("One of the selected photos was empty. Please try uploading it again.");
                        continue;
                    }

                    await using var stream = file.OpenReadStream();
                    var uploadResult = await _photoService.UploadAsync(result.Entity.Id, stream, file.FileName, file.ContentType, caption, userId, cancellationToken);
                    if (uploadResult.Outcome == VisitPhotoUploadOutcome.Success)
                    {
                        successfulUploads++;
                        continue;
                    }

                    if (uploadResult.Outcome == VisitPhotoUploadOutcome.NotFound)
                    {
                        errors.Add("The visit was created but could not be found for photo upload.");
                        break;
                    }

                    if (uploadResult.Errors.Count > 0)
                    {
                        errors.AddRange(uploadResult.Errors);
                    }
                    else
                    {
                        errors.Add("Unable to upload one of the selected photos. Please try again from the edit page.");
                    }
                }

                if (successfulUploads > 0)
                {
                    var suffix = successfulUploads == 1 ? "photo" : "photos";
                    toastMessage = $"Visit created and {successfulUploads} {suffix} uploaded.";
                }

                if (errors.Count > 0)
                {
                    TempData["ToastError"] = string.Join(" ", errors);
                }
            }

            TempData["ToastMessage"] = toastMessage;
            return RedirectToPage("Index");
        }

        if (result.Outcome == VisitMutationOutcome.VisitTypeInactive || result.Outcome == VisitMutationOutcome.VisitTypeNotFound)
        {
            ModelState.AddModelError(nameof(Input.VisitTypeId), "Please choose an active visit type.");
        }
        else if (result.Errors.Count > 0)
        {
            ModelState.AddModelError(string.Empty, result.Errors[0]);
        }
        else
        {
            ModelState.AddModelError(string.Empty, "Unable to create the visit.");
        }

        await LoadVisitTypesAsync(cancellationToken);
        return Page();
    }

    private async Task LoadVisitTypesAsync(CancellationToken cancellationToken)
    {
        var types = await _visitTypeService.GetAllAsync(includeInactive: false, cancellationToken);
        var list = new List<SelectListItem>
        {
            new("Select a visit type", string.Empty)
        };

        foreach (var type in types)
        {
            list.Add(new SelectListItem(type.Name, type.Id.ToString())
            {
                Selected = Input.VisitTypeId.HasValue && type.Id == Input.VisitTypeId.Value
            });
        }

        VisitTypeOptions = list;
    }

    private bool IsProjectOfficeMember()
    {
        return User.IsInRole("ProjectOffice") || User.IsInRole("Project Office");
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
        public int Strength { get; set; } = 1;

        [StringLength(2000)]
        public string? Remarks { get; set; }
    }
}
