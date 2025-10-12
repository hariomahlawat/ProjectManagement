using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.VisitTypes;

[Authorize(Roles = "Admin")]
public class NewModel : PageModel
{
    private readonly VisitTypeService _service;
    private readonly UserManager<ApplicationUser> _userManager;

    public NewModel(VisitTypeService service, UserManager<ApplicationUser> userManager)
    {
        _service = service;
        _userManager = userManager;
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
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _service.CreateAsync(Input.Name, Input.Description, _userManager.GetUserId(User) ?? string.Empty, cancellationToken);
        if (result.Outcome == VisitTypeMutationOutcome.Success)
        {
            TempData["ToastMessage"] = "Visit type created.";
            return RedirectToPage("Index");
        }

        if (result.Outcome == VisitTypeMutationOutcome.DuplicateName)
        {
            ModelState.AddModelError(nameof(Input.Name), "A visit type with this name already exists.");
        }
        else if (result.Errors.Count > 0)
        {
            ModelState.AddModelError(string.Empty, result.Errors[0]);
        }
        else
        {
            ModelState.AddModelError(string.Empty, "Unable to create visit type.");
        }

        return Page();
    }
}
