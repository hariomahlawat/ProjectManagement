using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Services.Notebook;
using ProjectManagement.Services.Workspace;
using ProjectManagement.ViewModels.Notebook;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Pages.Notebook;

[Authorize]
public class IndexModel : PageModel
{
    private readonly INotebookService _notebook;
    private readonly IOfficerConferenceReadService _conferenceRead;
    private readonly UserManager<ApplicationUser> _users;

    public IndexModel(
        INotebookService notebook,
        IOfficerConferenceReadService conferenceRead,
        UserManager<ApplicationUser> users)
    {
        _notebook = notebook ?? throw new ArgumentNullException(nameof(notebook));
        _conferenceRead = conferenceRead ?? throw new ArgumentNullException(nameof(conferenceRead));
        _users = users ?? throw new ArgumentNullException(nameof(users));
    }

    // SECTION: Notebook query state only. All mutations are handled by the
    // versioned Notebook API so the module has one authoritative write path.
    [BindProperty(SupportsGet = true)]
    public string View { get; set; } = "home";

    [BindProperty(SupportsGet = true)]
    public string? Query { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? SelectedId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Filter { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Tag { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool EditLabels { get; set; }

    public NotebookIndexVm Notebook { get; private set; } = new();
    public ConferenceDirectionDigestVm? ConferenceDigest { get; private set; }
    public bool ShowConferenceDigest { get; private set; }
    public int SystemSharedSurfaceCount => ConferenceDigest is { TotalDirectionCount: > 0 } ? 1 : 0;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var userId = _users.GetUserId(User);
        if (userId is null)
        {
            return Unauthorized();
        }

        // Backwards compatibility for old drawer links. Editing now always opens
        // the modern modal through the ?note= query parameter.
        if (SelectedId.HasValue)
        {
            return RedirectToPage(new
            {
                note = SelectedId,
                view = View,
                query = Query,
                filter = Filter,
                tag = Tag
            });
        }

        // Root Labels navigation opens the in-place label manager over Home.
        if (string.Equals(View, "labels", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(Tag))
        {
            return RedirectToPage(new { view = "home", editLabels = true });
        }

        NormalizeLegacyTypeView();
        Notebook = await _notebook.GetIndexAsync(userId, View, Query, Filter, Tag, selectedId: null, ct);

        if (HasCommandNotebookRole())
        {
            ConferenceDigest = await _conferenceRead.GetLatestDirectionDigestAsync(userId, ct);
            if (ConferenceDigest is { TotalDirectionCount: > 0 })
            {
                AddSystemSharedSurfaceToRail();
                ShowConferenceDigest = ShouldShowConferenceDigest(ConferenceDigest);
            }
        }

        return Page();
    }

    private bool HasCommandNotebookRole()
        => User.IsInRole(RoleNames.Comdt) || User.IsInRole(RoleNames.HoD);

    private void AddSystemSharedSurfaceToRail()
    {
        var shared = Notebook.RailItems.FirstOrDefault(item =>
            string.Equals(item.Key, "shared", StringComparison.OrdinalIgnoreCase));
        if (shared is not null)
        {
            shared.Count += 1;
        }
    }

    private bool ShouldShowConferenceDigest(ConferenceDirectionDigestVm digest)
    {
        if (!string.Equals(Notebook.View, "shared", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(Filter)
            || !string.IsNullOrWhiteSpace(Tag))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(Query))
        {
            return true;
        }

        var term = Query.Trim();
        if (Contains("Latest Conference Directions", term)
            || Contains("Conference Review", term)
            || Contains("PRISM", term)
            || Contains("Command", term))
        {
            return true;
        }

        return digest.OfficerGroups.Any(group =>
            Contains(group.OfficerDisplayName, term)
            || group.Directions.Any(item =>
                Contains(item.Title, term)
                || Contains(item.DirectionText, term)));
    }

    private static bool Contains(string? value, string term)
        => !string.IsNullOrWhiteSpace(value)
            && value.Contains(term, StringComparison.OrdinalIgnoreCase);

    private void NormalizeLegacyTypeView()
    {
        if (string.Equals(View, "sticky", StringComparison.OrdinalIgnoreCase))
        {
            View = "home";
            Filter ??= "notes";
        }
        else if (string.Equals(View, "notes", StringComparison.OrdinalIgnoreCase))
        {
            View = "home";
            Filter ??= "notes";
        }
        else if (string.Equals(View, "checklists", StringComparison.OrdinalIgnoreCase))
        {
            View = "home";
            Filter ??= "checklists";
        }
    }
}
