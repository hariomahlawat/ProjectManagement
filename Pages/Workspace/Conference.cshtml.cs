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

    public ConferenceModel(
        IOfficerConferenceReadService readService,
        IConferenceRemarkCommandService commandService,
        UserManager<ApplicationUser> users)
    {
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));
        _users = users ?? throw new ArgumentNullException(nameof(users));
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
        [FromForm] string officerUserId,
        [FromForm] ConferenceItemKind kind,
        [FromForm] int itemId,
        [FromForm] string body,
        CancellationToken cancellationToken)
    {
        var userId = _users.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            var result = await _commandService.AddAsync(
                userId,
                new AddConferenceRemarkRequest(officerUserId, kind, itemId, body),
                cancellationToken);

            return new JsonResult(new
            {
                saved = true,
                direction = result.Direction,
                progressSummary = result.ProgressSummary,
                latestProgressText = result.LatestProgressText
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
