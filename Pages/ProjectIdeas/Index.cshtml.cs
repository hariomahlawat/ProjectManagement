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
    private readonly ProjectIdeaReadService _read;
    private readonly ProjectIdeaPermissionService _permissions;
    public IndexModel(ProjectIdeaReadService read, ProjectIdeaPermissionService permissions) { _read = read; _permissions = permissions; }

    // SECTION: Filters
    public const string CardsView = "cards";
    public const string TableView = "table";

    [BindProperty(SupportsGet = true)] public string Status { get; set; } = ProjectIdeaStatuses.Active;
    [BindProperty(SupportsGet = true)] public string? Query { get; set; }
    [BindProperty(SupportsGet = true)] public bool MyIdeas { get; set; }
    [BindProperty(SupportsGet = true)] public string View { get; set; } = CardsView;
    public IReadOnlyList<ProjectIdea> Ideas { get; private set; } = Array.Empty<ProjectIdea>();
    public bool CanCreate { get; private set; }

    // SECTION: Clean route values and view state
    public string? ActiveMyIdeasRouteValue => MyIdeas ? "true" : null;
    public string? ToggleMyIdeasRouteValue => MyIdeas ? null : "true";
    public bool IsTableView => string.Equals(View, TableView, StringComparison.Ordinal);

    public async Task OnGetAsync()
    {
        Status = NormaliseStatus(Status);
        View = NormaliseView(View);
        CanCreate = _permissions.CanCreateIdea(User);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var canViewAll = User.IsInRole(RoleNames.Admin) || User.IsInRole(RoleNames.HoD) || User.IsInRole(RoleNames.Comdt);
        Ideas = await _read.GetBoardIdeasAsync(Status, Query, MyIdeas, userId, canViewAll);
    }

    // SECTION: Display helpers
    public static DateTime LastActivity(ProjectIdea idea) => ProjectIdeaReadService.GetLastActivity(idea);
    public static string Display(string status) => ProjectIdeaStatuses.ToDisplay(status);
    public string DisplayUser(ApplicationUser? user) => user?.FullName ?? "Unassigned";
    public string DisplayDateTime(DateTime value) => value.ToLocalTime().ToString("dd MMM yyyy, hh:mm tt");

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
