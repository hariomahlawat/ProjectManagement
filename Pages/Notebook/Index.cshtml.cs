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
    private readonly INotebookSystemItemPreferenceService _systemItemPreferences;
    private readonly UserManager<ApplicationUser> _users;

    public IndexModel(
        INotebookService notebook,
        IOfficerConferenceReadService conferenceRead,
        INotebookSystemItemPreferenceService systemItemPreferences,
        UserManager<ApplicationUser> users)
    {
        _notebook = notebook ?? throw new ArgumentNullException(nameof(notebook));
        _conferenceRead = conferenceRead ?? throw new ArgumentNullException(nameof(conferenceRead));
        _systemItemPreferences = systemItemPreferences ?? throw new ArgumentNullException(nameof(systemItemPreferences));
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
    public NotebookSystemItemPreferenceVm? ConferenceDigestPreference { get; private set; }
    public bool ShowConferenceDigest { get; private set; }
    public bool ConferenceDigestIsHomePlacement => ShowConferenceDigest
        && string.Equals(Notebook.View, "home", StringComparison.OrdinalIgnoreCase);
    public bool ConferenceDigestIsLabelView => ShowConferenceDigest
        && string.Equals(Notebook.View, "labels", StringComparison.OrdinalIgnoreCase);
    public int SystemSharedSurfaceCount => ConferenceDigest is { TotalDirectionCount: > 0 } ? 1 : 0;
    public int SystemHomeSurfaceCount => ConferenceDigest is { TotalDirectionCount: > 0 }
        && ConferenceDigestPreference?.ShowInHome == true ? 1 : 0;

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
                ConferenceDigestPreference = await _systemItemPreferences.GetAsync(
                    userId,
                    NotebookSystemItemKeys.ConferenceDirections,
                    ct);
                AddSystemSurfaceToRail(ConferenceDigestPreference);
                ShowConferenceDigest = ShouldShowConferenceDigest(ConferenceDigest, ConferenceDigestPreference);
            }
        }

        return Page();
    }

    private bool HasCommandNotebookRole()
        => User.IsInRole(RoleNames.Comdt) || User.IsInRole(RoleNames.HoD);

    private void AddSystemSurfaceToRail(NotebookSystemItemPreferenceVm preference)
    {
        var shared = Notebook.RailItems.FirstOrDefault(item =>
            string.Equals(item.Key, "shared", StringComparison.OrdinalIgnoreCase));
        if (shared is not null)
        {
            shared.Count += 1;
        }

        // Once the user explicitly adds the live PRISM note to My Notebook it is
        // part of the visible All Notes surface, so the rail count should match
        // what the user actually sees without turning it into a NotebookItem.
        if (!preference.ShowInHome) return;

        var home = Notebook.RailItems.FirstOrDefault(item =>
            string.Equals(item.Key, "home", StringComparison.OrdinalIgnoreCase));
        if (home is not null)
        {
            home.Count += 1;
        }
    }

    public NotebookConferenceDigestCardVm CreateConferenceDigestCardVm()
    {
        if (ConferenceDigest is null || ConferenceDigestPreference is null)
        {
            throw new InvalidOperationException("Conference digest card state is unavailable.");
        }

        return new NotebookConferenceDigestCardVm
        {
            Digest = ConferenceDigest,
            Preference = ConferenceDigestPreference,
            View = Notebook.View,
            IsHomePlacement = ConferenceDigestIsHomePlacement,
            IsLabelView = ConferenceDigestIsLabelView
        };
    }

    private bool ShouldShowConferenceDigest(ConferenceDirectionDigestVm digest, NotebookSystemItemPreferenceVm preference)
    {
        if (!string.IsNullOrWhiteSpace(Filter))
        {
            return false;
        }

        var viewAllows = Notebook.View.ToLowerInvariant() switch
        {
            "shared" => string.IsNullOrWhiteSpace(Tag),
            "home" => preference.ShowInHome && string.IsNullOrWhiteSpace(Tag),
            "labels" => !string.IsNullOrWhiteSpace(Tag)
                && preference.Labels.Any(label => string.Equals(label, Tag, StringComparison.OrdinalIgnoreCase)),
            _ => false
        };

        if (!viewAllows)
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
            || Contains("Command", term)
            || preference.Labels.Any(label => Contains(label, term)))
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
