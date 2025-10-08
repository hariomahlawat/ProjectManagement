using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace ProjectManagement.Tests;

internal sealed class SimpleUrlHelper : IUrlHelper
{
    public SimpleUrlHelper(ActionContext context)
    {
        ActionContext = context ?? throw new ArgumentNullException(nameof(context));
    }

    public ActionContext ActionContext { get; }

    public string? Action(UrlActionContext actionContext) => throw new NotImplementedException();

    public string? Action(string? action, string? controller, object? values, string? protocol, string? host, string? fragment)
        => throw new NotImplementedException();

    public string? Action(string? action, string? controller, object? values, string? protocol)
        => throw new NotImplementedException();

    public string? Action(string? action, string? controller, object? values)
        => throw new NotImplementedException();

    public string? Action(string? action, string? controller)
        => throw new NotImplementedException();

    public string? Content(string? contentPath) => contentPath;

    public bool IsLocalUrl(string? url) => true;

    public string? Link(string? routeName, object? values) => throw new NotImplementedException();

    public string? RouteUrl(UrlRouteContext routeContext) => throw new NotImplementedException();

    public string? RouteUrl(string? routeName, object? values, string? protocol, string? host, string? fragment)
        => throw new NotImplementedException();

    public string? RouteUrl(string? routeName, object? values, string? protocol, string? host)
        => throw new NotImplementedException();

    public string? RouteUrl(string? routeName, object? values, string? protocol)
        => throw new NotImplementedException();

    public string? RouteUrl(string? routeName, object? values)
        => throw new NotImplementedException();

    public string? Page(string? pageName, string? pageHandler, object? values, string? protocol, string? host, string? fragment)
        => $"/Pages{pageName}?{values}";

    public string? Page(string? pageName, string? pageHandler, object? values, string? protocol)
        => throw new NotImplementedException();

    public string? Page(string? pageName, string? pageHandler, object? values)
        => throw new NotImplementedException();

    public string? Page(string? pageName, string? pageHandler)
        => throw new NotImplementedException();

    public string? Page(string? pageName, object? values)
        => Page(pageName, null, values, null, null, null);
}
