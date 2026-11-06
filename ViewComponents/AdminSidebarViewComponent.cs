using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Services.Navigation;
using ProjectManagement.Services.Navigation.ModuleNav;

namespace ProjectManagement.ViewComponents;

public sealed class AdminSidebarViewComponent : ViewComponent
{
    private readonly IAdminNavigationAccess _access;
    private readonly IAdminNavigationUrlBuilder _urlBuilder;

    public AdminSidebarViewComponent(
        IAdminNavigationAccess access,
        IAdminNavigationUrlBuilder urlBuilder)
    {
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _urlBuilder = urlBuilder ?? throw new ArgumentNullException(nameof(urlBuilder));
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var currentArea = RouteValue("area");
        var currentPage = RouteValue("page");
        var activeEntry = AdminNavigationCatalog.FindActiveEntry(
            currentArea,
            currentPage,
            HttpContext.Request.Query);

        // A page reached through another page (a hub's own pages) is not in the sidebar; while
        // it is open, the page that leads to it is highlighted instead.
        var activeKey = activeEntry?.SidebarParent ?? activeEntry?.Key;

        var openable = await _access.GetOpenableEntriesAsync(HttpContext.User);

        var visibleEntries = new List<AdminSidebarItemViewModel>();
        foreach (var entry in openable.Where(entry => entry.InSidebar))
        {
            visibleEntries.Add(new AdminSidebarItemViewModel
            {
                Key = entry.Key,
                Text = entry.Item.Text,
                Icon = entry.Item.Icon,
                Url = _urlBuilder.GetPath(HttpContext, entry.Item),
                IsActive = string.Equals(entry.Key, activeKey, StringComparison.Ordinal),
                Description = entry.Description,
                Keywords = entry.Keywords
            });
        }

        // Pages reached through a hub are not in the sidebar, but its filter still finds them:
        // they are rendered as search-only links, shown only while the administrator types.
        var searchOnly = new List<AdminSidebarItemViewModel>();
        foreach (var entry in openable.Where(entry => !entry.InSidebar))
        {
            searchOnly.Add(new AdminSidebarItemViewModel
            {
                Key = entry.Key,
                Text = entry.Item.Text,
                Icon = entry.Item.Icon,
                Url = _urlBuilder.GetPath(HttpContext, entry.Item),
                IsActive = false,
                Description = entry.Description,
                Keywords = entry.Keywords
            });
        }

        var groups = AdminNavigationGroups.Ordered
            .Select(groupName => new AdminSidebarGroupViewModel
            {
                Name = groupName,
                Items = visibleEntries
                    .Where(item => string.Equals(
                        AdminNavigationCatalog.GetEntry(item.Key).Group,
                        groupName,
                        StringComparison.Ordinal))
                    .ToArray()
            })
            .Where(group => group.Items.Count > 0)
            .ToArray();

        return View(new AdminSidebarViewModel
        {
            Groups = groups,
            SearchOnlyItems = searchOnly,
            CurrentSection = activeEntry?.Item.Text ?? "Administration"
        });
    }

    private string? RouteValue(string key) =>
        ViewContext.RouteData.Values.TryGetValue(key, out var value)
            ? value?.ToString()
            : null;
}

public sealed class AdminSidebarViewModel
{
    public IReadOnlyList<AdminSidebarGroupViewModel> Groups { get; init; } =
        Array.Empty<AdminSidebarGroupViewModel>();

    public string CurrentSection { get; init; } = "Administration";

    public IReadOnlyList<AdminSidebarItemViewModel> SearchOnlyItems { get; init; } =
        Array.Empty<AdminSidebarItemViewModel>();
}

public sealed class AdminSidebarGroupViewModel
{
    public string Name { get; init; } = string.Empty;

    public IReadOnlyList<AdminSidebarItemViewModel> Items { get; init; } =
        Array.Empty<AdminSidebarItemViewModel>();

    public bool IsActive => Items.Any(item => item.IsActive);

    public bool IsHome => string.Equals(Name, AdminNavigationGroups.Home, StringComparison.Ordinal);
}

public sealed class AdminSidebarItemViewModel
{
    public string Key { get; init; } = string.Empty;

    public string Text { get; init; } = string.Empty;

    public string? Icon { get; init; }

    public string? Url { get; init; }

    public string Description { get; init; } = string.Empty;

    public string Keywords { get; init; } = string.Empty;

    public bool IsActive { get; init; }
}
