using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Admin.ActivityTypes;

[Authorize(Policy = ProjectOfficeReportsPolicies.ManageActivityTypes)]
public class IndexModel : PageModel
{
    private readonly IActivityTypeService _service;

    public IndexModel(IActivityTypeService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public IReadOnlyList<ActivityTypeSummary> Items { get; private set; } = Array.Empty<ActivityTypeSummary>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Items = await _service.GetSummariesAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, string rowVersion, CancellationToken cancellationToken)
    {
        var bytes = DecodeRowVersion(rowVersion);
        if (bytes is null)
        {
            TempData["ToastError"] = "Unable to process the request. Please try again.";
            return RedirectToPage();
        }

        var result = await _service.DeleteAsync(id, bytes, cancellationToken);
        switch (result.Outcome)
        {
            case ActivityTypeDeletionOutcome.Success:
                TempData["ToastMessage"] = "Activity type deleted.";
                break;
            case ActivityTypeDeletionOutcome.InUse:
                TempData["ToastError"] = result.Errors.Count > 0
                    ? result.Errors[0]
                    : "Cannot delete the activity type because it is referenced by existing activities.";
                break;
            case ActivityTypeDeletionOutcome.ConcurrencyConflict:
                TempData["ToastError"] = result.Errors.Count > 0
                    ? result.Errors[0]
                    : "The activity type was modified by another user. Please reload and try again.";
                break;
            case ActivityTypeDeletionOutcome.NotFound:
                TempData["ToastError"] = "Activity type not found.";
                break;
            default:
                TempData["ToastError"] = "Unable to delete the activity type.";
                break;
        }

        return RedirectToPage();
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
