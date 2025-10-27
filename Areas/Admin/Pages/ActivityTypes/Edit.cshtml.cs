using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Models.Activities;
using ProjectManagement.Services.Activities;

namespace ProjectManagement.Areas.Admin.Pages.ActivityTypes;

[Authorize(Roles = "Admin,HoD")]
public sealed class EditModel : PageModel
{
    private readonly IActivityTypeService _activityTypeService;

    public EditModel(IActivityTypeService activityTypeService)
    {
        _activityTypeService = activityTypeService;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [TempData]
    public string? ErrorMessage { get; set; }

    public int ActivityTypeId { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var type = await FindActivityTypeAsync(id, cancellationToken);
        if (type is null)
        {
            return NotFound();
        }

        ActivityTypeId = type.Id;
        Input = new InputModel
        {
            Name = type.Name,
            Description = type.Description,
            IsActive = type.IsActive
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        ActivityTypeId = id;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var request = new ActivityTypeInput(Input.Name, Input.Description, Input.IsActive);

        try
        {
            var updated = await _activityTypeService.UpdateAsync(id, request, cancellationToken);
            TempData["StatusMessage"] = $"Updated '{updated.Name}'.";
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
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    private async Task<ActivityType?> FindActivityTypeAsync(int id, CancellationToken cancellationToken)
    {
        var types = await _activityTypeService.ListAsync(cancellationToken);
        return types.FirstOrDefault(t => t.Id == id);
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

        public bool IsActive { get; set; }
    }
}
