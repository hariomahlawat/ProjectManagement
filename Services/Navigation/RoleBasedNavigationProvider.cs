using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using ProjectManagement.Models;
using ProjectManagement.Models.Navigation;

namespace ProjectManagement.Services.Navigation;

public class RoleBasedNavigationProvider : INavigationProvider
{
    private static readonly IReadOnlyList<NavigationItem> AnonymousNavigation = Array.Empty<NavigationItem>();

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RoleBasedNavigationProvider(
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<IReadOnlyList<NavigationItem>> GetNavigationAsync()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.User?.Identity?.IsAuthenticated != true)
        {
            return AnonymousNavigation;
        }

        var user = await _userManager.GetUserAsync(httpContext.User);
        if (user is null)
        {
            return AnonymousNavigation;
        }

        var roleSet = await GetRoleSetAsync(user);

        var items = new List<NavigationItem>
        {
            new()
            {
                Text = "Projects",
                Page = "/Projects/Index"
            },
            new()
            {
                Text = "Process",
                Page = "/Process/Index"
            },
            new()
            {
                Text = "Calendar",
                Page = "/Calendar/Index"
            },
            new()
            {
                Text = "Dashboard",
                Page = "/Dashboard/Index"
            }
        };

        if (roleSet.Contains("Admin"))
        {
            items.Add(new NavigationItem
            {
                Text = "Admin Panel",
                Area = "Admin",
                Page = "/Index",
                RequiredRoles = new[] { "Admin" },
                Children = new[]
                {
                    new NavigationItem
                    {
                        Text = "Manage users",
                        Area = "Admin",
                        Page = "/Users/Index",
                        RequiredRoles = new[] { "Admin" }
                    },
                    new NavigationItem
                    {
                        Text = "Login scatter",
                        Area = "Admin",
                        Page = "/Analytics/Logins",
                        RequiredRoles = new[] { "Admin" }
                    },
                    new NavigationItem
                    {
                        Text = "Logs",
                        Area = "Admin",
                        Page = "/Logs/Index",
                        RequiredRoles = new[] { "Admin" }
                    },
                    new NavigationItem
                    {
                        Text = "DB health",
                        Area = "Admin",
                        Page = "/Diagnostics/DbHealth",
                        RequiredRoles = new[] { "Admin" }
                    },
                    new NavigationItem
                    {
                        Text = "Sponsoring units",
                        Area = "Admin",
                        Page = "/Lookups/SponsoringUnits/Index",
                        RequiredRoles = new[] { "Admin" }
                    },
                    new NavigationItem
                    {
                        Text = "Line directorates",
                        Area = "Admin",
                        Page = "/Lookups/LineDirectorates/Index",
                        RequiredRoles = new[] { "Admin" }
                    },
                    new NavigationItem
                    {
                        Text = "Project categories",
                        Area = "Admin",
                        Page = "/Categories/Index",
                        RequiredRoles = new[] { "Admin" }
                    },
                    new NavigationItem
                    {
                        Text = "Project trash",
                        Area = "Admin",
                        Page = "/Projects/Trash",
                        RequiredRoles = new[] { "Admin" }
                    },
                    new NavigationItem
                    {
                        Text = "Manage holidays",
                        Page = "/Settings/Holidays/Index",
                        RequiredRoles = new[] { "Admin" }
                    },
                    new NavigationItem
                    {
                        Text = "Manage celebrations",
                        Page = "/Celebrations/Index",
                        RequiredRoles = new[] { "Admin" }
                    }
                }
            });
        }

        if (roleSet.Contains("HoD") || roleSet.Contains("Admin"))
        {
            items.Add(new NavigationItem
            {
                Text = "Approvals",
                Page = "/Projects/Documents/Approvals/Index",
                RequiredRoles = new[] { "HoD", "Admin" }
            });
        }

        return items;
    }

    private async Task<HashSet<string>> GetRoleSetAsync(ApplicationUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);
        return roles.Count == 0
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase);
    }
}
