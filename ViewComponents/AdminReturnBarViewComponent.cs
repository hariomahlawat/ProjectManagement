using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Navigation;
using ProjectManagement.Services.Navigation.ModuleNav;

namespace ProjectManagement.ViewComponents;

/// <summary>
/// A slim "Administration › … › page" bar on the admin pages that keep their own module's
/// layout (Document Repository, media, social media lists, the repository deletion requests in
/// the Decision Centre). Shown only to people who can open Administration, so module users see
/// their pages as before (docs/ux/admin-review-fixes.md, PR 5).
/// </summary>
public sealed class AdminReturnBarViewComponent : ViewComponent
{
    private readonly IAuthorizationService _authorization;
    private readonly IAdminNavigationUrlBuilder _urlBuilder;

    public AdminReturnBarViewComponent(IAuthorizationService authorization, IAdminNavigationUrlBuilder urlBuilder)
    {
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _urlBuilder = urlBuilder ?? throw new ArgumentNullException(nameof(urlBuilder));
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var entry = AdminNavigationCatalog.FindActiveEntry(
            RouteValue("area"),
            RouteValue("page"),
            HttpContext.Request.Query);
        if (entry is null || entry.UsesAdminShell)
        {
            return Content(string.Empty);
        }

        if (!(await _authorization.AuthorizeAsync(HttpContext.User, AdminPolicies.Access)).Succeeded)
        {
            return Content(string.Empty);
        }

        var crumbs = new List<AdminReturnCrumb>
        {
            new("Administration", _urlBuilder.GetPath(HttpContext, AdminNavigationKeys.Dashboard))
        };
        if (entry.SidebarParent is { } parentKey)
        {
            var parent = AdminNavigationCatalog.GetEntry(parentKey);
            crumbs.Add(new AdminReturnCrumb(parent.Item.Text, _urlBuilder.GetPath(HttpContext, parent.Item)));
        }

        return View(new AdminReturnBarModel(crumbs, entry.Item.Text));
    }

    private string? RouteValue(string key) =>
        ViewContext.RouteData.Values.TryGetValue(key, out var value) ? value?.ToString() : null;
}

public sealed record AdminReturnCrumb(string Text, string? Href);

public sealed record AdminReturnBarModel(IReadOnlyList<AdminReturnCrumb> Crumbs, string Current);
