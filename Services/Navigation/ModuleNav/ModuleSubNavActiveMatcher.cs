using ProjectManagement.Models.Navigation;

namespace ProjectManagement.Services.Navigation.ModuleNav;

/// <summary>
/// Resolves module-tab active state without coupling route-family behaviour to a
/// specific view component. Exact destinations remain the default; selected tabs
/// may opt into a narrowly defined Razor Page prefix.
/// </summary>
public static class ModuleSubNavActiveMatcher
{
    public static bool IsActive(
        NavigationItem item,
        string? currentArea,
        string? currentPage,
        string? currentController,
        string? currentAction)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!string.IsNullOrEmpty(item.Page))
        {
            if (!SameArea(item.Area, currentArea))
            {
                return false;
            }

            if (string.Equals(currentPage, item.Page, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(item.ActivePagePrefix)
                   && !string.IsNullOrWhiteSpace(currentPage)
                   && currentPage.StartsWith(
                       item.ActivePagePrefix,
                       StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrEmpty(item.Controller) && !string.IsNullOrEmpty(item.Action))
        {
            return SameArea(item.Area, currentArea)
                   && string.Equals(currentController, item.Controller, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(currentAction, item.Action, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool SameArea(string? configuredArea, string? currentArea)
        => string.Equals(
            configuredArea ?? string.Empty,
            currentArea ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
}
