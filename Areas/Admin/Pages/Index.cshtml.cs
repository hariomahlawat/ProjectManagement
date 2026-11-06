using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Navigation;
using ProjectManagement.Services.Navigation.ModuleNav;

namespace ProjectManagement.Areas.Admin.Pages;

/// <summary>
/// Administration Home (docs/ux/admin-module.md): "Find a page or setting", one status line,
/// "To look at" only when something needs an administrator, six task tiles and recent changes.
/// The rules live in <see cref="AdminHomeRules"/>; this page only lays them out.
/// </summary>
[Authorize(Policy = AdminPolicies.Access)]
[ResponseCache(NoStore = true)]
public sealed class AdminIndexModel : PageModel
{
    // Everyday words that show what the search understands; each one finds at least one page
    // (AdminHomeTests keeps that true).
    public static readonly IReadOnlyList<string> ExampleSearches = new[]
    {
        "locked out", "trash", "dropdown", "holiday", "pdf"
    };

    private readonly IAdminHomeService _home;
    private readonly IAdminNavigationAccess _access;
    private readonly IAdminNavigationUrlBuilder _navigation;
    private readonly IAuthorizationService _authorization;
    private readonly IAuditActionPresentationCatalog _auditActions;

    public AdminIndexModel(
        IAdminHomeService home,
        IAdminNavigationAccess access,
        IAdminNavigationUrlBuilder navigation,
        IAuthorizationService authorization,
        IAuditActionPresentationCatalog auditActions)
    {
        _home = home ?? throw new ArgumentNullException(nameof(home));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _auditActions = auditActions ?? throw new ArgumentNullException(nameof(auditActions));
    }

    public AdminPageHeaderModel Header { get; private set; } = new();

    public AdminHomeStatus Status { get; private set; } =
        new(AdminHomeTone.Unknown, "Not checked yet", string.Empty);

    public IReadOnlyList<LookAtViewModel> LookAt { get; private set; } = Array.Empty<LookAtViewModel>();

    public IReadOnlyList<TileViewModel> Tiles { get; private set; } = Array.Empty<TileViewModel>();

    public IReadOnlyList<AdminDashboardAction> RecentActions { get; private set; } =
        Array.Empty<AdminDashboardAction>();

    /// <summary>Every page the viewer can open, for "Find a page or setting".</summary>
    public IReadOnlyList<SearchEntryViewModel> SearchEntries { get; private set; } =
        Array.Empty<SearchEntryViewModel>();

    public string? HealthHref { get; private set; }

    public string? AuditLogHref { get; private set; }

    public string? AllPagesHref { get; private set; }

    public string StatusCss => Status.Tone switch
    {
        AdminHomeTone.Urgent => "danger",
        AdminHomeTone.Attention => "warning",
        AdminHomeTone.Calm => "success",
        _ => "neutral"
    };

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _home.GetAsync(User, cancellationToken);
        var openable = await _access.GetOpenableEntriesAsync(User);
        var openableKeys = openable.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
        string? HrefIfOpenable(string key) => openableKeys.Contains(key) ? Href(key) : null;

        Header = new AdminPageHeaderModel
        {
            Title = "Administration",
            Description = "Look after users, sign-ins, deleted items, the lists used in forms, and PRISM's health.",
            Icon = "bi-shield-check",
            Actions = await BuildActionsAsync(HrefIfOpenable)
        };

        Status = snapshot.Status;
        LookAt = snapshot.LookAt
            .Select(item => new LookAtViewModel(item.Title, item.Detail, item.LinkText, Href(item.NavigationKey, item.RouteValues), item.Urgent))
            .ToArray();
        Tiles = snapshot.Tiles
            .Select(tile => new TileViewModel(tile.Title, tile.Body, tile.Icon, tile.Fact, Href(tile.NavigationKey)))
            .Where(tile => tile.Href is not null)
            .ToArray();
        RecentActions = snapshot.RecentActions;

        SearchEntries = openable
            .Select(entry => new SearchEntryViewModel(
                entry.Item.Text,
                entry.Group,
                entry.Description,
                entry.Keywords,
                _navigation.GetPath(HttpContext, entry.Item)))
            .Where(entry => entry.Href is not null)
            .ToArray();

        HealthHref = HrefIfOpenable(AdminNavigationKeys.DatabaseHealth);
        AuditLogHref = HrefIfOpenable(AdminNavigationKeys.Logs);
        AllPagesHref = HrefIfOpenable(AdminNavigationKeys.AllPages);
    }

    public AuditActionPresentation ActionPresentation(AdminDashboardAction action) =>
        _auditActions.Describe(action.Action, action.Level);

    private async Task<IReadOnlyList<AdminPageActionModel>> BuildActionsAsync(Func<string, string?> hrefIfOpenable)
    {
        var actions = new List<AdminPageActionModel>();
        if (hrefIfOpenable(AdminNavigationKeys.Help) is { } help)
        {
            actions.Add(new AdminPageActionModel { Text = "Administration guide", Href = help, Icon = "bi-question-circle" });
        }

        var canAddUsers = (await _authorization.AuthorizeAsync(User, AdminPolicies.UsersManage)).Succeeded;
        if (canAddUsers && Url.Page("/Users/Create", new { area = "Admin" }) is { } create)
        {
            actions.Add(new AdminPageActionModel { Text = "Add a user", Href = create, Icon = "bi-person-plus", IsPrimary = true });
        }

        return actions;
    }

    private string? Href(string navigationKey, IReadOnlyDictionary<string, object?>? routeValues = null) =>
        _navigation.GetPath(HttpContext, navigationKey, routeValues);

    public sealed record LookAtViewModel(string Title, string Detail, string LinkText, string? Href, bool Urgent);

    public sealed record TileViewModel(string Title, string Body, string Icon, string Fact, string? Href);

    public sealed record SearchEntryViewModel(string Text, string Group, string Description, string Keywords, string? Href)
    {
        public string SearchLabel => $"{Text} {Keywords} {Description}".ToLowerInvariant();
    }
}
