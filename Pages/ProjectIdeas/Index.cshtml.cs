using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectIdeas;
using ProjectManagement.Services.ProjectIdeas;

namespace ProjectManagement.Pages.ProjectIdeas;

[Authorize]
public class IndexModel : PageModel
{
    private const string ViewPreferenceCookie = "ProjectIdeas.BoardView";
    private static readonly string[] PlaceholderDescriptions =
    [
        "to be updated",
        "update required",
        "details to follow",
        "details awaited",
        "tbd"
    ];

    private readonly ProjectIdeaReadService _read;
    private readonly ProjectIdeaPermissionService _permissions;

    public IndexModel(ProjectIdeaReadService read, ProjectIdeaPermissionService permissions)
    {
        _read = read;
        _permissions = permissions;
    }

    // SECTION: Filters
    public const string CardsView = "cards";
    public const string TableView = "table";

    [BindProperty(SupportsGet = true)] public string Status { get; set; } = ProjectIdeaStatuses.Active;
    [BindProperty(SupportsGet = true)] public string? Query { get; set; }
    [BindProperty(SupportsGet = true)] public bool MyIdeas { get; set; }
    [BindProperty(SupportsGet = true)] public string View { get; set; } = CardsView;
    [BindProperty(SupportsGet = true)] public string Sort { get; set; } = ProjectIdeaSorts.LatestActivity;
    [BindProperty(SupportsGet = true)] public string? ProjectOfficerUserId { get; set; }
    [BindProperty(SupportsGet = true)] public string Assignment { get; set; } = ProjectIdeaAssignmentFilters.All;

    public IReadOnlyList<ProjectIdea> Ideas { get; private set; } = Array.Empty<ProjectIdea>();
    public IReadOnlyList<ProjectIdeaOfficerOption> ProjectOfficerOptions { get; private set; } =
        Array.Empty<ProjectIdeaOfficerOption>();
    public IReadOnlyDictionary<string, int> StatusCounts { get; private set; } =
        ProjectIdeaStatuses.All.ToDictionary(status => status, _ => 0);
    public bool CanCreate { get; private set; }

    // SECTION: Clean route values and view state
    public string? ActiveMyIdeasRouteValue => MyIdeas ? "true" : null;
    public string? ToggleMyIdeasRouteValue => MyIdeas ? null : "true";
    public bool IsTableView => string.Equals(View, TableView, StringComparison.Ordinal);
    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(Query) ||
        MyIdeas ||
        !string.IsNullOrWhiteSpace(ProjectOfficerUserId) ||
        Assignment != ProjectIdeaAssignmentFilters.All;
    public string CurrentStatusLabel => ProjectIdeaStatuses.ToDisplay(Status);
    public string? SelectedProjectOfficerName => ProjectOfficerOptions
        .FirstOrDefault(option => string.Equals(
            option.UserId,
            ProjectOfficerUserId,
            StringComparison.Ordinal))
        ?.DisplayName;

    public async Task OnGetAsync()
    {
        Status = NormaliseStatus(Status);
        Sort = ProjectIdeaSorts.Normalise(Sort);
        View = ResolveViewPreference();
        Query = NormaliseQuery(Query);
        ProjectOfficerUserId = NormaliseUserId(ProjectOfficerUserId);
        Assignment = ProjectIdeaAssignmentFilters.Normalise(Assignment);

        // "My Ideas" is an exclusive shortcut for ideas assigned to the current
        // user as Project Officer. Generic assignment filters must not narrow or
        // alter that meaning, including when a URL is manually composed.
        if (MyIdeas)
        {
            ProjectOfficerUserId = null;
            Assignment = ProjectIdeaAssignmentFilters.All;
        }
        // A named officer is already an assigned-only filter. Do not retain a
        // contradictory assignment value in the canonical page state.
        else if (ProjectOfficerUserId is not null)
        {
            Assignment = ProjectIdeaAssignmentFilters.All;
        }

        CanCreate = _permissions.CanCreateIdea(User);

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var canViewAll = User.IsInRole(RoleNames.Admin)
            || User.IsInRole(RoleNames.HoD)
            || User.IsInRole(RoleNames.Comdt);

        // ApplicationDbContext is scoped and does not support concurrent operations.
        // Keep these reads sequential to avoid intermittent production failures.
        Ideas = await _read.GetBoardIdeasAsync(
            Status,
            Query,
            MyIdeas,
            userId,
            canViewAll,
            Sort,
            ProjectOfficerUserId,
            Assignment);

        StatusCounts = await _read.GetBoardStatusCountsAsync(
            Query,
            MyIdeas,
            userId,
            canViewAll,
            ProjectOfficerUserId,
            Assignment);

        ProjectOfficerOptions = await _read.GetBoardProjectOfficersAsync(userId, canViewAll);
    }

    // SECTION: Display helpers
    public static DateTime LastActivity(ProjectIdea idea) => ProjectIdeaReadService.GetLastActivity(idea);

    public string DisplayUser(ApplicationUser? user) =>
        user?.FullName ?? user?.UserName ?? user?.Email ?? "Unassigned";

    public string DisplayDateTime(DateTime value) =>
        ToUtc(value).ToLocalTime().ToString("dd MMM yyyy, hh:mm tt");

    public string DisplayRelativeDate(DateTime value)
    {
        var utcValue = ToUtc(value);
        var elapsed = DateTime.UtcNow - utcValue;

        if (elapsed < TimeSpan.Zero || elapsed < TimeSpan.FromMinutes(1))
        {
            return "Just now";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{Math.Max(1, (int)elapsed.TotalMinutes)}m ago";
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            return $"{Math.Max(1, (int)elapsed.TotalHours)}h ago";
        }

        if (elapsed < TimeSpan.FromDays(2))
        {
            return "Yesterday";
        }

        if (elapsed < TimeSpan.FromDays(7))
        {
            return $"{Math.Max(2, (int)elapsed.TotalDays)}d ago";
        }

        var localValue = utcValue.ToLocalTime();
        return localValue.Year == DateTime.Now.Year
            ? localValue.ToString("dd MMM")
            : localValue.ToString("dd MMM yyyy");
    }

    public int StatusCount(string status) => StatusCounts.GetValueOrDefault(status);

    public static string CountLabel(int count, string singular) =>
        $"{count} {(count == 1 ? singular : singular + "s")}";

    public static int TotalActivity(ProjectIdea idea) =>
        idea.Comments.Count(comment => !comment.IsDeleted) +
        idea.Notes.Count(note => !note.IsDeleted) +
        idea.Documents.Count(document => !document.IsDeleted);

    public static bool NeedsDetails(ProjectIdea idea)
    {
        var description = idea.Description?.Trim();
        if (string.IsNullOrWhiteSpace(description))
        {
            return true;
        }

        return PlaceholderDescriptions.Any(placeholder =>
            description.Equals(placeholder, StringComparison.OrdinalIgnoreCase) ||
            description.StartsWith(placeholder + " ", StringComparison.OrdinalIgnoreCase) ||
            description.StartsWith(placeholder + " by ", StringComparison.OrdinalIgnoreCase));
    }

    public static string DisplayDescription(ProjectIdea idea) =>
        NeedsDetails(idea)
            ? "No meaningful summary has been provided."
            : idea.Description.Trim();

    public static bool IsStale(ProjectIdea idea)
    {
        if (!string.Equals(idea.Status, ProjectIdeaStatuses.Active, StringComparison.Ordinal))
        {
            return false;
        }

        return DateTime.UtcNow - ToUtc(LastActivity(idea)) >= TimeSpan.FromDays(30);
    }

    private string ResolveViewPreference()
    {
        if (Request.Query.ContainsKey(nameof(View)))
        {
            var explicitView = NormaliseView(View);
            Response.Cookies.Append(
                ViewPreferenceCookie,
                explicitView,
                new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                    HttpOnly = true,
                    IsEssential = true,
                    Path = "/ProjectIdeas",
                    SameSite = SameSiteMode.Lax,
                    Secure = Request.IsHttps
                });
            return explicitView;
        }

        return NormaliseView(Request.Cookies[ViewPreferenceCookie]);
    }

    private static string NormaliseStatus(string? status)
    {
        return status switch
        {
            ProjectIdeaStatuses.Active => ProjectIdeaStatuses.Active,
            ProjectIdeaStatuses.OnHold => ProjectIdeaStatuses.OnHold,
            ProjectIdeaStatuses.Archived => ProjectIdeaStatuses.Archived,
            _ => ProjectIdeaStatuses.Active
        };
    }

    private static string NormaliseView(string? view) =>
        string.Equals(view, TableView, StringComparison.OrdinalIgnoreCase)
            ? TableView
            : CardsView;

    private static string? NormaliseQuery(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= 200 ? trimmed : trimmed[..200];
    }

    private static string? NormaliseUserId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= 450 ? trimmed : trimmed[..450];
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
