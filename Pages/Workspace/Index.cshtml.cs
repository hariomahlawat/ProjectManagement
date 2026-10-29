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
    private readonly BriefingDeskCommandService _deskCommands;
    private readonly ILogger<IndexModel> _logger;

    public const string DeskDraftAnchorKey = "BriefingDesk.DraftAnchor";
    public const string DeskDraftTextKey = "BriefingDesk.DraftText";
    public const string DeskDraftFieldKey = "BriefingDesk.DraftField";

    // A desk post sent by the desk's script (not a plain form post) carries this header. It is
    // answered with the re-rendered desk, or a JSON message when refused, instead of a redirect.
    public const string DeskFetchHeader = "X-Briefing-Desk";
    public const string DeskFetchValue = "fetch";
    public const string DeskFragmentHeader = "X-Desk-Fragment";
    public const string DeskMessageHeader = "X-Desk-Message";
    public const string DeskItemReadyHeader = "X-Desk-Item-Ready";

    public ProjectOfficerWorkspaceVm Workspace { get; private set; } = new();
    public CommandWorkspaceVm CommandWorkspace { get; private set; } = new();
    public OfficerConferenceVm Conference { get; private set; } = new();
    public bool IsCommandMode { get; private set; }
    public CommandWorkspaceRailVm NavigationRail { get; private set; } = new();
    public bool CanViewDocuments { get; private set; }

    [BindProperty(SupportsGet = true)] public string? Mode { get; set; }
    [BindProperty(SupportsGet = true)] public string View { get; set; } = "officers";
    [BindProperty(SupportsGet = true)] public List<int> ParentCategoryIds { get; set; } = new();
    [BindProperty(SupportsGet = true)] public int PatternDays { get; set; } = 7;
    [BindProperty(SupportsGet = true)] public string? PatternUserId { get; set; }
    [BindProperty(SupportsGet = true)] public string? PatternRole { get; set; }
    [BindProperty(SupportsGet = true)] public string? PatternModule { get; set; }
    [BindProperty(SupportsGet = true)] public string? PatternSignal { get; set; }
    [BindProperty(SupportsGet = true)] public string? ActivityPeriod { get; set; }
    [BindProperty(SupportsGet = true)] public string? Focus { get; set; }

    public IndexModel(
        ProjectOfficerWorkspaceService projectOfficerWorkspaceService,
        CommandWorkspaceService commandWorkspaceService,
        IOfficerConferenceReadService conferenceReadService,
        UserManager<ApplicationUser> userManager,
        IAuthorizationService authorization,
        BriefingDeskCommandService deskCommands,
        ILogger<IndexModel> logger)
    {
        _projectOfficerWorkspaceService = projectOfficerWorkspaceService;
        _commandWorkspaceService = commandWorkspaceService;
        _conferenceReadService = conferenceReadService;
        _userManager = userManager;
        _authorization = authorization;
        _deskCommands = deskCommands;
        _logger = logger;
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var hasCommandRole = User.IsInRole(RoleNames.Comdt) || User.IsInRole(RoleNames.HoD);
        var hasProjectOfficerRole = User.IsInRole(RoleNames.ProjectOfficer);
        if (!hasCommandRole && !hasProjectOfficerRole) return RedirectToPage("/Dashboard/Index");

        CanViewDocuments = (await _authorization.AuthorizeAsync(User, "DocRepo.View")).Succeeded;
        IsCommandMode = hasCommandRole && (!string.Equals(Mode, "project-officer", StringComparison.OrdinalIgnoreCase) || !hasProjectOfficerRole);

        // The HoD's and the Comdt's workspace opens on the Command desk (owner decision,
        // 23 Oct 2026). A link that asks for a view (Officer workload, ERP adoption, …) still
        // opens it.
        if (IsCommandMode && !Request.Query.ContainsKey("view"))
        {
            return RedirectToPage("/Workspace/CommandDesk");
        }

        // Project portfolio was replaced by Project analytics (27 Oct 2026): the same stage
        // chart, fuller, on the Analytics Ongoing tab. Old links land there with their categories.
        if (IsCommandMode && string.Equals(View?.Trim(), "portfolio", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToPage("/Analytics/Index", new { ongoingStageParentCategoryIds = ParentCategoryIds });
        }

        if (IsCommandMode)
        {
            View = View?.Trim().ToLowerInvariant() switch
            {
                "adoption" => "adoption",
                "usage-pattern" => "usage-pattern",
                "pattern" => "usage-pattern",
                "my-activity" => "my-activity",
                "activity" => "my-activity",
                _ => "officers"
            };
            CommandWorkspace = await _commandWorkspaceService.GetAsync(new CommandWorkspaceQuery
            {
                View = View,
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
            else if (hasProjectOfficerRole)
            {
                // Keep the personal navigation shell available while command content is open.
                // This avoids making dual-role users switch context just to discover their work.
                Workspace = await _projectOfficerWorkspaceService.GetProjectOfficerWorkspaceAsync(
                    userId,
                    User,
                    ProjectOfficerWorkspaceView.Conference,
                    includeDocuments: false,
                    ct: ct);
            }
        }
        else
        {
            var projectOfficerView = ProjectOfficerWorkspaceViewParser.Parse(View);
            if (projectOfficerView.IsFoldedIntoDesk(out var deskSection))
            {
                return RedirectToPage("/Workspace/Index", null, new { mode = "project-officer", view = "overview" }, deskSection);
            }

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

            if (hasCommandRole)
            {
                CommandWorkspace = await _commandWorkspaceService.GetNavigationShellAsync(
                    activeView: "officers",
                    cancellationToken: ct);
            }
        }

        NavigationRail = BuildNavigationRail(
            hasCommandRole,
            hasProjectOfficerRole,
            CommandWorkspace,
            Workspace);

        return Page();
    }

    private CommandWorkspaceRailVm BuildNavigationRail(
        bool hasCommandAccess,
        bool hasProjectOfficerAccess,
        CommandWorkspaceVm command,
        ProjectOfficerWorkspaceVm personal)
    {
        var activeItem = IsCommandMode
            ? View switch
            {
                "adoption" => "command-adoption",
                "usage-pattern" => "command-usage-pattern",
                "my-activity" => "my-activity",
                _ => "command-officers"
            }
            : View switch
            {
                "actions" => "personal-actions",
                "conference" => "personal-conference",
                "projects" => "personal-projects",
                "tasks" => "personal-tasks",
                "ideas" => "personal-ideas",
                "follow-ups" => "personal-follow-ups",
                "documents" => "personal-documents",
                "activity" => "my-activity",
                _ => "personal-overview"
            };

        return new CommandWorkspaceRailVm
        {
            HasCommandAccess = hasCommandAccess,
            HasProjectOfficerAccess = hasProjectOfficerAccess,
            CanViewDocuments = hasProjectOfficerAccess && CanViewDocuments,
            ActiveLens = IsCommandMode ? "command" : "personal",
            ActiveItem = activeItem,
            ProjectOfficerCount = command.ProjectOfficerCount,
            TotalOngoingProjects = command.TotalOngoingProjects,
            AssignedProjectCount = personal.AssignedProjectCount,
            AssignedTaskCount = personal.OfficialTaskCount,
            AssignedIdeaCount = personal.AssignedIdeaCount,
            FollowUpCount = personal.FollowUpCount,
            AotsUnreadCount = personal.AotsUnreadCount
        };
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

    // SECTION: Briefing desk commands
    // Each post returns to the desk with the same card open; failures keep the typed text.
    // Posted by the desk's script, the reply is the re-rendered desk instead, so the page does
    // not reload; a plain form post (no script) keeps the redirect.
    public Task<IActionResult> OnPostDeskProjectUpdateAsync(int itemId, string? body, CancellationToken ct)
        => RunDeskCommandAsync(ConferenceItemKind.Project, itemId, body, BriefingDeskDraftFields.Body,
            userId => _deskCommands.PostProjectUpdateAsync(User, userId, itemId, body, ct), ct);

    public Task<IActionResult> OnPostDeskIdeaNoteAsync(int itemId, string? body, CancellationToken ct)
        => RunDeskCommandAsync(ConferenceItemKind.ProjectIdea, itemId, body, BriefingDeskDraftFields.Body,
            userId => _deskCommands.PostIdeaNoteAsync(User, userId, itemId, body, ct), ct);

    public Task<IActionResult> OnPostDeskTaskProgressAsync(int itemId, string? body, CancellationToken ct)
        => RunDeskCommandAsync(ConferenceItemKind.ActionTask, itemId, body, BriefingDeskDraftFields.Body,
            userId => _deskCommands.PostTaskProgressAsync(User, userId, itemId, body, ct), ct);

    public Task<IActionResult> OnPostDeskTaskSubmitAsync(int itemId, string? rowVersion, string? note, CancellationToken ct)
        => RunDeskCommandAsync(ConferenceItemKind.ActionTask, itemId, note, BriefingDeskDraftFields.Submit,
            userId => _deskCommands.SubmitTaskAsync(User, userId, itemId, rowVersion, note, ct), ct);

    public Task<IActionResult> OnPostDeskTaskBlockedAsync(int itemId, string? rowVersion, string? note, CancellationToken ct)
        => RunDeskCommandAsync(ConferenceItemKind.ActionTask, itemId, note, BriefingDeskDraftFields.Blocked,
            userId => _deskCommands.FlagTaskBlockedAsync(User, userId, itemId, rowVersion, note, ct), ct);

    private async Task<IActionResult> RunDeskCommandAsync(
        ConferenceItemKind kind,
        int itemId,
        string? draft,
        string draftField,
        Func<string, Task<BriefingDeskCommandResult>> command,
        CancellationToken ct = default)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var isFetch = IsDeskFetch();
        var result = await command(userId);
        switch (result.Outcome)
        {
            case BriefingDeskCommandOutcome.Forbidden:
                return isFetch ? DeskMessage(StatusCodes.Status403Forbidden, result.Message) : Forbid();
            case BriefingDeskCommandOutcome.NotFound:
                return isFetch ? DeskMessage(StatusCodes.Status404NotFound, result.Message) : NotFound();
        }

        var anchor = $"desk-{BriefingDeskLabels.KindKey(kind)}-{itemId}";
        if (isFetch)
        {
            // Refused: the typed text is still in the box on the page, so only the reason goes back.
            return result.Succeeded
                ? await DeskFragmentAsync(userId, anchor, result.Message, ct)
                : DeskMessage(StatusCodes.Status422UnprocessableEntity, result.Message);
        }

        if (result.Succeeded)
        {
            TempData["ToastSuccess"] = result.Message;
            TempData.Remove(DeskDraftAnchorKey);
            TempData.Remove(DeskDraftTextKey);
            TempData.Remove(DeskDraftFieldKey);
        }
        else
        {
            TempData["ToastError"] = result.Message;
            if (!string.IsNullOrWhiteSpace(draft))
            {
                // The text goes back into the box it came from, including a closure note or a
                // blocker refused because the task changed in the meantime.
                TempData[DeskDraftAnchorKey] = anchor;
                TempData[DeskDraftFieldKey] = draftField;
                TempData[DeskDraftTextKey] = draft.Length > BriefingDeskCommandService.MaxBodyLength
                    ? draft[..BriefingDeskCommandService.MaxBodyLength]
                    : draft;
            }
        }

        return LocalRedirect(WorkspaceRouteHelper.BriefingDeskItem(anchor));
    }

    private bool IsDeskFetch()
        => string.Equals(Request.Headers[DeskFetchHeader], DeskFetchValue, StringComparison.Ordinal);

    private static JsonResult DeskMessage(int statusCode, string message)
        => new(new { message }) { StatusCode = statusCode };

    // The desk is read again after the post and rendered by the same partial as the page, so
    // the readiness rules stay in BriefingDeskBuilder and are never repeated in the script.
    private async Task<IActionResult> DeskFragmentAsync(string userId, string anchor, string message, CancellationToken ct)
    {
        CanViewDocuments = (await _authorization.AuthorizeAsync(User, "DocRepo.View")).Succeeded;
        View = ProjectOfficerWorkspaceView.Overview.ToRouteValue();
        Workspace = await _projectOfficerWorkspaceService.GetProjectOfficerWorkspaceAsync(
            userId,
            User,
            ProjectOfficerWorkspaceView.Overview,
            includeDocuments: false,
            ct: ct);

        var desk = Workspace.BriefingDesk;
        var item = desk.Sections
            .SelectMany(section => section.Items)
            .Concat(desk.RecentlyCompleted)
            .FirstOrDefault(candidate => candidate.AnchorId == anchor);
        // A card that still needs work stays open; one now ready folds away and the next opens.
        var ready = item?.IsBriefReady ?? false;
        Focus = ready ? null : anchor;

        Response.Headers[DeskFragmentHeader] = "1";
        Response.Headers[DeskMessageHeader] = Uri.EscapeDataString(message);
        Response.Headers[DeskItemReadyHeader] = ready ? "true" : "false";
        Response.Headers.CacheControl = "no-store";
        return Partial("_ProjectOfficerBriefingDesk", this);
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
