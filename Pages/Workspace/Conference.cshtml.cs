using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Services.ConferenceRemarks;
using ProjectManagement.Services.Workspace;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Pages.Workspace;

[Authorize(Policy = Policies.ConferenceRemarks.Manage)]
public sealed class ConferenceModel : PageModel
{
    private readonly IOfficerConferenceReadService _readService;
    private readonly IConferenceRemarkCommandService _commandService;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILogger<ConferenceModel> _logger;

    public ConferenceModel(
        IOfficerConferenceReadService readService,
        IConferenceRemarkCommandService commandService,
        UserManager<ApplicationUser> users,
        ILogger<ConferenceModel> logger)
    {
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public OfficerConferenceVm Conference { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(
        string officerUserId,
        CancellationToken cancellationToken)
    {
        var userId = _users.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var conference = await _readService.GetAsync(
            userId,
            officerUserId,
            cancellationToken);
        if (conference is null)
        {
            return NotFound();
        }

        Conference = conference;
        return Page();
    }

    public async Task<IActionResult> OnPostAddAsync(
        [FromForm] AddConferenceDirectionInput input,
        CancellationToken cancellationToken)
    {
        var userId = _users.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        if (!ModelState.IsValid || !Enum.IsDefined(input.Kind))
        {
            return BadRequest(new
            {
                message = "The conference direction request is invalid."
            });
        }

        try
        {
            var result = await _commandService.AddAsync(
                userId,
                new AddConferenceRemarkRequest(
                    input.OfficerUserId,
                    input.Kind,
                    input.ItemId,
                    input.Body),
                cancellationToken);

            return new JsonResult(new
            {
                saved = true,
                direction = result.Direction,
                progressEntries = result.ProgressEntries,
                emptyProgressText = result.EmptyProgressText,
                progressSummary = result.ProgressSummary,
                latestProgressText = result.LatestProgressText
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return new JsonResult(new { message = ex.Message })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
        catch (InvalidOperationException ex)
        {
            return new JsonResult(new { message = ex.Message })
            {
                StatusCode = StatusCodes.Status400BadRequest
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var traceId = HttpContext.TraceIdentifier;
            _logger.LogError(
                ex,
                "Conference direction save failed. TraceId={TraceId}, Officer={OfficerUserId}, Kind={Kind}, ItemId={ItemId}",
                traceId,
                input.OfficerUserId,
                input.Kind,
                input.ItemId);

            return new JsonResult(new
            {
                message = "The direction could not be saved.",
                traceId
            })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }
}
