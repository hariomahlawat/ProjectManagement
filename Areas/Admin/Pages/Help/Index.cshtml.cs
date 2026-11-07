using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Navigation;
using ProjectManagement.Services.Navigation.ModuleNav;

namespace ProjectManagement.Areas.Admin.Pages.Help;

[Authorize(Policy = AdminPolicies.Access)]
[ResponseCache(NoStore = true)]
public sealed class IndexModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Section { get; set; }

    private readonly IAdminNavigationAccess _access;
    private readonly IAdminNavigationUrlBuilder _navigation;

    public IndexModel(IAdminNavigationAccess access, IAdminNavigationUrlBuilder navigation)
    {
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
    }

    public AdminPageHeaderModel Header { get; private set; } = new();

    /// <summary>"How do I…": the common admin jobs, each with the page that does it.</summary>
    public IReadOnlyList<AdminHowDoI> Answers { get; private set; } = Array.Empty<AdminHowDoI>();

    /// <summary>The questions, in page order. Each names the exact button on its page.</summary>
    public static IReadOnlyList<(string Question, string Answer, string NavigationKey)> Questions { get; } = new[]
    {
        ("Add a new user", "Users › Add user. They get a temporary password and must choose their own at first sign-in.", AdminNavigationKeys.Users),
        ("Help someone who is locked out", "Users › open the person › Clear lockout. If they forgot their password, use Reset password instead.", AdminNavigationKeys.Users),
        ("Change what someone can do", "Users › open the person › Edit account, then change their roles.", AdminNavigationKeys.Users),
        ("Stop someone signing in", "Users › open the person › Disable account. Their records and history stay.", AdminNavigationKeys.Users),
        ("See who changed something", "Audit log: filter by person, record or date.", AdminNavigationKeys.Logs),
        ("Bring back something deleted", "Restore deleted items › pick the kind (Projects, Project documents…) › Restore.", AdminNavigationKeys.RecoveryCentre),
        ("Add a choice to a dropdown", "Lists used in forms › open the list › Add.", AdminNavigationKeys.MasterDataCentre),
        ("Retire a dropdown choice that records use", "Open its list and Deactivate it. Records that use it keep it; forms stop offering it.", AdminNavigationKeys.MasterDataCentre),
        ("Add a holiday", "Holidays › Add holiday entry. Stage dates skip office holidays.", AdminNavigationKeys.Holidays),
        ("Check that PRISM is working", "System health › Run checks again. Each row says what to do if something is wrong.", AdminNavigationKeys.DatabaseHealth),
        ("Fix search results that are missing", "Search index › Retry all failed, or Rebuild search index if many results are wrong.", AdminNavigationKeys.SearchIndex),
        ("Free up space used by chat files", "Chat file storage › Remove chat files › Preview › Remove. To change your mind, use Undo under Recent clean-ups before the time it shows; after that the files are gone.", AdminNavigationKeys.ChatStorage)
    };

    public async Task OnGetAsync()
    {
        // A link only to a page the viewer can open; the answer still reads without it.
        var openable = (await _access.GetOpenableEntriesAsync(User)).Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
        Answers = Questions
            .Select(item => new AdminHowDoI(
                item.Question,
                item.Answer,
                AdminNavigationCatalog.GetEntry(item.NavigationKey).Item.Text,
                openable.Contains(item.NavigationKey) ? _navigation.GetPath(HttpContext, item.NavigationKey) : null))
            .ToArray();

        Header = new AdminPageHeaderModel
        {
            Title = "Administration guide",
            Description = "Short answers to the common admin jobs, then how each part of Administration works.",
            Icon = "bi-question-circle"
        };
    }
}

public sealed record AdminHowDoI(string Question, string Answer, string LinkText, string? Href);
