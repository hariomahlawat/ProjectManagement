using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using ProjectManagement.Models.Navigation;
using ProjectManagement.Services.Navigation;

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

        var nodes = items.Select(item => CreateNode(item, area, page, controller, action)).ToList();

        return new NavigationDrawerViewModel
        {
            Brand = ViewContext.ViewData["AppName"] as string ?? "PRISM ERP",
            UserName = HttpContext.User.Identity?.IsAuthenticated == true ? HttpContext.User.Identity?.Name : null,
            Items = nodes
        };
    }

    private NavigationDrawerNode CreateNode(
        NavigationItem item,
        string? currentArea,
        string? currentPage,
        string? currentController,
        string? currentAction)
    {
        var childNodes = item.Children.Select(child => CreateNode(child, currentArea, currentPage, currentController, currentAction)).ToList();
        var url = BuildUrl(item);
        var isActive = IsActive(item, currentArea, currentPage, currentController, currentAction);
        var hasActiveDescendant = childNodes.Any(child => child.IsActive || child.HasActiveDescendant);

        return new NavigationDrawerNode
        {
            Text = item.Text,
            Url = url,
            IsActive = isActive,
            HasActiveDescendant = hasActiveDescendant,
            Children = childNodes,
            BadgeViewComponentName = item.BadgeViewComponentName,
            BadgeViewComponentParameters = item.BadgeViewComponentParameters,
            Icon = item.Icon,
            Accent = item.Accent
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

    private static RouteValueDictionary? BuildRouteValues(NavigationItem item)
    {
        if (string.IsNullOrEmpty(item.Area) && (item.RouteValues is null || item.RouteValues.Count == 0))
        {
            return null;
        }

        var values = new RouteValueDictionary();

        if (!string.IsNullOrEmpty(item.Area))
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

    private static bool IsActive(
        NavigationItem item,
        string? currentArea,
        string? currentPage,
        string? currentController,
        string? currentAction)
    {
        if (item.ActivePagePrefixes is { Count: > 0 } && !string.IsNullOrEmpty(currentPage))
        {
            if (!string.Equals(item.Area ?? string.Empty, currentArea ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (item.ActivePagePrefixes.Any(prefix =>
                    !string.IsNullOrWhiteSpace(prefix) &&
                    currentPage.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        if (!string.IsNullOrEmpty(item.Page))
        {
            if (!string.Equals(currentPage, item.Page, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return string.Equals(item.Area ?? string.Empty, currentArea ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrEmpty(item.Controller) && !string.IsNullOrEmpty(item.Action))
        {
            if (!string.Equals(currentController, item.Controller, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.Equals(currentAction, item.Action, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return string.Equals(item.Area ?? string.Empty, currentArea ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        return false;
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
}

public sealed record class NavigationDrawerItemsViewModel(
    IReadOnlyList<NavigationDrawerNode> Nodes,
    bool IsOffcanvas,
    int Depth);
