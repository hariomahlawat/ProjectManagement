using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using ProjectManagement.Models.Navigation;
using ProjectManagement.Services.Navigation.ModuleNav;

namespace ProjectManagement.ViewComponents;

public sealed class ModuleSubNavViewComponent : ViewComponent
{
    // ===========================
    // QUERY STATE PRESERVATION
    // ===========================
    private static readonly HashSet<string> PreservedQueryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Query",
        "Search",
        "CategoryId",
        "ProjectCategoryId",
        "TechnicalCategoryId",
        "LeadPoUserId",
        "ProjectOfficerId",
        "HodUserId",
        "Lifecycle",
        "CompletedYear",
        "TotStatus",
        "TotCompleted",
        "TechStatus",
        "AvailableForProliferation",
        "IncludeArchived",
        "IncludeCategoryDescendants",
        "ProjectTypeId",
        "ProjectTypeUnclassified",
        "Build",
        "View"
    };

    // ===========================
    // ADMIN OVERFLOW SETTINGS
    // ===========================
    private const int AdminPrimaryTabCount = 7;
    private const string AdminOverflowLabel = "More";

    private readonly LinkGenerator _linkGenerator;
    private readonly IAuthorizationService _authorizationService;

    public ModuleSubNavViewComponent(
        LinkGenerator linkGenerator,
        IAuthorizationService authorizationService)
    {
        _linkGenerator = linkGenerator;
        _authorizationService = authorizationService;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        // ===========================
        // MODULE RESOLUTION
        // ===========================
        var currentPage = ViewContext.RouteData.Values.TryGetValue("page", out var pageValue)
            ? pageValue?.ToString()
            : null;
        var currentArea = ViewContext.RouteData.Values.TryGetValue("area", out var areaValue)
            ? areaValue?.ToString()
            : null;
        var currentController = ViewContext.RouteData.Values.TryGetValue("controller", out var controllerValue)
            ? controllerValue?.ToString()
            : null;
        var currentAction = ViewContext.RouteData.Values.TryGetValue("action", out var actionValue)
            ? actionValue?.ToString()
            : null;

        var projectModuleItems = ProjectModuleNavDefinition.Build();
        var adminModuleItems = AdminModuleNavDefinition.Build();
        var isInProjectsScope = IsInProjectsScope(currentPage, projectModuleItems);
        var isInAdminScope = IsInAdminScope(currentArea, currentPage, adminModuleItems);

        if (!isInProjectsScope && !isInAdminScope)
        {
            return Content(string.Empty);
        }

        var moduleItems = isInAdminScope ? adminModuleItems : projectModuleItems;
        var moduleLabel = isInAdminScope ? "Admin module" : "Projects module";

        // ===========================
        // AUTHORIZATION FILTERING
        // ===========================
        var visibleItems = await FilterAuthorizedItemsAsync(moduleItems);
        if (visibleItems.Count == 0)
        {
            return Content(string.Empty);
        }

        // ===========================
        // TAB MODEL BUILDING
        // ===========================
        var preservedQuery = BuildPreservedQuery();
        var tabs = visibleItems.Select(item => BuildTab(item, currentArea, currentPage, currentController, currentAction, preservedQuery))
            .ToList();

        // ===========================
        // OVERFLOW SPLIT (ADMIN ONLY)
        // ===========================
        var primaryTabs = tabs;
        var overflowTabs = Array.Empty<ModuleSubNavItem>();
        var isOverflowActive = false;

        if (isInAdminScope && tabs.Count > AdminPrimaryTabCount)
        {
            primaryTabs = tabs.Take(AdminPrimaryTabCount).ToList();
            overflowTabs = tabs.Skip(AdminPrimaryTabCount).ToList();
            isOverflowActive = overflowTabs.Any(tab => tab.IsActive);

            if (isOverflowActive)
            {
                var activeOverflow = overflowTabs.First(tab => tab.IsActive);
                var lastPrimary = primaryTabs[^1];
                primaryTabs.RemoveAt(primaryTabs.Count - 1);

                overflowTabs.Remove(activeOverflow);
                primaryTabs.Add(activeOverflow);
                overflowTabs.Insert(0, lastPrimary);
            }
        }

        return View(new ModuleSubNavViewModel
        {
            Tabs = primaryTabs,
            OverflowTabs = overflowTabs,
            OverflowLabel = AdminOverflowLabel,
            IsOverflowActive = isOverflowActive,
            AriaLabel = moduleLabel
        });
    }

    // ===========================
    // MODULE SCOPE
    // ===========================
    private static bool IsInProjectsScope(string? currentPage, IReadOnlyList<NavigationItem> projectItems)
    {
        if (string.IsNullOrWhiteSpace(currentPage))
        {
            return false;
        }

        if (currentPage.StartsWith("/Projects/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return projectItems.Any(item =>
            !string.IsNullOrWhiteSpace(item.Page)
            && string.Equals(item.Page, currentPage, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(item.Area));
    }

    private static bool IsInAdminScope(
        string? currentArea,
        string? currentPage,
        IReadOnlyList<NavigationItem> adminItems)
    {
        if (string.Equals(currentArea, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(currentPage))
        {
            return false;
        }

        return adminItems.Any(item =>
            !string.IsNullOrWhiteSpace(item.Page)
            && string.Equals(item.Page, currentPage, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.Area ?? string.Empty, currentArea ?? string.Empty, StringComparison.OrdinalIgnoreCase));
    }

    // ===========================
    // AUTHORIZATION HELPERS
    // ===========================
    private async Task<IReadOnlyList<NavigationItem>> FilterAuthorizedItemsAsync(IReadOnlyList<NavigationItem> items)
    {
        var result = new List<NavigationItem>(items.Count);
        foreach (var item in items)
        {
            if (!await IsAuthorizedAsync(item))
            {
                continue;
            }

            result.Add(item);
        }

        return result;
    }

    private async Task<bool> IsAuthorizedAsync(NavigationItem item)
    {
        if (item.RequiredRoles is { Count: > 0 })
        {
            var hasRole = item.RequiredRoles.Any(role => User.IsInRole(role));
            if (!hasRole)
            {
                return false;
            }
        }

        if (!string.IsNullOrEmpty(item.AuthorizationPolicy))
        {
            var authorizationResult = await _authorizationService.AuthorizeAsync(HttpContext.User, null, item.AuthorizationPolicy);
            if (!authorizationResult.Succeeded)
            {
                return false;
            }
        }

        return true;
    }

    // ===========================
    // TAB CREATION
    // ===========================
    private ModuleSubNavItem BuildTab(
        NavigationItem item,
        string? currentArea,
        string? currentPage,
        string? currentController,
        string? currentAction,
        IReadOnlyDictionary<string, string?> preservedQuery)
    {
        var url = BuildUrl(item);
        if (!string.IsNullOrEmpty(url) && preservedQuery.Count > 0)
        {
            url = QueryHelpers.AddQueryString(url, preservedQuery);
        }

        return new ModuleSubNavItem
        {
            Text = MapTabText(item.Text),
            Url = url,
            IsActive = IsActive(item, currentArea, currentPage, currentController, currentAction),
            Icon = item.Icon,
            BadgeViewComponentName = item.BadgeViewComponentName,
            BadgeViewComponentParameters = item.BadgeViewComponentParameters
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

    private static string MapTabText(string text) => text switch
    {
        "Projects repository" => "Repository",
        "Ongoing projects" => "Ongoing",
        "Completed projects summary" => "Completed",
        "Pending approvals" => "Approvals",
        _ => text
    };

    // ===========================
    // QUERY STRING PRESERVATION
    // ===========================
    private Dictionary<string, string?> BuildPreservedQuery()
    {
        var preserved = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in Request.Query)
        {
            if (!PreservedQueryKeys.Contains(key))
            {
                continue;
            }

            var rawValue = value.ToString();
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                continue;
            }

            preserved[key] = rawValue;
        }

        return preserved;
    }
}

// ===========================
// VIEW MODELS
// ===========================
public sealed record class ModuleSubNavViewModel
{
    public required IReadOnlyList<ModuleSubNavItem> Tabs { get; init; }

    public required IReadOnlyList<ModuleSubNavItem> OverflowTabs { get; init; }

    public required string OverflowLabel { get; init; }

    public required bool IsOverflowActive { get; init; }

    public required string AriaLabel { get; init; }
}

public sealed record class ModuleSubNavItem
{
    public required string Text { get; init; }

    public string? Url { get; init; }

    public bool IsActive { get; init; }

    public string? Icon { get; init; }

    public string? BadgeViewComponentName { get; init; }

    public object? BadgeViewComponentParameters { get; init; }
}
