using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Application.Training.Dtos;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Services;
using ProjectManagement.Infrastructure.Ui;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Training;

[Authorize(Policy = ProjectOfficeReportsPolicies.ApproveTrainingTracker)]
[ValidateAntiForgeryToken]
public class ApprovalsModel : PageModel
{
    private readonly TrainingTrackerReadService _readService;
    private readonly TrainingWriteService _writeService;
    private readonly IUserContext _userContext;

    public ApprovalsModel(
        TrainingTrackerReadService readService,
        TrainingWriteService writeService,
        IUserContext userContext)
    {
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _writeService = writeService ?? throw new ArgumentNullException(nameof(writeService));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
    }

    public IReadOnlyList<TrainingDeleteRequestDto> Pending { get; private set; } = Array.Empty<TrainingDeleteRequestDto>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Pending = await _readService.GetPendingDeleteRequestsAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostApproveAsync(Guid requestId, CancellationToken cancellationToken)
    {
        if (requestId == Guid.Empty)
        {
            TempData.ToastError("The delete request could not be identified.");
            return RedirectToPage();
        }

        var approverId = _userContext.UserId ?? string.Empty;
        var result = await _writeService.ApproveDeleteAsync(requestId, approverId, cancellationToken);

        if (!result.IsSuccess)
        {
            var message = result.FailureCode switch
            {
                TrainingDeleteFailureCode.RequestNotFound => "The delete request could not be found.",
                TrainingDeleteFailureCode.RequestNotPending => "The delete request is no longer pending.",
                TrainingDeleteFailureCode.ConcurrencyConflict => "The delete request was updated by another user. Reload and try again.",
                TrainingDeleteFailureCode.MissingUserId => "You are not signed in or your session has expired.",
                TrainingDeleteFailureCode.TrainingNotFound => "The associated training could not be found.",
                _ => result.ErrorMessage ?? "The delete request could not be approved."
            };

            TempData.ToastError(message);
        }
        else
        {
            TempData.ToastSuccess("The training was removed.");
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRejectAsync(Guid requestId, string reason, CancellationToken cancellationToken)
    {
        if (requestId == Guid.Empty)
        {
            TempData.ToastError("The delete request could not be identified.");
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            TempData.ToastError("Provide a reason before rejecting the delete request.");
            return RedirectToPage();
        }

        var approverId = _userContext.UserId ?? string.Empty;
        var result = await _writeService.RejectDeleteAsync(requestId, reason, approverId, cancellationToken);

        if (!result.IsSuccess)
        {
            var message = result.FailureCode switch
            {
                TrainingDeleteFailureCode.RequestNotFound => "The delete request could not be found.",
                TrainingDeleteFailureCode.RequestNotPending => "The delete request is no longer pending.",
                TrainingDeleteFailureCode.ConcurrencyConflict => "The delete request was updated by another user. Reload and try again.",
                TrainingDeleteFailureCode.MissingUserId => "You are not signed in or your session has expired.",
                TrainingDeleteFailureCode.InvalidReason => "Provide a reason before rejecting the delete request.",
                _ => result.ErrorMessage ?? "The delete request could not be rejected."
            };

            TempData.ToastError(message);
        }
        else
        {
            TempData.ToastInfo("The delete request was rejected.");
        }

        return RedirectToPage();
    }
}
