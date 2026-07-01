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

    public IReadOnlyList<ProjectIdea> Ideas { get; private set; } = Array.Empty<ProjectIdea>();
    public IReadOnlyDictionary<string, int> StatusCounts { get; private set; } =
        ProjectIdeaStatuses.All.ToDictionary(status => status, _ => 0);
    public bool CanCreate { get; private set; }

    // SECTION: Clean route values and view state
    public string? ActiveMyIdeasRouteValue => MyIdeas ? "true" : null;
    public string? ToggleMyIdeasRouteValue => MyIdeas ? null : "true";
    public bool IsTableView => string.Equals(View, TableView, StringComparison.Ordinal);
    public bool UseScrollableTable => Ideas.Count > 10;

    public async Task OnGetAsync()
    {
        Status = NormaliseStatus(Status);
        Sort = ProjectIdeaSorts.Normalise(Sort);
        View = ResolveViewPreference();
        CanCreate = _permissions.CanCreateIdea(User);

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var canViewAll = User.IsInRole(RoleNames.Admin)
            || User.IsInRole(RoleNames.HoD)
            || User.IsInRole(RoleNames.Comdt);

        Ideas = await _read.GetBoardIdeasAsync(Status, Query, MyIdeas, userId, canViewAll, Sort);
        StatusCounts = await _read.GetBoardStatusCountsAsync(Query, MyIdeas, userId, canViewAll);
    }

    // SECTION: Display helpers
    public static DateTime LastActivity(ProjectIdea idea) => ProjectIdeaReadService.GetLastActivity(idea);
    public string DisplayUser(ApplicationUser? user) => user?.FullName ?? user?.UserName ?? user?.Email ?? "Unassigned";
    public string DisplayDateTime(DateTime value) => value.ToLocalTime().ToString("dd MMM yyyy, hh:mm tt");
    public int StatusCount(string status) => StatusCounts.GetValueOrDefault(status);

    public static string CountLabel(int count, string singular) =>
        $"{count} {(count == 1 ? singular : singular + "s")}";

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
}
