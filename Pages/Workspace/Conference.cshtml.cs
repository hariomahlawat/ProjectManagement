using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Services.ActionTasks;
using ProjectManagement.Services.ConferenceRemarks;
using ProjectManagement.Services.Workspace;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Pages.Workspace;

[Authorize(Policy = Policies.ConferenceRemarks.Manage)]
public sealed class ConferenceModel : PageModel
{
    private readonly IOfficerConferenceReadService _readService;
    private readonly CommandWorkspaceService _commandWorkspaceService;
    private readonly IConferenceRemarkCommandService _remarkCommandService;
    private readonly IConferenceTaskCommandService _taskCommandService;
    private readonly IConferenceIdeaCommandService _ideaCommandService;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IActionTrackerClock _taskClock;
    private readonly ILogger<ConferenceModel> _logger;

    public ConferenceModel(
        IOfficerConferenceReadService readService,
        CommandWorkspaceService commandWorkspaceService,
        IConferenceRemarkCommandService remarkCommandService,
        IConferenceTaskCommandService taskCommandService,
        IConferenceIdeaCommandService ideaCommandService,
        UserManager<ApplicationUser> users,
        IActionTrackerClock taskClock,
        ILogger<ConferenceModel> logger)
    {
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _commandWorkspaceService = commandWorkspaceService ?? throw new ArgumentNullException(nameof(commandWorkspaceService));
        _remarkCommandService = remarkCommandService ?? throw new ArgumentNullException(nameof(remarkCommandService));
        _taskCommandService = taskCommandService ?? throw new ArgumentNullException(nameof(taskCommandService));
        _ideaCommandService = ideaCommandService ?? throw new ArgumentNullException(nameof(ideaCommandService));
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _taskClock = taskClock ?? throw new ArgumentNullException(nameof(taskClock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public OfficerConferenceVm Conference { get; private set; } = new();
    public CommandWorkspaceRailVm CommandRail { get; private set; } = new() { ActiveView = "conference" };
    public IReadOnlyList<OfficerConferenceOfficerOptionVm> OfficerOptions { get; private set; }
        = Array.Empty<OfficerConferenceOfficerOptionVm>();
    public bool HasSelectedOfficer => !string.IsNullOrWhiteSpace(Conference.OfficerUserId);
    public DateTime MinimumTaskDueDate => _taskClock.IstToday;
    public DateTime DefaultTaskDueDate => _taskClock.IstToday.AddDays(7);
    public ConferenceIdeaCreationOptionsVm IdeaCreation { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(
        string? officerUserId,
        CancellationToken cancellationToken)
    {
        var userId = _users.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        if (string.IsNullOrWhiteSpace(officerUserId))
        {
            OfficerOptions = await _readService.GetOfficerOptionsAsync(
                userId,
                selectedOfficerUserId: null,
                cancellationToken: cancellationToken);
        }
        else
        {
            var conference = await _readService.GetAsync(
                userId,
                officerUserId,
                cancellationToken);
            if (conference is null)
            {
                return NotFound();
            }

            Conference = conference;
            OfficerOptions = conference.OfficerOptions;
            IdeaCreation = await BuildIdeaCreationOptionsAsync(userId);
        }

        var navigation = await _commandWorkspaceService.GetNavigationShellAsync(
            "conference",
            cancellationToken);
        CommandRail = new CommandWorkspaceRailVm
        {
            CanSwitchWorkspace =
                (User.IsInRole(RoleNames.Comdt) || User.IsInRole(RoleNames.HoD))
                && User.IsInRole(RoleNames.ProjectOfficer),
            ActiveView = "conference",
            ProjectOfficerCount = OfficerOptions.Count > 0
                ? OfficerOptions.Count
                : navigation.ProjectOfficerCount,
            TotalOngoingProjects = navigation.TotalOngoingProjects
        };

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
            var result = await _remarkCommandService.AddAsync(
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
            return JsonError(StatusCodes.Status403Forbidden, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return JsonError(StatusCodes.Status400BadRequest, ex.Message);
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

            return JsonError(
                StatusCodes.Status500InternalServerError,
                "The direction could not be saved.",
                traceId);
        }
    }

    public async Task<IActionResult> OnPostCreateTaskAsync(
        string officerUserId,
        [FromForm] CreateConferenceTaskInput input,
        CancellationToken cancellationToken)
    {
        var userId = _users.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        if (!ModelState.IsValid || !input.DueDate.HasValue)
        {
            return new JsonResult(new
            {
                message = "Review the task details and correct the highlighted fields.",
                errors = GetModelErrors()
            })
            {
                StatusCode = StatusCodes.Status400BadRequest
            };
        }

        try
        {
            var result = await _taskCommandService.CreateAsync(
                userId,
                new CreateConferenceTaskRequest(
                    officerUserId,
                    input.Title,
                    input.Description,
                    input.DueDate.Value,
                    input.Priority),
                cancellationToken);

            return new JsonResult(new
            {
                saved = true,
                task = result.Task
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return JsonError(StatusCodes.Status403Forbidden, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return JsonError(StatusCodes.Status400BadRequest, ex.Message);
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
                "Conference task creation failed. TraceId={TraceId}, Officer={OfficerUserId}",
                traceId,
                officerUserId);

            return JsonError(
                StatusCodes.Status500InternalServerError,
                "The task could not be assigned.",
                traceId);
        }
    }

    public async Task<IActionResult> OnPostCreateIdeaAsync(
        string officerUserId,
        [FromForm] CreateConferenceIdeaInput input,
        CancellationToken cancellationToken)
    {
        var userId = _users.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        if (!ModelState.IsValid)
        {
            return new JsonResult(new
            {
                message = "Review the idea details and correct the highlighted fields.",
                errors = GetModelErrors()
            })
            {
                StatusCode = StatusCodes.Status400BadRequest
            };
        }

        try
        {
            var result = await _ideaCommandService.CreateAsync(
                userId,
                new CreateConferenceIdeaRequest(
                    officerUserId,
                    input.Title,
                    input.Description,
                    input.AssignedHodUserId),
                cancellationToken);

            return new JsonResult(new
            {
                saved = true,
                idea = result.Idea
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return JsonError(StatusCodes.Status403Forbidden, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return JsonError(StatusCodes.Status400BadRequest, ex.Message);
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
                "Conference idea creation failed. TraceId={TraceId}, Officer={OfficerUserId}",
                traceId,
                officerUserId);

            return JsonError(
                StatusCodes.Status500InternalServerError,
                "The idea could not be created.",
                traceId);
        }
    }

    private async Task<ConferenceIdeaCreationOptionsVm> BuildIdeaCreationOptionsAsync(string actorUserId)
    {
        var actor = await _users.FindByIdAsync(actorUserId);
        if (actor is null || actor.IsDisabled || actor.PendingDeletion)
        {
            return new ConferenceIdeaCreationOptionsVm
            {
                CanCreate = false,
                UnavailableReason = "The command user account is unavailable."
            };
        }

        var actorRoles = await _users.GetRolesAsync(actor);
        if (actorRoles.Contains(RoleNames.HoD, StringComparer.OrdinalIgnoreCase))
        {
            return new ConferenceIdeaCreationOptionsVm
            {
                CanCreate = true,
                RequiresHodSelection = false,
                FixedHodUserId = actor.Id,
                FixedHodDisplayName = DisplayUser(actor)
            };
        }

        if (!actorRoles.Contains(RoleNames.Comdt, StringComparer.OrdinalIgnoreCase))
        {
            return new ConferenceIdeaCreationOptionsVm
            {
                CanCreate = false,
                UnavailableReason = "Only Comdt or HoD can create ideas from this page."
            };
        }

        var hodUsers = (await _users.GetUsersInRoleAsync(RoleNames.HoD))
            .Where(IsActiveAssignableUser)
            .OrderBy(user => user.Rank, StringComparer.OrdinalIgnoreCase)
            .ThenBy(DisplayUser, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (hodUsers.Count == 0)
        {
            return new ConferenceIdeaCreationOptionsVm
            {
                CanCreate = false,
                RequiresHodSelection = true,
                UnavailableReason = "No active HoD account is available for idea oversight."
            };
        }

        return new ConferenceIdeaCreationOptionsVm
        {
            CanCreate = true,
            RequiresHodSelection = true,
            HodOptions = hodUsers
                .Select((user, index) => new ConferenceHodOptionVm(
                    user.Id,
                    DisplayUser(user),
                    hodUsers.Count == 1 && index == 0))
                .ToList()
        };
    }

    private bool IsActiveAssignableUser(ApplicationUser user)
        => !user.IsDisabled
            && !user.PendingDeletion
            && (!user.LockoutEnd.HasValue
                || user.LockoutEnd.Value.UtcDateTime <= _taskClock.UtcNow);

    private static string DisplayUser(ApplicationUser user)
    {
        var name = string.IsNullOrWhiteSpace(user.FullName)
            ? user.UserName ?? user.Email ?? user.Id
            : user.FullName.Trim();
        return string.IsNullOrWhiteSpace(user.Rank)
            ? name
            : $"{user.Rank.Trim()} {name}";
    }

    private Dictionary<string, string[]> GetModelErrors()
        => ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => entry.Key.Contains('.')
                    ? entry.Key[(entry.Key.LastIndexOf('.') + 1)..]
                    : entry.Key,
                entry => entry.Value!.Errors
                    .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                        ? "The value is invalid."
                        : error.ErrorMessage)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

    private JsonResult JsonError(int statusCode, string message, string? traceId = null)
        => new(new
        {
            message,
            traceId
        })
        {
            StatusCode = statusCode
        };
}
