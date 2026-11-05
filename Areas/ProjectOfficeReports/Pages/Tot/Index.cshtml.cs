using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Services;
using ProjectManagement.Services.Remarks;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Tot;

/// <summary>
/// The ToT register. Rows open the shared ToT drawer (<see cref="ProjectTotDrawerService"/>) with
/// the register's access rule: Project Office, HoD and Admin for any project, a Project Officer
/// for projects they lead; HoD and Admin save directly and decide requests, others submit for
/// approval.
/// </summary>
[Authorize(Policy = ProjectOfficeReportsPolicies.ViewTotTracker)]
public sealed class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ProjectTotTrackerReadService _trackerService;
    private readonly IProjectTotExportService _exportService;
    private readonly IAuthorizationService _authorizationService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IClock _clock;
    private readonly IRemarkService _remarkService;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        ApplicationDbContext db,
        ProjectTotTrackerReadService trackerService,
        IProjectTotExportService exportService,
        IAuthorizationService authorizationService,
        UserManager<ApplicationUser> userManager,
        IClock clock,
        IRemarkService remarkService,
        ILogger<IndexModel> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _trackerService = trackerService ?? throw new ArgumentNullException(nameof(trackerService));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _remarkService = remarkService ?? throw new ArgumentNullException(nameof(remarkService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty(SupportsGet = true)]
    public ProjectTotStatus? TotStatusFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public ProjectTotRequestDecisionState? RequestStateFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool OnlyPending { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool RequiresTotOnly { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool MetCompletedOnly { get; set; }

    // Inclusive window on the ToT completion date (yyyy-MM-dd); set by Command desk drill-downs.
    [BindProperty(SupportsGet = true)]
    public DateOnly? CompletedFrom { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? CompletedTo { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? SelectedProjectId { get; set; }

    /// <summary>Register order: name (default), status, tot (ToT completion), project (project completion).</summary>
    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    /// <summary>asc (default) or desc.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Dir { get; set; }

    /// <summary>Only Not started projects completed more than five years ago (the role section's "See all").</summary>
    [BindProperty(SupportsGet = true)]
    public bool LongWaiting { get; set; }

    [BindProperty(SupportsGet = true)]
    public ViewModeOption ViewMode { get; set; } = ViewModeOption.Cards;

    private string? _searchTerm;

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm
    {
        get => _searchTerm;
        set => _searchTerm = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public IReadOnlyList<ProjectTotTrackerRow> Projects { get; private set; } = Array.Empty<ProjectTotTrackerRow>();

    public TotTrackerSummary Summary { get; private set; } = TotTrackerSummary.Empty;

    /// <summary>The overview over every ToT project, whatever the register's filters.</summary>
    public ProjectTotPosition Position { get; private set; } = ProjectTotPosition.Empty;

    public string Narrative { get; private set; } = string.Empty;

    /// <summary>Who is reading decides the top section: decide, maintain, or check.</summary>
    public enum TotRoleView
    {
        Approver,
        Maintainer,
        Viewer
    }

    public TotRoleView RoleView { get; private set; } = TotRoleView.Viewer;

    /// <summary>Requests waiting for the HoD, oldest first.</summary>
    public IReadOnlyList<ProjectTotRegisterFacts> WaitingForDecision { get; private set; } = Array.Empty<ProjectTotRegisterFacts>();

    /// <summary>The first five long-waiting projects, longest waiting first; <see cref="LongWaitingCount"/> is all of them.</summary>
    public IReadOnlyList<ProjectTotRegisterFacts> LongWaitingProjects { get; private set; } = Array.Empty<ProjectTotRegisterFacts>();

    public int LongWaitingCount { get; private set; }

    /// <summary>The listed projects' facts, for the Project completed and Note columns.</summary>
    public IReadOnlyDictionary<int, ProjectTotRegisterFacts> RowFacts { get; private set; } = new Dictionary<int, ProjectTotRegisterFacts>();

    /// <summary>Optional columns appear only when some listed project has data for them.</summary>
    public bool ShowStartedColumn { get; private set; }

    public bool ShowMetColumn { get; private set; }

    public bool ShowRequestColumn { get; private set; }

    /// <summary>Counts for the status buttons: every filter except the status ones (search included).</summary>
    public ProjectTotPosition StatusCounts { get; private set; } = ProjectTotPosition.Empty;

    public int StatusCountAll { get; private set; }

    public string SortKey => Sort?.ToLowerInvariant() switch
    {
        "status" => "status",
        "tot" => "tot",
        "project" => "project",
        _ => "name"
    };

    public bool SortDescending => string.Equals(Dir, "desc", StringComparison.OrdinalIgnoreCase);

    /// <summary>The register's current filters and order, as link values (no selected project).</summary>
    public Dictionary<string, object?> RegisterRoute()
    {
        var route = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        void Add(string key, object? value)
        {
            if (value is null || value is false || value is string { Length: 0 })
            {
                return;
            }

            route[key] = value;
        }

        Add(nameof(SearchTerm), SearchTerm);
        Add(nameof(TotStatusFilter), TotStatusFilter?.ToString());
        Add(nameof(RequestStateFilter), RequestStateFilter?.ToString());
        Add(nameof(OnlyPending), OnlyPending ? "true" : null);
        Add(nameof(RequiresTotOnly), RequiresTotOnly ? "true" : null);
        Add(nameof(MetCompletedOnly), MetCompletedOnly ? "true" : null);
        Add(nameof(LongWaiting), LongWaiting ? "true" : null);
        Add(nameof(CompletedFrom), DayValue(CompletedFrom));
        Add(nameof(CompletedTo), DayValue(CompletedTo));
        Add(nameof(Sort), SortKey == "name" ? null : SortKey);
        Add(nameof(Dir), SortDescending ? "desc" : null);
        return route;
    }

    /// <summary>The current route with some keys changed or removed (null removes).</summary>
    public Dictionary<string, object?> RegisterRouteWith(params (string Key, object? Value)[] changes)
    {
        var route = RegisterRoute();
        foreach (var (key, value) in changes)
        {
            if (value is null)
            {
                route.Remove(key);
            }
            else
            {
                route[key] = value;
            }
        }

        return route;
    }

    /// <summary>The five latest ToT completions.</summary>
    public IReadOnlyList<ProjectTotRegisterFacts> RecentlyCompleted { get; private set; } = Array.Empty<ProjectTotRegisterFacts>();

    public bool CanSubmit { get; private set; }

    public bool CanApprove { get; private set; }

    [BindProperty]
    public ExportRequestInput Export { get; set; } = new();

    /// <summary>The shared drawer as the register hosts it; a selected project opens on load.</summary>
    public ProjectTotDrawerViewModel TotDrawerView => new()
    {
        Endpoint = Url.Page("/Tot/Index", new { area = "ProjectOfficeReports" }) ?? "/ProjectOfficeReports/Tot",
        ProjectId = SelectedProjectId,
        CanManage = CanSubmit,
        SaveNeedsApproval = !CanApprove,
        CanDecide = CanApprove,
        LinkToProject = true
    };

    public sealed class ExportRequestInput
    {
        public ProjectTotStatus? TotStatus { get; set; }

        public DateOnly? StartedFrom { get; set; }

        public DateOnly? StartedTo { get; set; }

        public DateOnly? CompletedFrom { get; set; }

        public DateOnly? CompletedTo { get; set; }
    }

    public sealed class TotTrackerSummary
    {
        public static TotTrackerSummary Empty { get; } = new();

        public int TotalProjects { get; init; }

        public int TotNotRequired { get; init; }

        public int TotNotStarted { get; init; }

        public int TotInProgress { get; init; }

        public int TotCompleted { get; init; }

        public int PendingApprovals { get; init; }

        public int ApprovedRequests { get; init; }

        public int RejectedRequests { get; init; }

        public int ProjectsRequiringTot { get; init; }

        public int ProjectsWithMetCompleted { get; init; }

        public int ProjectsWithFirstProductionModel { get; init; }

        public static TotTrackerSummary FromProjects(IReadOnlyList<ProjectTotTrackerRow>? projects)
        {
            var items = projects ?? Array.Empty<ProjectTotTrackerRow>();

            var total = items.Count;
            var notRequired = items.Count(p => p.TotStatus == ProjectTotStatus.NotRequired);
            var notStarted = items.Count(p => p.TotStatus == ProjectTotStatus.NotStarted);
            var inProgress = items.Count(p => p.TotStatus == ProjectTotStatus.InProgress);
            var completed = items.Count(p => p.TotStatus == ProjectTotStatus.Completed);
            var pending = items.Count(p => p.RequestState == ProjectTotRequestDecisionState.Pending);
            var approved = items.Count(p => p.RequestState == ProjectTotRequestDecisionState.Approved);
            var rejected = items.Count(p => p.RequestState == ProjectTotRequestDecisionState.Rejected);
            var metCompleted = items.Count(p => p.TotMetCompletedOn.HasValue);
            var firstProduction = items.Count(p => p.TotFirstProductionModelManufactured == true);

            return new TotTrackerSummary
            {
                TotalProjects = total,
                TotNotRequired = notRequired,
                TotNotStarted = notStarted,
                TotInProgress = inProgress,
                TotCompleted = completed,
                PendingApprovals = pending,
                ApprovedRequests = approved,
                RejectedRequests = rejected,
                ProjectsRequiringTot = total - notRequired,
                ProjectsWithMetCompleted = metCompleted,
                ProjectsWithFirstProductionModel = firstProduction
            };
        }
    }

    public enum ViewModeOption
    {
        Cards,
        List
    }

    // Redirects carry the completion window as yyyy-MM-dd, not the server culture's short date.
    private static string? DayValue(DateOnly? date)
        => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await PopulateAsync(cancellationToken);
        Export = new ExportRequestInput
        {
            TotStatus = TotStatusFilter,
            CompletedFrom = CompletedFrom,
            CompletedTo = CompletedTo
        };
    }

    public async Task<IActionResult> OnPostExportAsync(CancellationToken cancellationToken)
    {
        await PopulatePermissionsAsync();
        if (!CanSubmit)
        {
            return PermissionDenied(
                "export Transfer of Technology data",
                ProjectOfficeReportsPolicies.ManageTotTracker,
                SelectedProjectId);
        }

        if (!ModelState.IsValid)
        {
            await PopulateAsync(cancellationToken);
            ViewData["ShowTotExportModal"] = true;
            return Page();
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            TempData["ToastError"] = "You are not signed in or your session has expired. Please sign in again.";
            _logger.LogWarning("Blocked Transfer of Technology export because the user is not authenticated.");
            return Challenge();
        }

        // The export carries the same filters as the register on screen.
        var request = new ProjectTotExportRequest(
            Export.TotStatus ?? TotStatusFilter,
            Export.StartedFrom,
            Export.StartedTo,
            Export.CompletedFrom ?? CompletedFrom,
            Export.CompletedTo ?? CompletedTo,
            SearchTerm,
            userId,
            RequestStateFilter,
            OnlyPending,
            RequiresTotOnly,
            MetCompletedOnly,
            LongWaiting);

        var result = await _exportService.ExportAsync(request, cancellationToken);
        if (!result.Success || result.File is null)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            if (result.Errors.Count > 0)
            {
                TempData["ToastError"] = result.Errors[0];
            }

            await PopulateAsync(cancellationToken);
            ViewData["ShowTotExportModal"] = true;
            return Page();
        }

        return File(result.File.Content, result.File.ContentType, result.File.FileName);
    }

    // GET ?handler=Register: just the register (buttons, chips, table), for the live search.
    public async Task<IActionResult> OnGetRegisterAsync(CancellationToken cancellationToken)
    {
        await PopulateAsync(cancellationToken);
        return Partial("_TotRegister", this);
    }

    // GET ?handler=Yearly: [{ "year": 2021, "count": 4 }, ...] for the "completed per year" chart.
    public async Task<JsonResult> OnGetYearlyAsync(CancellationToken cancellationToken)
    {
        var projects = await _trackerService.GetAsync(new ProjectTotTrackerFilter(), cancellationToken);
        return new JsonResult(ProjectTotOverview.BuildYearlyCompletions(projects)
            .Select(point => new { year = point.Year, count = point.Count })
            .ToList());
    }

    // SECTION: The shared ToT drawer, with the register's access rule.

    public async Task<IActionResult> OnGetTotDetailsAsync(int id, CancellationToken ct)
        => ToJson(await TotDrawer().GetDetailsAsync(id, await DrawerAccessAsync(), ct));

    public async Task<IActionResult> OnPostTotAsync(int id, [FromForm] ProjectTotDrawerInput? input, CancellationToken ct)
    {
        var response = await TotDrawer().SaveAsync(id, input, await DrawerAccessAsync(), ct);
        if (response.StatusCode == 200)
        {
            _logger.LogInformation(
                "Project ToT saved from the register. ProjectId={ProjectId}, UserId={UserId}, Status={Status}",
                id,
                CurrentUserId(),
                input?.Status);
        }

        return ToJson(response);
    }

    public async Task<IActionResult> OnPostTotDecideAsync(
        int id,
        [FromForm] bool approve,
        [FromForm] string? rowVersion,
        [FromForm] string? reason,
        CancellationToken ct)
    {
        var response = await TotDrawer().DecideAsync(id, approve, rowVersion, reason, await DrawerAccessAsync(), ct);
        if (response.StatusCode == 200)
        {
            _logger.LogInformation(
                "Project ToT request {Decision} from the register. ProjectId={ProjectId}, UserId={UserId}",
                approve ? "approved" : "rejected",
                id,
                CurrentUserId());
        }

        return ToJson(response);
    }

    public async Task<IActionResult> OnPostTotRemarkAsync(
        int id,
        [FromForm] int projectId,
        [FromForm] string? body,
        CancellationToken ct)
    {
        var roles = User.FindAll(ClaimTypes.Role).Select(claim => claim.Value);
        return ToJson(await TotDrawer().AddRemarkAsync(id, projectId, body, roles, _remarkService, await DrawerAccessAsync(), ct));
    }

    private ProjectTotDrawerService TotDrawer() => new(_db, _clock);

    private async Task<ProjectTotDrawerAccess> DrawerAccessAsync()
    {
        await PopulatePermissionsAsync();
        var userId = CurrentUserId();
        var canSubmit = CanSubmit;
        return new ProjectTotDrawerAccess(
            userId,
            (project, _) => Task.FromResult(
                canSubmit
                && !project.IsArchived
                && ProjectOfficeReportsPolicies.CanSubmitTotFor(User, userId, project.LeadPoUserId)),
            CanApprove,
            User.IsInRole(RoleNames.Admin),
            User.IsInRole(RoleNames.HoD));
    }

    private static JsonResult ToJson(ProjectTotDrawerResponse response)
        => new(response.Body) { StatusCode = response.StatusCode };

    private async Task PopulateAsync(CancellationToken cancellationToken)
    {
        await PopulatePermissionsAsync();

        var filter = new ProjectTotTrackerFilter
        {
            TotStatus = TotStatusFilter,
            RequestState = OnlyPending ? null : RequestStateFilter,
            OnlyPendingRequests = OnlyPending,
            RequiresTotOnly = RequiresTotOnly,
            MetCompletedOnly = MetCompletedOnly,
            CompletedFrom = CompletedFrom,
            CompletedTo = CompletedTo,
            SearchTerm = SearchTerm
        };

        Projects = await _trackerService.GetAsync(filter, cancellationToken);
        Summary = TotTrackerSummary.FromProjects(Projects);

        var register = await new ProjectTotRegisterFactsService(_trackerService, _clock)
            .GetAsync(new ProjectTotTrackerFilter(), cancellationToken);
        Position = register.Position;
        Narrative = ProjectTotOverview.Narrative(Position);

        // SECTION: The role top section, from the same facts.
        RoleView = CanApprove ? TotRoleView.Approver : CanSubmit ? TotRoleView.Maintainer : TotRoleView.Viewer;
        var facts = register.Projects;
        WaitingForDecision = facts
            .Where(p => p.Pending is not null)
            .OrderBy(p => p.Pending!.SubmittedOnUtc)
            .ThenBy(p => p.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var longWaiting = facts
            .Where(p => p.IsLongWaiting)
            .OrderBy(p => p.ProjectCompletedYear)
            .ThenBy(p => p.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        LongWaitingCount = longWaiting.Count;
        LongWaitingProjects = longWaiting.Take(5).ToList();
        RecentlyCompleted = facts
            .Where(p => p.CompletedOn.HasValue)
            .OrderByDescending(p => p.CompletedOn)
            .ThenBy(p => p.ProjectName, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        if (LongWaiting)
        {
            // The same rule the export applies (ProjectTotRegisterFactsBuilder.KeepLongWaiting).
            var ids = longWaiting.Select(p => p.ProjectId).ToHashSet();
            Projects = Projects.Where(p => ids.Contains(p.ProjectId)).ToList();
            Summary = TotTrackerSummary.FromProjects(Projects);
        }

        // SECTION: The register table: facts per row, optional columns, order, status counts.
        var today = DateOnly.FromDateTime(ProjectManagement.Infrastructure.IstClock.ToIst(_clock.UtcNow.UtcDateTime));
        var rowFacts = Projects.ToDictionary(row => row.ProjectId, row => ProjectTotRegisterFactsBuilder.BuildFacts(row, today));
        RowFacts = rowFacts;
        ShowStartedColumn = Projects.Any(row => row.TotStartedOn.HasValue);
        ShowMetColumn = Projects.Any(row => row.TotMetCompletedOn.HasValue);
        ShowRequestColumn = Projects.Any(row => row.RequestState is not null);
        Projects = OrderRows(Projects, rowFacts);

        var statusFree = TotStatusFilter is null && !RequiresTotOnly && !OnlyPending && !LongWaiting;
        var countRows = statusFree
            ? Projects
            : await _trackerService.GetAsync(filter with { TotStatus = null, RequiresTotOnly = false, OnlyPendingRequests = false }, cancellationToken);
        var counted = ProjectTotRegisterFactsBuilder.Build(countRows, today);
        StatusCounts = counted.Position;
        StatusCountAll = counted.Projects.Count;
    }

    public static string StatusChipClass(ProjectTotStatus status) => status switch
    {
        ProjectTotStatus.NotRequired => "chip-status-not-required",
        ProjectTotStatus.NotStarted => "chip-status-not-started",
        ProjectTotStatus.InProgress => "chip-status-in-progress",
        ProjectTotStatus.Completed => "chip-status-completed",
        ProjectTotStatus.NotKnown => "chip-status-not-required",
        _ => "chip-status-not-started"
    };

    public static string RequestStateLabel(ProjectTotRequestDecisionState state) => state switch
    {
        ProjectTotRequestDecisionState.Pending => "Pending approval",
        ProjectTotRequestDecisionState.Approved => "Approved",
        ProjectTotRequestDecisionState.Rejected => "Rejected",
        _ => state.ToString()
    };

    public static string RequestStateChipClass(ProjectTotRequestDecisionState state) => state switch
    {
        ProjectTotRequestDecisionState.Pending => "chip-decision-pending",
        ProjectTotRequestDecisionState.Approved => "chip-decision-approved",
        ProjectTotRequestDecisionState.Rejected => "chip-decision-rejected",
        _ => "chip-decision-pending"
    };

    private IReadOnlyList<ProjectTotTrackerRow> OrderRows(
        IReadOnlyList<ProjectTotTrackerRow> rows,
        IReadOnlyDictionary<int, ProjectTotRegisterFacts> facts)
    {
        IOrderedEnumerable<ProjectTotTrackerRow> ordered = SortKey switch
        {
            // The order a reader expects: Completed, In progress, Not started, Not known, Not required.
            "status" => Order(rows, row => StatusRank(facts[row.ProjectId].Status)),
            "tot" => Order(rows, row => row.TotStatus == ProjectTotStatus.Completed ? row.TotCompletedOn : null),
            "project" => Order(rows, row => facts[row.ProjectId].ProjectCompletedYear),
            _ => SortDescending
                ? rows.OrderByDescending(row => row.ProjectName, StringComparer.OrdinalIgnoreCase)
                : rows.OrderBy(row => row.ProjectName, StringComparer.OrdinalIgnoreCase)
        };
        return ordered.ThenBy(row => row.ProjectName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Projects without a value go last in either direction.
    private IOrderedEnumerable<ProjectTotTrackerRow> Order<TKey>(IReadOnlyList<ProjectTotTrackerRow> rows, Func<ProjectTotTrackerRow, TKey?> key)
    {
        var withValue = rows.OrderBy(row => key(row) is null ? 1 : 0);
        return SortDescending ? withValue.ThenByDescending(key) : withValue.ThenBy(key);
    }

    private static int StatusRank(ProjectTotStatus status) => status switch
    {
        ProjectTotStatus.Completed => 0,
        ProjectTotStatus.InProgress => 1,
        ProjectTotStatus.NotStarted => 2,
        ProjectTotStatus.NotKnown => 3,
        _ => 4
    };

    private IActionResult PermissionDenied(string actionDescription, string policy, int? projectId)
    {
        var userId = _userManager.GetUserId(User) ?? "anonymous";
        var roleClaims = User?.Claims
            .Where(c => c.Type == ClaimTypes.Role)
            .Select(c => c.Value)
            .ToArray() ?? Array.Empty<string>();

        _logger.LogWarning(
            "User {UserId} ({UserName}) lacks policy {Policy} when attempting to {Action}. Roles: {Roles}",
            userId,
            User?.Identity?.Name ?? "unknown",
            policy,
            actionDescription,
            roleClaims.Length == 0 ? "(none)" : string.Join(", ", roleClaims));

        // Policy names and role lists stay in the server log; the user gets a plain message.
        TempData["ToastError"] = $"You do not have permission to {actionDescription}.";

        return RedirectToPage(new
        {
            TotStatusFilter,
            RequestStateFilter,
            SearchTerm,
            OnlyPending,
            RequiresTotOnly,
            MetCompletedOnly,
            CompletedFrom = DayValue(CompletedFrom),
            CompletedTo = DayValue(CompletedTo),
            SelectedProjectId = projectId ?? SelectedProjectId,
            ViewMode
        });
    }

    private async Task PopulatePermissionsAsync()
    {
        var submitResult = await _authorizationService.AuthorizeAsync(User, null, ProjectOfficeReportsPolicies.ManageTotTracker);
        CanSubmit = submitResult.Succeeded;

        var approveResult = await _authorizationService.AuthorizeAsync(User, null, ProjectOfficeReportsPolicies.ApproveTotTracker);
        CanApprove = approveResult.Succeeded;
    }

    private string? CurrentUserId()
        => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? _userManager.GetUserId(User);
}
