using Microsoft.AspNetCore.Identity;
using ProjectManagement.Configuration;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Navigation;

/// <summary>
/// Resolves the application's default post-authentication landing page from a user's roles.
/// Explicit local return URLs continue to take precedence in the login flow.
/// </summary>
public sealed class DefaultLandingPageResolver
{
    private readonly UserManager<ApplicationUser> _userManager;

    public DefaultLandingPageResolver(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<string> ResolveAsync(ApplicationUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);
        var roleSet = roles.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Comdt/HoD takes precedence when a user also holds the Project Officer role.
        if (roleSet.Contains(RoleNames.Comdt) || roleSet.Contains(RoleNames.HoD))
        {
            return "/Dashboard/Index";
        }

        if (roleSet.Contains(RoleNames.ProjectOfficer))
        {
            return "/Workspace/Index";
        }

        return "/Dashboard/Index";
    }
}
