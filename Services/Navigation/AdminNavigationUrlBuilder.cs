using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ProjectManagement.Models.Navigation;
using ProjectManagement.Services.Navigation.ModuleNav;

namespace ProjectManagement.Services.Navigation;

public interface IAdminNavigationUrlBuilder
{
    string? GetPath(
        HttpContext httpContext,
        string navigationKey,
        IReadOnlyDictionary<string, object?>? additionalRouteValues = null);

    string? GetPath(
        HttpContext httpContext,
        NavigationItem item,
        IReadOnlyDictionary<string, object?>? additionalRouteValues = null);
}

public sealed class AdminNavigationUrlBuilder : IAdminNavigationUrlBuilder
{
    private readonly LinkGenerator _linkGenerator;

    public AdminNavigationUrlBuilder(LinkGenerator linkGenerator)
    {
        _linkGenerator = linkGenerator ?? throw new ArgumentNullException(nameof(linkGenerator));
    }

    public string? GetPath(
        HttpContext httpContext,
        string navigationKey,
        IReadOnlyDictionary<string, object?>? additionalRouteValues = null) =>
        GetPath(httpContext, AdminNavigationCatalog.Get(navigationKey), additionalRouteValues);

    public string? GetPath(
        HttpContext httpContext,
        NavigationItem item,
        IReadOnlyDictionary<string, object?>? additionalRouteValues = null)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(item);

        var values = BuildRouteValues(item, additionalRouteValues);

        if (!string.IsNullOrWhiteSpace(item.Page))
        {
            return _linkGenerator.GetPathByPage(
                httpContext,
                page: item.Page,
                values: values);
        }

        if (!string.IsNullOrWhiteSpace(item.Controller)
            && !string.IsNullOrWhiteSpace(item.Action))
        {
            return _linkGenerator.GetPathByAction(
                httpContext,
                action: item.Action,
                controller: item.Controller,
                values: values);
        }

        return null;
    }

    private static RouteValueDictionary? BuildRouteValues(
        NavigationItem item,
        IReadOnlyDictionary<string, object?>? additionalRouteValues)
    {
        var hasArea = item.Area is not null;
        var hasItemValues = item.RouteValues is { Count: > 0 };
        var hasAdditionalValues = additionalRouteValues is { Count: > 0 };

        if (!hasArea && !hasItemValues && !hasAdditionalValues)
        {
            return null;
        }

        var values = new RouteValueDictionary();

        // An explicit empty area is significant: it clears an ambient Admin area
        // when linking to root Razor Pages such as Holidays or Celebrations.
        if (hasArea)
        {
            values["area"] = item.Area;
        }

        if (item.RouteValues is not null)
        {
            foreach (var routeValue in item.RouteValues)
            {
                values[routeValue.Key] = routeValue.Value;
            }
        }

        if (additionalRouteValues is not null)
        {
            foreach (var routeValue in additionalRouteValues)
            {
                values[routeValue.Key] = routeValue.Value;
            }
        }

        return values;
    }
}
