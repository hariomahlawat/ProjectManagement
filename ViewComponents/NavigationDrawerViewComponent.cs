using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using ProjectManagement.Models.Navigation;
using ProjectManagement.Services.Navigation;
using ProjectManagement.Services.Navigation.ModuleNav;

namespace ProjectManagement.ViewComponents;

public class NavigationDrawerViewComponent : ViewComponent
{
    private readonly INavigationProvider _navigationProvider;
    private readonly LinkGenerator _linkGenerator;

    public NavigationDrawerViewComponent(
        INavigationProvider navigationProvider,
        LinkGenerator linkGenerator)
    {
        _navigationProvider = navigationProvider;
        _linkGenerator = linkGenerator;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var items = await _navigationProvider.GetNavigationAsync();
        var model = BuildViewModel(items);
        return View(model);
    }

    private NavigationDrawerViewModel BuildViewModel(IEnumerable<NavigationItem> items)
    {
        var area = ViewContext.RouteData.Values.TryGetValue("area", out var areaValue)
            ? areaValue?.ToString()
            : null;
        var page = ViewContext.RouteData.Values.TryGetValue("page", out var pageValue)
            ? pageValue?.ToString()
            : null;
        var controller = ViewContext.RouteData.Values.TryGetValue("controller", out var controllerValue)
            ? controllerValue?.ToString()
            : null;
        var action = ViewContext.RouteData.Values.TryGetValue("action", out var actionValue)
            ? actionValue?.ToString()
            : null;

        var nodes = BuildNodes(items, area, page, controller, action, BuildUrl);

        return new NavigationDrawerViewModel
        {
            Brand = ViewContext.ViewData["AppName"] as string ?? "PRISM ERP",
            UserName = HttpContext.User.Identity?.IsAuthenticated == true ? HttpContext.User.Identity?.Name : null,
            Items = nodes
        };
    }

    /// <summary>
    /// Projects navigation items into drawer nodes. An item is active when it matches the
    /// current page exactly or, where it declares one, its <see cref="NavigationItem.ActivePagePrefix"/>
    /// (the same rule the module sub-nav uses, so the drawer and the tabs agree).
    /// </summary>
    internal static IReadOnlyList<NavigationDrawerNode> BuildNodes(
        IEnumerable<NavigationItem> items,
        string? currentArea,
        string? currentPage,
        string? currentController,
        string? currentAction,
        Func<NavigationItem, string?> buildUrl)
    {
        return items
            .Select(item => CreateNode(item, currentArea, currentPage, currentController, currentAction, buildUrl))
            .ToList();
    }

    private static NavigationDrawerNode CreateNode(
        NavigationItem item,
        string? currentArea,
        string? currentPage,
        string? currentController,
        string? currentAction,
        Func<NavigationItem, string?> buildUrl)
    {
        var childNodes = BuildNodes(item.Children, currentArea, currentPage, currentController, currentAction, buildUrl);
        var isActive = ModuleSubNavActiveMatcher.IsActive(item, currentArea, currentPage, currentController, currentAction);
        var hasActiveDescendant = childNodes.Any(child => child.IsActive || child.HasActiveDescendant);

        return new NavigationDrawerNode
        {
            Text = item.Text,
            Url = buildUrl(item),
            IsActive = isActive,
            HasActiveDescendant = hasActiveDescendant,
            Children = childNodes,
            BadgeViewComponentName = item.BadgeViewComponentName,
            BadgeViewComponentParameters = item.BadgeViewComponentParameters,
            Icon = item.Icon,
            Accent = item.Accent,
            OpensInNewTab = item.OpensInNewTab
        };
    }

    private string? BuildUrl(NavigationItem item)
    {
        if (!string.IsNullOrEmpty(item.Page))
        {
            var values = BuildRouteValues(item);
            return _linkGenerator.GetPathByPage(HttpContext, page: item.Page, values: values);
        }

        if (!string.IsNullOrEmpty(item.Controller) && !string.IsNullOrEmpty(item.Action))
        {
            var values = BuildRouteValues(item);
            return _linkGenerator.GetPathByAction(HttpContext, action: item.Action, controller: item.Controller, values: values);
        }

        return null;
    }

    internal static RouteValueDictionary? BuildRouteValues(NavigationItem item)
    {
        if (item.Area is null && (item.RouteValues is null || item.RouteValues.Count == 0))
        {
            return null;
        }

        var values = new RouteValueDictionary();

        // An explicit empty area is significant: it clears an ambient Admin area when
        // linking to a root page (same rule as AdminNavigationUrlBuilder).
        if (item.Area is not null)
        {
            values["area"] = item.Area;
        }

        if (item.RouteValues is not null)
        {
            foreach (var pair in item.RouteValues)
            {
                values[pair.Key] = pair.Value;
            }
        }

        return values;
    }
}

public sealed record class NavigationDrawerViewModel
{
    public required IReadOnlyList<NavigationDrawerNode> Items { get; init; }

    public string Brand { get; init; } = "PRISM ERP";

    public string? UserName { get; init; }
}

public sealed record class NavigationDrawerNode
{
    public required string Text { get; init; }

    public string? Url { get; init; }

    public bool IsActive { get; init; }

    public bool HasActiveDescendant { get; init; }

    public IReadOnlyList<NavigationDrawerNode> Children { get; init; } = Array.Empty<NavigationDrawerNode>();

    public string? BadgeViewComponentName { get; init; }

    public object? BadgeViewComponentParameters { get; init; }

    public string? Icon { get; init; }

    public string? Accent { get; init; }

    public bool OpensInNewTab { get; init; }
}

public sealed record class NavigationDrawerItemsViewModel(
    IReadOnlyList<NavigationDrawerNode> Nodes,
    int Depth);
