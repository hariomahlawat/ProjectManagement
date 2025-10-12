using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Visits;

[Authorize]
public class NewModel : PageModel
{
    private readonly VisitService _visitService;
    private readonly VisitTypeService _visitTypeService;
    private readonly UserManager<ApplicationUser> _userManager;

    public NewModel(VisitService visitService, VisitTypeService visitTypeService, UserManager<ApplicationUser> userManager)
    {
        _visitService = visitService;
        _visitTypeService = visitTypeService;
        _userManager = userManager;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IReadOnlyList<SelectListItem> VisitTypeOptions { get; private set; } = Array.Empty<SelectListItem>();

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

        if (!ModelState.IsValid)
        {
            await LoadVisitTypesAsync(cancellationToken);
            return Page();
        }

        var result = await _visitService.CreateAsync(Input.VisitTypeId!.Value, Input.DateOfVisit!.Value, Input.VisitorName, Input.Strength, Input.Remarks, _userManager.GetUserId(User) ?? string.Empty, cancellationToken);
        if (result.Outcome == VisitMutationOutcome.Success && result.Entity != null)
        {
            TempData["ToastMessage"] = "Visit created.";
            return RedirectToPage("Edit", new { id = result.Entity.Id });
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
