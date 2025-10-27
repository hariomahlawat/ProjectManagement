using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Services.Activities;

namespace ProjectManagement.Areas.Admin.Pages.ActivityTypes;

[Authorize(Roles = "Admin,HoD")]
public sealed class CreateModel : PageModel
{
    private readonly IActivityTypeService _activityTypeService;

    public CreateModel(IActivityTypeService activityTypeService)
    {
        _activityTypeService = activityTypeService;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [TempData]
    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var request = new ActivityTypeInput(Input.Name, Input.Description, Input.IsActive);

        try
        {
            var created = await _activityTypeService.CreateAsync(request, cancellationToken);
            TempData["StatusMessage"] = $"Created '{created.Name}'.";
            return RedirectToPage("Index");
        }
        catch (ActivityValidationException ex)
        {
            AddErrors(ex);
            return Page();
        }
        catch (ActivityAuthorizationException)
        {
            ErrorMessage = "You are not authorised to manage activity types.";
            return RedirectToPage("Index");
        }
    }

    private void AddErrors(ActivityValidationException ex)
    {
        foreach (var (key, errors) in ex.Errors)
        {
            var modelKey = string.IsNullOrEmpty(key) ? string.Empty : $"Input.{key}";
            foreach (var error in errors)
            {
                ModelState.AddModelError(modelKey, error);
            }
        }
    }

    public sealed class InputModel
    {
        [Required]
        [StringLength(120)]
        public string Name { get; set; } = string.Empty;

        [StringLength(512)]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
