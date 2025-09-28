using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Helpers;
using ProjectManagement.Services;
using ProjectManagement.Services.Stages;

namespace ProjectManagement.Pages.Projects.Stages;

[Authorize(Roles = "Project Officer")]
[AutoValidateAntiforgeryToken]
public class RequestChangeModel : PageModel
{
    private readonly StageRequestService _stageRequestService;
    private readonly IUserContext _userContext;

    public RequestChangeModel(StageRequestService stageRequestService, IUserContext userContext)
    {
        _stageRequestService = stageRequestService;
        _userContext = userContext;
    }

    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync([FromBody] StageChangeRequestInput input, CancellationToken cancellationToken)
    {
        if (input is null)
        {
            return BadRequest(new { ok = false, error = "Request body is required." });
        }

        var userId = _userContext.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            return Forbid();
        }

        var result = await _stageRequestService.CreateAsync(input, userId, cancellationToken);

        return result.Outcome switch
        {
            StageRequestOutcome.Success when result.RequestId is int requestId => HttpContext.SetSuccess(new { ok = true, id = requestId }),
            StageRequestOutcome.NotProjectOfficer => Forbid(),
            StageRequestOutcome.StageNotFound => HttpContext.SetStatusCode(
                StatusCodes.Status404NotFound,
                new { ok = false, error = "Stage not found." }),
            StageRequestOutcome.DuplicatePending => HttpContext.SetStatusCode(
                StatusCodes.Status409Conflict,
                new { ok = false, error = "duplicate" }),
            StageRequestOutcome.ValidationFailed => HttpContext.SetStatusCode(
                StatusCodes.Status422UnprocessableEntity,
                CreateValidationError(result.Details, result.MissingPredecessors)),
            _ => HttpContext.SetInternalServerError()
        };
    }

    private static object CreateValidationError(
        IReadOnlyList<string>? details,
        IReadOnlyList<string>? missingPredecessors)
    {
        var detailArray = details is { Count: > 0 }
            ? details.ToArray()
            : Array.Empty<string>();

        var missingArray = missingPredecessors is { Count: > 0 }
            ? missingPredecessors.ToArray()
            : Array.Empty<string>();

        return new
        {
            ok = false,
            error = "validation",
            details = detailArray,
            missingPredecessors = missingArray
        };
    }
}
