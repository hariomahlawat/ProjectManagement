using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using ProjectManagement.Services.Navigation.ModuleNav;

namespace ProjectManagement.Services.Navigation;

/// <summary>
/// Decides which Administration catalogue entries a person may open, so the sidebar, its search
/// and the "Every admin page" index show the same pages. Each entry keeps its own role list and
/// policy; this is a display filter, and every page still enforces its own authorization.
/// </summary>
public interface IAdminNavigationAccess
{
    Task<bool> CanOpenAsync(ClaimsPrincipal user, AdminNavigationEntry entry);

    Task<IReadOnlyList<AdminNavigationEntry>> GetOpenableEntriesAsync(ClaimsPrincipal user);
}

public sealed class AdminNavigationAccess : IAdminNavigationAccess
{
    private readonly IAuthorizationService _authorizationService;

    public AdminNavigationAccess(IAuthorizationService authorizationService)
    {
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
    }

    public async Task<bool> CanOpenAsync(ClaimsPrincipal user, AdminNavigationEntry entry)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Item.RequiredRoles is { Count: > 0 }
            && !entry.Item.RequiredRoles.Any(user.IsInRole))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(entry.Item.AuthorizationPolicy))
        {
            return true;
        }

        var result = await _authorizationService.AuthorizeAsync(
            user,
            resource: null,
            policyName: entry.Item.AuthorizationPolicy);

        return result.Succeeded;
    }

    public async Task<IReadOnlyList<AdminNavigationEntry>> GetOpenableEntriesAsync(ClaimsPrincipal user)
    {
        var openable = new List<AdminNavigationEntry>();
        foreach (var entry in AdminNavigationCatalog.Entries.OrderBy(entry => entry.Order))
        {
            if (await CanOpenAsync(user, entry))
            {
                openable.Add(entry);
            }
        }

        return openable;
    }
}
