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
public class NewModel : PageModel
{
    private readonly IActivityTypeService _service;

    public NewModel(IActivityTypeService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required]
        [StringLength(128)]
        public string Name { get; set; } = string.Empty;

        [StringLength(512)]
        public string? Description { get; set; }
            = string.Empty;

        [Display(Name = "Display order")]
        public int Ordinal { get; set; }
            = 0;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _service.CreateAsync(Input.Name, Input.Description, Input.Ordinal, cancellationToken);
        if (result.Outcome == ActivityTypeMutationOutcome.Success)
        {
            TempData["ToastMessage"] = "Activity type created.";
            return RedirectToPage("Index");
        }

        if (result.Outcome == ActivityTypeMutationOutcome.DuplicateName)
        {
            ModelState.AddModelError(nameof(Input.Name), "An activity type with this name already exists.");
        }
        else if (result.Errors.Count > 0)
        {
            ModelState.AddModelError(string.Empty, result.Errors[0]);
        }
        else
        {
            ModelState.AddModelError(string.Empty, "Unable to create activity type.");
        }

        return Page();
    }
}
