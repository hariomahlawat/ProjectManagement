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
public sealed class EditModel : PageModel
{
    private readonly IMiscActivityViewService _viewService;
    private readonly IMiscActivityService _activityService;

    public EditModel(
        IMiscActivityViewService viewService,
        IMiscActivityService activityService)
    {
        _viewService = viewService ?? throw new ArgumentNullException(nameof(viewService));
        _activityService = activityService ?? throw new ArgumentNullException(nameof(activityService));
    }

    [BindProperty]
    public MiscActivityFormViewModel Form { get; set; } = new();

    public Guid ActivityId { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var form = await _viewService.GetEditFormAsync(id, cancellationToken);
        if (form is null)
        {
            return NotFound();
        }

        ActivityId = id;
        Form = form;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        ActivityId = id;
        await PopulateActivityTypeOptionsAsync(cancellationToken);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (Form.OccurrenceDate is null)
        {
            ModelState.AddModelError(nameof(Form.OccurrenceDate), "Select the activity date.");
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Form.RowVersion))
        {
            ModelState.AddModelError(string.Empty, "The activity has changed. Please reload and try again.");
            return Page();
        }

        byte[] rowVersion;
        try
        {
            rowVersion = Convert.FromBase64String(Form.RowVersion);
        }
        catch (FormatException)
        {
            ModelState.AddModelError(string.Empty, "The activity has changed. Please reload and try again.");
            return Page();
        }

        var request = new MiscActivityUpdateRequest(
            Form.ActivityTypeId,
            Form.OccurrenceDate.Value,
            Form.Nomenclature,
            Form.Description,
            Form.ExternalLink,
            rowVersion);

        var result = await _activityService.UpdateAsync(id, request, cancellationToken);
        if (result.Outcome == MiscActivityMutationOutcome.Success && result.Entity is not null)
        {
            TempData["Flash"] = "Activity updated.";
            return RedirectToPage("Details", new { id });
        }

        switch (result.Outcome)
        {
            case MiscActivityMutationOutcome.NotFound:
            case MiscActivityMutationOutcome.Deleted:
                ModelState.AddModelError(string.Empty, "The activity could not be found or has been removed.");
                break;
            case MiscActivityMutationOutcome.ActivityTypeNotFound:
            case MiscActivityMutationOutcome.ActivityTypeInactive:
                ModelState.AddModelError(nameof(Form.ActivityTypeId), "Select an active activity type.");
                break;
            case MiscActivityMutationOutcome.Invalid when result.Errors.Count > 0:
                ModelState.AddModelError(string.Empty, result.Errors[0]);
                break;
            case MiscActivityMutationOutcome.ConcurrencyConflict:
                ModelState.AddModelError(string.Empty, "The activity was updated by someone else. Please reload and try again.");
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
                    ModelState.AddModelError(string.Empty, "Unable to update the activity.");
                }
                break;
        }

        return Page();
    }

    private async Task PopulateActivityTypeOptionsAsync(CancellationToken cancellationToken)
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
