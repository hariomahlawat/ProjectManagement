using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
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
    [BindProperty(SupportsGet = true)] public string Status { get; set; } = ProjectIdeaStatuses.Active;
    [BindProperty(SupportsGet = true)] public string? Query { get; set; }
    [BindProperty(SupportsGet = true)] public bool MyIdeas { get; set; }
    public IReadOnlyList<ProjectIdea> Ideas { get; private set; } = Array.Empty<ProjectIdea>();
    public bool CanCreate { get; private set; }

    public async Task OnGetAsync()
    {
        if (!ProjectIdeaStatuses.All.Contains(Status)) Status = ProjectIdeaStatuses.Active;
        CanCreate = _permissions.CanCreateIdea(User);
        Ideas = await _read.GetBoardIdeasAsync(Status, Query, MyIdeas, User.FindFirstValue(ClaimTypes.NameIdentifier));
    }

    public static DateTime LastActivity(ProjectIdea idea) => ProjectIdeaReadService.GetLastActivity(idea);
    public static string Display(string status) => ProjectIdeaStatuses.ToDisplay(status);
}
