using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Contracts.Stages;
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

    public async Task<IActionResult> OnPostAsync([FromBody] BatchStageChangeRequestInput input, CancellationToken cancellationToken)
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

        var result = await _stageRequestService.CreateBatchAsync(input, userId, cancellationToken);

        return result.Outcome switch
        {
            BatchStageRequestOutcome.Success => HttpContext.SetSuccess(new
            {
                ok = true,
                items = result.Items
                    .Select(item => new
                    {
                        stageCode = item.StageCode,
                        id = item.Result.RequestId
                    })
                    .ToArray()
            }),
            BatchStageRequestOutcome.NotProjectOfficer => Forbid(),
            BatchStageRequestOutcome.StageNotFound => HttpContext.SetStatusCode(
                StatusCodes.Status404NotFound,
                new { ok = false, error = "Stage not found." }),
            BatchStageRequestOutcome.ValidationFailed => HttpContext.SetStatusCode(
                StatusCodes.Status422UnprocessableEntity,
                new
                {
                    ok = false,
                    error = "validation",
                    details = result.Items
                        .Select(item => new
                        {
                            stageCode = item.StageCode,
                            errors = item.Result.Errors is { Count: > 0 }
                                ? item.Result.Errors.ToArray()
                                : Array.Empty<string>(),
                            missingPredecessors = item.Result.MissingPredecessors is { Count: > 0 }
                                ? item.Result.MissingPredecessors.ToArray()
                                : Array.Empty<string>()
                        })
                        .ToArray(),
                    errors = result.Errors.ToArray()
                }),
            _ => HttpContext.SetInternalServerError()
        };
    }
}
