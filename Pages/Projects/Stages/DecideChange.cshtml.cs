using System;
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

[Authorize(Roles = "HoD")]
[AutoValidateAntiforgeryToken]
public class DecideChangeModel : PageModel
{
    private readonly StageDecisionService _stageDecisionService;
    private readonly IUserContext _userContext;

    public DecideChangeModel(StageDecisionService stageDecisionService, IUserContext userContext)
    {
        _stageDecisionService = stageDecisionService;
        _userContext = userContext;
    }

    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync([FromBody] StageDecisionRequest input, CancellationToken cancellationToken)
    {
        if (input is null)
        {
            return BadRequest(new { ok = false, error = "Request body is required." });
        }

        if (!StageDecisionRequest.TryParseDecision(input.Decision, out var action))
        {
            return BadRequest(new { ok = false, error = "Decision must be Approve or Reject." });
        }

        var userId = _userContext.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            return Forbid();
        }

        var serviceInput = new StageDecisionInput(input.RequestId, action, input.DecisionNote);
        var result = await _stageDecisionService.DecideAsync(serviceInput, userId, cancellationToken);

        return result.Outcome switch
        {
            StageDecisionOutcome.Success => HttpContext.SetSuccess(),
            StageDecisionOutcome.NotHeadOfDepartment => Forbid(),
            StageDecisionOutcome.RequestNotFound => HttpContext.SetStatusCode(
                StatusCodes.Status404NotFound,
                new { ok = false, error = "Request not found." }),
            StageDecisionOutcome.StageNotFound => HttpContext.SetStatusCode(
                StatusCodes.Status404NotFound,
                new { ok = false, error = "Stage not found." }),
            StageDecisionOutcome.AlreadyDecided => HttpContext.SetStatusCode(
                StatusCodes.Status409Conflict,
                new { ok = false, error = "This request has already been decided." }),
            StageDecisionOutcome.ValidationFailed => HttpContext.SetStatusCode(
                StatusCodes.Status422UnprocessableEntity,
                new { ok = false, error = result.Error }),
            _ => HttpContext.SetInternalServerError()
        };
    }
}

public sealed record StageDecisionRequest
{
    public int RequestId { get; init; }
    public string Decision { get; init; } = string.Empty;
    public string? DecisionNote { get; init; }

    public static bool TryParseDecision(string? value, out StageDecisionAction action)
    {
        if (string.Equals(value, "Approve", StringComparison.OrdinalIgnoreCase))
        {
            action = StageDecisionAction.Approve;
            return true;
        }

        if (string.Equals(value, "Reject", StringComparison.OrdinalIgnoreCase))
        {
            action = StageDecisionAction.Reject;
            return true;
        }

        action = default;
        return false;
    }
}
