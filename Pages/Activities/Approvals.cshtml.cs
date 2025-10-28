using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Infrastructure.Ui;
using ProjectManagement.Services.Activities;

namespace ProjectManagement.Pages.Activities;

[Authorize(Roles = "Admin,HoD")]
[ValidateAntiForgeryToken]
public sealed class ApprovalsModel : PageModel
{
    private readonly IActivityDeleteRequestService _deleteRequestService;

    public ApprovalsModel(IActivityDeleteRequestService deleteRequestService)
    {
        _deleteRequestService = deleteRequestService ?? throw new ArgumentNullException(nameof(deleteRequestService));
    }

    public IReadOnlyList<ActivityDeleteRequestSummary> Pending { get; private set; } = Array.Empty<ActivityDeleteRequestSummary>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Pending = await _deleteRequestService.GetPendingAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostApproveAsync(int requestId, CancellationToken cancellationToken)
    {
        if (requestId <= 0)
        {
            TempData.ToastError("The delete request could not be identified.");
            return RedirectToPage();
        }

        try
        {
            await _deleteRequestService.ApproveAsync(requestId, cancellationToken);
            TempData.ToastSuccess("The activity was removed.");
        }
        catch (ActivityAuthorizationException)
        {
            TempData.ToastError("You are not authorised to approve activity delete requests.");
        }
        catch (InvalidOperationException)
        {
            TempData.ToastError("The delete request is no longer pending.");
        }
        catch (KeyNotFoundException)
        {
            TempData.ToastError("The delete request could not be found.");
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRejectAsync(int requestId, string? reason, CancellationToken cancellationToken)
    {
        if (requestId <= 0)
        {
            TempData.ToastError("The delete request could not be identified.");
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            TempData.ToastError("Provide a reason before rejecting the delete request.");
            return RedirectToPage();
        }

        try
        {
            await _deleteRequestService.RejectAsync(requestId, reason, cancellationToken);
            TempData.ToastInfo("The delete request was rejected.");
        }
        catch (ActivityAuthorizationException)
        {
            TempData.ToastError("You are not authorised to reject activity delete requests.");
        }
        catch (InvalidOperationException)
        {
            TempData.ToastError("The delete request is no longer pending.");
        }
        catch (KeyNotFoundException)
        {
            TempData.ToastError("The delete request could not be found.");
        }

        return RedirectToPage();
    }
}
