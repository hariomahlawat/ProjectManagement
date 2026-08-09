using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Services.Workspace;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Pages.Workspace;

[Authorize]
public class IndexModel : PageModel
{
    private readonly ProjectOfficerWorkspaceService _projectOfficerWorkspaceService;
    private readonly CommandWorkspaceService _commandWorkspaceService;
    private readonly IOfficerConferenceReadService _conferenceReadService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuthorizationService _authorization;
    private readonly ILogger<IndexModel> _logger;

    public ProjectOfficerWorkspaceVm Workspace { get; private set; } = new();
    public CommandWorkspaceVm CommandWorkspace { get; private set; } = new();
    public OfficerConferenceVm Conference { get; private set; } = new();
    public bool IsCommandMode { get; private set; }
    public bool CanSwitchWorkspace { get; private set; }
    public bool CanViewDocuments { get; private set; }

    [BindProperty(SupportsGet = true)] public string? Mode { get; set; }
    [BindProperty(SupportsGet = true)] public string View { get; set; } = "officers";
    [BindProperty(SupportsGet = true)] public List<int> ParentCategoryIds { get; set; } = new();
    [BindProperty(SupportsGet = true)] public string? ProjectSearch { get; set; }
    [BindProperty(SupportsGet = true)] public bool PopulatedStagesOnly { get; set; }
    [BindProperty(SupportsGet = true)] public int PatternDays { get; set; } = 7;
    [BindProperty(SupportsGet = true)] public string? PatternUserId { get; set; }
    [BindProperty(SupportsGet = true)] public string? PatternRole { get; set; }
    [BindProperty(SupportsGet = true)] public string? PatternModule { get; set; }
    [BindProperty(SupportsGet = true)] public string? PatternSignal { get; set; }
    [BindProperty(SupportsGet = true)] public string? ActivityPeriod { get; set; }

    public IndexModel(
        ProjectOfficerWorkspaceService projectOfficerWorkspaceService,
        CommandWorkspaceService commandWorkspaceService,
        IOfficerConferenceReadService conferenceReadService,
        UserManager<ApplicationUser> userManager,
        IAuthorizationService authorization,
        ILogger<IndexModel> logger)
    {
        _projectOfficerWorkspaceService = projectOfficerWorkspaceService;
        _commandWorkspaceService = commandWorkspaceService;
        _conferenceReadService = conferenceReadService;
        _userManager = userManager;
        _authorization = authorization;
        _logger = logger;
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var hasCommandRole = User.IsInRole(RoleNames.Comdt) || User.IsInRole(RoleNames.HoD);
        var hasProjectOfficerRole = User.IsInRole(RoleNames.ProjectOfficer);
        if (!hasCommandRole && !hasProjectOfficerRole) return RedirectToPage("/Dashboard/Index");

        CanSwitchWorkspace = hasCommandRole && hasProjectOfficerRole;
        CanViewDocuments = (await _authorization.AuthorizeAsync(User, "DocRepo.View")).Succeeded;
        IsCommandMode = hasCommandRole && (!string.Equals(Mode, "project-officer", StringComparison.OrdinalIgnoreCase) || !hasProjectOfficerRole);

        if (IsCommandMode)
        {
            View = View?.Trim().ToLowerInvariant() switch
            {
                "portfolio" => "portfolio",
                "adoption" => "adoption",
                "usage-pattern" => "usage-pattern",
                "pattern" => "usage-pattern",
                "my-activity" => "my-activity",
                "activity" => "my-activity",
                _ => "officers"
            };
            if (View == "portfolio" && !Request.Query.ContainsKey(nameof(PopulatedStagesOnly)))
            {
                PopulatedStagesOnly = true;
            }
            CommandWorkspace = await _commandWorkspaceService.GetAsync(new CommandWorkspaceQuery
            {
                View = View,
                ParentCategoryIds = ParentCategoryIds,
                ProjectSearch = ProjectSearch,
                PopulatedStagesOnly = PopulatedStagesOnly,
                PatternDays = PatternDays,
                PatternUserId = PatternUserId,
                PatternRole = PatternRole,
                PatternModule = PatternModule,
                PatternSignal = PatternSignal,
                RequestingUserId = userId
            }, ct);

            if (View == "my-activity")
            {
                Workspace = await _projectOfficerWorkspaceService.GetProjectOfficerWorkspaceAsync(
                    userId,
                    User,
                    ProjectOfficerWorkspaceView.Activity,
                    includeDocuments: false,
                    ct: ct,
                    activityPeriod: ActivityPeriod);
            }
        }
        else
        {
            var projectOfficerView = ProjectOfficerWorkspaceViewParser.Parse(View);
            View = projectOfficerView.ToRouteValue();
            if (projectOfficerView == ProjectOfficerWorkspaceView.Documents && !CanViewDocuments)
            {
                return Forbid();
            }

            Workspace = await _projectOfficerWorkspaceService.GetProjectOfficerWorkspaceAsync(
                userId,
                User,
                projectOfficerView,
                includeDocuments: CanViewDocuments && projectOfficerView == ProjectOfficerWorkspaceView.Documents,
                ct: ct,
                activityPeriod: projectOfficerView == ProjectOfficerWorkspaceView.Activity ? ActivityPeriod : null);

            if (projectOfficerView == ProjectOfficerWorkspaceView.Conference)
            {
                Conference = await _conferenceReadService.GetForProjectOfficerAsync(userId, ct)
                    ?? new OfficerConferenceVm
                    {
                        OfficerUserId = userId,
                        OfficerName = Workspace.UserDisplayName,
                        Sections = new[]
                        {
                            new OfficerConferenceSectionVm { Kind = ConferenceItemKind.Project, Title = "Projects", IconClass = "bi-kanban" },
                            new OfficerConferenceSectionVm { Kind = ConferenceItemKind.ProjectIdea, Title = "Ideas", IconClass = "bi-lightbulb" },
                            new OfficerConferenceSectionVm { Kind = ConferenceItemKind.ActionTask, Title = "Other tasks", IconClass = "bi-list-check" }
                        }
                    };
            }
        }

        return Page();
    }

    public async Task<IActionResult> OnGetDirectionHistoryAsync(
        ConferenceItemKind kind,
        int itemId,
        CancellationToken ct)
    {
        if (!User.IsInRole(RoleNames.ProjectOfficer))
        {
            return Forbid();
        }

        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        if (itemId <= 0 || !Enum.IsDefined(kind))
        {
            return new JsonResult(new { message = "The direction-history request is invalid." })
            {
                StatusCode = StatusCodes.Status400BadRequest
            };
        }

        Response.Headers.CacheControl = "no-store, no-cache";
        Response.Headers.Pragma = "no-cache";

        try
        {
            var history = await _conferenceReadService.GetDirectionHistoryForProjectOfficerAsync(
                userId,
                kind,
                itemId,
                ct);

            if (history is null)
            {
                return new JsonResult(new { message = "Direction history is unavailable for this item." })
                {
                    StatusCode = StatusCodes.Status404NotFound
                };
            }

            return new JsonResult(history);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var traceId = HttpContext.TraceIdentifier;
            _logger.LogError(
                ex,
                "Project Officer conference direction history failed. TraceId={TraceId}, UserId={UserId}, Kind={Kind}, ItemId={ItemId}",
                traceId,
                userId,
                kind,
                itemId);

            return new JsonResult(new
            {
                message = "Direction history could not be loaded.",
                traceId
            })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }

    public async Task<IActionResult> OnPostSaveOfficerOrderAsync([FromBody] SaveOfficerOrderRequest request, CancellationToken ct)
    {
        if (!User.IsInRole(RoleNames.Comdt) && !User.IsInRole(RoleNames.HoD)) return Forbid();
        if (request.OfficerUserIds is null || request.OfficerUserIds.Count > 250) return BadRequest();

        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var validOfficerIds = (await _userManager.GetUsersInRoleAsync(RoleNames.ProjectOfficer))
            .Where(x => !x.IsDisabled && !x.PendingDeletion)
            .Select(x => x.Id)
            .ToHashSet(StringComparer.Ordinal);

        var normalized = request.OfficerUserIds
            .Where(id => !string.IsNullOrWhiteSpace(id) && validOfficerIds.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var currentUser = await _userManager.FindByIdAsync(userId);
        if (currentUser is null) return Challenge();
        currentUser.ComdtOfficerWorkloadOrderJson = JsonSerializer.Serialize(normalized);
        var result = await _userManager.UpdateAsync(currentUser);
        if (!result.Succeeded)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "The officer order could not be saved." });
        }

        return new JsonResult(new { saved = true });
    }

    public sealed class SaveOfficerOrderRequest
    {
        public List<string> OfficerUserIds { get; init; } = new();
    }

}
