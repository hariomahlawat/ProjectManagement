using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Admin.ActivityTypes;

[Authorize(Policy = ProjectOfficeReportsPolicies.ManageActivityTypes)]
public class EditModel : PageModel
{
    private readonly IActivityTypeService _service;

    public EditModel(IActivityTypeService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [HiddenInput]
        public Guid Id { get; set; }

        [Required]
        [StringLength(128)]
        public string Name { get; set; } = string.Empty;

        [StringLength(512)]
        public string? Description { get; set; }
            = string.Empty;

        [Display(Name = "Display order")]
        public int Ordinal { get; set; }
            = 0;

        [Display(Name = "Active")]
        public bool IsActive { get; set; }
            = true;

        [HiddenInput]
        public string RowVersion { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _service.FindAsync(id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        Input = new InputModel
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            IsActive = entity.IsActive,
            Ordinal = entity.Ordinal,
            RowVersion = Convert.ToBase64String(entity.RowVersion)
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var rowVersion = DecodeRowVersion(Input.RowVersion);
        if (rowVersion is null)
        {
            ModelState.AddModelError(string.Empty, "Unable to process the request. Please reload the page and try again.");
            return Page();
        }

        var result = await _service.UpdateAsync(Input.Id, Input.Name, Input.Description, Input.IsActive, Input.Ordinal, rowVersion, cancellationToken);
        switch (result.Outcome)
        {
            case ActivityTypeMutationOutcome.Success:
                TempData["ToastMessage"] = "Activity type updated.";
                return RedirectToPage("Index");
            case ActivityTypeMutationOutcome.DuplicateName:
                ModelState.AddModelError(nameof(Input.Name), "An activity type with this name already exists.");
                break;
            case ActivityTypeMutationOutcome.Invalid when result.Errors.Count > 0:
                ModelState.AddModelError(string.Empty, result.Errors[0]);
                break;
            case ActivityTypeMutationOutcome.ConcurrencyConflict when result.Errors.Count > 0:
                ModelState.AddModelError(string.Empty, result.Errors[0]);
                break;
            case ActivityTypeMutationOutcome.NotFound:
                ModelState.AddModelError(string.Empty, "The activity type could not be found. It may have been deleted.");
                break;
            default:
                ModelState.AddModelError(string.Empty, "Unable to update activity type.");
                break;
        }

        return Page();
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
}
