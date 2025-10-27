using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.MiscActivities.ViewModels;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.MiscActivities;

[Authorize(Policy = ProjectOfficeReportsPolicies.ManageMiscActivities)]
public sealed class CreateModel : PageModel
{
    private readonly IMiscActivityViewService _viewService;
    private readonly IMiscActivityService _activityService;

    public CreateModel(
        IMiscActivityViewService viewService,
        IMiscActivityService activityService)
    {
        _viewService = viewService ?? throw new ArgumentNullException(nameof(viewService));
        _activityService = activityService ?? throw new ArgumentNullException(nameof(activityService));
    }

    [BindProperty]
    public MiscActivityFormViewModel Form { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await PopulateFormAsync(cancellationToken);
        if (Form.OccurrenceDate is null)
        {
            Form.OccurrenceDate = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await PopulateFormAsync(cancellationToken);
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (Form.OccurrenceDate is null)
        {
            ModelState.AddModelError(nameof(Form.OccurrenceDate), "Select the activity date.");
            return Page();
        }

        var request = new MiscActivityCreateRequest(
            Form.ActivityTypeId,
            Form.OccurrenceDate.Value,
            Form.Nomenclature,
            Form.Description,
            Form.ExternalLink);

        var result = await _activityService.CreateAsync(request, cancellationToken);
        if (result.Outcome == MiscActivityMutationOutcome.Success && result.Entity is not null)
        {
            TempData["Flash"] = "Activity created.";
            return RedirectToPage("Details", new { id = result.Entity.Id });
        }

        switch (result.Outcome)
        {
            case MiscActivityMutationOutcome.ActivityTypeNotFound:
            case MiscActivityMutationOutcome.ActivityTypeInactive:
                ModelState.AddModelError(nameof(Form.ActivityTypeId), "Select an active activity type.");
                break;
            case MiscActivityMutationOutcome.Invalid when result.Errors.Count > 0:
                ModelState.AddModelError(string.Empty, result.Errors[0]);
                break;
            case MiscActivityMutationOutcome.Unauthorized:
                return Forbid();
            default:
                if (result.Errors.Count > 0)
                {
                    ModelState.AddModelError(string.Empty, result.Errors[0]);
                }
                else
                {
                    ModelState.AddModelError(string.Empty, "Unable to create the activity.");
                }
                break;
        }

        return Page();
    }

    private async Task PopulateFormAsync(CancellationToken cancellationToken)
    {
        var template = await _viewService.GetCreateFormAsync(cancellationToken);
        var options = template.ActivityTypeOptions;
        if (Form.ActivityTypeId.HasValue)
        {
            foreach (var option in options)
            {
                option.Selected = Guid.TryParse(option.Value, out var id) && id == Form.ActivityTypeId.Value;
            }
        }

        Form.ActivityTypeOptions = options;
    }
}
