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

        var result = await _stageRequestService.RequestChangeAsync(input, userId, cancellationToken);

        return result.Outcome switch
        {
            StageRequestOutcome.Success => HttpContext.SetSuccess(),
            StageRequestOutcome.NotProjectOfficer => Forbid(),
            StageRequestOutcome.StageNotFound => HttpContext.SetStatusCode(
                StatusCodes.Status404NotFound,
                new { ok = false, error = "Stage not found." }),
            StageRequestOutcome.DuplicatePending => HttpContext.SetStatusCode(
                StatusCodes.Status409Conflict,
                new { ok = false, error = result.Error }),
            StageRequestOutcome.ValidationFailed => HttpContext.SetStatusCode(
                StatusCodes.Status422UnprocessableEntity,
                new { ok = false, error = result.Error }),
            _ => HttpContext.SetInternalServerError()
        };
    }
}
