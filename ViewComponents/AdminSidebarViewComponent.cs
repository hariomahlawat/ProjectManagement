using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Services.Navigation;
using ProjectManagement.Services.Navigation.ModuleNav;

namespace ProjectManagement.ViewComponents;

public sealed class AdminSidebarViewComponent : ViewComponent
{
    private readonly IAuthorizationService _authorizationService;
    private readonly IAdminNavigationUrlBuilder _urlBuilder;

    public AdminSidebarViewComponent(
        IAuthorizationService authorizationService,
        IAdminNavigationUrlBuilder urlBuilder)
    {
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
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

        var visibleEntries = new List<AdminSidebarItemViewModel>();
        foreach (var entry in AdminNavigationCatalog.Entries.OrderBy(entry => entry.Order))
        {
            if (!await IsAuthorizedAsync(entry))
            {
                continue;
            }

            visibleEntries.Add(new AdminSidebarItemViewModel
            {
                Key = entry.Key,
                Text = entry.Item.Text,
                Icon = entry.Item.Icon,
                Url = _urlBuilder.GetPath(HttpContext, entry.Item),
                IsActive = string.Equals(entry.Key, activeEntry?.Key, StringComparison.Ordinal)
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
            CurrentSection = activeEntry?.Item.Text ?? "Administration"
        });
    }

    private string? RouteValue(string key) =>
        ViewContext.RouteData.Values.TryGetValue(key, out var value)
            ? value?.ToString()
            : null;

    private async Task<bool> IsAuthorizedAsync(AdminNavigationEntry entry)
    {
        if (entry.Item.RequiredRoles is { Count: > 0 }
            && !entry.Item.RequiredRoles.Any(User.IsInRole))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(entry.Item.AuthorizationPolicy))
        {
            return true;
        }

        var result = await _authorizationService.AuthorizeAsync(
            HttpContext.User,
            resource: null,
            policyName: entry.Item.AuthorizationPolicy);

        return result.Succeeded;
    }
}

public sealed class AdminSidebarViewModel
{
    public IReadOnlyList<AdminSidebarGroupViewModel> Groups { get; init; } =
        Array.Empty<AdminSidebarGroupViewModel>();

    public string CurrentSection { get; init; } = "Administration";
}

public sealed class AdminSidebarGroupViewModel
{
    public string Name { get; init; } = string.Empty;

    public IReadOnlyList<AdminSidebarItemViewModel> Items { get; init; } =
        Array.Empty<AdminSidebarItemViewModel>();

    public bool IsActive => Items.Any(item => item.IsActive);
}

public sealed class AdminSidebarItemViewModel
{
    public string Key { get; init; } = string.Empty;

    public string Text { get; init; } = string.Empty;

    public string? Icon { get; init; }

    public string? Url { get; init; }

    public bool IsActive { get; init; }
}
