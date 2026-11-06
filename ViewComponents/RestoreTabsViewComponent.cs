using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Services.Admin.Recovery;
using ProjectManagement.Services.Navigation;
using ProjectManagement.Services.Navigation.ModuleNav;

namespace ProjectManagement.ViewComponents;

/// <summary>The tab strip shared by every "Restore deleted items" page.</summary>
public sealed class RestoreTabsViewComponent : ViewComponent
{
    private readonly IAdminRestoreTabsService _tabs;
    private readonly IAdminNavigationUrlBuilder _urlBuilder;

    public RestoreTabsViewComponent(IAdminRestoreTabsService tabs, IAdminNavigationUrlBuilder urlBuilder)
    {
        _tabs = tabs ?? throw new ArgumentNullException(nameof(tabs));
        _urlBuilder = urlBuilder ?? throw new ArgumentNullException(nameof(urlBuilder));
    }

    public async Task<IViewComponentResult> InvokeAsync(string active)
    {
        var tabs = await _tabs.GetAsync(HttpContext.User, HttpContext.RequestAborted);
        var items = tabs
            .Select(tab => new RestoreTabViewModel(
                tab.Label,
                tab.Count,
                Href(tab),
                string.Equals(tab.Key, active, StringComparison.Ordinal),
                tab.OpensAnotherPage))
            .Where(tab => tab.Href is not null)
            .ToArray();

        return View(items);
    }

    private string? Href(AdminRestoreTab tab) =>
        tab.HrefFrom(_urlBuilder.GetPath(HttpContext, AdminNavigationCatalog.Get(tab.NavigationKey)));
}

public sealed record RestoreTabViewModel(string Label, int? Count, string? Href, bool IsActive, bool OpensAnotherPage);
